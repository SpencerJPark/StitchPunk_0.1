// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// The cross-entity half of rig binding: resolves each baked part's target id into the dense
    /// target index of its actor's registry blob, fills the actor's <see cref="RigPartRef"/> buffer,
    /// and writes <see cref="RigPartBinding.actorRoot"/> and <see cref="RigPartBinding.targetIndex"/>
    /// on the part.
    /// </summary>
    // Buffers are cleared and rebuilt from scratch every pass, not appended to: incremental baking
    // re-runs only the bakers whose inputs changed, but this system sees every actor every pass, so
    // an append-only build would duplicate parts that were not re-baked — the same reason
    // RigPartBakeLink is a baking type rather than a temporary one.
    // The resolve pass is single-threaded on purpose: it appends into a buffer belonging to another
    // entity, and several parts share one actor, so a parallel schedule would race on the buffer
    // and the duplicate-claim check that reads it. The RigPartRef order this produces is not a
    // guarantee — RigBindingSystem rebuilds it from LinkedEntityGroup at spawn regardless.
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    [BurstCompile]
    public partial struct RigBindingBakingSystem : ISystem
    {
        private ComponentLookup<ClipRegistry> clipRegistryLookup;
        private ComponentLookup<ActorBakeFailed> actorBakeFailedLookup;
        private BufferLookup<RigPartRef> rigPartRefLookup;

        /// <inheritdoc />
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            clipRegistryLookup = state.GetComponentLookup<ClipRegistry>(true);
            actorBakeFailedLookup = state.GetComponentLookup<ActorBakeFailed>(true);
            rigPartRefLookup = state.GetBufferLookup<RigPartRef>();
        }

        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            clipRegistryLookup.Update(ref state);
            actorBakeFailedLookup.Update(ref state);
            rigPartRefLookup.Update(ref state);

            ClearRigPartRefsJob clearJob = new ClearRigPartRefsJob();
            state.Dependency = clearJob.ScheduleParallel(state.Dependency);

            ResolveRigPartBindingsJob resolveJob = new ResolveRigPartBindingsJob
            {
                clipRegistryLookup = clipRegistryLookup,
                actorBakeFailedLookup = actorBakeFailedLookup,
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

    // Reported here unless something else already has: a part whose id the rig does not declare is
    // reported by RigTargetBaker, which withholds the RigPartBakeLink so it never reaches this job;
    // an actor whose own bake failed is tagged ActorBakeFailed by ActorBaker, checked below before
    // complaining that the registry is missing. What is left for this pass alone: two parts of one
    // actor claiming the same target, and an actor missing its registry with nothing explaining why.
    /// <summary>
    /// Resolves one part's target id against its actor's registry and records the binding on both
    /// ends. A part that cannot be bound is left inert — its <see cref="RigPartBinding.targetIndex"/>
    /// stays −1 and it never enters the actor's <see cref="RigPartRef"/> buffer.
    /// </summary>
    [BurstCompile]
    [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
    internal partial struct ResolveRigPartBindingsJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<ClipRegistry> clipRegistryLookup;

        [ReadOnly] public ComponentLookup<ActorBakeFailed> actorBakeFailedLookup;

        public BufferLookup<RigPartRef> rigPartRefLookup;

        private void Execute(Entity partEntity, in RigPartBakeLink bakeLink, ref RigPartBinding partBinding)
        {
            partBinding.actorRoot = Entity.Null;
            partBinding.targetIndex = -1;

            if (!clipRegistryLookup.HasComponent(bakeLink.actorRoot) ||
                !rigPartRefLookup.HasBuffer(bakeLink.actorRoot) ||
                !clipRegistryLookup[bakeLink.actorRoot].Value.IsCreated)
            {
                // Tagged means ActorBaker already logged the one message naming the asset and the
                // rule — restating it once per part would bury it under copies nobody can act on.
                // Untagged means nothing has explained the missing registry, and every part under
                // this actor is about to stop animating, so this is the only place left to say so.
                if (actorBakeFailedLookup.HasComponent(bakeLink.actorRoot))
                {
                    return;
                }
                Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' belongs to an actor that has no usable clip registry, and no earlier message explained why. The part is skipped and will not animate. Check whether another baking system in this project removes components from actor entities; if none does, this is a toolkit defect worth reporting.");
                return;
            }

            ClipRegistry clipRegistry = clipRegistryLookup[bakeLink.actorRoot];
            if (!ClipRegistryApi.TryResolveTarget(
                    ref clipRegistry.Value.Value,
                    new TargetId(bakeLink.targetId),
                    out int denseTargetIndex))
            {
                // Not the ordinary "wrong id" case — RigTargetBaker catches that against the
                // RigAsset. Reaching here means the rig declares the id but the actor's baked
                // registry does not carry it: the builder's canonical target list and the rig
                // disagree, which should never happen by construction.
                Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' references target id {bakeLink.targetId}, which its rig declares but the actor's baked clip registry does not carry. The part is skipped. The rig asset and the registry built from it are meant to hold the same target set, so this is a toolkit defect worth reporting rather than a content mistake.");
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
