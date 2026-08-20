// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// One fixture per rule of the architecture section 3.5 table, as module M1's acceptance list
    /// requires. Each fixture starts from a set that validates clean and breaks exactly one thing,
    /// then asserts that the rule under test fires <em>and that nothing else does</em> — so a rule
    /// cannot pass by accident on a fixture that is broken in several ways at once.
    /// </summary>
    public sealed class ClipValidationTests
    {
        private const uint FirstTargetId = 7u;
        private const uint SecondTargetId = 3u;
        private const ulong WalkClipId = 0x100UL;
        private const ulong RunClipId = 0x200UL;
        private const ulong SetKey = 0x300UL;
        private const ulong RigKey = 0x400UL;
        private const ulong VatSetKey = 0x500UL;
        private const float FloatTolerance = 1e-6f;

        private AuthoringTestAssets assets;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
        }

        [TearDown]
        public void TearDown()
        {
            assets.DestroyAll();
        }

        // -----------------------------------------------------------------------------------
        // Baseline.
        // -----------------------------------------------------------------------------------

        private RigAsset CreateValidRig()
        {
            return assets.CreateRig("Rig", RigKey, 2, new uint[] { FirstTargetId, SecondTargetId });
        }

        private ClipAsset CreateValidClip(RigAsset rig, string assetName, ulong clipId)
        {
            ClipAsset clip = assets.CreateClip(assetName, rig, clipId, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, FirstTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                track, 0f, new float3(0f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                track, 1f, new float3(1f, 2f, 0f), 90f, new float3(2f, 1f, 1f), Interpolation.EaseInOut);
            AuthoringTestAssets.AddEvent(clip, 0.5f, 16u, 4, 0.25f);
            return clip;
        }

        private ClipSetAsset CreateValidSet(out RigAsset rig, out ClipAsset clip)
        {
            rig = CreateValidRig();
            clip = CreateValidClip(rig, "Walk", WalkClipId);
            return assets.CreateSet("Set", rig, SetKey, clip);
        }

        /// <summary>
        /// A VAT source that genuinely opts a clip into VAT, i.e. one that names a clip for the
        /// texture baker to sample.
        /// </summary>
        /// <remarks>
        /// The <c>sourceClip</c> is what makes it real (amendment A36). A bare
        /// <c>new VatClipSource()</c> is the shape Unity's serializer produces for a clip that never
        /// opted in at all, so using one here would assert V07 against the case that must *not*
        /// raise it.
        /// </remarks>
        private static VatClipSource NewVatSource()
        {
            VatClipSource vatSource = new VatClipSource();
            vatSource.sourceClip = new AnimationClip();
            return vatSource;
        }

        [Test]
        public void AValidSet_ProducesNoFindingsAtAll()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);

            List<ValidationMessage> messages = ClipValidation.ValidateSet(clipSet);

            Assert.IsEmpty(messages, "The baseline fixture must validate clean: " + Describe(messages));
        }

        // -----------------------------------------------------------------------------------
        // V01 - V04: per-clip data.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V01_FiresWhenTheClipDurationIsBelowTheOneMillisecondFloor()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clip.duration = 0.0005f;
            // Blend defaults would otherwise exceed the shortened duration and add a V12 warning.
            clip.defaultBlendIn = 0f;
            clip.defaultBlendOut = 0f;

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V01, ValidationSeverity.Error);
        }

        [Test]
        public void V02_FiresWhenATrackTargetsAnIdTheRigDoesNotDeclare()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clip.transformTracks[0].targetId = 999u;

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V02, ValidationSeverity.Error);
        }

        [Test]
        public void V03_FiresWhenTrackKeysAreNotStrictlyTimeSorted()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            // Keys become 0, 1, 0.5 - all inside [0, 1], so only the ordering rule can fire.
            AuthoringTestAssets.AddTransformKey(
                clip.transformTracks[0], 0.5f, new float3(0f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V03, ValidationSeverity.Error);
        }

        [Test]
        public void V04_FiresWhenAKeyTimeLeavesTheZeroToOneRange()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            // Appended after 1, so the track stays strictly ascending and only the range rule fires.
            AuthoringTestAssets.AddTransformKey(
                clip.transformTracks[0], 1.5f, new float3(0f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V04, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // V05: id uniqueness, in both scopes the rule covers.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V05_FiresWhenTwoClipsInOneSetShareAClipId()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset walkClip = CreateValidClip(rig, "Walk", WalkClipId);
            ClipAsset runClip = CreateValidClip(rig, "Run", WalkClipId);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, walkClip, runClip);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V05, ValidationSeverity.Error);
        }

        [Test]
        public void V05_FiresWhenTwoTargetRowsInOneRigShareATargetId()
        {
            RigAsset rig = assets.CreateRig("Rig", RigKey, 2, new uint[] { FirstTargetId, FirstTargetId });
            ClipAsset clip = CreateValidClip(rig, "Walk", WalkClipId);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, clip);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V05, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // V06 - V08: set-scoped references.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V06_FiresWhenAClipIsAuthoredAgainstADifferentRigThanItsSet()
        {
            RigAsset setRig = CreateValidRig();
            // The other rig declares the same targets, so only the rig-identity rule can fire.
            RigAsset otherRig = assets.CreateRig(
                "OtherRig", RigKey + 1UL, 2, new uint[] { FirstTargetId, SecondTargetId });
            ClipAsset clip = CreateValidClip(otherRig, "Walk", WalkClipId);
            ClipSetAsset clipSet = assets.CreateSet("Set", setRig, SetKey, clip);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V06, ValidationSeverity.Error);
        }

        [Test]
        public void V07_FiresWhenAVatSourcedClipHasNoTextureSetAtAll()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clip.vatSource = NewVatSource();

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V07, ValidationSeverity.Error);
        }

        /// <summary>
        /// <strong>Amendment A36.</strong> A clip that never opted into VAT must not trip V07 just
        /// because it has been saved to disk once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Catches: testing <c>clip.vatSource</c> for null to decide whether a clip is VAT-sourced.
        /// <c>vatSource</c> is a plain <c>[Serializable]</c> class field, and Unity cannot serialize
        /// null for one — it writes a default block and materialises a non-null instance on load. So
        /// every clip asset in a real project reads as VAT-sourced, V07 fires on any set without a
        /// texture set, <c>ClipRegistryBuilder</c> throws, no registry is baked, and every actor in
        /// the game stands still.
        /// </para>
        /// <para>
        /// The whole suite missed this because every other fixture builds clips with
        /// <c>ScriptableObject.CreateInstance</c> and never writes one to disk, where the field
        /// genuinely is null. This fixture reproduces the deserialized shape directly by assigning
        /// the empty source the serializer would have produced — the cheap half of what the host
        /// repo's smoke scene found by building real, saved assets.
        /// </para>
        /// </remarks>
        [Test]
        public void V07_DoesNotFireForAnEmptyVatSource_WhichIsWhatDeserializationProduces()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);

            // Exactly what Unity hands back for a clip authored without VAT: present, but naming
            // nothing for the texture baker to sample.
            clip.vatSource = new VatClipSource();

            Assert.IsEmpty(
                ClipValidation.ValidateSet(clipSet),
                "A clip that names no source clip has no VAT intent, so a set with no texture set "
                + "is complete as authored.");
        }

        [Test]
        public void V07_FiresWhenTheTextureSetHasNoRangeForAVatSourcedClip()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clip.vatSource = NewVatSource();
            VatTextureSetAsset vatTextureSet = assets.CreateVatTextureSet("VatSet", VatSetKey);
            vatTextureSet.clipRanges.Add(new VatClipRange
            {
                clipId = RunClipId,
                frameStart = 0,
                frameCount = 4,
                fps = 30f
            });
            clipSet.vatTextures = vatTextureSet;

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V07, ValidationSeverity.Error);
        }

        [Test]
        public void V08_FiresAsAnErrorWhileAuthoring_WhenTheTextureSetSourceHashHasMoved()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clipSet.vatTextures = assets.CreateVatTextureSet("VatSet", VatSetKey);

            List<ValidationMessage> messages = ClipValidation.ValidateSet(
                clipSet,
                ValidationStage.Authoring,
                true,
                clipSet.vatTextures.sourceHash + 1UL);

            AssertOnlyCode(messages, ValidationCode.V08, ValidationSeverity.Error);
        }

        [Test]
        public void V08_DowngradesToAWarningAtBakeTime_BecauseStaleTexturesStillRender()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clipSet.vatTextures = assets.CreateVatTextureSet("VatSet", VatSetKey);

            List<ValidationMessage> messages = ClipValidation.ValidateSet(
                clipSet,
                ValidationStage.Bake,
                true,
                clipSet.vatTextures.sourceHash + 1UL);

            AssertOnlyCode(messages, ValidationCode.V08, ValidationSeverity.Warning);
        }

        [Test]
        public void V08_StaysSilentWhenTheSourceHashWasNotRecomputed()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clipSet.vatTextures = assets.CreateVatTextureSet("VatSet", VatSetKey);

            List<ValidationMessage> messages = ClipValidation.ValidateSet(clipSet);

            Assert.IsEmpty(
                messages,
                "Without a recomputed hash there is nothing to compare, so V08 must not guess: " +
                Describe(messages));
        }

        // -----------------------------------------------------------------------------------
        // V09 - V14: events, empties, duplicates, blends, layers, slices.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V09_FiresWhenAnEventUsesAKeyReservedByThePackage()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            EventMarker reservedMarker = clip.events[0];
            reservedMarker.eventKey = (uint)ReservedEventKeys.ClipFinished;
            clip.events[0] = reservedMarker;

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V09, ValidationSeverity.Error);
        }

        [Test]
        public void V19_FiresWhenAnEventWindowIsNegative()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            EventMarker negativeWindowMarker = clip.events[0];
            negativeWindowMarker.windowSeconds = -0.25f;
            clip.events[0] = negativeWindowMarker;

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V19, ValidationSeverity.Error);
        }

        [Test]
        public void V20_FiresAsAWarningWhenAWindowIsAuthoredOnAPulseOnlyKey()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            EventMarker unmaskableMarker = clip.events[0];
            unmaskableMarker.eventKey = AnimEventMaskKeys.LastMaskKey + 1u;
            unmaskableMarker.windowSeconds = 0.25f;
            clip.events[0] = unmaskableMarker;

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V20, ValidationSeverity.Warning);
        }

        [Test]
        public void AWindowOnAMaskableKey_IsClean()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            EventMarker windowedMarker = clip.events[0];
            windowedMarker.eventKey = AnimEventMaskKeys.FirstMaskKey;
            windowedMarker.windowSeconds = 0.25f;
            clip.events[0] = windowedMarker;

            Assert.IsEmpty(
                ClipValidation.ValidateSet(clipSet),
                "A window on a key that owns a mask bit is the ordinary case and must not warn.");
        }

        [Test]
        public void APulseOnlyKeyWithoutAWindow_IsClean()
        {
            // The key is outside the maskable range but authors no window, so there is nothing
            // inert about it — V20 must not fire merely for using a high key.
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            EventMarker highKeyMarker = clip.events[0];
            highKeyMarker.eventKey = AnimEventMaskKeys.LastMaskKey + 1u;
            highKeyMarker.windowSeconds = 0f;
            clip.events[0] = highKeyMarker;

            Assert.IsEmpty(
                ClipValidation.ValidateSet(clipSet),
                "A pulse-only key with no window is legal: " + Describe(ClipValidation.ValidateSet(clipSet)));
        }

        [Test]
        public void V10_FiresAsAWarningForAClipWithNoTracksAndNoEvents()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset emptyClip = assets.CreateClip("Empty", rig, WalkClipId, 1f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, emptyClip);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V10, ValidationSeverity.Warning);
        }

        [Test]
        public void V11_FiresAsAWarningWhenOneClipIsListedTwiceInASet()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset clip = CreateValidClip(rig, "Walk", WalkClipId);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, clip, clip);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V11, ValidationSeverity.Warning);
        }

        [Test]
        public void V12_FiresAsAWarningWhenABlendDefaultOutlastsTheClip()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clip.defaultBlendIn = 2f;

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V12, ValidationSeverity.Warning);
        }

        [Test]
        public void V13_FiresWhenTheRigDeclaresNoLayers()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            rig.layers.Clear();

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V13, ValidationSeverity.Error);
        }

        [Test]
        public void V13_FiresWhenTheRigDeclaresMoreThanEightLayers()
        {
            RigAsset rig = assets.CreateRig(
                "Rig", RigKey, RigAsset.MaxLayerCount + 1, new uint[] { FirstTargetId, SecondTargetId });
            ClipAsset clip = CreateValidClip(rig, "Walk", WalkClipId);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, clip);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V13, ValidationSeverity.Error);
        }

        [Test]
        public void V13_FiresWhenTheSetHasNoRigAtAll()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset clip = CreateValidClip(rig, "Walk", WalkClipId);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, clip);
            clipSet.rig = null;
            clip.rig = null;

            List<ValidationMessage> messages = ClipValidation.ValidateSet(clipSet);

            // With no rig the clip's own track can no longer name a target either, so V02 rides
            // along; the point of this fixture is that a missing rig is reported, not swallowed.
            AssertContainsCode(messages, ValidationCode.V13, ValidationSeverity.Error);
        }

        [Test]
        public void V14_FiresAsAWarningForASliceIndexBelowMinusOne()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            SpriteTrack spriteTrack = AuthoringTestAssets.AddSpriteTrack(
                clip, SecondTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(spriteTrack, 0f, -5, float4.zero);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V14, ValidationSeverity.Warning);
        }

        // -----------------------------------------------------------------------------------
        // Argument contracts and the bake gate.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ValidateClipAndValidateSet_RejectNullArguments()
        {
            Assert.Throws<ArgumentNullException>(
                delegate { ClipValidation.ValidateClip(null); },
                "ValidateClip must reject a null clip rather than return an empty result.");
            Assert.Throws<ArgumentNullException>(
                delegate { ClipValidation.ValidateSet(null); },
                "ValidateSet must reject a null set rather than return an empty result.");
        }

        [Test]
        public void Build_ThrowsClipValidationException_ListingTheOffendingCodes()
        {
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            clip.transformTracks[0].targetId = 999u;

            BlobAssetReferenceScope registryScope = new BlobAssetReferenceScope();
            try
            {
                ClipValidationException thrownException = Assert.Throws<ClipValidationException>(
                    delegate { registryScope.Build(clipSet); },
                    "A set with validation errors must not bake.");

                Assert.IsTrue(
                    thrownException.Message.Contains("V02"),
                    "The exception message must name the offending rule codes: " + thrownException.Message);
                AssertContainsCode(thrownException.Messages, ValidationCode.V02, ValidationSeverity.Error);
                Assert.IsFalse(
                    registryScope.Registry.IsCreated,
                    "A rejected bake must not leave a blob behind.");
            }
            finally
            {
                registryScope.Dispose();
            }
        }

        [Test]
        public void Build_RejectsANullClipSet()
        {
            Assert.Throws<ArgumentNullException>(
                delegate
                {
                    BlobAssetReferenceScope registryScope = new BlobAssetReferenceScope();
                    registryScope.Build(null);
                },
                "Build must reject a null set.");
        }

        [Test]
        public void Build_SucceedsWhenOnlyWarningsWereReported()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset clip = CreateValidClip(rig, "Walk", WalkClipId);
            clip.defaultBlendIn = 5f;
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, clip, clip);

            BlobAssetReferenceScope registryScope = new BlobAssetReferenceScope();
            try
            {
                registryScope.Build(clipSet);

                Assert.IsTrue(registryScope.Registry.IsCreated, "Warnings must not block a bake.");
                Assert.AreEqual(
                    1,
                    registryScope.Registry.Value.clips.Length,
                    "The duplicate listing (V11) must contribute exactly one baked clip.");
                Assert.AreEqual(
                    1f,
                    registryScope.Registry.Value.clips[0].defaultBlendIn,
                    FloatTolerance,
                    "The over-long blend default (V12) must be clamped to the clip duration.");
            }
            finally
            {
                registryScope.Dispose();
            }
        }

        // -----------------------------------------------------------------------------------
        // ValidateRig — the rig-only entry point.
        //
        // Public API with no in-package caller: the inspectors and the clip editor (build step C7)
        // validate a rig on its own, without a set to hang it off. Untested until now, which is how
        // published surface quietly rots.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ValidateRig_ReportsNothing_ForAWellFormedRig()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { FirstTargetId, SecondTargetId });

            AssertNoFindings(ClipValidation.ValidateRig(rig), "A well-formed rig has no findings.");
        }

        [Test]
        public void ValidateRig_ReportsV05_ForDuplicateTargetIds()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { FirstTargetId, FirstTargetId });

            AssertOnlyCode(ClipValidation.ValidateRig(rig), ValidationCode.V05, ValidationSeverity.Error);
        }

        [Test]
        public void ValidateRig_ReportsV13_ForANullRig_BecauseASetWithoutARigHasNoLayers()
        {
            AssertOnlyCode(ClipValidation.ValidateRig(null), ValidationCode.V13, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // Boundary cases.
        //
        // Each fixture above breaks its rule in one direction only, so each would still pass if the
        // rule's comparison were wrong in the other direction. These pin the boundaries.
        // -----------------------------------------------------------------------------------

        [Test]
        public void V04_AlsoFiresForANegativeKeyTime_NotOnlyForOneAboveTheRange()
        {
            // The paired fixture only exceeds 1. Without this, a rule written `time > 1f` instead
            // of `time < 0f || time > 1f` would still pass.
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            TransformTrack negativeTimeTrack = AuthoringTestAssets.AddTransformTrack(
                clip, SecondTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                negativeTimeTrack, -0.25f, new float3(0f, 0f, 0f), 0f, new float3(1f, 1f, 1f),
                Interpolation.Linear);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V04, ValidationSeverity.Error);
        }

        [Test]
        public void V03_FiresForTwoKeysAtTheSameTime_BecauseTheOrderMustBeStrict()
        {
            // The paired fixture uses distinct out-of-order times, so a rule accepting equal times
            // (non-strict ascending) would pass it. Equal times are the case that makes segment
            // lookup ambiguous at runtime.
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            TransformTrack duplicateTimeTrack = AuthoringTestAssets.AddTransformTrack(
                clip, SecondTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                duplicateTimeTrack, 0.5f, new float3(0f, 0f, 0f), 0f, new float3(1f, 1f, 1f),
                Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                duplicateTimeTrack, 0.5f, new float3(1f, 0f, 0f), 0f, new float3(1f, 1f, 1f),
                Interpolation.Linear);

            AssertOnlyCode(ClipValidation.ValidateSet(clipSet), ValidationCode.V03, ValidationSeverity.Error);
        }

        [Test]
        public void V09_FiresForTheTopOfTheReservedRange_AndPassesTheFirstUserKey()
        {
            // The paired fixture uses only ClipFinished (1). The reserved band is 1 to 15, so a
            // rule checking a couple of named constants rather than the range would pass it.
            RigAsset rig;
            ClipAsset topOfReservedClip;
            ClipSetAsset topOfReservedSet = CreateValidSet(out rig, out topOfReservedClip);
            EventMarker topOfReservedMarker = topOfReservedClip.events[0];
            topOfReservedMarker.eventKey = (uint)ReservedEventKeys.FirstUserKey - 1u;
            topOfReservedClip.events[0] = topOfReservedMarker;

            AssertOnlyCode(
                ClipValidation.ValidateSet(topOfReservedSet), ValidationCode.V09, ValidationSeverity.Error);

            ClipAsset firstUserKeyClip;
            ClipSetAsset firstUserKeySet = CreateValidSet(out rig, out firstUserKeyClip);
            EventMarker firstUserKeyMarker = firstUserKeyClip.events[0];
            firstUserKeyMarker.eventKey = (uint)ReservedEventKeys.FirstUserKey;
            firstUserKeyClip.events[0] = firstUserKeyMarker;

            AssertNoFindings(
                ClipValidation.ValidateSet(firstUserKeySet),
                "The first user key is the lowest key the package does not reserve.");
        }

        [Test]
        public void V14_AcceptsMinusOne_BecauseItIsTheNoChangeSentinelNotAnError()
        {
            // The paired fixture uses -5. Without this, a rule written `sliceIndex < 0` would pass
            // it while rejecting the -1 "leave the slice alone" sentinel the sampler depends on.
            RigAsset rig;
            ClipAsset clip;
            ClipSetAsset clipSet = CreateValidSet(out rig, out clip);
            SpriteTrack sentinelTrack = AuthoringTestAssets.AddSpriteTrack(
                clip, SecondTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(sentinelTrack, 0f, -1, float4.zero);

            AssertNoFindings(
                ClipValidation.ValidateSet(clipSet),
                "-1 means 'no change' on an authored sprite key and must validate cleanly.");
        }

        // -----------------------------------------------------------------------------------
        // Assertion helpers.
        // -----------------------------------------------------------------------------------

        private static void AssertNoFindings(IReadOnlyList<ValidationMessage> messages, string because)
        {
            Assert.AreEqual(0, messages.Count, because + " Got: " + Describe(messages));
        }

        private static void AssertOnlyCode(
            IReadOnlyList<ValidationMessage> messages,
            ValidationCode expectedCode,
            ValidationSeverity expectedSeverity)
        {
            AssertContainsCode(messages, expectedCode, expectedSeverity);
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                Assert.AreEqual(
                    expectedCode,
                    messages[messageIndex].code,
                    "The fixture breaks exactly one rule, so no other code may fire: " +
                    Describe(messages));
            }
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
                    Assert.IsNotNull(
                        messages[messageIndex].text,
                        "Every finding must carry an explanation.");
                    return;
                }
            }
            Assert.Fail(
                "Expected " + expectedCode + " at severity " + expectedSeverity +
                " but got: " + Describe(messages));
        }

        private static string Describe(IReadOnlyList<ValidationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return "(no findings)";
            }
            StringBuilder description = new StringBuilder();
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                description.Append("\n  ").Append(messages[messageIndex].ToString());
            }
            return description.ToString();
        }
    }
}
