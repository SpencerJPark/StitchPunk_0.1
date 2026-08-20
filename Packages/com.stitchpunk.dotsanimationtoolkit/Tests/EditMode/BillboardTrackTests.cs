// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The clip half of amendment A44: billboard tracks through the bake and out of the sampler,
    /// plus validation rule V24. Built from real authored assets rather than hand-made blobs, so the
    /// builder's canonicalisation and the sampler's reading are checked against each other.
    /// </summary>
    public sealed class BillboardTrackTests
    {
        private const uint TargetId = 7u;
        private const uint TorsoRootId = 0x100u;
        private const uint HeadRootId = 0x200u;
        private const ulong RigKey = 0x4401UL;
        private const ulong ClipKey = 0x4402UL;
        private const ulong SetKey = 0x4403UL;
        private const float Tolerance = 1e-5f;

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

        private RigAsset CreateRigWithRoots()
        {
            RigAsset rig = assets.CreateRig("Rig", RigKey, 2, new uint[] { TargetId });
            rig.billboardRoots.Add(new BillboardRootDefinition
            {
                displayName = "Torso",
                stableId = TorsoRootId,
                address = new BillboardNodeAddress
                {
                    kind = BillboardAddressKind.RigTarget,
                    targetId = TargetId
                }
            });
            rig.billboardRoots.Add(new BillboardRootDefinition
            {
                displayName = "Head",
                stableId = HeadRootId,
                address = new BillboardNodeAddress
                {
                    kind = BillboardAddressKind.HierarchyPath,
                    hierarchyPath = "Head"
                }
            });
            return rig;
        }

        private static BillboardTrack AddBillboardTrack(ClipAsset clip, uint rootStableId)
        {
            BillboardTrack track = new BillboardTrack { rootStableId = rootStableId };
            clip.billboardTracks.Add(track);
            return track;
        }

        private static void AddKey(
            BillboardTrack track,
            float normalizedTime,
            float angleOffsetDegrees,
            float blendWeight,
            bool enabled,
            Interpolation interpolation)
        {
            track.keys.Add(new BillboardKey
            {
                normalizedTime = normalizedTime,
                angleOffsetDegrees = angleOffsetDegrees,
                blendWeight = blendWeight,
                enabled = enabled,
                interpolation = interpolation
            });
        }

        private ClipSetAsset BuildSet(RigAsset rig, ClipAsset clip)
        {
            return assets.CreateSet("Set", rig, SetKey, clip);
        }

        private ref BillboardTrackBlob FirstTrack()
        {
            return ref registryScope.Registry.Value.clips[0].billboardTracks[0];
        }

        // -----------------------------------------------------------------------------------
        // Bake.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AClipWithNoBillboardTracksBakesAnEmptyArray()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);

            registryScope.Build(BuildSet(rig, clip));

            Assert.AreEqual(0, registryScope.Registry.Value.clips[0].billboardTracks.Length);
        }

        [Test]
        public void AuthoredDegreesAreBakedAsRadians()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            BillboardTrack track = AddBillboardTrack(clip, TorsoRootId);
            AddKey(track, 0f, 45f, 1f, true, Interpolation.Linear);

            registryScope.Build(BuildSet(rig, clip));

            Assert.AreEqual(math.radians(45f), FirstTrack().keys[0].angleOffsetRadians, Tolerance);
        }

        /// <summary>
        /// Canonical order is ascending root id, so the same clip authored either way round bakes
        /// the same bytes (architecture section 4.5).
        /// </summary>
        [Test]
        public void TracksAreBakedInAscendingRootIdOrder_WhateverOrderTheyWereAuthoredIn()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            AddKey(AddBillboardTrack(clip, HeadRootId), 0f, 0f, 1f, true, Interpolation.Linear);
            AddKey(AddBillboardTrack(clip, TorsoRootId), 0f, 0f, 1f, true, Interpolation.Linear);

            registryScope.Build(BuildSet(rig, clip));

            Assert.AreEqual(TorsoRootId, registryScope.Registry.Value.clips[0].billboardTracks[0].rootId);
            Assert.AreEqual(HeadRootId, registryScope.Registry.Value.clips[0].billboardTracks[1].rootId);
        }

        // -----------------------------------------------------------------------------------
        // Sampling.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AnEmptyTrackResolvesToTheNeutralValues_SoAddingOneIsANoOp()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            AddBillboardTrack(clip, TorsoRootId);

            registryScope.Build(BuildSet(rig, clip));
            ClipSampler.SampleBillboardTrack(
                ref FirstTrack(), 0.5f, out float angleOffset, out float blendWeight, out bool enabled);

            Assert.AreEqual(0f, angleOffset, Tolerance);
            Assert.AreEqual(1f, blendWeight, Tolerance, "Full blend is the neutral value.");
            Assert.IsTrue(enabled, "An empty track must not silently disable the root.");
        }

        [Test]
        public void TheContinuousChannelsInterpolateBetweenKeys()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            BillboardTrack track = AddBillboardTrack(clip, TorsoRootId);
            AddKey(track, 0f, 0f, 1f, true, Interpolation.Linear);
            AddKey(track, 1f, 90f, 0f, true, Interpolation.Linear);

            registryScope.Build(BuildSet(rig, clip));
            ClipSampler.SampleBillboardTrack(
                ref FirstTrack(), 0.5f, out float angleOffset, out float blendWeight, out bool enabled);

            Assert.AreEqual(math.radians(45f), angleOffset, Tolerance);
            Assert.AreEqual(0.5f, blendWeight, Tolerance);
        }

        /// <summary>
        /// Amendment A43's rule, applied to the other discrete channel in the package: an enable flag
        /// is an instruction that fires at a moment, not an approximation of anything between two
        /// moments, so it is held from its key rather than eased.
        /// </summary>
        [Test]
        public void TheEnableFlagIsHeldFromItsKey_AndNeverBlends()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            BillboardTrack track = AddBillboardTrack(clip, TorsoRootId);
            AddKey(track, 0f, 0f, 1f, true, Interpolation.Linear);
            AddKey(track, 1f, 0f, 1f, false, Interpolation.Linear);

            registryScope.Build(BuildSet(rig, clip));

            ClipSampler.SampleBillboardTrack(
                ref FirstTrack(), 0.99f, out float _, out float _, out bool justBeforeTheKey);
            ClipSampler.SampleBillboardTrack(
                ref FirstTrack(), 1f, out float _, out float _, out bool atTheKey);

            Assert.IsTrue(
                justBeforeTheKey,
                "The first key still holds right up to the second key's own time.");
            Assert.IsFalse(atTheKey, "And it changes exactly on that key, not before it.");
        }

        [Test]
        public void StepInterpolationHoldsTheContinuousChannelsToo()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            BillboardTrack track = AddBillboardTrack(clip, TorsoRootId);
            AddKey(track, 0f, 0f, 1f, true, Interpolation.Step);
            AddKey(track, 1f, 90f, 0f, true, Interpolation.Linear);

            registryScope.Build(BuildSet(rig, clip));
            ClipSampler.SampleBillboardTrack(
                ref FirstTrack(), 0.5f, out float angleOffset, out float blendWeight, out bool _);

            Assert.AreEqual(0f, angleOffset, Tolerance);
            Assert.AreEqual(1f, blendWeight, Tolerance);
        }

        [Test]
        public void BeforeTheFirstKey_TheFirstKeyHolds()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            BillboardTrack track = AddBillboardTrack(clip, TorsoRootId);
            AddKey(track, 0.5f, 30f, 0.25f, false, Interpolation.Linear);

            registryScope.Build(BuildSet(rig, clip));
            ClipSampler.SampleBillboardTrack(
                ref FirstTrack(), 0f, out float angleOffset, out float blendWeight, out bool enabled);

            Assert.AreEqual(math.radians(30f), angleOffset, Tolerance);
            Assert.AreEqual(0.25f, blendWeight, Tolerance);
            Assert.IsFalse(enabled);
        }

        [Test]
        public void AnAuthoredBlendWeightOutsideTheUnitRangeIsSaturatedAtBake()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            BillboardTrack track = AddBillboardTrack(clip, TorsoRootId);
            AddKey(track, 0f, 0f, 4f, true, Interpolation.Linear);

            registryScope.Build(BuildSet(rig, clip));

            Assert.AreEqual(1f, FirstTrack().keys[0].blendWeight, Tolerance);
        }

        // -----------------------------------------------------------------------------------
        // Validation rule V24.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V24_FiresWhenATrackNamesNoBillboardRootOfTheRig()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            AddKey(AddBillboardTrack(clip, 0xDEADu), 0f, 0f, 1f, true, Interpolation.Linear);

            AssertContainsCode(
                ClipValidation.ValidateClip(clip), ValidationCode.V24, ValidationSeverity.Error);
        }

        [Test]
        public void V24_FiresForTheReservedZeroRootId()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            AddKey(AddBillboardTrack(clip, 0u), 0f, 0f, 1f, true, Interpolation.Linear);

            AssertContainsCode(
                ClipValidation.ValidateClip(clip), ValidationCode.V24, ValidationSeverity.Error);
        }

        [Test]
        public void V24_DoesNotFireForADeclaredRoot()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            AddKey(AddBillboardTrack(clip, TorsoRootId), 0f, 0f, 1f, true, Interpolation.Linear);

            List<ValidationMessage> messages = ClipValidation.ValidateClip(clip);

            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                Assert.AreNotEqual(ValidationCode.V24, messages[messageIndex].code);
            }
        }

        [Test]
        public void V03_FiresWhenBillboardKeysAreOutOfOrder()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            BillboardTrack track = AddBillboardTrack(clip, TorsoRootId);
            AddKey(track, 0.7f, 0f, 1f, true, Interpolation.Linear);
            AddKey(track, 0.2f, 0f, 1f, true, Interpolation.Linear);

            AssertContainsCode(
                ClipValidation.ValidateClip(clip), ValidationCode.V03, ValidationSeverity.Error);
        }

        [Test]
        public void V04_FiresWhenABillboardKeyLeavesTheUnitTimeRange()
        {
            RigAsset rig = CreateRigWithRoots();
            ClipAsset clip = assets.CreateClip("Clip", rig, ClipKey, 1f);
            AddKey(AddBillboardTrack(clip, TorsoRootId), 1.5f, 0f, 1f, true, Interpolation.Linear);

            AssertContainsCode(
                ClipValidation.ValidateClip(clip), ValidationCode.V04, ValidationSeverity.Error);
        }

        private static void AssertContainsCode(
            IReadOnlyList<ValidationMessage> messages,
            ValidationCode expectedCode,
            ValidationSeverity expectedSeverity)
        {
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                if (messages[messageIndex].code == expectedCode &&
                    messages[messageIndex].severity == expectedSeverity)
                {
                    return;
                }
            }
            Assert.Fail("Expected " + expectedCode + " at severity " + expectedSeverity + ".");
        }
    }
}
