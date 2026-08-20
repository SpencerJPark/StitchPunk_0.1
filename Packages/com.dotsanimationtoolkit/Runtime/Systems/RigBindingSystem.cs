// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Re-binds an actor's parts to itself after instantiation (architecture section 5.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why this exists — corrected by amendment A35.</strong> The original rationale was that
    /// instantiate remaps entity references held in <em>components</em> but not those held inside
    /// <em>dynamic buffers</em>, leaving a copied actor's <see cref="RigPartRef"/> list pointing at the
    /// prefab's parts. <strong>That is not true of Entities 6.5:</strong>
    /// <c>InstantiateEntitiesGroup</c> passes the archetype's buffer patches to
    /// <c>EntityRemapUtility.PatchEntitiesForPrefab</c>, so a reference to a member of the
    /// instantiated <c>LinkedEntityGroup</c> is remapped inside a buffer exactly as it is inside a
    /// component. A baked actor arrives here already bound to its own parts.
    /// </para>
    /// <para>
    /// The rebuild is kept anyway, and deliberately: it is what binds an actor that reached this
    /// system by any other route — a pooling pass that re-parents parts and re-enables the tag, or an
    /// actor assembled without going through <c>RigBindingBakingSystem</c> — and it costs one walk of
    /// <c>LinkedEntityGroup</c> once per spawn. The two things that are load-bearing on every path are
    /// the per-instance <c>phase01</c> re-derivation below and the tag disable, neither of which
    /// instantiate can do. See A35 for the evidence and the revert note.
    /// </para>
    /// <para>
    /// <strong>How the parts are found.</strong> From <c>LinkedEntityGroup</c>, which instantiate
    /// <em>does</em> remap — it is the one structure guaranteed to name this instance's own children.
    /// Element 0 is the root itself and is skipped. Parts are recognised by carrying
    /// <see cref="RigPartBinding"/>, and their <c>targetIndex</c> is plain data that survives
    /// instantiation unchanged, so the rebuild needs no lookup into the registry blob.
    /// </para>
    /// <para>
    /// <strong>Buffer order is deliberately not preserved.</strong> Parts are appended in
    /// <c>LinkedEntityGroup</c> order, which is not the baked order. Architecture section 5.3 states
    /// <see cref="RigPartRef"/> order is unspecified precisely so that this system is free to rebuild
    /// it; every consumer reads <c>targetIndex</c>, never position.
    /// </para>
    /// <para>
    /// <strong>Parallel safety.</strong> Each actor touches only entities inside its own
    /// <c>LinkedEntityGroup</c>, and an entity belongs to exactly one such group, so no two workers
    /// can write the same part. That is what makes the
    /// <see cref="NativeDisableParallelForRestrictionAttribute"/> on the binding lookup sound rather
    /// than merely convenient — it is a claim about the data, not a suppression of the check.
    /// </para>
    /// <para>
    /// Runs in the binding group, which is <c>OrderFirst</c>, so no system observes a half-remapped
    /// actor. The enableable tag is baked ENABLED, so instantiated copies inherit it enabled and are
    /// picked up on their first frame; this system disables it and never runs on that actor again.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitBindingSystemGroup))]
    [BurstCompile]
    public partial struct RigBindingSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RigBindingUninitialized>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RebindActorPartsJob rebindJob = new RebindActorPartsJob
            {
                partBindingLookup = SystemAPI.GetComponentLookup<RigPartBinding>()
            };
            state.Dependency = rebindJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Rebuilds one actor's <see cref="RigPartRef"/> buffer and rewrites its parts' back-references.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(RigBindingUninitialized))]
    internal partial struct RebindActorPartsJob : IJobEntity
    {
        /// <summary>
        /// Written on part entities, never on the actor being iterated. Safe in parallel because an
        /// entity appears in exactly one <c>LinkedEntityGroup</c> — see the type-level remarks.
        /// </summary>
        [NativeDisableParallelForRestriction] public ComponentLookup<RigPartBinding> partBindingLookup;

        private void Execute(
            Entity actorEntity,
            ref DynamicBuffer<RigPartRef> partRefs,
            ref SampleSettings sampleSettings,
            in DynamicBuffer<LinkedEntityGroup> linkedEntities,
            EnabledRefRW<RigBindingUninitialized> rigBindingUninitializedEnabled)
        {
            partRefs.Clear();

            // Element 0 is the root itself (Entities' contract for LinkedEntityGroup), so the walk
            // starts at 1. A root that also carried RigPartBinding would otherwise bind to itself.
            for (int linkedIndex = 1; linkedIndex < linkedEntities.Length; linkedIndex++)
            {
                Entity candidateEntity = linkedEntities[linkedIndex].Value;
                if (!partBindingLookup.HasComponent(candidateEntity))
                {
                    continue;
                }

                RefRW<RigPartBinding> partBinding = partBindingLookup.GetRefRW(candidateEntity);
                partBinding.ValueRW.actorRoot = actorEntity;

                partRefs.Add(new RigPartRef
                {
                    part = candidateEntity,
                    targetIndex = partBinding.ValueRO.targetIndex
                });
            }

            sampleSettings.phase01 = DerivePhaseFromEntity(actorEntity);

            rigBindingUninitializedEnabled.ValueRW = false;
        }

        /// <summary>
        /// Spreads a crowd's sampling across frames by giving each <em>instance</em> its own phase.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The baked phase comes from the authoring hierarchy path (amendment A18), which is stable
        /// across sessions but identical for every copy of one prefab — so a crowd instantiated from a
        /// single prefab would sample in lockstep and reintroduce the same-tick spike the phase exists
        /// to prevent. Re-deriving here, per instance, is what actually staggers them.
        /// </para>
        /// <para>
        /// Deriving from the entity id is correct <em>here</em> and would be wrong at bake, which is
        /// worth stating because the opposite rule governs twenty lines of <c>ActorBaker</c>: an entity
        /// id is session-local, so baking one makes the same prefab produce different bytes every
        /// session and breaks subscene reproducibility. Nothing is persisted here — the value is
        /// recomputed on every spawn — so session-locality is exactly the property wanted.
        /// </para>
        /// <para>
        /// Same <c>&gt;&gt; 8</c> shape as A18's baked derivation, for one reason: both feed the same
        /// consumer and should have the same distribution. The result lands in [0, 1), which section
        /// 5.6 requires.
        /// </para>
        /// </remarks>
        private static float DerivePhaseFromEntity(Entity actorEntity)
        {
            uint entityHash = math.hash(new int2(actorEntity.Index, actorEntity.Version));
            return (entityHash >> 8) * (1f / 16777216f);
        }
    }
}
