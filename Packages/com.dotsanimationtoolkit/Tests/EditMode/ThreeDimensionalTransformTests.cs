// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Pins that transform data is genuinely three-dimensional end to end.
    /// </summary>
    /// <remarks>
    /// The rest of the suite was written when a rotation was one angle and a scale was two numbers,
    /// so almost every fixture in it leaves x and y at zero and would still pass if the new axes
    /// were dropped on the way to the blob. These exercise the axes nothing else touches — which is
    /// the whole point of the change, and therefore the part most worth a test that fails loudly.
    /// </remarks>
    public sealed class ThreeDimensionalTransformTests
    {
        private const float FloatTolerance = 1e-4f;

        private AuthoringTestAssets assets;
        private BlobAssetReferenceScope registryScope;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            registryScope = new BlobAssetReferenceScope();
        }

        [TearDown]
        public void TearDown()
        {
            registryScope.Dispose();
            assets.DestroyAll();
        }

        [Test]
        public void AllThreeRotationAxes_SurviveTheBake_AndConvertToRadians()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = assets.CreateClip("Roll", rig, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.Rotation);

            TransformKey pitchedKey = new TransformKey
            {
                normalizedTime = 0f,
                position = float3.zero,
                rotation = new float3(90f, 45f, -180f),
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            };
            track.keys.Add(pitchedKey);

            registryScope.Build(assets.CreateSet("Set", rig, 2UL, clip));
            ref TransformTrackBlob trackBlob =
                ref registryScope.Registry.Value.clips[0].transformTracks[0];

            Assert.AreEqual(
                math.PI * 0.5f, trackBlob.keys[0].rotation.x, FloatTolerance,
                "A pitch of 90 degrees must reach the blob as radians on the x axis.");
            Assert.AreEqual(math.PI * 0.25f, trackBlob.keys[0].rotation.y, FloatTolerance);
            Assert.AreEqual(-math.PI, trackBlob.keys[0].rotation.z, FloatTolerance);
        }

        [Test]
        public void ScaleZ_SurvivesTheBake()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = assets.CreateClip("Squash", rig, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.Scale);
            AuthoringTestAssets.AddTransformKey(
                track, 0f, float3.zero, 0f, new float3(1f, 2f, 3f), Interpolation.Linear);

            registryScope.Build(assets.CreateSet("Set", rig, 2UL, clip));
            ref TransformTrackBlob trackBlob =
                ref registryScope.Registry.Value.clips[0].transformTracks[0];

            Assert.AreEqual(1f, trackBlob.keys[0].scale.x, FloatTolerance);
            Assert.AreEqual(2f, trackBlob.keys[0].scale.y, FloatTolerance);
            Assert.AreEqual(
                3f, trackBlob.keys[0].scale.z, FloatTolerance,
                "Depth scale is a real channel now, not a constant 1 the bake can drop.");
        }

        [Test]
        public void SamplingInterpolatesEveryRotationAxisIndependently()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = assets.CreateClip("Tumble", rig, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.Rotation | AnimatedChannels.Scale);

            track.keys.Add(new TransformKey
            {
                normalizedTime = 0f,
                position = float3.zero,
                rotation = float3.zero,
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });
            track.keys.Add(new TransformKey
            {
                normalizedTime = 1f,
                position = float3.zero,
                rotation = new float3(90f, 180f, -90f),
                scale = new float3(3f, 5f, 7f),
                interpolation = Interpolation.Linear
            });

            registryScope.Build(assets.CreateSet("Set", rig, 2UL, clip));
            ref TransformTrackBlob trackBlob =
                ref registryScope.Registry.Value.clips[0].transformTracks[0];

            float3 sampledPosition;
            float3 sampledRotation;
            float3 sampledScale;
            ClipSampler.SampleTransformTrack(
                ref trackBlob, 0.5f, out sampledPosition, out sampledRotation, out sampledScale);

            Assert.AreEqual(math.radians(45f), sampledRotation.x, FloatTolerance);
            Assert.AreEqual(math.radians(90f), sampledRotation.y, FloatTolerance);
            Assert.AreEqual(math.radians(-45f), sampledRotation.z, FloatTolerance);
            Assert.AreEqual(2f, sampledScale.x, FloatTolerance);
            Assert.AreEqual(3f, sampledScale.y, FloatTolerance);
            Assert.AreEqual(4f, sampledScale.z, FloatTolerance);
        }

        [Test]
        public void CompositionOffsetsEveryRotationAxisFromRest()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = assets.CreateClip("Lean", rig, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.Rotation);
            track.keys.Add(new TransformKey
            {
                normalizedTime = 0f,
                position = float3.zero,
                rotation = new float3(10f, 20f, 30f),
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });

            registryScope.Build(assets.CreateSet("Set", rig, 2UL, clip));

            // A rest pose tilted on every axis, so "the key replaced rest" and "the key offset from
            // rest" cannot look the same on any component.
            TargetRestPose restPose = new TargetRestPose
            {
                localPosition = float3.zero,
                rotation = new float3(1f, 2f, 3f),
                scale = new float3(1f, 1f, 1f),
                restSliceIndex = 0
            };

            TargetPose pose;
            ClipSampler.SamplePose(
                ref registryScope.Registry.Value.clips[0], 0, 0f, restPose, out pose);

            Assert.AreEqual(1f + math.radians(10f), pose.rotation.x, FloatTolerance);
            Assert.AreEqual(2f + math.radians(20f), pose.rotation.y, FloatTolerance);
            Assert.AreEqual(3f + math.radians(30f), pose.rotation.z, FloatTolerance);
        }

        [Test]
        public void LegacySingleAxisRotation_MigratesOntoTheZAxis()
        {
            // The migration path clips authored before rotation was 3D take. It runs on load and is
            // idempotent, so calling it directly is the same thing deserialization does.
            ClipAsset clip = assets.CreateClip("Legacy", null, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.Rotation);
            track.keys.Add(new TransformKey
            {
                normalizedTime = 0f,
                position = float3.zero,
                rotationZ = -35f,
                rotation = float3.zero,
                scale = new float3(1f, 1f, 0f),
                interpolation = Interpolation.Linear
            });

            clip.OnAfterDeserialize();

            Assert.AreEqual(-35f, track.keys[0].rotation.z, FloatTolerance,
                "The legacy z angle must become the z component rather than being dropped.");
            Assert.AreEqual(0f, track.keys[0].rotationZ, FloatTolerance,
                "And the legacy field is consumed, so a later 3D edit is not overwritten by it.");
            Assert.AreEqual(1f, track.keys[0].scale.z, FloatTolerance,
                "A z scale of 0 is unmigrated 2D data, not a deliberately collapsed part.");
        }

        [Test]
        public void MigrationLeavesAGenuineThreeDimensionalRotationAlone()
        {
            ClipAsset clip = assets.CreateClip("Modern", null, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.Rotation);
            track.keys.Add(new TransformKey
            {
                normalizedTime = 0f,
                position = float3.zero,

                // Both set: a stale legacy value beside a real 3D one. The 3D value wins, or a clip
                // edited after migrating would snap back the next time it loaded.
                rotationZ = 99f,
                rotation = new float3(11f, 22f, 33f),
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });

            clip.OnAfterDeserialize();

            Assert.AreEqual(11f, track.keys[0].rotation.x, FloatTolerance);
            Assert.AreEqual(22f, track.keys[0].rotation.y, FloatTolerance);
            Assert.AreEqual(33f, track.keys[0].rotation.z, FloatTolerance);
        }
    }
}
