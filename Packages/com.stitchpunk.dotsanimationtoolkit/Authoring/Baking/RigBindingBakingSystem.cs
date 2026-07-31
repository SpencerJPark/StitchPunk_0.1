// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// The cross-entity half of rig binding (architecture section 4.1): resolves each baked part's
    /// target id into the dense target index of its actor's registry blob, fills the actor's
    /// <see cref="RigPartRef"/> buffer, and writes <see cref="RigPartBinding.actorRoot"/> and
    /// <see cref="RigPartBinding.targetIndex"/> on the part.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a pure entity-data pass and is Burst-compiled throughout: it touches no managed
    /// object. Everything that needs a managed reference — materials, ScriptableObjects, the
    /// texture-set check of section 4.4 — lives in the Bakers, which are managed code by
    /// construction.
    /// </para>
    /// <para>
    /// The buffers are cleared and rebuilt from scratch on every pass rather than appended to.
    /// Incremental baking re-runs only the bakers whose inputs changed, but this system sees every
    /// actor every pass, so an append-only build would duplicate the parts that were not re-baked.
    /// The rebuild is also why <see cref="RigPartBakeLink"/> is a baking type rather than a
    /// temporary one — the parts that were not re-baked must still be visible here.
    /// </para>
    /// <para>
    /// The resolve pass is single-threaded on purpose: it appends into a buffer that belongs to
    /// another entity and several parts share one actor, so a parallel schedule would race on the
    /// buffer and on the duplicate-claim check that reads it. The work is one binary search per
    /// part, so the thread is cheap to give up.
    /// </para>
    /// <para>
    /// Single-threading also makes the resulting <see cref="RigPartRef"/> order repeatable in
    /// practice — chunk iteration order follows entity creation order, which follows baking order —
    /// but that is an emergent property of Entities, not a guarantee this package can make, and
    /// nothing here relies on it: architecture section 5.3's <c>RigBindingSystem</c> rebuilds the
    /// buffer from the <c>LinkedEntityGroup</c> at spawn, so the baked order never reaches a frame.
    /// Treat the order as unspecified.
    /// </para>
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    [BurstCompile]
    public partial struct RigBindingBakingSystem : ISystem
    {
        private ComponentLookup<ClipRegistry> clipRegistryLookup;
        private BufferLookup<RigPartRef> rigPartRefLookup;

        /// <inheritdoc />
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            clipRegistryLookup = state.GetComponentLookup<ClipRegistry>(true);
            rigPartRefLookup = state.GetBufferLookup<RigPartRef>();
        }

        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            clipRegistryLookup.Update(ref state);
            rigPartRefLookup.Update(ref state);

            ClearRigPartRefsJob clearJob = new ClearRigPartRefsJob();
            state.Dependency = clearJob.ScheduleParallel(state.Dependency);

            ResolveRigPartBindingsJob resolveJob = new ResolveRigPartBindingsJob
            {
                clipRegistryLookup = clipRegistryLookup,
                rigPartRefLookup = rigPartRefLookup
            };
            state.Dependency = resolveJob.Schedule(state.Dependency);
        }
    }

    /// <summary>
    /// Empties every actor's <see cref="RigPartRef"/> buffer so the resolve pass can rebuild it from
    /// the parts that exist right now.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(ClipRegistry))]
    [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
    internal partial struct ClearRigPartRefsJob : IJobEntity
    {
        private void Execute(ref DynamicBuffer<RigPartRef> rigPartRefs)
        {
            rigPartRefs.Clear();
        }
    }

    /// <summary>
    /// Resolves one part's target id against its actor's registry and records the binding on both
    /// ends. A part that cannot be bound is reported once and left inert — its
    /// <see cref="RigPartBinding.targetIndex"/> stays −1 and it never enters the actor's
    /// <see cref="RigPartRef"/> buffer — so one bad part never fails the bake of the actor around it.
    /// </summary>
    [BurstCompile]
    [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
    internal partial struct ResolveRigPartBindingsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<ClipRegistry> clipRegistryLookup;

        public BufferLookup<RigPartRef> rigPartRefLookup;

        private void Execute(Entity partEntity, in RigPartBakeLink bakeLink, ref RigPartBinding partBinding)
        {
            partBinding.actorRoot = Entity.Null;
            partBinding.targetIndex = -1;

            if (!clipRegistryLookup.HasComponent(bakeLink.actorRoot) ||
                !rigPartRefLookup.HasBuffer(bakeLink.actorRoot))
            {
                // Stay silent when the actor entity exists but carries no registry. That is the
                // shape of an actor whose own bake bailed out — a missing clip set, a set that
                // failed validation — and ActorBaker has already logged the one message naming the
                // asset and the rule. Repeating it here once per part buries that message under N
                // copies of a restatement, none of which a user can act on. A genuinely absent
                // actor root still reports, because nothing else has spoken for it.
                if (bakeLink.actorRoot != Entity.Null)
                {
                    return;
                }
                Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' has no baked actor to bind to. Its actor's clip set is probably missing or invalid. The part is skipped.");
                return;
            }

            ClipRegistry clipRegistry = clipRegistryLookup[bakeLink.actorRoot];
            if (!clipRegistry.Value.IsCreated)
            {
                Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' belongs to an actor whose clip registry failed to build. The part is skipped.");
                return;
            }

            if (!ClipRegistryUtil.ResolveTargetIndex(
                    ref clipRegistry.Value.Value,
                    new TargetId(bakeLink.targetId),
                    out int denseTargetIndex))
            {
                Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' references target id {bakeLink.targetId}, which the actor's rig does not declare. The part is skipped.");
                return;
            }

            DynamicBuffer<RigPartRef> rigPartRefs = rigPartRefLookup[bakeLink.actorRoot];
            for (int refIndex = 0; refIndex < rigPartRefs.Length; refIndex++)
            {
                if (rigPartRefs[refIndex].targetIndex != denseTargetIndex)
                {
                    continue;
                }
                Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' claims target id {bakeLink.targetId}, which another part of the same actor already claims. The duplicate is skipped.");
                return;
            }

            rigPartRefs.Add(new RigPartRef
            {
                part = partEntity,
                targetIndex = denseTargetIndex
            });
            partBinding.actorRoot = bakeLink.actorRoot;
            partBinding.targetIndex = denseTargetIndex;
        }
    }
}
