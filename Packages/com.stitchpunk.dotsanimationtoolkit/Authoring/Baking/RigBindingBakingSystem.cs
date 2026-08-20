// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
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
    /// nothing here relies on it. Architecture section 5.3 specifies <c>RigBindingSystem</c> —
    /// which build step C4 will add; it does not exist yet — to rebuild the buffer from the
    /// <c>LinkedEntityGroup</c> at spawn, after which the baked order never reaches a frame.
    /// Until then no shipped system reads this buffer at all. Treat the order as unspecified
    /// either way: it is unspecified today because nothing consumes it, and unspecified after C4
    /// because C4 overwrites it.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Resolves one part's target id against its actor's registry and records the binding on both
    /// ends. A part that cannot be bound is left inert — its
    /// <see cref="RigPartBinding.targetIndex"/> stays −1 and it never enters the actor's
    /// <see cref="RigPartRef"/> buffer — so one bad part never fails the bake of the actor around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is reported here <em>unless</em> something else has already spoken for it. Two failures
    /// are diagnosed earlier and better by managed code, which can name assets and attach a
    /// click-to-select context object: a part whose id the rig does not declare is reported by
    /// <see cref="RigTargetBaker"/>, which then withholds the part's <see cref="RigPartBakeLink"/>
    /// so it never reaches this job at all; and an actor whose own bake reported a failure and
    /// stopped is tagged <see cref="ActorBakeFailed"/> by <see cref="ActorBaker"/>, which this job
    /// checks before complaining that the registry is missing. Both are architecture section 4.1 as
    /// amended by A22.
    /// </para>
    /// <para>
    /// What is left here is what only this pass can see: two parts of one actor claiming the same
    /// target, and an actor missing its registry with <em>nothing</em> having explained why.
    /// </para>
    /// </remarks>
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
                // An actor that reported its own failure and stopped is tagged, and this pass says
                // nothing more about it: ActorBaker has already logged the one message naming the
                // asset and the rule, and restating it once per part buries that message under N
                // copies of a restatement none of which a user can act on.
                //
                // Without the tag the registry is missing for a reason nobody has given, and that
                // must not pass in silence — every part under this actor is about to stop animating
                // and this is the only place left that can say so. The tag is what makes the
                // difference a claim rather than an inference: before it, silence here was correct
                // only because each of ActorBaker's bail-outs happened to log first, which nothing
                // enforced.
                if (actorBakeFailedLookup.HasComponent(bakeLink.actorRoot))
                {
                    return;
                }
                Debug.LogError($"[DOTS Animation Toolkit] Rig part '{bakeLink.authoringPath}' belongs to an actor that has no usable clip registry, and no earlier message explained why. The part is skipped and will not animate. Check whether another baking system in this project removes components from actor entities; if none does, this is a toolkit defect worth reporting.");
                return;
            }

            ClipRegistry clipRegistry = clipRegistryLookup[bakeLink.actorRoot];
            if (!ClipRegistryUtil.ResolveTargetIndex(
                    ref clipRegistry.Value.Value,
                    new TargetId(bakeLink.targetId),
                    out int denseTargetIndex))
            {
                // Not the ordinary "you typed the wrong id" case — RigTargetBaker catches that
                // against the RigAsset and the part never gets here. Reaching this line means the
                // rig declares the id but the actor's baked registry does not carry it, i.e. the
                // builder's canonical target list and the rig asset disagree. The two are supposed
                // to be the same set by construction, so this is the guard on that construction
                // holding, and it is the only check standing between a builder-side regression and
                // parts that silently stop animating.
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
