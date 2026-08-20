// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Samples every visible actor's layers into its parts' <see cref="TargetPose"/>
    /// (architecture section 5.6). The first half of the transform technique; the second half is
    /// <c>TransformApplySystem</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It composites nothing itself.</strong> All of the actual sampling — bottom-up layer
    /// order, channel masks, additive-over-composited, crossfade lerping, loop-mode time mapping —
    /// lives in <see cref="ClipSampler.CompositeLayers"/>, and this system's whole job is to decide
    /// <em>which</em> actors and parts to call it for. That is section 5.11 as a structural
    /// guarantee rather than a convention: the divergence between an editor sampler and a runtime
    /// sampler is the defect the source audit found in the host game (§3.4), and it is
    /// unrepresentable when there is only one function.
    /// </para>
    /// <para>
    /// <strong>Gated on <see cref="AnimVisible"/>, unlike the logic group.</strong> An off-screen
    /// actor keeps exact time and keeps firing events — it simply does not compute poses nobody can
    /// see. Re-appearing needs no dirty tracking: this runs every frame for every visible actor, so
    /// the first visible frame re-samples everything from scratch.
    /// </para>
    /// <para>
    /// <strong>Sample-rate quantization is per actor, phase-offset per instance.</strong> An actor
    /// with a positive rate samples only on frames where its phase-offset sample index advances
    /// (<see cref="ClipSampler.ShouldSample"/>), with <see cref="SampleSettings.phase01"/> spreading
    /// a crowd across frames instead of spiking on one. Playback <em>time</em> is never quantized —
    /// only how often it is read.
    /// </para>
    /// <para>
    /// <strong>LOD arrives here as three separate effects, not one</strong> (§5.10, via
    /// <see cref="AnimationLodPolicy"/>): it scales the effective sample rate, it snaps crossfade
    /// weights from level 2, and it freezes the pose from level 3 until the actor's clips change.
    /// All three are presentation-only — nothing below touches a timer or an event, so a
    /// distant actor stays frame-accurate to the simulation and merely looks cheaper.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(AnimLodDistanceSystem))]
    [BurstCompile]
    public partial struct TransformSampleSystem : ISystem
    {
        /// <summary>
        /// World elapsed time at the previous update, the other edge of the quantization interval.
        /// </summary>
        /// <remarks>
        /// System state rather than a component: the interval is a property of the frame, and every
        /// actor's own contribution to the decision is already carried by its rate and phase.
        /// </remarks>
        private float previousElapsedTime;

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
            // to not sampling at all. Zero is the "every frame" value, so the default is the safe one.
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
                targetPoseLookup = SystemAPI.GetComponentLookup<TargetPose>()
            };
            state.Dependency = sampleJob.ScheduleParallel(state.Dependency);

            previousElapsedTime = currentElapsedTime;
        }
    }

    /// <summary>
    /// Composites one actor's layers into each of its bound parts' poses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Parallel safety.</strong> Parts are reached through
    /// <see cref="RigPartRef"/>, which <c>RigBindingSystem</c> rebuilds per instance so that it
    /// lists only this actor's own children. An entity belongs to exactly one actor, so no two
    /// workers can write the same <see cref="TargetPose"/> — that is what makes the
    /// <see cref="NativeDisableParallelForRestrictionAttribute"/> a claim about the data rather than
    /// a suppression of the check.
    /// </para>
    /// </remarks>
    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct SampleActorPosesJob : IJobEntity
    {
        public float previousElapsedTime;
        public float currentElapsedTime;
        public float defaultSampleRateHz;

        /// <summary>
        /// A lookup rather than an <c>in</c> parameter because <see cref="AnimLod"/> is opt-in and
        /// its absence is conformant (amendment A23). Taking it as a parameter would quietly
        /// restrict this job to the actors that enabled distance LOD — which is most of them
        /// excluded, sampling nothing, with no error anywhere.
        /// </summary>
        [ReadOnly] public ComponentLookup<AnimLod> animLodLookup;

        [ReadOnly] public ComponentLookup<TargetRestPose> restPoseLookup;

        /// <summary>
        /// A lookup rather than an <c>in</c> parameter for the same reason as
        /// <see cref="animLodLookup"/>: <see cref="PartFacing"/> is opt-in (amendment A37, on
        /// the A23 precedent), so taking it as a parameter would restrict this job to parts that
        /// face somewhere — excluding every ordinary part, silently.
        /// </summary>
        [ReadOnly] public ComponentLookup<PartFacing> partFacingLookup;

        /// <summary>Written on part entities, never on the actor being iterated — see the type-level remarks.</summary>
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
            float effectiveRateHz = AnimationLodPolicy.EffectiveSampleRateHz(lodLevel, requestedRateHz);
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
            // signature entirely, which is what lets an actor walking toward the camera resume on
            // the ordinary rate rule rather than waiting for a clip change that may never come.
            // The signature is nonetheless recorded at every level so that raising the level does
            // not begin with a spurious extra sample.
            int clipSignature = ComputeClipSignature(in layerArray);
            if (AnimationLodPolicy.FreezesPose(lodLevel) && clipSignature == sampleState.sampledClipSignature)
            {
                return;
            }
            sampleState.sampledClipSignature = clipSignature;

            bool snapBlendWeights = AnimationLodPolicy.SnapsBlendWeights(lodLevel);

            for (int partRefIndex = 0; partRefIndex < partRefs.Length; partRefIndex++)
            {
                RigPartRef partRef = partRefs[partRefIndex];

                // targetIndex stays −1 for a part whose target id never resolved against the rig
                // (RigTargetBaker leaves it so deliberately, rather than defaulting to 0 and
                // animating the wrong part). Such a part holds its rest pose forever, which is the
                // intended inert behaviour.
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

                // The facing terms (amendment A37), applied after composition so that no clip on any
                // layer can outrank them. Skipped entirely for a part that never opted in, which
                // keeps the ordinary case exactly as it was.
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

                    // A mirror reflects the whole part about the actor's vertical axis, which is
                    // three negations rather than one:
                    //
                    //   position.x — the plane moves to the other side. An ear sits left of the
                    //                head at rest; mirrored, it belongs on the right. Flipping only
                    //                the art leaves it pinned to the wrong side of the skull.
                    //   rotation   — rotation is handed. An arm swung +30° reflects to −30°;
                    //                leaving it alone makes a mirrored pose lean the wrong way.
                    //                Reflecting about x negates the y and z angles and leaves the
                    //                x angle alone: a roll about the mirror axis survives a
                    //                reflection, a yaw and a pitch reverse.
                    //   scale.x    — the art itself, so the drawn shape faces the other way.
                    //
                    // All are negations rather than assignments, so a part authored already
                    // offset, rotated or flipped composes with facing instead of being overridden.
                    if (partFacing.mirrorX)
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

        /// <summary>
        /// Folds which clips an actor's layers are showing into one comparable int.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Order-sensitive and cheap, deliberately. It is only ever compared against the previous
        /// frame's value, never decoded, and the cost of a collision is one skipped re-sample on an
        /// actor already frozen at LOD 3 — so a real hash would buy accuracy nobody could perceive
        /// at a price paid every frame by every actor.
        /// </para>
        /// <para>
        /// The crossfade source is folded in as well as the current clip: a blend that has just
        /// begun shows both, and treating a layer as unchanged while its outgoing clip swapped
        /// would hold the wrong pose. An inactive layer contributes a constant, so deactivating one
        /// counts as a change.
        /// </para>
        /// </remarks>
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
