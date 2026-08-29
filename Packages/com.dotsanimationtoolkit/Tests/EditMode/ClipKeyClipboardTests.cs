// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ClipKeyClipboard"/> — copying keys off one object and
    /// pasting them onto another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buffer used to hold track indices and paste them straight back, so "paste onto a
    /// different object" was not something it could express. What these check is that the round trip
    /// is now described in terms of objects and their components: that a key lands on the object
    /// that was selected, that a destination missing the component gets one, and that a pose crosses
    /// between the two transform kinds rather than being dropped.
    /// </para>
    /// <para>
    /// Every paste anchors at the time passed in, with the copied group's internal spacing intact —
    /// which is the one property that makes paste useful at all in the same clip it was copied from.
    /// </para>
    /// </remarks>
    public sealed class ClipKeyClipboardTests
    {
        private const uint HeadTargetId = 0x11u;
        private const uint HandTargetId = 0x22u;
        private const string BoneName = "Bone.Spine";
        private const string BonePath = "Root/Spine";

        private AuthoringTestAssets assets;
        private RigAsset rig;
        private ClipAsset clip;
        private List<KeyAddress> addresses;
        private List<ClipObjectRef> destinations;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            rig = assets.CreateRig("Rig", 1uL, 1, new uint[] { HeadTargetId, HandTargetId });
            clip = assets.CreateClip("Clip", 2uL, 1f);
            addresses = new List<KeyAddress>();
            destinations = new List<ClipObjectRef>();
            ClipKeyClipboard.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ClipKeyClipboard.Clear();
            assets.DestroyAll();
        }

        private static ClipObjectRef Part(uint targetId)
        {
            return ClipObjectRef.RigTarget(targetId, 0u);
        }

        private static ClipObjectRef Node()
        {
            return ClipObjectRef.Bone(BoneName, 0u, 0u, BonePath);
        }

        /// <summary>A transform track on the head with two keys, a quarter of the clip apart.</summary>
        private TransformTrack GivenHeadTransformWithTwoKeys()
        {
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, HeadTargetId, TrackBlendOp.Additive, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                track, 0.5f, new float3(1f, 0f, 0f), 30f, new float3(1f, 1f, 1f),
                Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                track, 0.75f, new float3(2f, 0f, 0f), 0f, new float3(1f, 1f, 1f),
                Interpolation.Linear);
            return track;
        }

        private void CopyEveryKeyOf(TimelineTrackKind trackKind, int trackIndex, int keyCount)
        {
            addresses.Clear();
            for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                addresses.Add(new KeyAddress(trackKind, trackIndex, keyIndex));
            }
            ClipKeyClipboard.Copy(clip, addresses);
        }

        // -----------------------------------------------------------------------------------
        // Landing on the object that was selected.
        // -----------------------------------------------------------------------------------

        [Test]
        public void KeysLandOnTheSelectedObject_NotTheOneTheyWereCopiedFrom()
        {
            GivenHeadTransformWithTwoKeys();
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);

            destinations.Add(Part(HandTargetId));
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(2, result.keyCount);
            Assert.AreEqual(2, clip.transformTracks.Count, "The hand had no transform track yet.");
            Assert.AreEqual(1, result.addedComponentCount);

            TransformTrack pasted = clip.transformTracks[1];
            Assert.AreEqual(HandTargetId, pasted.targetId);
            Assert.AreEqual(2, pasted.keys.Count);
            Assert.AreEqual(
                2, clip.transformTracks[0].keys.Count, "The source is left exactly as it was.");
        }

        [Test]
        public void ThePastedGroupLandsAtTheAnchorWithItsSpacingIntact()
        {
            GivenHeadTransformWithTwoKeys();
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);

            destinations.Add(Part(HandTargetId));
            ClipKeyClipboard.Paste(clip, rig, destinations, 0.1f);

            List<TransformKey> keys = clip.transformTracks[1].keys;
            Assert.AreEqual(
                0.1f, keys[0].normalizedTime, 1e-5f,
                "The earliest copied key lands on the anchor, wherever it sat in the source.");
            Assert.AreEqual(
                0.35f, keys[1].normalizedTime, 1e-5f,
                "And the rest keep their distance from it — the quarter-clip gap survives.");
        }

        [Test]
        public void AnAddedTrackInheritsTheSourceTrackSettings()
        {
            GivenHeadTransformWithTwoKeys();
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);

            destinations.Add(Part(HandTargetId));
            ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(
                TrackBlendOp.Additive, clip.transformTracks[1].blendOp,
                "A track created by the paste has to mean what the source meant; the default blend "
                + "op would play the same keys as a replacement rather than an addition.");
            Assert.AreEqual(AnimatedChannels.PositionXY, clip.transformTracks[1].channels);
        }

        [Test]
        public void AnExistingTrackKeepsItsOwnSettings()
        {
            GivenHeadTransformWithTwoKeys();
            TransformTrack handTrack = AuthoringTestAssets.AddTransformTrack(
                clip, HandTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);

            destinations.Add(Part(HandTargetId));
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(0, result.addedComponentCount, "The hand already had a transform.");
            Assert.AreEqual(
                TrackBlendOp.Override, handTrack.blendOp,
                "Stamping the source's settings onto a track somebody else authored would retune "
                + "animation the paste was never asked to touch.");
            Assert.AreEqual(2, handTrack.keys.Count);
        }

        // -----------------------------------------------------------------------------------
        // Missing components are created.
        // -----------------------------------------------------------------------------------

        [Test]
        public void PastingAFlipbookOntoAPartWithNoneAddsOne()
        {
            SpriteTrack source = AuthoringTestAssets.AddSpriteTrack(
                clip, HeadTargetId, SpriteFrameMode.Slice);
            source.sliceSpace = SpriteSliceSpace.RelativeToRest;
            source.baseIndex = 32;
            AuthoringTestAssets.AddSpriteKey(source, 0.25f, 4, float4.zero);
            CopyEveryKeyOf(TimelineTrackKind.Sprite, 0, 1);

            destinations.Add(Part(HandTargetId));
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(1, result.keyCount);
            Assert.AreEqual(1, result.addedComponentCount);
            Assert.AreEqual(2, clip.spriteTracks.Count);

            SpriteTrack pasted = clip.spriteTracks[1];
            Assert.AreEqual(HandTargetId, pasted.targetId);
            Assert.AreEqual(
                32, pasted.baseIndex,
                "A relative sprite key is only interpretable beside its track's base — pasted onto "
                + "a track based at 0 it would address a different block of the texture array.");
            Assert.AreEqual(SpriteSliceSpace.RelativeToRest, pasted.sliceSpace);
            Assert.AreEqual(4, pasted.keys[0].sliceIndex);
        }

        [Test]
        public void PastingASecondFlipbookAddsAsManyAsTheOrdinalNeeds()
        {
            AuthoringTestAssets.AddSpriteTrack(clip, HeadTargetId, SpriteFrameMode.Slice);
            SpriteTrack second = AuthoringTestAssets.AddSpriteTrack(
                clip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(second, 0.5f, 7, float4.zero);

            // The head's *second* flipbook, which is track index 1 in the clip.
            CopyEveryKeyOf(TimelineTrackKind.Sprite, 1, 1);

            destinations.Add(Part(HandTargetId));
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(
                2, result.addedComponentCount,
                "Pasting an object's second flipbook onto one with none has to make both, or the "
                + "keys would pile onto a first track that is not where they belong.");
            Assert.AreEqual(4, clip.spriteTracks.Count);
            Assert.AreEqual(
                0, clip.spriteTracks[2].keys.Count, "The hand's first flipbook stays empty.");
            Assert.AreEqual(7, clip.spriteTracks[3].keys[0].sliceIndex);
        }

        [Test]
        public void PastingAFlipbookOntoAnUnclaimedNodeDeclaresItAPart()
        {
            SpriteTrack source = AuthoringTestAssets.AddSpriteTrack(
                clip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(source, 0f, 3, float4.zero);
            CopyEveryKeyOf(TimelineTrackKind.Sprite, 0, 1);

            destinations.Add(Node());
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.IsTrue(
                result.touchedRig,
                "The caller records undo on the rig off the back of this — a paste that declared a "
                + "part without saying so would leave half the edit outside the undo.");
            Assert.AreEqual(3, rig.targets.Count);

            RigTargetDefinition minted = rig.targets[2];
            Assert.AreEqual(BoneName, minted.displayName);
            Assert.AreEqual(BonePath, minted.sourceNodePath);
            Assert.AreEqual(1, result.keyCount);
            Assert.AreEqual(minted.Id.Value, clip.spriteTracks[1].targetId);
        }

        [Test]
        public void KeysWithNowhereToGoAreReportedRatherThanLostSilently()
        {
            SpriteTrack source = AuthoringTestAssets.AddSpriteTrack(
                clip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(source, 0f, 3, float4.zero);
            CopyEveryKeyOf(TimelineTrackKind.Sprite, 0, 1);

            // No rig, so there is nothing to declare the node a part on.
            destinations.Add(Node());
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, null, destinations, 0f);

            Assert.AreEqual(0, result.keyCount);
            Assert.AreEqual(
                1, result.droppedKeyCount,
                "A paste that writes fewer keys than were copied has to say so, or the loss shows "
                + "up later as a channel nobody remembers dropping.");
        }

        // -----------------------------------------------------------------------------------
        // Crossing between the two transform kinds.
        // -----------------------------------------------------------------------------------

        [Test]
        public void APartsPoseCopiedOntoANodeBecomesABoneKey()
        {
            GivenHeadTransformWithTwoKeys();
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);

            destinations.Add(Node());
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(2, result.keyCount);
            Assert.AreEqual(
                1, clip.boneTracks.Count,
                "The destination decides where the pose lands. A node is posed by name, so a "
                + "transform track here would be one the bake never reads for it.");
            Assert.AreEqual(BoneName, clip.boneTracks[0].boneName);

            BoneKey carried = clip.boneTracks[0].keys[0];
            Assert.AreEqual(1f, carried.localPosition.x, 1e-4f);
            Assert.AreEqual(
                30f, ClipBoneEditing.ToSignedEulerDegrees(carried.localRotation).z, 1e-3f,
                "The Euler degrees a transform key stores, as the quaternion a bone key stores.");
        }

        [Test]
        public void ANodesPoseCopiedOntoAPartBecomesATransformKey()
        {
            BoneTrack source = new BoneTrack { boneName = BoneName };
            source.keys.Add(new BoneKey
            {
                normalizedTime = 0.5f,
                localPosition = new float3(0f, 4f, 0f),
                localRotation = quaternion.Euler(math.radians(new float3(0f, 0f, 90f))),
                localScale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });
            clip.boneTracks.Add(source);
            CopyEveryKeyOf(TimelineTrackKind.Bone, 0, 1);

            destinations.Add(Part(HeadTargetId));
            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(1, result.keyCount);
            Assert.AreEqual(1, clip.transformTracks.Count);

            TransformKey carried = clip.transformTracks[0].keys[0];
            Assert.AreEqual(4f, carried.position.y, 1e-4f);
            Assert.AreEqual(90f, carried.rotation.z, 1e-3f);
        }

        // -----------------------------------------------------------------------------------
        // How the buffer spreads over a selection.
        // -----------------------------------------------------------------------------------

        [Test]
        public void PastingWithNothingSelectedPutsTheKeysBackWhereTheyCameFrom()
        {
            GivenHeadTransformWithTwoKeys();
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);

            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0.1f);

            Assert.AreEqual(2, result.keyCount);
            Assert.AreEqual(
                1, clip.transformTracks.Count, "No new track — this is duplicate at the playhead.");
            Assert.AreEqual(
                4, clip.transformTracks[0].keys.Count,
                "The copied pair joins the pair already there.");
        }

        [Test]
        public void OneCopiedObjectSpreadsOverEverySelectedObject()
        {
            GivenHeadTransformWithTwoKeys();
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);

            Assert.AreEqual(1, ClipKeyClipboard.ObjectCount);
            destinations.Add(Part(HandTargetId));
            destinations.Add(Node());

            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(
                4, result.keyCount,
                "Putting one part's motion onto a row of selected objects is the point of a "
                + "multi-selection paste.");
            Assert.AreEqual(2, clip.transformTracks[1].keys.Count);
            Assert.AreEqual(2, clip.boneTracks[0].keys.Count);
        }

        [Test]
        public void SeveralCopiedObjectsAreMatchedToTheSelectionInOrder()
        {
            GivenHeadTransformWithTwoKeys();
            TransformTrack handTrack = AuthoringTestAssets.AddTransformTrack(
                clip, HandTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                handTrack, 0.5f, new float3(9f, 0f, 0f), 0f, new float3(1f, 1f, 1f),
                Interpolation.Linear);

            addresses.Clear();
            addresses.Add(new KeyAddress(TimelineTrackKind.Transform, 0, 0));
            addresses.Add(new KeyAddress(TimelineTrackKind.Transform, 1, 0));
            ClipKeyClipboard.Copy(clip, addresses);
            Assert.AreEqual(2, ClipKeyClipboard.ObjectCount);

            // Reversed, so a paste that ignored the pairing would be indistinguishable from one
            // that honoured it.
            destinations.Add(Node());
            destinations.Add(Part(HandTargetId));
            ClipKeyClipboard.Paste(clip, rig, destinations, 0f);

            Assert.AreEqual(
                1, clip.boneTracks.Count, "The head's key went to the first selected object.");
            Assert.AreEqual(
                2, handTrack.keys.Count, "And the hand's went back to the hand, which is second.");
        }

        [Test]
        public void EventsBelongToTheClipAndLandWhateverIsSelected()
        {
            AuthoringTestAssets.AddEvent(clip, 0.5f, 7u, 1, 2f);
            addresses.Clear();
            addresses.Add(new KeyAddress(TimelineTrackKind.Event, 0, 0));
            ClipKeyClipboard.Copy(clip, addresses);

            ClipKeyPasteResult result = ClipKeyClipboard.Paste(clip, rig, destinations, 0.25f);

            Assert.AreEqual(1, result.keyCount);
            Assert.AreEqual(2, clip.events.Count);
            Assert.AreEqual(0.25f, clip.events[1].normalizedTime, 1e-5f);
            Assert.AreEqual(7u, clip.events[1].eventKey);
        }

        [Test]
        public void CopyingNothingLeavesTheBufferEmpty()
        {
            GivenHeadTransformWithTwoKeys();
            CopyEveryKeyOf(TimelineTrackKind.Transform, 0, 2);
            Assert.IsTrue(ClipKeyClipboard.HasContent);

            addresses.Clear();
            ClipKeyClipboard.Copy(clip, addresses);

            Assert.IsFalse(
                ClipKeyClipboard.HasContent,
                "A copy with nothing selected replaces the buffer rather than leaving the last one "
                + "armed — otherwise the next paste writes something the author did not just copy.");
            Assert.AreEqual(0, ClipKeyClipboard.KeyCount);
        }

        [Test]
        public void AStaleAddressIsSkippedRatherThanThrowing()
        {
            GivenHeadTransformWithTwoKeys();

            addresses.Clear();
            addresses.Add(new KeyAddress(TimelineTrackKind.Transform, 0, 0));
            addresses.Add(new KeyAddress(TimelineTrackKind.Transform, 9, 0));
            addresses.Add(new KeyAddress(TimelineTrackKind.Transform, 0, 9));
            ClipKeyClipboard.Copy(clip, addresses);

            Assert.AreEqual(
                1, ClipKeyClipboard.KeyCount,
                "An address left over from a rebuild must fail closed, not take the copy down.");
        }
    }
}
