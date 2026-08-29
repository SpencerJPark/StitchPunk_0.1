// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="TargetTagBindingUtility.CountRigTargetBindings(uint, IReadOnlyList{RigAsset})"/>
    /// — the pure overload behind the count <see cref="TargetTagRegistryEditor"/> shows before a
    /// delete (Phase E target-tags spec §4.2.2), so a person sees how many rig targets a tag deletion
    /// will break before they confirm it rather than discovering the damage in the console afterward.
    /// </summary>
    /// <remarks>
    /// Exercises the pure, rig-list overload rather than
    /// <see cref="TargetTagBindingUtility.CountRigTargetBindings(TargetTagEntry)"/>, which scans the
    /// project's real asset database and would make this an asset-on-disk fixture for no benefit -
    /// the counting logic under test does not know or care where its rig list came from.
    /// </remarks>
    public sealed class TargetTagBindingUtilityTests
    {
        private const uint TagId = 777u;
        private const uint OtherTagId = 888u;

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
        public void CountRigTargetBindings_ReturnsZero_ForANullRigList()
        {
            Assert.AreEqual(0, TargetTagBindingUtility.CountRigTargetBindings(TagId, null));
        }

        [Test]
        public void CountRigTargetBindings_ReturnsZero_ForAnEmptyRigList()
        {
            Assert.AreEqual(0, TargetTagBindingUtility.CountRigTargetBindings(TagId, new List<RigAsset>()));
        }

        [Test]
        public void CountRigTargetBindings_ReturnsZero_ForTagIdZero_EvenIfTargetsHappenToCarryZero()
        {
            // Every target created by AuthoringTestAssets.CreateRig defaults tagId to 0 ("untagged").
            // Asking to count bindings for id 0 must never report those as bindings to anything.
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u, 2u, 3u });

            Assert.AreEqual(0, TargetTagBindingUtility.CountRigTargetBindings(0u, new List<RigAsset> { rig }));
        }

        [Test]
        public void CountRigTargetBindings_ReturnsZero_WhenNoTargetInTheRigUsesTheTag()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u, 2u });
            rig.targets[0].tagId = OtherTagId;
            rig.targets[1].tagId = OtherTagId;

            Assert.AreEqual(0, TargetTagBindingUtility.CountRigTargetBindings(TagId, new List<RigAsset> { rig }));
        }

        [Test]
        public void CountRigTargetBindings_CountsOnlyTheTaggedTargets_NotTheUntaggedOnes()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u, 2u, 3u });
            rig.targets[0].tagId = TagId;
            rig.targets[1].tagId = TagId;
            // targets[2].tagId is left at its default 0 ("untagged") - must not be counted.

            Assert.AreEqual(2, TargetTagBindingUtility.CountRigTargetBindings(TagId, new List<RigAsset> { rig }));
        }

        [Test]
        public void CountRigTargetBindings_SumsAcrossMultipleRigs()
        {
            RigAsset firstRig = assets.CreateRig("RigA", 1UL, 1, new uint[] { 1u });
            firstRig.targets[0].tagId = TagId;
            RigAsset secondRig = assets.CreateRig("RigB", 2UL, 1, new uint[] { 1u, 2u });
            secondRig.targets[0].tagId = TagId;
            secondRig.targets[1].tagId = OtherTagId;
            RigAsset thirdRig = assets.CreateRig("RigC", 3UL, 1, new uint[] { 1u });
            thirdRig.targets[0].tagId = TagId;

            int bindingCount = TargetTagBindingUtility.CountRigTargetBindings(
                TagId, new List<RigAsset> { firstRig, secondRig, thirdRig });

            Assert.AreEqual(3, bindingCount, "One binding from each of the three rigs.");
        }

        [Test]
        public void CountRigTargetBindings_IgnoresANullEntryInTheRigList()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            rig.targets[0].tagId = TagId;

            int bindingCount = TargetTagBindingUtility.CountRigTargetBindings(
                TagId, new List<RigAsset> { null, rig, null });

            Assert.AreEqual(1, bindingCount, "A null rig in the list must be skipped, not throw.");
        }

        // -----------------------------------------------------------------------------------
        // CountTrackBindings (E3): the track half of a delete's real cost, mirroring every case
        // above for TransformTrack and SpriteTrack instead of RigTargetDefinition.
        // -----------------------------------------------------------------------------------

        [Test]
        public void CountTrackBindings_ReturnsZero_ForANullClipList()
        {
            Assert.AreEqual(0, TargetTagBindingUtility.CountTrackBindings(TagId, null));
        }

        [Test]
        public void CountTrackBindings_ReturnsZero_ForAnEmptyClipList()
        {
            Assert.AreEqual(0, TargetTagBindingUtility.CountTrackBindings(TagId, new List<ClipAsset>()));
        }

        [Test]
        public void CountTrackBindings_ReturnsZero_ForTagIdZero_EvenIfTracksHappenToCarryZero()
        {
            // Every track AuthoringTestAssets.AddTransformTrack/AddSpriteTrack creates defaults
            // tagId to 0 ("bind by target id instead"). Asking to count bindings for id 0 must
            // never report those as tag bindings.
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY);

            Assert.AreEqual(0, TargetTagBindingUtility.CountTrackBindings(0u, new List<ClipAsset> { clip }));
        }

        [Test]
        public void CountTrackBindings_ReturnsZero_WhenNoTrackUsesTheTag()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY)
                .tagId = OtherTagId;

            Assert.AreEqual(0, TargetTagBindingUtility.CountTrackBindings(TagId, new List<ClipAsset> { clip }));
        }

        [Test]
        public void CountTrackBindings_CountsTransformAndSpriteTracksTogether()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u, 2u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY)
                .tagId = TagId;
            AuthoringTestAssets.AddSpriteTrack(clip, 2u, SpriteFrameMode.Slice).tagId = TagId;
            // A third, target-id-bound track (tagId left at 0) must not be counted.
            AuthoringTestAssets.AddTransformTrack(clip, 2u, TrackBlendOp.Override, AnimatedChannels.PositionXY);

            Assert.AreEqual(2, TargetTagBindingUtility.CountTrackBindings(TagId, new List<ClipAsset> { clip }));
        }

        [Test]
        public void CountTrackBindings_SumsAcrossMultipleClips()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset firstClip = assets.CreateClip("ClipA", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(firstClip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY)
                .tagId = TagId;
            ClipAsset secondClip = assets.CreateClip("ClipB", 2UL, 1f);
            AuthoringTestAssets.AddSpriteTrack(secondClip, 1u, SpriteFrameMode.Slice).tagId = TagId;

            int bindingCount = TargetTagBindingUtility.CountTrackBindings(
                TagId, new List<ClipAsset> { firstClip, secondClip });

            Assert.AreEqual(2, bindingCount, "One binding from each of the two clips.");
        }

        [Test]
        public void CountTrackBindings_IgnoresANullEntryInTheClipList()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 1UL, 1f);
            AuthoringTestAssets.AddTransformTrack(clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY)
                .tagId = TagId;

            int bindingCount = TargetTagBindingUtility.CountTrackBindings(
                TagId, new List<ClipAsset> { null, clip, null });

            Assert.AreEqual(1, bindingCount, "A null clip in the list must be skipped, not throw.");
        }
    }
}
