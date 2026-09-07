// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ClipPreviewController.SampleCompositedPose"/> (Amendment
    /// A71-T2): the Actor Editor's multi-layer entry point must agree with
    /// <see cref="ClipSampler.CompositeLayers"/> and mirror exactly as <c>TransformSampleSystem</c> does.
    /// </summary>
    public sealed class ClipPreviewCompositeTests
    {
        private const float Tolerance = 1e-4f;
        private const uint TargetStableId = 7u;
        private const ulong RigStableId = 0x400UL;
        private const ulong SetStableId = 0x300UL;

        private AuthoringTestAssets testAssets;
        private ClipPreviewController controller;

        [SetUp]
        public void CreateFixture()
        {
            testAssets = new AuthoringTestAssets();
            controller = new ClipPreviewController();
        }

        [TearDown]
        public void DestroyFixture()
        {
            controller.Dispose();
            testAssets.DestroyAll();
        }

        private static ClipAsset CreateRotationClip(
            AuthoringTestAssets testAssets, string assetName, ulong clipStableId, float rotationDegrees)
        {
            ClipAsset clip = testAssets.CreateClip(assetName, clipStableId, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, TargetStableId, TrackBlendOp.Override, AnimatedChannels.Rotation);
            // Both keys carry the same value, so any sampled time reads the same constant rotation
            // and the fixture cannot fail from an interpolation detail this test does not exercise.
            AuthoringTestAssets.AddTransformKey(
                track, 0f, float3.zero, rotationDegrees, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                track, 1f, float3.zero, rotationDegrees, new float3(1f, 1f, 1f), Interpolation.Linear);
            return clip;
        }

        [Test]
        public void SampleCompositedPose_TwoOverrideRotationLayers_MatchesClipSamplerCompositeLayersDirectly()
        {
            RigAsset rig = testAssets.CreateRig("Rig", RigStableId, new uint[] { TargetStableId });
            ClipAsset baseClip = CreateRotationClip(testAssets, "Base", 0x100UL, 10f);
            ClipAsset topClip = CreateRotationClip(testAssets, "Top", 0x200UL, 45f);
            ClipSetAsset clipSet = testAssets.CreateSet("Set", rig, SetStableId, baseClip, topClip);

            controller.SetRig(rig);
            controller.SetClipSets(new ClipSetAsset[] { clipSet });
            Assert.IsTrue(controller.HasRegistry, "A valid rig/set bind must build a registry.");

            BlobAssetReferenceScope expectedScope = new BlobAssetReferenceScope(testAssets);
            try
            {
                expectedScope.Build(clipSet);
                int baseIndex = FindClipIndex(expectedScope, baseClip.Id.Value);
                int topIndex = FindClipIndex(expectedScope, topClip.Id.Value);
                Assert.AreNotEqual(-1, baseIndex);
                Assert.AreNotEqual(-1, topIndex);

                NativeArray<PlaybackLayer> layers = new NativeArray<PlaybackLayer>(2, Allocator.Temp);
                try
                {
                    layers[0] = new PlaybackLayer
                    {
                        clipIndex = baseIndex, loop = LoopMode.UseClipDefault, flags = PlaybackFlags.Active
                    };
                    layers[1] = new PlaybackLayer
                    {
                        clipIndex = topIndex, loop = LoopMode.UseClipDefault, flags = PlaybackFlags.Active
                    };

                    TargetRestPose restPose = new TargetRestPose { scale = new float3(1f, 1f, 1f) };
                    TargetPose expectedPose;
                    ClipSampler.CompositeLayers(
                        ref expectedScope.Registry.Value, in layers, 0, in restPose, false, out expectedPose);

                    Assert.IsTrue(controller.SampleCompositedPose(in layers, false));

                    Transform sampledTransform = controller.ResolveRagdollNode(new RigNodeAddress
                    {
                        kind = RigNodeAddressKind.RigTarget, targetId = TargetStableId
                    });
                    Assert.IsNotNull(sampledTransform, "The mirror must hold a quad for the rig's one target.");

                    float expectedRotationZDegrees = expectedPose.rotation.z * Mathf.Rad2Deg;
                    float rotationAngleDelta = Quaternion.Angle(
                        sampledTransform.localRotation, Quaternion.Euler(0f, 0f, expectedRotationZDegrees));
                    Assert.Less(
                        rotationAngleDelta, 0.1f,
                        "SampleCompositedPose must land on exactly what ClipSampler.CompositeLayers computes.");
                }
                finally
                {
                    layers.Dispose();
                }
            }
            finally
            {
                expectedScope.Dispose();
            }
        }

        [Test]
        public void SampleCompositedPose_MirrorXTrueOnAFacingTarget_NegatesLocalPositionX()
        {
            RigAsset rig = testAssets.CreateRig("Rig", RigStableId, new uint[] { TargetStableId });
            rig.targets[0].facesDirection = true;

            ClipAsset clip = testAssets.CreateClip("Clip", 0x100UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, TargetStableId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                track, 0f, new float3(2f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                track, 1f, new float3(2f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            ClipSetAsset clipSet = testAssets.CreateSet("Set", rig, SetStableId, clip);

            controller.SetRig(rig);
            controller.SetClipSets(new ClipSetAsset[] { clipSet });
            Assert.IsTrue(controller.HasRegistry, "A valid rig/set bind must build a registry.");

            NativeArray<PlaybackLayer> layers = new NativeArray<PlaybackLayer>(1, Allocator.Temp);
            try
            {
                // The set holds exactly one clip, so the registry's one dense clip index is 0 by construction.
                layers[0] = new PlaybackLayer
                {
                    clipIndex = 0, loop = LoopMode.UseClipDefault, flags = PlaybackFlags.Active
                };

                Assert.IsTrue(controller.SampleCompositedPose(in layers, true));
            }
            finally
            {
                layers.Dispose();
            }

            Transform sampledTransform = controller.ResolveRagdollNode(new RigNodeAddress
            {
                kind = RigNodeAddressKind.RigTarget, targetId = TargetStableId
            });
            Assert.IsNotNull(sampledTransform, "The mirror must hold a quad for the rig's one target.");
            Assert.AreEqual(-2f, sampledTransform.localPosition.x, Tolerance);
        }

        private static int FindClipIndex(BlobAssetReferenceScope scope, ulong clipId)
        {
            ref ClipRegistryBlob registryBlob = ref scope.Registry.Value;
            for (int index = 0; index < registryBlob.sortedClipIds.Length; index++)
            {
                if (registryBlob.sortedClipIds[index] == clipId)
                {
                    return index;
                }
            }
            return -1;
        }
    }
}
