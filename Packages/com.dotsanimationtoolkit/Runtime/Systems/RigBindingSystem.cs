// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs <c>OrderFirst</c> in <see cref="AnimationToolkitBindingSystemGroup"/>: rebinds an
    /// actor's parts to itself after instantiation. Entities 6.5 already remaps buffer-held entity
    /// references on instantiate, so a baked actor arrives already bound; this system exists for
    /// what instantiate cannot do — deriving a fresh per-instance <c>phase01</c> and disabling this
    /// tag — and for actors assembled by any other route (pooling, manual assembly).
    /// </summary>
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

    [BurstCompile]
    [WithAll(typeof(RigBindingUninitialized))]
    internal partial struct RebindActorPartsJob : IJobEntity
    {
        // Written on part entities, never on the actor being iterated. Safe in parallel because an
        // entity appears in exactly one LinkedEntityGroup.
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
            // Parts land in LinkedEntityGroup order here, not the baked order — RigPartRef order is
            // unspecified for exactly this reason, and every consumer reads targetIndex, never position.
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

        // The baked phase is stable across sessions but identical for every copy of one prefab, so a
        // crowd instantiated from a single prefab would sample in lockstep; re-deriving per instance
        // here is what staggers them. Deriving from the entity id is correct here and would be wrong
        // at bake (an entity id is session-local, so baking one would break subscene reproducibility)
        // — this recomputes on every spawn, so session-locality is exactly what's wanted.
        private static float DerivePhaseFromEntity(Entity actorEntity)
        {
            uint entityHash = math.hash(new int2(actorEntity.Index, actorEntity.Version));
            return (entityHash >> 8) * (1f / 16777216f);
        }
    }
}
