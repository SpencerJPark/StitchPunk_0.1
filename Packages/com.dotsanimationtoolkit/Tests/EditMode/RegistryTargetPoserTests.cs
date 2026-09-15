// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class RegistryTargetPoserTests
    {
        private const float FloatTolerance = 1e-5f;

        private const uint TargetIdA = 3u;
        private const uint TargetIdB = 7u;

        private AuthoringTestAssets assets;
        private RegistryTargetPoser poser;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            poser = new RegistryTargetPoser();
        }

        [TearDown]
        public void TearDown()
        {
            poser.Dispose();
            assets.DestroyAll();
        }

        private sealed class DecliningWriter : RegistryTargetPoser.ITargetPoseWriter
        {
            private readonly TargetRestPose restForTargetA;

            internal readonly List<uint> WrittenTargetIds = new List<uint>();
            internal readonly List<TargetPose> WrittenPoses = new List<TargetPose>();

            internal DecliningWriter(TargetRestPose restForTargetA)
            {
                this.restForTargetA = restForTargetA;
            }

            public bool TryGetRestPose(uint targetId, out TargetRestPose restPose)
            {
                if (targetId == TargetIdB)
                {
                    restPose = default(TargetRestPose);
                    return false;
                }
                restPose = restForTargetA;
                return true;
            }

            public void WritePose(uint targetId, in TargetPose pose)
            {
                WrittenTargetIds.Add(targetId);
                WrittenPoses.Add(pose);
            }
        }

        [Test]
        public void PoseTargets_SkipsATargetTheWriterDeclines_AndWritesTheOthersAsClipSamplerDoes()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, new uint[] { TargetIdA, TargetIdB });
            ClipAsset clip = assets.CreateClip("Walk", 0x10UL, 1f);

            AnimatedChannels positionChannels = AnimatedChannels.PositionXY | AnimatedChannels.PositionZ;
            TransformTrack trackA = AuthoringTestAssets.AddTransformTrack(
                clip, TargetIdA, TrackBlendOp.Override, positionChannels);
            AuthoringTestAssets.AddTransformKey(
                trackA, 0f, new float3(1f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                trackA, 1f, new float3(3f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);

            TransformTrack trackB = AuthoringTestAssets.AddTransformTrack(
                clip, TargetIdB, TrackBlendOp.Override, positionChannels);
            AuthoringTestAssets.AddTransformKey(
                trackB, 0f, new float3(0f, 1f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                trackB, 1f, new float3(0f, 5f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);

            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            RegistryBuildOutcome outcome = poser.Rebuild(rig, new List<ClipSetAsset> { clipSet });
            Assert.AreEqual(RegistryBuildOutcome.Built, outcome, "Fixture must build cleanly.");

            int clipIndex;
            Assert.IsTrue(poser.TryResolveClipIndex(clip.stableId, out clipIndex));

            int targetIndexA = -1;
            ref ClipRegistryBlob registryBlob = ref poser.Registry.Value;
            for (int index = 0; index < registryBlob.sortedTargetIds.Length; index++)
            {
                if (registryBlob.sortedTargetIds[index] == TargetIdA)
                {
                    targetIndexA = index;
                    break;
                }
            }
            Assert.AreNotEqual(-1, targetIndexA, "Target A must be present in the registry.");

            TargetRestPose restForTargetA = new TargetRestPose
            {
                localPosition = new float3(9f, 8f, 7f),
                rotation = new float3(0.1f, 0.2f, 0.3f),
                scale = new float3(2f, 2f, 2f),
                restSliceIndex = 4
            };
            DecliningWriter writer = new DecliningWriter(restForTargetA);

            bool posed = poser.PoseTargets(clip.stableId, 0.5f, writer);

            Assert.IsTrue(posed);
            Assert.AreEqual(1, writer.WrittenTargetIds.Count, "Only the accepted target must be written.");
            Assert.AreEqual(TargetIdA, writer.WrittenTargetIds[0]);

            ref ClipBlob clipBlob = ref registryBlob.clips[clipIndex];
            TargetPose expectedPose;
            ClipSampler.SamplePose(ref clipBlob, targetIndexA, 0.5f, in restForTargetA, out expectedPose);

            TargetPose actualPose = writer.WrittenPoses[0];
            Assert.AreEqual(expectedPose.localPosition.x, actualPose.localPosition.x, FloatTolerance);
            Assert.AreEqual(expectedPose.localPosition.y, actualPose.localPosition.y, FloatTolerance);
            Assert.AreEqual(expectedPose.localPosition.z, actualPose.localPosition.z, FloatTolerance);
            Assert.AreEqual(expectedPose.rotation.x, actualPose.rotation.x, FloatTolerance);
            Assert.AreEqual(expectedPose.rotation.y, actualPose.rotation.y, FloatTolerance);
            Assert.AreEqual(expectedPose.rotation.z, actualPose.rotation.z, FloatTolerance);
            Assert.AreEqual(expectedPose.scale.x, actualPose.scale.x, FloatTolerance);
            Assert.AreEqual(expectedPose.scale.y, actualPose.scale.y, FloatTolerance);
            Assert.AreEqual(expectedPose.scale.z, actualPose.scale.z, FloatTolerance);
            Assert.AreEqual(expectedPose.sliceIndex, actualPose.sliceIndex);
            Assert.AreEqual(expectedPose.atlasRect.x, actualPose.atlasRect.x, FloatTolerance);
            Assert.AreEqual(expectedPose.atlasRect.y, actualPose.atlasRect.y, FloatTolerance);
            Assert.AreEqual(expectedPose.atlasRect.z, actualPose.atlasRect.z, FloatTolerance);
            Assert.AreEqual(expectedPose.atlasRect.w, actualPose.atlasRect.w, FloatTolerance);
        }
    }
}
