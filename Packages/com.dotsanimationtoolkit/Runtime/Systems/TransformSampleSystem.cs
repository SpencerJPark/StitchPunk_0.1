// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>AnimLodDistanceSystem</c>: samples every visible actor's layers into its
    /// parts' <see cref="TargetPose"/>. Gated on <see cref="AnimVisible"/>, unlike the logic group.
    /// All sampling lives in <see cref="ClipSampler.CompositeLayers"/> — this system only decides
    /// which actors and parts to call it for.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(AnimLodDistanceSystem))]
    [BurstCompile]
    public partial struct TransformSampleSystem : ISystem
    {
        private float previousElapsedTime; // system state, not a component: the interval is a property of the frame, not of any one actor

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TargetPose>();
            previousElapsedTime = 0f;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float currentElapsedTime = (float)SystemAPI.Time.ElapsedTime;

            // The config singleton is created by ConfigBootstrapSystem, which lives in a different
            // group; a world that has not run it yet falls back to sampling every frame rather than
            // to not sampling at all.
            float defaultSampleRateHz = 0f;
            if (SystemAPI.TryGetSingleton(out AnimationToolkitConfig toolkitConfig))
            {
                defaultSampleRateHz = toolkitConfig.defaultSampleRateHz;
            }

            SampleActorPosesJob sampleJob = new SampleActorPosesJob
            {
                previousElapsedTime = previousElapsedTime,
                currentElapsedTime = currentElapsedTime,
                defaultSampleRateHz = defaultSampleRateHz,
                animLodLookup = SystemAPI.GetComponentLookup<AnimLod>(true),
                restPoseLookup = SystemAPI.GetComponentLookup<TargetRestPose>(true),
                partFacingLookup = SystemAPI.GetComponentLookup<PartFacing>(true),
                mirrorFromAncestorLookup = SystemAPI.GetComponentLookup<PartMirrorFromAncestor>(true),
                targetPoseLookup = SystemAPI.GetComponentLookup<TargetPose>()
            };
            state.Dependency = sampleJob.ScheduleParallel(state.Dependency);

            previousElapsedTime = currentElapsedTime;
        }
    }

    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct SampleActorPosesJob : IJobEntity
    {
        public float previousElapsedTime;
        public float currentElapsedTime;
        public float defaultSampleRateHz;

        // A lookup, not an `in` parameter: AnimLod is opt-in, and taking it as a parameter would
        // quietly restrict this job to actors that enabled distance LOD, excluding most of them
        // from sampling with no error anywhere.
        [ReadOnly] public ComponentLookup<AnimLod> animLodLookup;

        [ReadOnly] public ComponentLookup<TargetRestPose> restPoseLookup;

        // Same reason as animLodLookup: PartFacing is opt-in, so taking it as a parameter would
        // silently exclude every ordinary part that doesn't face anywhere.
        [ReadOnly] public ComponentLookup<PartFacing> partFacingLookup;

        [ReadOnly] public ComponentLookup<PartMirrorFromAncestor> mirrorFromAncestorLookup;

        [NativeDisableParallelForRestriction] public ComponentLookup<TargetPose> targetPoseLookup;

        private void Execute(
            Entity actorEntity,
            in DynamicBuffer<RigPartRef> partRefs,
            in DynamicBuffer<PlaybackLayer> layers,
            in ClipRegistry clipRegistry,
            in SampleSettings sampleSettings,
            ref AnimSampleState sampleState)
        {
            byte lodLevel = 0;
            if (animLodLookup.HasComponent(actorEntity))
            {
                lodLevel = animLodLookup[actorEntity].level;
            }

            float requestedRateHz = sampleSettings.rateHz > 0f ? sampleSettings.rateHz : defaultSampleRateHz;
            float effectiveRateHz = AnimationLodResolver.EffectiveSampleRateHz(lodLevel, requestedRateHz);
            if (!ClipSampler.ShouldSample(
                    previousElapsedTime, currentElapsedTime, effectiveRateHz, sampleSettings.phase01))
            {
                return;
            }

            // Locals first: both are `in` parameters, and reaching through them to a ref-returning
            // property or a non-readonly instance method is the construct that compiles into a
            // defensive copy.
            BlobAssetReference<ClipRegistryBlob> registryReference = clipRegistry.Value;
            ref ClipRegistryBlob registry = ref registryReference.Value;

            DynamicBuffer<PlaybackLayer> layerBuffer = layers;
            NativeArray<PlaybackLayer> layerArray = layerBuffer.AsNativeArray();

            // LOD 3 holds the last pose until the clips change; every lower level ignores the
            // signature entirely, so an actor walking back into view resumes the ordinary rate rule
            // rather than waiting for a clip change that may never come. Recorded at every level
            // regardless, so raising the level doesn't begin with a spurious extra sample.
            int clipSignature = ComputeClipSignature(in layerArray);
            if (AnimationLodResolver.FreezesPose(lodLevel) && clipSignature == sampleState.sampledClipSignature)
            {
                return;
            }
            sampleState.sampledClipSignature = clipSignature;

            bool snapBlendWeights = AnimationLodResolver.SnapsBlendWeights(lodLevel);

            for (int partRefIndex = 0; partRefIndex < partRefs.Length; partRefIndex++)
            {
                RigPartRef partRef = partRefs[partRefIndex];

                // targetIndex stays -1 for a part whose target id never resolved against the rig
                // (deliberate, rather than defaulting to 0 and animating the wrong part); such a
                // part holds its rest pose forever.
                if (partRef.targetIndex < 0)
                {
                    continue;
                }
                if (!restPoseLookup.HasComponent(partRef.part) || !targetPoseLookup.HasComponent(partRef.part))
                {
                    continue;
                }

                TargetRestPose restPose = restPoseLookup[partRef.part];
                ClipSampler.CompositeLayers(
                    ref registry,
                    in layerArray,
                    partRef.targetIndex,
                    in restPose,
                    snapBlendWeights,
                    out TargetPose sampledPose);

                // Facing terms, applied after composition so that no clip on any layer can outrank
                // them. Skipped entirely for a part that never opted in.
                if (partFacingLookup.HasComponent(partRef.part))
                {
                    PartFacing partFacing = partFacingLookup[partRef.part];

                    // An alt view is a different frame: an ear from behind is different art.
                    int framesPerVariant = partRef.targetIndex < registry.targetFramesPerVariant.Length
                        ? registry.targetFramesPerVariant[partRef.targetIndex]
                        : 1;
                    sampledPose.sliceIndex = ClipSampler.ResolveViewSlice(
                        sampledPose.sliceIndex,
                        restPose.restSliceIndex,
                        partFacing.viewOffset,
                        framesPerVariant);

                    // A mirror reflects the whole part about the actor's vertical axis: position.x
                    // (the plane moves to the other side), rotation.y/z (rotation is handed — a roll
                    // about the mirror axis survives, yaw and pitch reverse), and scale.x (the art
                    // itself faces the other way). All are negations, not assignments, so a part
                    // authored already offset/rotated/flipped composes with facing rather than being
                    // overridden. Skipped under a mirrored ancestor: scale composes down the
                    // hierarchy, so the ancestor's negated x already reflects this part, and
                    // negating again would cancel it back to unmirrored.
                    if (partFacing.mirrorX && !mirrorFromAncestorLookup.HasComponent(partRef.part))
                    {
                        sampledPose.localPosition.x = -sampledPose.localPosition.x;
                        sampledPose.rotation.y = -sampledPose.rotation.y;
                        sampledPose.rotation.z = -sampledPose.rotation.z;
                        sampledPose.scale.x = -sampledPose.scale.x;
                    }
                }

                targetPoseLookup[partRef.part] = sampledPose;
            }
        }

        // Order-sensitive and cheap, deliberately: compared only against the previous frame's
        // value, never decoded, so a collision just costs one skipped re-sample on an actor already
        // frozen at LOD 3. The crossfade source is folded in too — a blend that has just begun shows
        // both clips, and ignoring the outgoing one would hold the wrong pose when it changes. An
        // inactive layer contributes a constant, so deactivating one still counts as a change.
        private static int ComputeClipSignature(in NativeArray<PlaybackLayer> layers)
        {
            int signature = 17;
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];
                bool isActive = (layer.flags & PlaybackFlags.Active) != 0;
                signature = signature * 31 + (isActive ? layer.clipIndex : -1);
                bool isBlending = isActive && (layer.flags & PlaybackFlags.Blending) != 0;
                signature = signature * 31 + (isBlending ? layer.previousClipIndex : -1);
            }
            return signature;
        }
    }
}
