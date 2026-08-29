// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="SharedClipBindingUtility"/> — rule T4 (V37, Phase E target-tags spec §6):
    /// a clip referenced by more than one clip set that still binds a track by target id rather than
    /// by tag, the rule that turns "my shared clip does nothing on the second character" into a
    /// message at authoring time.
    /// </summary>
    /// <remarks>
    /// Exercises the pure, set-list overload
    /// <see cref="SharedClipBindingUtility.CountReferencingClipSets(ClipAsset, IReadOnlyList{ClipSetAsset})"/>
    /// and <see cref="SharedClipBindingUtility.ValidateSharedClipBinding(ClipAsset, IReadOnlyList{ClipSetAsset})"/>
    /// rather than the project-scanning overloads, which touch the asset database and would make
    /// this an asset-on-disk fixture for no benefit — the counting logic under test does not know or
    /// care where its clip-set list came from.
    /// </remarks>
    public sealed class SharedClipBindingUtilityTests
    {
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

        [Test]
        public void CountReferencingClipSets_ReturnsZero_ForANullClip()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            Assert.AreEqual(
                0, SharedClipBindingUtility.CountReferencingClipSets(null, new List<ClipSetAsset> { clipSet }));
        }

        [Test]
        public void CountReferencingClipSets_ReturnsZero_ForANullSetList()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);

            Assert.AreEqual(0, SharedClipBindingUtility.CountReferencingClipSets(clip, null));
        }

        [Test]
        public void CountReferencingClipSets_CountsEachDistinctSetOnce_EvenIfTheClipRepeatsInsideIt()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip, clip);

            int referencingSetCount = SharedClipBindingUtility.CountReferencingClipSets(
                clip, new List<ClipSetAsset> { clipSet });

            Assert.AreEqual(1, referencingSetCount, "V11 already covers a set repeating its own clip.");
        }

        [Test]
        public void CountReferencingClipSets_SumsAcrossDistinctSets()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            ClipSetAsset firstSet = assets.CreateSet("SetA", rig, 2UL, clip);
            ClipSetAsset secondSet = assets.CreateSet("SetB", rig, 3UL, clip);
            ClipSetAsset thirdSetWithoutTheClip = assets.CreateSet("SetC", rig, 4UL);

            int referencingSetCount = SharedClipBindingUtility.CountReferencingClipSets(
                clip, new List<ClipSetAsset> { firstSet, secondSet, thirdSetWithoutTheClip });

            Assert.AreEqual(2, referencingSetCount);
        }

        [Test]
        public void ValidateSharedClipBinding_ReportsNothing_WhenTheClipIsInAtMostOneSet()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            List<ValidationMessage> messages = SharedClipBindingUtility.ValidateSharedClipBinding(
                clip, new List<ClipSetAsset> { clipSet });

            Assert.IsEmpty(messages, "A clip in only one set has nowhere to fail to travel to.");
        }

        [Test]
        public void ValidateSharedClipBinding_ReportsNothing_WhenEverySharedTrackIsTagBound()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Blink", 1UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 0u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            track.tagId = 999u;
            ClipSetAsset firstSet = assets.CreateSet("SetA", rig, 2UL, clip);
            ClipSetAsset secondSet = assets.CreateSet("SetB", rig, 3UL, clip);

            List<ValidationMessage> messages = SharedClipBindingUtility.ValidateSharedClipBinding(
                clip, new List<ClipSetAsset> { firstSet, secondSet });

            Assert.IsEmpty(messages, "A fully tag-bound clip is exactly what T4 must not warn about.");
        }

        [Test]
        public void ValidateSharedClipBinding_ReportsV37_ForATargetIdBoundTrack_SharedAcrossSets()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Walk", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            ClipSetAsset firstSet = assets.CreateSet("SetA", rig, 2UL, clip);
            ClipSetAsset secondSet = assets.CreateSet("SetB", rig, 3UL, clip);

            List<ValidationMessage> messages = SharedClipBindingUtility.ValidateSharedClipBinding(
                clip, new List<ClipSetAsset> { firstSet, secondSet });

            Assert.AreEqual(1, messages.Count);
            Assert.AreEqual(ValidationCode.V37, messages[0].code);
            Assert.AreEqual(ValidationSeverity.Warning, messages[0].severity);
            StringAssert.Contains("Walk", messages[0].text);
        }

        [Test]
        public void ValidateSharedClipBinding_ReportsOneFindingPerOffendingTrack()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u, 2u });
            ClipAsset clip = assets.CreateClip("Walk", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddSpriteTrack(clip, 2u, SpriteFrameMode.Slice);
            ClipSetAsset firstSet = assets.CreateSet("SetA", rig, 2UL, clip);
            ClipSetAsset secondSet = assets.CreateSet("SetB", rig, 3UL, clip);

            List<ValidationMessage> messages = SharedClipBindingUtility.ValidateSharedClipBinding(
                clip, new List<ClipSetAsset> { firstSet, secondSet });

            Assert.AreEqual(2, messages.Count, "One finding for the transform track, one for the sprite track.");
        }

        [Test]
        public void ValidateSharedClipBinding_IgnoresAnUntargetedTrack()
        {
            // targetId == 0 and tagId == 0 together mean "nothing authored yet" (e.g. a freshly
            // added track), not "binds by target id" - T4 must not flag a track that names nothing.
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Walk", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 0u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            ClipSetAsset firstSet = assets.CreateSet("SetA", rig, 2UL, clip);
            ClipSetAsset secondSet = assets.CreateSet("SetB", rig, 3UL, clip);

            List<ValidationMessage> messages = SharedClipBindingUtility.ValidateSharedClipBinding(
                clip, new List<ClipSetAsset> { firstSet, secondSet });

            Assert.IsEmpty(messages);
        }

        [Test]
        public void ValidateSharedClipBinding_ReportsNothing_ForANullClip()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            List<ValidationMessage> messages = SharedClipBindingUtility.ValidateSharedClipBinding(
                null, new List<ClipSetAsset> { clipSet });

            Assert.IsEmpty(messages);
        }
    }
}
