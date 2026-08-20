// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>VatMaterialSystem.WriteVatPropertiesJob.TryResolveGlobalFrame</c>'s C10
    /// multi-source VAT track resolution: a part's dense target index is scanned against
    /// <c>ClipBlob.vatTargetRanges</c> first, falling back to the clip-wide
    /// <c>vatFrameStart</c>/<c>vatFrameCount</c>/<c>vatFps</c> range only when no entry names it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PlaybackTestActor.BuildRegistry</c> (the builder <c>VatMaterialSystemTests</c> uses) has
    /// no <c>ClipSpec</c> field for per-target VAT ranges — its <c>FillClip</c> never calls
    /// <c>builder.Allocate</c> against <c>ClipBlob.vatTargetRanges</c>, so every registry it
    /// produces carries an empty array for every clip. <c>TestBlobFactory</c> (the EditMode
    /// builder) has the identical gap. Neither can express the shape under test here, and the only
    /// production code that fills <see cref="VatTrackRangeBlob"/> is
    /// <c>ClipRegistryBuilder.BuildVatTargetRanges</c>, which reads a baked
    /// <c>VatTextureSetAsset.clipRanges</c> and is reachable only through the full
    /// ScriptableObject-asset bake pipeline — far too heavy a rig for a system-behaviour test and a
    /// dependency this fixture must not take on. So this file builds the registry blob directly
    /// with <c>BlobBuilder</c>, mirroring <c>PlaybackTestActor.BuildRegistry</c>'s field-by-field
    /// layout (same schema fields, same id-ascending clip order, same 1-based target ids) with one
    /// addition: an optional <c>VatRangeSpec[]</c> per clip that fills
    /// <see cref="ClipBlob.vatTargetRanges"/>, sorted ascending by dense target index exactly as
    /// <c>ClipRegistryBuilder</c> sorts it. Actors and parts still go through
    /// <see cref="PlaybackTestActor.CreateActor"/> and <see cref="PlaybackTestActor.AddPart"/>
    /// unchanged.
    /// </para>
    /// </remarks>
    public sealed class VatTargetRangeTests
    {
        private const float Tolerance = 1e-4f;
        private const float SentinelFrame = -777f;

        private const ulong MultiClipId = 600;
        private const int MultiClipIndex = 0;

        /// <summary>The clip-wide, untargeted range every pre-C10 clip resolves through.</summary>
        private const int UntargetedFrameStart = 50;
        private const int UntargetedFrameCount = 25;
        private const float UntargetedFps = 24f;

        /// <summary>The torso's own dedicated range on <see cref="MultiClipId"/> — same fps as the
        /// untargeted range, different base row, so only <c>frameStart</c> tells the two apart.</summary>
        private const int TorsoFrameStart = 100;
        private const int TorsoFrameCount = 25;
        private const float TorsoFps = 24f;

        /// <summary>The cape's own dedicated range on <see cref="MultiClipId"/> — different base
        /// row and a different fps, so a fixture reading the wrong block also reads the wrong rate.</summary>
        private const int CapeFrameStart = 200;
        private const int CapeFrameCount = 13;
        private const float CapeFps = 6f;

        private const int TorsoTargetIndex = 0;
        private const int CapeTargetIndex = 1;

        /// <summary>Authoring-side description of one clip in a test registry, extended with the
        /// C10 per-target VAT ranges neither existing test builder can express.</summary>
        private sealed class VatClipSpec
        {
            internal ulong clipId;
            internal float duration = 1f;
            internal LoopMode defaultLoop = LoopMode.Loop;
            internal int vatFrameStart = -1;
            internal int vatFrameCount;
            internal float vatFps;
            internal VatRangeSpec[] targetRanges = Array.Empty<VatRangeSpec>();
        }

        private struct VatRangeSpec
        {
            internal int targetIndex;
            internal int frameStart;
            internal int frameCount;
            internal float fps;
        }

        private static VatRangeSpec Range(int targetIndex, int frameStart, int frameCount, float fps)
        {
            return new VatRangeSpec
            {
                targetIndex = targetIndex,
                frameStart = frameStart,
                frameCount = frameCount,
                fps = fps
            };
        }

        /// <summary>
        /// Builds a registry in the same canonical layout <c>PlaybackTestActor.BuildRegistry</c>
        /// produces (ascending clip-id order, 1-based dense target ids), filling
        /// <see cref="ClipBlob.vatTargetRanges"/> per clip sorted ascending by target index — the
        /// order <c>ClipRegistryBuilder.BuildVatTargetRanges</c> bakes it in.
        /// </summary>
        private static BlobAssetReference<ClipRegistryBlob> BuildRegistry(VatClipSpec[] clipSpecs, int targetCount)
        {
            VatClipSpec[] canonicalSpecs = new VatClipSpec[clipSpecs.Length];
            Array.Copy(clipSpecs, canonicalSpecs, clipSpecs.Length);
            Array.Sort(canonicalSpecs, (firstSpec, secondSpec) => firstSpec.clipId.CompareTo(secondSpec.clipId));

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref ClipRegistryBlob registryRoot = ref builder.ConstructRoot<ClipRegistryBlob>();
                registryRoot.schemaVersion = 4;
                registryRoot.setKey = 1;
                registryRoot.vatSetKey = 1;
                registryRoot.layerCount = 4;

                BlobBuilderArray<ClipBlob> clipArray = builder.Allocate(ref registryRoot.clips, canonicalSpecs.Length);
                BlobBuilderArray<ulong> sortedClipIdArray =
                    builder.Allocate(ref registryRoot.sortedClipIds, canonicalSpecs.Length);
                for (int denseClipIndex = 0; denseClipIndex < canonicalSpecs.Length; denseClipIndex++)
                {
                    VatClipSpec clipSpec = canonicalSpecs[denseClipIndex];
                    ref ClipBlob clip = ref clipArray[denseClipIndex];
                    clip.clipId = clipSpec.clipId;
                    clip.debugName = new FixedString64Bytes("TestClip");
                    clip.duration = clipSpec.duration;
                    clip.defaultLoop = clipSpec.defaultLoop;
                    clip.defaultBlendIn = 0f;
                    clip.defaultBlendOut = 0f;
                    clip.vatFrameStart = clipSpec.vatFrameStart;
                    clip.vatFrameCount = clipSpec.vatFrameCount;
                    clip.vatFps = clipSpec.vatFps;
                    clip.offsetBounds = new AABB { Center = float3.zero, Extents = float3.zero };

                    builder.Allocate(ref clip.transformTracks, 0);
                    builder.Allocate(ref clip.spriteTracks, 0);
                    builder.Allocate(ref clip.events, 0);

                    VatRangeSpec[] sortedRanges = new VatRangeSpec[clipSpec.targetRanges.Length];
                    Array.Copy(clipSpec.targetRanges, sortedRanges, clipSpec.targetRanges.Length);
                    Array.Sort(sortedRanges, (firstRange, secondRange) =>
                        firstRange.targetIndex.CompareTo(secondRange.targetIndex));

                    BlobBuilderArray<VatTrackRangeBlob> rangeArray =
                        builder.Allocate(ref clip.vatTargetRanges, sortedRanges.Length);
                    for (int rangeIndex = 0; rangeIndex < sortedRanges.Length; rangeIndex++)
                    {
                        rangeArray[rangeIndex] = new VatTrackRangeBlob
                        {
                            targetIndex = sortedRanges[rangeIndex].targetIndex,
                            frameStart = sortedRanges[rangeIndex].frameStart,
                            frameCount = sortedRanges[rangeIndex].frameCount,
                            fps = sortedRanges[rangeIndex].fps
                        };
                    }

                    sortedClipIdArray[denseClipIndex] = clipSpec.clipId;
                }

                BlobBuilderArray<uint> sortedTargetIdArray =
                    builder.Allocate(ref registryRoot.sortedTargetIds, targetCount);
                BlobBuilderArray<float3> targetBoundsExtentsArray =
                    builder.Allocate(ref registryRoot.targetBoundsExtents, targetCount);
                BlobBuilderArray<int> targetFramesPerVariantArray =
                    builder.Allocate(ref registryRoot.targetFramesPerVariant, targetCount);
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    sortedTargetIdArray[targetIndex] = (uint)(targetIndex + 1);
                    targetBoundsExtentsArray[targetIndex] = new float3(0.5f, 0.5f, 0.5f);
                    targetFramesPerVariantArray[targetIndex] = 1;
                }

                registryRoot.vatInfo = new VatTextureInfoBlob
                {
                    flavor = VatFlavor.VertexPosition,
                    textureWidth = 0,
                    rowsPerFrame = 1,
                    boneOrVertexCount = 0
                };

                return builder.CreateBlobAssetReference<ClipRegistryBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        private static void ScribbleProperties(World world, Entity partEntity)
        {
            EntityManager entityManager = world.EntityManager;
            entityManager.SetComponentData(partEntity, new VatFrameAProperty { Value = SentinelFrame });
            entityManager.SetComponentData(partEntity, new VatFrameBProperty { Value = SentinelFrame });
            entityManager.SetComponentData(partEntity, new VatBlendProperty { Value = SentinelFrame });
        }

        private static void PlayOnLayer(World world, Entity actor, int layerIndex, ulong clipId, int clipIndex, float playbackTime)
        {
            PlaybackLayer layer = PlaybackTestActor.NewLayer();
            layer.clip = new ClipId(clipId);
            layer.clipIndex = clipIndex;
            layer.time = playbackTime;
            layer.advanceStartTime = playbackTime;
            layer.speed = 1f;
            layer.loop = LoopMode.Loop;
            layer.flags = PlaybackFlags.Active;
            PlaybackTestActor.SetLayer(world, actor, layerIndex, layer);
        }

        private static void RunSystem(World world)
        {
            SystemHandle vatSystem = world.GetOrCreateSystem<VatMaterialSystem>();
            vatSystem.Update(world.Unmanaged);
            world.EntityManager.CompleteAllTrackedJobs();
        }

        /// <summary>
        /// Catches: resolving only the clip-wide range and ignoring
        /// <see cref="ClipBlob.vatTargetRanges"/> entirely. The torso has both an untargeted range
        /// (base row 50) and its own dedicated range (base row 100) on the same clip; if the
        /// dedicated entry is never consulted the part lands on row 62 instead of 112.
        /// </summary>
        [Test]
        public void APartWithADedicatedRange_ResolvesItInsteadOfTheClipWideRange()
        {
            VatClipSpec multiClip = new VatClipSpec
            {
                clipId = MultiClipId,
                duration = 1f,
                defaultLoop = LoopMode.Loop,
                vatFrameStart = UntargetedFrameStart,
                vatFrameCount = UntargetedFrameCount,
                vatFps = UntargetedFps,
                targetRanges = new[] { Range(TorsoTargetIndex, TorsoFrameStart, TorsoFrameCount, TorsoFps) }
            };

            World world = new World("VatTargetRangeTests");
            BlobAssetReference<ClipRegistryBlob> registry = BuildRegistry(new[] { multiClip }, targetCount: 2);
            try
            {
                Entity actor = PlaybackTestActor.CreateActor(world, registry);
                Entity torso = PlaybackTestActor.AddPart(
                    world, actor, TorsoTargetIndex, restPosition: float3.zero, vatDrivingLayerIndex: 0);
                ScribbleProperties(world, torso);
                PlayOnLayer(world, actor, 0, MultiClipId, MultiClipIndex, playbackTime: 0.5f);

                RunSystem(world);

                Assert.AreEqual(
                    TorsoFrameStart + 12f,
                    world.EntityManager.GetComponentData<VatFrameAProperty>(torso).Value,
                    Tolerance,
                    "0.5 s x 24 fps = local frame 12 inside the torso's own dedicated range at row 100, " +
                    "not the clip-wide range at row 50.");
            }
            finally
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
                if (registry.IsCreated)
                {
                    registry.Dispose();
                }
            }
        }

        /// <summary>
        /// Catches: a regression in the fallback rule that every pre-C10 clip depends on. The cape
        /// has no entry in <see cref="ClipBlob.vatTargetRanges"/> for this clip, so it must resolve
        /// the clip-wide untargeted range exactly as it would have before multi-source tracks
        /// existed — not fail to resolve, and not silently pick up the torso's dedicated range.
        /// </summary>
        [Test]
        public void APartWithNoDedicatedRange_FallsBackToTheClipWideRange()
        {
            VatClipSpec multiClip = new VatClipSpec
            {
                clipId = MultiClipId,
                duration = 1f,
                defaultLoop = LoopMode.Loop,
                vatFrameStart = UntargetedFrameStart,
                vatFrameCount = UntargetedFrameCount,
                vatFps = UntargetedFps,
                targetRanges = new[] { Range(TorsoTargetIndex, TorsoFrameStart, TorsoFrameCount, TorsoFps) }
            };

            World world = new World("VatTargetRangeTests");
            BlobAssetReference<ClipRegistryBlob> registry = BuildRegistry(new[] { multiClip }, targetCount: 2);
            try
            {
                Entity actor = PlaybackTestActor.CreateActor(world, registry);
                Entity cape = PlaybackTestActor.AddPart(
                    world, actor, CapeTargetIndex, restPosition: float3.zero, vatDrivingLayerIndex: 0);
                ScribbleProperties(world, cape);
                PlayOnLayer(world, actor, 0, MultiClipId, MultiClipIndex, playbackTime: 0.5f);

                RunSystem(world);

                Assert.AreEqual(
                    UntargetedFrameStart + 12f,
                    world.EntityManager.GetComponentData<VatFrameAProperty>(cape).Value,
                    Tolerance,
                    "The cape names no entry in vatTargetRanges, so it must resolve the clip-wide " +
                    "range at row 50 — the backward-compatibility path every pre-C10 clip relies on.");
            }
            finally
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
                if (registry.IsCreated)
                {
                    registry.Dispose();
                }
            }
        }

        /// <summary>
        /// Catches: cross-talk between two parts' ranges on the same actor and clip — the actual
        /// multi-source scenario this schema version exists for. Torso and cape share one clip and
        /// one driving layer but each carries its own base row and fps; a shared-state bug (e.g. a
        /// cached frameStart/fps not reset per Execute) would leave the second part evaluated to
        /// read the first part's block.
        /// </summary>
        [Test]
        public void TwoPartsOnOneActor_ResolveDifferentRangesOnTheSameClipSimultaneously()
        {
            VatClipSpec multiClip = new VatClipSpec
            {
                clipId = MultiClipId,
                duration = 1f,
                defaultLoop = LoopMode.Loop,
                vatFrameStart = UntargetedFrameStart,
                vatFrameCount = UntargetedFrameCount,
                vatFps = UntargetedFps,
                targetRanges = new[]
                {
                    Range(TorsoTargetIndex, TorsoFrameStart, TorsoFrameCount, TorsoFps),
                    Range(CapeTargetIndex, CapeFrameStart, CapeFrameCount, CapeFps)
                }
            };

            World world = new World("VatTargetRangeTests");
            BlobAssetReference<ClipRegistryBlob> registry = BuildRegistry(new[] { multiClip }, targetCount: 2);
            try
            {
                Entity actor = PlaybackTestActor.CreateActor(world, registry);
                Entity torso = PlaybackTestActor.AddPart(
                    world, actor, TorsoTargetIndex, restPosition: new float3(0f, 0f, 0f), vatDrivingLayerIndex: 0);
                Entity cape = PlaybackTestActor.AddPart(
                    world, actor, CapeTargetIndex, restPosition: new float3(1f, 0f, 0f), vatDrivingLayerIndex: 0);
                ScribbleProperties(world, torso);
                ScribbleProperties(world, cape);
                PlayOnLayer(world, actor, 0, MultiClipId, MultiClipIndex, playbackTime: 0.5f);

                RunSystem(world);

                EntityManager entityManager = world.EntityManager;
                Assert.AreEqual(
                    TorsoFrameStart + 12f,
                    entityManager.GetComponentData<VatFrameAProperty>(torso).Value,
                    Tolerance,
                    "Torso: 0.5 s x 24 fps = local frame 12 inside its own range at row 100.");
                Assert.AreEqual(
                    CapeFrameStart + 3f,
                    entityManager.GetComponentData<VatFrameAProperty>(cape).Value,
                    Tolerance,
                    "Cape: 0.5 s x 6 fps = local frame 3 inside its own range at row 200 — a different " +
                    "base row and a different rate from the torso, evaluated on the same driving layer.");
            }
            finally
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
                if (registry.IsCreated)
                {
                    registry.Dispose();
                }
            }
        }

        /// <summary>
        /// Catches: falling through to the clip-wide range when there is none, instead of failing
        /// resolution outright. This clip has targeted ranges only — no untargeted range at
        /// all (<c>vatFrameStart</c> = -1, the "clip has no VAT range" sentinel) — so a part with no
        /// dedicated entry must publish nothing and zero the blend weight. Reading a neighbouring
        /// target's block here would render one part wearing another's animation, which is exactly
        /// the failure multi-source tracks must not introduce.
        /// </summary>
        [Test]
        public void AClipWithOnlyTargetedRanges_LeavesAnUnmatchedPartUnresolved()
        {
            VatClipSpec targetedOnlyClip = new VatClipSpec
            {
                clipId = MultiClipId,
                duration = 1f,
                defaultLoop = LoopMode.Loop,
                vatFrameStart = -1,
                vatFrameCount = 0,
                vatFps = 0f,
                targetRanges = new[] { Range(TorsoTargetIndex, TorsoFrameStart, TorsoFrameCount, TorsoFps) }
            };

            World world = new World("VatTargetRangeTests");
            BlobAssetReference<ClipRegistryBlob> registry = BuildRegistry(new[] { targetedOnlyClip }, targetCount: 2);
            try
            {
                Entity actor = PlaybackTestActor.CreateActor(world, registry);
                Entity cape = PlaybackTestActor.AddPart(
                    world, actor, CapeTargetIndex, restPosition: float3.zero, vatDrivingLayerIndex: 0);
                ScribbleProperties(world, cape);
                world.EntityManager.SetComponentData(cape, new VatBlendProperty { Value = 0.5f });
                PlayOnLayer(world, actor, 0, MultiClipId, MultiClipIndex, playbackTime: 0.5f);

                RunSystem(world);

                EntityManager entityManager = world.EntityManager;
                Assert.AreEqual(
                    SentinelFrame,
                    entityManager.GetComponentData<VatFrameAProperty>(cape).Value,
                    Tolerance,
                    "No entry names the cape and there is no clip-wide range to fall back to, so the " +
                    "frame must hold rather than resolve to the torso's block at row 100.");
                Assert.AreEqual(
                    0f,
                    entityManager.GetComponentData<VatBlendProperty>(cape).Value,
                    Tolerance,
                    "The weight is forced to 0 exactly as the no-VAT-range path does.");
            }
            finally
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
                if (registry.IsCreated)
                {
                    registry.Dispose();
                }
            }
        }

        /// <summary>
        /// Catches: the targeted and fallback paths diverging in their time mapping — e.g. one
        /// going through <see cref="ClipSampler.MapTime"/> and the other multiplying raw playback
        /// time directly. Both parts play the same clip past its 1 s loop point at the same
        /// playback time; each must wrap through <c>MapTime</c> independently and land on the same
        /// *relative* local frame within its own block (local frame 6 of a 24 fps range), differing
        /// only by their own range's base row.
        /// </summary>
        [Test]
        public void TimeMapping_IsIdenticalOnTheTargetedAndFallbackPaths()
        {
            VatClipSpec multiClip = new VatClipSpec
            {
                clipId = MultiClipId,
                duration = 1f,
                defaultLoop = LoopMode.Loop,
                vatFrameStart = UntargetedFrameStart,
                vatFrameCount = UntargetedFrameCount,
                vatFps = UntargetedFps,
                targetRanges = new[] { Range(TorsoTargetIndex, TorsoFrameStart, TorsoFrameCount, TorsoFps) }
            };

            World world = new World("VatTargetRangeTests");
            BlobAssetReference<ClipRegistryBlob> registry = BuildRegistry(new[] { multiClip }, targetCount: 2);
            try
            {
                Entity actor = PlaybackTestActor.CreateActor(world, registry);

                // Torso resolves the targeted path (its own range); cape resolves the fallback path
                // (no entry names it). Both ranges share the same fps and frame count here so the
                // two base rows are the only difference a correct implementation should produce.
                Entity torso = PlaybackTestActor.AddPart(
                    world, actor, TorsoTargetIndex, restPosition: float3.zero, vatDrivingLayerIndex: 0);
                Entity cape = PlaybackTestActor.AddPart(
                    world, actor, CapeTargetIndex, restPosition: new float3(1f, 0f, 0f), vatDrivingLayerIndex: 1);
                ScribbleProperties(world, torso);
                ScribbleProperties(world, cape);

                // 1.25 s on a 1 s looping clip wraps to 0.25 s -> local frame 6 at 24 fps, exactly
                // the wrap ALoopingClipPastItsEnd_WrapsRatherThanHoldingTheLastFrame pins for the
                // single-source path in VatMaterialSystemTests.
                PlayOnLayer(world, actor, 0, MultiClipId, MultiClipIndex, playbackTime: 1.25f);
                PlayOnLayer(world, actor, 1, MultiClipId, MultiClipIndex, playbackTime: 1.25f);

                RunSystem(world);

                EntityManager entityManager = world.EntityManager;
                float torsoFrame = entityManager.GetComponentData<VatFrameAProperty>(torso).Value;
                float capeFrame = entityManager.GetComponentData<VatFrameAProperty>(cape).Value;

                Assert.AreEqual(
                    TorsoFrameStart + 6f, torsoFrame, Tolerance,
                    "Targeted path: 1.25 s wraps to 0.25 s, local frame 6 inside the torso's own range.");
                Assert.AreEqual(
                    UntargetedFrameStart + 6f, capeFrame, Tolerance,
                    "Fallback path: the same wrap, local frame 6 inside the clip-wide range.");
                Assert.AreEqual(
                    torsoFrame - TorsoFrameStart, capeFrame - UntargetedFrameStart, Tolerance,
                    "Both paths must land on the same relative local frame within their own block — " +
                    "the two ranges differ only by base row, never by how time was mapped.");
            }
            finally
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
                if (registry.IsCreated)
                {
                    registry.Dispose();
                }
            }
        }
    }
}
