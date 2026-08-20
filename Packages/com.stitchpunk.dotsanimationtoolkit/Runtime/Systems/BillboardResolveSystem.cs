// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns every billboard root to face the viewer, and publishes the frame it resolved
    /// (amendment A44).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Runs after the pose, and that order decides what a keyed rotation means.</strong>
    /// <c>TransformSampleSystem</c> composites the clips, <c>TransformApplySystem</c> writes the
    /// result onto each part's transform, and only then does this run. So the animated pose is the
    /// billboard's <em>rest</em> orientation, and at full blend weight the billboard replaces it
    /// outright — which is why blend weight and angle offset exist, and why keying rotation on a
    /// fully billboarded node changes nothing visible.
    /// </para>
    /// <para>
    /// <strong>Roots are resolved shallowest first, through live transform values.</strong> Each
    /// node's rest orientation is read by walking <c>LocalTransform</c> up the parent chain rather
    /// than from <c>LocalToWorld</c>, for two reasons: <c>LocalToWorld</c> is a frame stale relative
    /// to the pose just written, and — decisively — an ancestor that is <em>itself</em> a billboard
    /// root has already written its result by the time a nested root reads through it. That is what
    /// stops the inner billboard composing on top of the outer one and turning twice, and it is why
    /// the baked buffer's depth ordering is load-bearing rather than cosmetic.
    /// </para>
    /// <para>
    /// <strong>Gated on <see cref="AnimVisible"/>.</strong> Turning an off-screen actor toward a
    /// camera that cannot see it is the definition of presentation work worth skipping, and the
    /// first visible frame recomputes everything from scratch — there is no state to catch up.
    /// </para>
    /// <para>
    /// Supersedes <c>ActorBillboardSystem</c>, which turned one rotation on the actor root. The
    /// whole-actor case is now the ordinary case of a single root at depth 0.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformApplySystem))]
    [BurstCompile]
    public partial struct BillboardResolveSystem : ISystem
    {
        private ComponentLookup<LocalTransform> localTransformLookup;
        private ComponentLookup<Parent> parentLookup;

        /// <inheritdoc />
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BillboardRootElement>();

            // Without a camera there is nothing to face. The host writes this singleton; the package
            // never reads a Camera, because it cannot know which of a host's cameras matters.
            state.RequireForUpdate<AnimationToolkitCameraData>();

            localTransformLookup = state.GetComponentLookup<LocalTransform>();
            parentLookup = state.GetComponentLookup<Parent>(true);
        }

        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            AnimationToolkitCameraData cameraData = SystemAPI.GetSingleton<AnimationToolkitCameraData>();

            localTransformLookup.Update(ref state);
            parentLookup.Update(ref state);

            ResolveBillboardRootsJob resolveJob = new ResolveBillboardRootsJob
            {
                cameraPosition = cameraData.position,
                cameraForward = cameraData.forward,
                localTransformLookup = localTransformLookup,
                parentLookup = parentLookup
            };
            state.Dependency = resolveJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Resolves one actor's billboard roots, in buffer order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parallel across actors, which is safe because every entity this touches belongs to the actor
    /// being processed: a billboard root is a node of that actor's own prefab hierarchy, and no two
    /// actors share one. The <c>NativeDisableParallelForRestriction</c> asserts exactly that, and it
    /// is the reason the roots are collected per actor rather than iterated as loose components —
    /// loose roots would give the scheduler no way to know the writes cannot collide.
    /// </para>
    /// </remarks>
    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct ResolveBillboardRootsJob : IJobEntity
    {
        public float3 cameraPosition;
        public float3 cameraForward;

        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup;

        [ReadOnly] public ComponentLookup<Parent> parentLookup;

        private void Execute(
            ref DynamicBuffer<BillboardRootElement> rootElements,
            in DynamicBuffer<PlaybackLayer> layers,
            in ClipRegistry clipRegistry)
        {
            for (int rootIndex = 0; rootIndex < rootElements.Length; rootIndex++)
            {
                BillboardRootElement rootElement = rootElements[rootIndex];
                if (!localTransformLookup.HasComponent(rootElement.node))
                {
                    continue;
                }

                // The authored settings are the rest state; a clip's billboard track keys on top of
                // them rather than replacing them, so a rig that sits permanently three-quarters-on
                // can still be animated off that rest.
                BillboardSettings settings = rootElement.settings;
                ApplyKeyedChannels(ref settings, rootElement.rootId, in layers, in clipRegistry);

                LocalTransform nodeWorld = ComputeWorldTransform(rootElement.node);

                quaternion resolvedRotation;
                bool resolved = BillboardMath.TryResolve(
                    settings,
                    nodeWorld.Position,
                    cameraPosition,
                    cameraForward,
                    nodeWorld.Rotation,
                    out resolvedRotation);

                // Published either way. A consumer reading the frame must never have to ask whether
                // the value it just read is real or a leftover from the last frame that succeeded.
                rootElement.resolvedRotation = resolvedRotation;
                rootElements[rootIndex] = rootElement;

                if (!resolved)
                {
                    continue;
                }

                // The world result is converted back into the node's own parent space, which is what
                // cancels an outer billboard root's rotation before this one applies its own. This is
                // the host game's InverseTransformRotation, generalised to arbitrary depth.
                quaternion parentWorldRotation = ComputeParentWorldRotation(rootElement.node);
                RefRW<LocalTransform> nodeTransform = localTransformLookup.GetRefRW(rootElement.node);
                nodeTransform.ValueRW.Rotation =
                    math.mul(math.inverse(parentWorldRotation), resolvedRotation);
            }
        }

        /// <summary>
        /// Folds a clip's keyed billboard channels into a root's authored settings.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Highest active layer that carries a track for this root wins.</strong> Layer
        /// index is priority — higher composites later and therefore wins (architecture
        /// section 3.1) — so the search runs downward and stops at the first match. A layer with no
        /// track for this root is not an override of anything and is skipped rather than treated as
        /// a neutral value that would blank a lower layer's key.
        /// </para>
        /// <para>
        /// <strong>Nothing here interprets clip data itself.</strong> Every read goes through
        /// <c>ClipSampler</c>, which keeps the single-sampler guarantee of section 5.11 intact — a
        /// second place that decoded keys would be a second sampler, whatever it was called.
        /// </para>
        /// <para>
        /// The angle offset <em>adds</em> to the authored one while the blend weight and the enable
        /// flag <em>replace</em> theirs. That asymmetry is deliberate and matches what each channel
        /// means: an offset is a displacement from a rest orientation, so displacements accumulate;
        /// a weight and a flag are absolute statements about how much billboard applies, and two
        /// absolute statements cannot be added.
        /// </para>
        /// </remarks>
        private void ApplyKeyedChannels(
            ref BillboardSettings settings,
            uint rootId,
            in DynamicBuffer<PlaybackLayer> layers,
            in ClipRegistry clipRegistry)
        {
            if (!clipRegistry.Value.IsCreated)
            {
                return;
            }
            ref ClipRegistryBlob registry = ref clipRegistry.Value.Value;

            for (int layerIndex = layers.Length - 1; layerIndex >= 0; layerIndex--)
            {
                PlaybackLayer layer = layers[layerIndex];
                if ((layer.flags & PlaybackFlags.Active) == 0)
                {
                    continue;
                }
                if (layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
                {
                    continue;
                }

                ref ClipBlob clip = ref registry.clips[layer.clipIndex];
                int trackIndex = FindBillboardTrack(ref clip, rootId);
                if (trackIndex < 0)
                {
                    continue;
                }

                LoopMode resolvedLoop =
                    layer.loop == LoopMode.UseClipDefault ? clip.defaultLoop : layer.loop;
                float normalizedTime =
                    ClipSampler.MapTimeNormalized(layer.time, clip.duration, resolvedLoop);

                float keyedAngleOffset;
                float keyedBlendWeight;
                bool keyedEnabled;
                ClipSampler.SampleBillboardTrack(
                    ref clip.billboardTracks[trackIndex],
                    normalizedTime,
                    out keyedAngleOffset,
                    out keyedBlendWeight,
                    out keyedEnabled);

                settings.angleOffsetRadians += keyedAngleOffset;
                settings.blendWeight = keyedBlendWeight;
                settings.enabled = settings.enabled && keyedEnabled;
                return;
            }
        }

        /// <summary>The clip's billboard track for a root, or −1 when it has none.</summary>
        /// <remarks>
        /// Linear over an array a clip almost always leaves empty, and which holds a handful of
        /// entries when it does not. A binary search over the canonical root-id order would be
        /// correct and slower to read for no measurable gain at these sizes.
        /// </remarks>
        private static int FindBillboardTrack(ref ClipBlob clip, uint rootId)
        {
            for (int trackIndex = 0; trackIndex < clip.billboardTracks.Length; trackIndex++)
            {
                if (clip.billboardTracks[trackIndex].rootId == rootId)
                {
                    return trackIndex;
                }
            }
            return -1;
        }

        /// <summary>
        /// Composes a node's world transform from live local values, walking up the parent chain.
        /// </summary>
        /// <remarks>
        /// Live <c>LocalTransform</c> rather than <c>LocalToWorld</c>, because the pose written
        /// moments ago in this same group has not reached <c>LocalToWorld</c> yet, and because an
        /// ancestor billboard root's freshly written rotation must be visible here — that visibility
        /// is the whole mechanism by which a nested root avoids turning twice.
        /// </remarks>
        private LocalTransform ComputeWorldTransform(Entity node)
        {
            LocalTransform accumulated = localTransformLookup[node];
            Entity walker = node;

            // Bounded by the rig's depth, which is small. A cycle is impossible: Entities' parent
            // hierarchy is a tree by construction.
            while (parentLookup.HasComponent(walker))
            {
                Entity parent = parentLookup[walker].Value;
                if (!localTransformLookup.HasComponent(parent))
                {
                    break;
                }

                LocalTransform parentTransform = localTransformLookup[parent];
                accumulated = new LocalTransform
                {
                    Position = parentTransform.Position
                        + math.mul(parentTransform.Rotation, accumulated.Position * parentTransform.Scale),
                    Rotation = math.mul(parentTransform.Rotation, accumulated.Rotation),
                    Scale = parentTransform.Scale * accumulated.Scale
                };
                walker = parent;
            }
            return accumulated;
        }

        /// <summary>The world rotation of a node's parent, or identity when it has none.</summary>
        private quaternion ComputeParentWorldRotation(Entity node)
        {
            if (!parentLookup.HasComponent(node))
            {
                return quaternion.identity;
            }
            Entity parent = parentLookup[node].Value;
            if (!localTransformLookup.HasComponent(parent))
            {
                return quaternion.identity;
            }
            return ComputeWorldTransform(parent).Rotation;
        }
    }
}
