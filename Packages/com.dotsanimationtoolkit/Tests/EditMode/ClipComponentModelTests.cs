// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ClipComponentModel"/> — the rules behind the clip editor's
    /// component stack.
    /// </summary>
    /// <remarks>
    /// The stack derives what an object has from the tracks bound to it rather than storing a list,
    /// so every one of these is really the same question asked five ways: does the derivation agree
    /// with the asset? A disagreement would show as a component that cannot be removed because the
    /// inspector is pointing at the wrong index, which is the failure worth catching here rather
    /// than in a panel.
    /// </remarks>
    public sealed class ClipComponentModelTests
    {
        private const uint HeadTargetId = 0x11u;
        private const uint HandTargetId = 0x22u;
        private const uint BillboardRootId = 0x99u;
        private const string BoneName = "Bone.Spine";

        private AuthoringTestAssets assets;
        private RigAsset rig;
        private ClipAsset clip;
        private List<ClipComponentInstance> instances;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            rig = assets.CreateRig("Rig", 1uL, 1, new uint[] { HeadTargetId, HandTargetId });
            clip = assets.CreateClip("Clip", rig, 2uL, 1f);
            instances = new List<ClipComponentInstance>();
        }

        [TearDown]
        public void TearDown()
        {
            assets.DestroyAll();
        }

        private ClipObjectRef Head
        {
            get { return ClipObjectRef.RigTarget(HeadTargetId, 0u); }
        }

        private ClipObjectRef Bone
        {
            get { return ClipObjectRef.Bone(BoneName, 0u, "Root/Spine", true); }
        }

        /// <summary>A bone with no previewed hierarchy, so no path to be addressed by.</summary>
        private ClipObjectRef UnaddressableBone
        {
            get { return ClipObjectRef.Bone(BoneName, 0u, string.Empty, false); }
        }

        /// <summary>The head, already declared a billboard root by the rig.</summary>
        private ClipObjectRef BillboardingHead
        {
            get { return ClipObjectRef.RigTarget(HeadTargetId, BillboardRootId); }
        }

        // -----------------------------------------------------------------------------------
        // Applicability.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ARigTargetTakesTransformAndFlipbook_ButNotBoneTransform()
        {
            string reason;
            Assert.IsTrue(ClipComponentModel.AppliesTo(ClipComponentKind.Transform, Head, out reason));
            Assert.IsTrue(ClipComponentModel.AppliesTo(ClipComponentKind.Flipbook, Head, out reason));
            Assert.IsFalse(
                ClipComponentModel.AppliesTo(ClipComponentKind.BoneTransform, Head, out reason),
                "A rig target has no bone track — the two kinds address different things.");
            Assert.IsNotEmpty(reason, "An unavailable kind must say why, or the menu reads as broken.");
        }

        [Test]
        public void ABoneTakesBoneTransform_ButNotTransformOrFlipbook()
        {
            string reason;
            Assert.IsTrue(
                ClipComponentModel.AppliesTo(ClipComponentKind.BoneTransform, Bone, out reason));
            Assert.IsFalse(ClipComponentModel.AppliesTo(ClipComponentKind.Transform, Bone, out reason));
            Assert.IsFalse(ClipComponentModel.AppliesTo(ClipComponentKind.Flipbook, Bone, out reason));
        }

        [Test]
        public void BothKindsTakeASocket()
        {
            string reason;
            Assert.IsTrue(ClipComponentModel.AppliesTo(ClipComponentKind.Socket, Head, out reason));
            Assert.IsTrue(ClipComponentModel.AppliesTo(ClipComponentKind.Socket, Bone, out reason));
        }

        [Test]
        public void BillboardIsOfferedWhereverTheRigCanAddressTheNode()
        {
            string reason;
            Assert.IsTrue(
                ClipComponentModel.AppliesTo(ClipComponentKind.Billboard, Head, out reason),
                "Adding Billboard is what makes a node a root, so it is offered to nodes that are "
                + "not one yet.");
            Assert.IsTrue(
                ClipComponentModel.AppliesTo(ClipComponentKind.Billboard, Bone, out reason));
            Assert.IsFalse(
                ClipComponentModel.AppliesTo(
                    ClipComponentKind.Billboard, UnaddressableBone, out reason),
                "With no hierarchy to read a path against, an empty path would silently mean the "
                + "prefab root rather than this bone.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void BillboardIsRigScoped_BecauseTheComponentIsTheRoot()
        {
            Assert.AreEqual(
                ClipComponentScope.Rig, ClipComponentModel.Scope(ClipComponentKind.Billboard),
                "A node carrying it faces the viewer in every clip, animated or not.");
        }

        [Test]
        public void AddingBillboardDeclaresTheNodeARoot_AndPresenceFollowsThatRoot()
        {
            ClipComponentInstance added =
                ClipComponentModel.Add(clip, rig, Head, ClipComponentKind.Billboard, string.Empty);

            Assert.AreEqual(0, added.index);
            Assert.AreEqual(1, rig.billboardRoots.Count);
            Assert.AreEqual(
                BillboardAddressKind.RigTarget, rig.billboardRoots[0].address.kind);
            Assert.AreEqual(HeadTargetId, rig.billboardRoots[0].address.targetId);
            Assert.AreEqual(
                0, clip.billboardTracks.Count,
                "The root is the component; the track is made by the first edit, the way a "
                + "flipbook's first key is.");

            // Ids are minted by the caller, exactly as they are for a socket.
            rig.EnsureStableIds();
            ClipObjectRef rootedHead =
                ClipObjectRef.RigTarget(HeadTargetId, rig.billboardRoots[0].Id.Value);

            ClipComponentModel.CollectInstances(clip, rig, rootedHead, instances);
            Assert.AreEqual(1, instances.Count);
            Assert.AreEqual(ClipComponentKind.Billboard, instances[0].kind);
            Assert.AreEqual(
                0, instances[0].index, "The instance addresses the rig's root list.");
        }

        [Test]
        public void ANodeThatIsNotARootCarriesNoBillboardComponent()
        {
            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.AreEqual(0, instances.Count);
        }

        [Test]
        public void RemovingBillboardTakesTheRootAndTheTracksThatAddressedIt()
        {
            ClipComponentModel.Add(clip, rig, Head, ClipComponentKind.Billboard, string.Empty);
            rig.EnsureStableIds();
            uint rootStableId = rig.billboardRoots[0].Id.Value;

            clip.billboardTracks.Add(new BillboardTrack { rootStableId = rootStableId });
            clip.billboardTracks.Add(new BillboardTrack { rootStableId = 0xAAu });

            ClipObjectRef rootedHead = ClipObjectRef.RigTarget(HeadTargetId, rootStableId);
            ClipComponentModel.CollectInstances(clip, rig, rootedHead, instances);
            Assert.IsTrue(ClipComponentModel.Remove(clip, rig, instances[0]));

            Assert.AreEqual(0, rig.billboardRoots.Count);
            Assert.AreEqual(
                1, clip.billboardTracks.Count,
                "A track left bound to a root the rig no longer declares fails V24 and animates "
                + "nothing, so it goes with the root — but another root's track must not.");
            Assert.AreEqual(0xAAu, clip.billboardTracks[0].rootStableId);
        }

        [Test]
        public void BillboardKeyCountSumsTheRootsOwnTracks()
        {
            BillboardTrack track = new BillboardTrack { rootStableId = BillboardRootId };
            track.keys.Add(new BillboardKey { normalizedTime = 0f });
            track.keys.Add(new BillboardKey { normalizedTime = 1f });
            clip.billboardTracks.Add(new BillboardTrack { rootStableId = 0xAAu });
            clip.billboardTracks.Add(track);

            Assert.AreEqual(
                2,
                ClipComponentModel.KeyCount(
                    clip, BillboardingHead,
                    new ClipComponentInstance(ClipComponentKind.Billboard, 0)),
                "The instance addresses the rig's roots, so the keys are found by root id rather "
                + "than by that index.");
        }

        // -----------------------------------------------------------------------------------
        // Presence is derived from the tracks.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AnObjectWithNoTracksHasNoComponents()
        {
            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.AreEqual(0, instances.Count);
        }

        [Test]
        public void ATrackBoundToTheObjectIsItsComponent_AndOneBoundElsewhereIsNot()
        {
            AuthoringTestAssets.AddTransformTrack(
                clip, HandTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformTrack(
                clip, HeadTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);

            ClipComponentModel.CollectInstances(clip, rig, Head, instances);

            Assert.AreEqual(1, instances.Count, "Only the head's own track is the head's component.");
            Assert.AreEqual(ClipComponentKind.Transform, instances[0].kind);
            Assert.AreEqual(
                1, instances[0].index,
                "The instance must index the track's real position in the clip's list, not its "
                + "position among the object's own tracks — removal addresses the list.");
        }

        [Test]
        public void SeveralFlipbooksOnOnePartAreSeveralComponents()
        {
            AuthoringTestAssets.AddSpriteTrack(clip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteTrack(clip, HeadTargetId, SpriteFrameMode.Slice);

            ClipComponentModel.CollectInstances(clip, rig, Head, instances);

            Assert.AreEqual(2, instances.Count);
            Assert.IsTrue(ClipComponentModel.AllowsMultiple(ClipComponentKind.Flipbook));
            Assert.IsFalse(ClipComponentModel.AllowsMultiple(ClipComponentKind.Transform));
        }

        [Test]
        public void ASocketIsAComponentOfWhateverItFollows()
        {
            rig.sockets = new List<SocketDefinition>();
            rig.sockets.Add(new SocketDefinition
            {
                displayName = "Hand Socket",
                mode = SocketAttachMode.RigTarget,
                targetId = HandTargetId
            });
            rig.sockets.Add(new SocketDefinition
            {
                displayName = "Spine Socket",
                mode = SocketAttachMode.Bone,
                boneName = BoneName
            });

            ClipComponentModel.CollectInstances(clip, rig, Bone, instances);
            Assert.AreEqual(1, instances.Count, "Only the socket following this bone.");
            Assert.AreEqual(ClipComponentKind.Socket, instances[0].kind);
            Assert.AreEqual(1, instances[0].index);

            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.AreEqual(0, instances.Count, "The head follows nothing — the socket is the hand's.");
        }

        [Test]
        public void ComponentsComeBackInStackOrder()
        {
            AuthoringTestAssets.AddSpriteTrack(clip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddTransformTrack(
                clip, HeadTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            rig.sockets = new List<SocketDefinition>();
            rig.sockets.Add(new SocketDefinition
            {
                mode = SocketAttachMode.RigTarget,
                targetId = HeadTargetId
            });

            ClipComponentModel.CollectInstances(clip, rig, Head, instances);

            Assert.AreEqual(3, instances.Count);
            Assert.AreEqual(
                ClipComponentKind.Transform, instances[0].kind,
                "Transform leads the stack whatever order the tracks were authored in.");
            Assert.AreEqual(ClipComponentKind.Flipbook, instances[1].kind);
            Assert.AreEqual(ClipComponentKind.Socket, instances[2].kind);
        }

        // -----------------------------------------------------------------------------------
        // Add.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AddingATransformCreatesAnEmptyTrackBoundToTheObject()
        {
            ClipComponentInstance added =
                ClipComponentModel.Add(clip, rig, Head, ClipComponentKind.Transform, string.Empty);

            Assert.AreEqual(0, added.index);
            Assert.AreEqual(1, clip.transformTracks.Count);
            Assert.AreEqual(HeadTargetId, clip.transformTracks[0].targetId);
            Assert.AreEqual(
                0, clip.transformTracks[0].keys.Count,
                "Adding a component is not keying it — an empty track is a valid, bakeable state.");
        }

        [Test]
        public void AddingASecondSingletonComponentIsRefused()
        {
            ClipComponentModel.Add(clip, rig, Head, ClipComponentKind.Transform, string.Empty);

            string reason;
            Assert.IsFalse(
                ClipComponentModel.CanAdd(
                    clip, rig, Head, ClipComponentKind.Transform, out reason),
                "Two transform tracks on one part is a validation error whichever wins the bake.");

            ClipComponentInstance refused =
                ClipComponentModel.Add(clip, rig, Head, ClipComponentKind.Transform, string.Empty);
            Assert.AreEqual(-1, refused.index);
            Assert.AreEqual(1, clip.transformTracks.Count, "Nothing was added.");
        }

        [Test]
        public void AddingASocketBindsItToTheObjectItWasAddedOn()
        {
            ClipComponentInstance addedOnBone =
                ClipComponentModel.Add(clip, rig, Bone, ClipComponentKind.Socket, "Spine Socket");

            Assert.AreEqual(0, addedOnBone.index);
            Assert.AreEqual(SocketAttachMode.Bone, rig.sockets[0].mode);
            Assert.AreEqual(BoneName, rig.sockets[0].boneName);
            Assert.AreEqual("Spine Socket", rig.sockets[0].displayName);

            ClipComponentModel.Add(clip, rig, Head, ClipComponentKind.Socket, "Head Socket");
            Assert.AreEqual(SocketAttachMode.RigTarget, rig.sockets[1].mode);
            Assert.AreEqual(HeadTargetId, rig.sockets[1].targetId);
        }

        [Test]
        public void ARigScopedComponentCannotBeAddedWithoutARig()
        {
            string reason;
            Assert.IsFalse(
                ClipComponentModel.CanAdd(clip, null, Head, ClipComponentKind.Socket, out reason));
            Assert.IsNotEmpty(reason);

            Assert.AreEqual(
                ClipComponentScope.Rig, ClipComponentModel.Scope(ClipComponentKind.Socket),
                "A socket is rig structure: every clip in the set sees the same one.");
            Assert.AreEqual(
                ClipComponentScope.Clip, ClipComponentModel.Scope(ClipComponentKind.Transform));
        }

        [Test]
        public void AClipScopedComponentCannotBeAddedWithoutAClip()
        {
            string reason;
            Assert.IsFalse(
                ClipComponentModel.CanAdd(null, rig, Head, ClipComponentKind.Transform, out reason));
            Assert.IsNotEmpty(reason);
        }

        // -----------------------------------------------------------------------------------
        // Remove.
        // -----------------------------------------------------------------------------------

        [Test]
        public void RemovingAComponentDeletesTheTrackItStandsFor()
        {
            AuthoringTestAssets.AddTransformTrack(
                clip, HandTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformTrack(
                clip, HeadTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);

            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.IsTrue(ClipComponentModel.Remove(clip, rig, instances[0]));

            Assert.AreEqual(1, clip.transformTracks.Count);
            Assert.AreEqual(
                HandTargetId, clip.transformTracks[0].targetId,
                "The other part's track must survive removing this one.");
        }

        [Test]
        public void RemovingASocketDeletesItFromTheRig()
        {
            ClipComponentModel.Add(clip, rig, Bone, ClipComponentKind.Socket, "Spine Socket");
            ClipComponentModel.CollectInstances(clip, rig, Bone, instances);

            Assert.IsTrue(ClipComponentModel.Remove(clip, rig, instances[0]));
            Assert.AreEqual(0, rig.sockets.Count);
        }

        [Test]
        public void RemovingAStaleInstanceIsRefusedRatherThanThrowing()
        {
            Assert.IsFalse(
                ClipComponentModel.Remove(
                    clip, rig, new ClipComponentInstance(ClipComponentKind.Transform, 4)),
                "An index left over from a rebuild must fail closed, not take out a neighbour.");
        }

        [Test]
        public void KeyCountReportsWhatRemovingWouldDestroy()
        {
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, HeadTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.AreEqual(0, ClipComponentModel.KeyCount(clip, Head, instances[0]));

            AuthoringTestAssets.AddTransformKey(
                track, 0f, Unity.Mathematics.float3.zero, 0f,
                new Unity.Mathematics.float3(1f, 1f, 1f), Interpolation.Linear);

            Assert.AreEqual(1, ClipComponentModel.KeyCount(clip, Head, instances[0]));
        }
    }
}
