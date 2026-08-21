// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ClipComponentModel"/> — the rules behind the clip editor's
    /// component stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stack derives what an object has from the tracks bound to it rather than storing a list,
    /// so most of these are the same question asked several ways: does the derivation agree with the
    /// asset? A disagreement would show as a component that cannot be removed because the inspector
    /// is pointing at the wrong index, which is the failure worth catching here rather than in a
    /// panel.
    /// </para>
    /// <para>
    /// The transform kinds are the exception and get their own question: they are present whether or
    /// not anything derives them, so what is tested there is that the stack says so, that exactly
    /// one of the two turns up, and that promoting a node moves its poses onto the other one.
    /// </para>
    /// </remarks>
    public sealed class ClipComponentModelTests
    {
        private const uint HeadTargetId = 0x11u;
        private const uint HandTargetId = 0x22u;
        private const uint BillboardRootId = 0x99u;
        private const string BoneName = "Bone.Spine";
        private const string BonePath = "Root/Spine";

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

        /// <summary>A previewed node the rig declares no part for.</summary>
        private ClipObjectRef Bone
        {
            get { return ClipObjectRef.Bone(BoneName, 0u, 0u, BonePath, true); }
        }

        /// <summary>A node with no previewed hierarchy, so no path to be addressed by.</summary>
        private ClipObjectRef UnaddressableBone
        {
            get { return ClipObjectRef.Bone(BoneName, 0u, 0u, string.Empty, false); }
        }

        /// <summary>The head, already declared a billboard root by the rig.</summary>
        private ClipObjectRef BillboardingHead
        {
            get { return ClipObjectRef.RigTarget(HeadTargetId, BillboardRootId); }
        }

        /// <summary>The first instance of a kind in <see cref="instances"/>, or −1 when absent.</summary>
        private int IndexOfKind(ClipComponentKind kind)
        {
            for (int instanceIndex = 0; instanceIndex < instances.Count; instanceIndex++)
            {
                if (instances[instanceIndex].kind == kind)
                {
                    return instanceIndex;
                }
            }
            return -1;
        }

        // -----------------------------------------------------------------------------------
        // Transform is intrinsic.
        // -----------------------------------------------------------------------------------

        [Test]
        public void EveryObjectHasATransform_BeforeAnythingKeysIt()
        {
            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.AreEqual(1, instances.Count);
            Assert.AreEqual(ClipComponentKind.Transform, instances[0].kind);
            Assert.IsFalse(
                instances[0].HasTrack,
                "The component is present and the track is not — that is what 'not keyed yet' is.");

            ClipComponentModel.CollectInstances(clip, rig, Bone, instances);
            Assert.AreEqual(1, instances.Count);
            Assert.AreEqual(
                ClipComponentKind.BoneTransform, instances[0].kind,
                "A node the rig declares no part for is posed by name, on a bone track.");
            Assert.IsFalse(instances[0].HasTrack);
        }

        [Test]
        public void AnObjectHasExactlyOneOfTheTwoTransformKinds()
        {
            string reason;
            Assert.IsTrue(ClipComponentModel.AppliesTo(ClipComponentKind.Transform, Head, out reason));
            Assert.IsFalse(
                ClipComponentModel.AppliesTo(ClipComponentKind.BoneTransform, Head, out reason),
                "A part is posed on its transform track; two transform components would be two "
                + "answers to one question.");
            Assert.IsNotEmpty(reason, "An unavailable kind must say why, or the stack reads as broken.");

            Assert.IsTrue(
                ClipComponentModel.AppliesTo(ClipComponentKind.BoneTransform, Bone, out reason));
            Assert.IsFalse(
                ClipComponentModel.AppliesTo(ClipComponentKind.Transform, Bone, out reason));
        }

        [Test]
        public void AClaimedNodeIsPosedOnATransformTrack_NotABoneTrack()
        {
            ClipObjectRef claimed = ClipObjectRef.Bone(BoneName, HeadTargetId, 0u, BonePath, true);

            Assert.AreEqual(
                ClipComponentKind.Transform, ClipComponentModel.TransformKindFor(claimed),
                "Once the rig declares a part for the node, the part's track is what the bake "
                + "samples.");
            Assert.AreEqual(
                ClipComponentKind.BoneTransform, ClipComponentModel.TransformKindFor(Bone));
        }

        [Test]
        public void AddComponentDoesNotOfferTheTransformKinds()
        {
            IReadOnlyList<ClipComponentKind> addable = ClipComponentModel.AddableKinds;
            for (int kindIndex = 0; kindIndex < addable.Count; kindIndex++)
            {
                Assert.IsFalse(
                    ClipComponentModel.IsIntrinsic(addable[kindIndex]),
                    "Offering to add a component that is never missing is offering nothing.");
            }

            CollectionAssert.AreEqual(
                new ClipComponentKind[]
                {
                    ClipComponentKind.Flipbook,
                    ClipComponentKind.Billboard,
                    ClipComponentKind.Socket
                },
                addable,
                "The add-ons, in stack order.");
        }

        [Test]
        public void EveryKindDescribesItself_BecauseTheHoverCardIsTheOnlyPlaceItIsSaid()
        {
            IReadOnlyList<ClipComponentKind> kinds = ClipComponentModel.AllKinds;
            for (int kindIndex = 0; kindIndex < kinds.Count; kindIndex++)
            {
                Assert.IsNotEmpty(
                    ClipComponentModel.Describe(kinds[kindIndex]),
                    "The picker moved the descriptions onto hover; a missing one is a row that "
                    + "explains nothing at all.");
            }
        }

        // -----------------------------------------------------------------------------------
        // Applicability of the add-ons.
        // -----------------------------------------------------------------------------------

        [Test]
        public void EveryAddOnAppliesToEveryObject()
        {
            IReadOnlyList<ClipComponentKind> addable = ClipComponentModel.AddableKinds;
            for (int kindIndex = 0; kindIndex < addable.Count; kindIndex++)
            {
                string reason;
                Assert.IsTrue(
                    ClipComponentModel.AppliesTo(addable[kindIndex], Head, out reason),
                    "Add-on refused on a part: " + reason);
                Assert.IsTrue(
                    ClipComponentModel.AppliesTo(addable[kindIndex], Bone, out reason),
                    "Add-on refused on a node: " + reason);
            }
        }

        [Test]
        public void AFlipbookCanBeAddedToANodeTheRigDeclaresNoPartFor()
        {
            string reason;
            Assert.IsTrue(
                ClipComponentModel.CanAdd(clip, rig, Bone, ClipComponentKind.Flipbook, out reason),
                "A plane in the prefab hierarchy is the ordinary thing to put a flipbook on; "
                + "refusing it because no part is declared yet is the data model talking.");
        }

        [Test]
        public void AFlipbookOnAnUnclaimedNodeNeedsARigToDeclareThePartOn()
        {
            string reason;
            Assert.IsFalse(
                ClipComponentModel.CanAdd(clip, null, Bone, ClipComponentKind.Flipbook, out reason));
            Assert.IsNotEmpty(reason);
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
                ClipComponentModel.CanAdd(
                    clip, rig, UnaddressableBone, ClipComponentKind.Billboard, out reason),
                "With no hierarchy to read a path against, an empty path would silently mean the "
                + "prefab root rather than this node.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void BillboardIsRigScoped_BecauseTheComponentIsTheRoot()
        {
            Assert.AreEqual(
                ClipComponentScope.Rig, ClipComponentModel.Scope(ClipComponentKind.Billboard),
                "A node carrying it faces the viewer in every clip, animated or not.");
        }

        // -----------------------------------------------------------------------------------
        // Promotion: a node becomes a part.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AddingAFlipbookToANodeDeclaresAPartAndBindsTheTrackToIt()
        {
            int targetCountBefore = rig.targets.Count;

            ClipComponentInstance added =
                ClipComponentModel.Add(clip, rig, Bone, ClipComponentKind.Flipbook, string.Empty);

            Assert.AreEqual(0, added.index);
            Assert.AreEqual(
                targetCountBefore + 1, rig.targets.Count, "The node became a part of the rig.");

            RigTargetDefinition minted = rig.targets[rig.targets.Count - 1];
            Assert.AreEqual(BoneName, minted.displayName);
            Assert.AreEqual(
                BonePath, minted.sourceNodePath,
                "The path is the record of which node this part is — without it the editor would "
                + "have to guess again next time.");
            Assert.AreNotEqual(
                0u, minted.Id.Value, "A part saved with id 0 is one no track could address.");

            Assert.AreEqual(1, clip.spriteTracks.Count);
            Assert.AreEqual(minted.Id.Value, clip.spriteTracks[0].targetId);
            Assert.AreEqual(
                0, clip.spriteTracks[0].keys.Count,
                "Adding a component is not keying it — an empty track is a valid, bakeable state.");
        }

        [Test]
        public void PromotingANodeTwiceReusesTheSamePart()
        {
            ClipComponentModel.Add(clip, rig, Bone, ClipComponentKind.Flipbook, string.Empty);
            uint mintedId = rig.targets[rig.targets.Count - 1].Id.Value;
            int targetCountAfterFirst = rig.targets.Count;

            // The window rebuilds its object reference from the rig, so the second add sees the
            // node already carrying the part.
            ClipObjectRef claimed = ClipObjectRef.Bone(BoneName, mintedId, 0u, BonePath, true);
            ClipComponentModel.Add(clip, rig, claimed, ClipComponentKind.Flipbook, string.Empty);

            Assert.AreEqual(
                targetCountAfterFirst, rig.targets.Count,
                "A second flipbook on the same node is a second track, not a second part.");
            Assert.AreEqual(2, clip.spriteTracks.Count);
            Assert.AreEqual(mintedId, clip.spriteTracks[1].targetId);
        }

        [Test]
        public void PromotionAdoptsAPartAlreadyNamedAfterTheNode()
        {
            // The link a rig authored before sourceNodePath existed has: the part and the node are
            // called the same thing, and a RigTargetAuthoring in the scene binds them.
            rig.targets[0].displayName = BoneName;

            RigTargetDefinition adopted = ClipComponentModel.PromoteToRigTarget(clip, rig, Bone);

            Assert.AreSame(
                rig.targets[0], adopted,
                "Minting a second part for a node that already has one would split it in two — one "
                + "baked, one not.");
            Assert.AreEqual(2, rig.targets.Count, "Nothing new was declared.");
            Assert.AreEqual(BonePath, adopted.sourceNodePath, "The adoption is recorded.");
        }

        [Test]
        public void PromotionCarriesTheNodesBoneKeysOntoItsTransformTrack()
        {
            BoneTrack boneTrack = new BoneTrack { boneName = BoneName };
            boneTrack.keys.Add(new BoneKey
            {
                normalizedTime = 0.25f,
                localPosition = new float3(1f, 2f, 3f),
                localRotation = quaternion.Euler(math.radians(new float3(0f, 0f, 90f))),
                localScale = new float3(2f, 2f, 2f),
                interpolation = Interpolation.Linear
            });
            clip.boneTracks.Add(boneTrack);

            RigTargetDefinition promoted = ClipComponentModel.PromoteToRigTarget(clip, rig, Bone);

            Assert.AreEqual(
                0, clip.boneTracks.Count,
                "A bone track on a node that is now a part is a track nothing samples.");
            Assert.AreEqual(1, clip.transformTracks.Count);
            Assert.AreEqual(promoted.Id.Value, clip.transformTracks[0].targetId);

            Assert.AreEqual(1, clip.transformTracks[0].keys.Count);
            TransformKey carried = clip.transformTracks[0].keys[0];
            Assert.AreEqual(0.25f, carried.normalizedTime, 1e-5f);
            Assert.AreEqual(1f, carried.position.x, 1e-4f);
            Assert.AreEqual(2f, carried.scale.y, 1e-4f);
            Assert.AreEqual(
                90f, carried.rotation.z, 1e-3f,
                "The quaternion the bone key stored, in the degrees a transform key stores.");
        }

        [Test]
        public void PromotionStrandsBoneKeysRatherThanOverwritingAnAlreadyKeyedPart()
        {
            rig.targets[0].displayName = BoneName;
            AuthoringTestAssets.AddTransformTrack(
                clip, HeadTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                clip.transformTracks[0], 0.5f, new float3(9f, 9f, 9f), 0f,
                new float3(1f, 1f, 1f), Interpolation.Linear);

            clip.boneTracks.Add(new BoneTrack { boneName = BoneName });
            clip.boneTracks[0].keys.Add(new BoneKey { normalizedTime = 0f });

            ClipComponentModel.PromoteToRigTarget(clip, rig, Bone);

            Assert.AreEqual(1, clip.transformTracks[0].keys.Count);
            Assert.AreEqual(
                9f, clip.transformTracks[0].keys[0].position.x, 1e-4f,
                "Appending a node's keys to a part somebody already animated would interleave two "
                + "animations into one unreadable track.");
            Assert.AreEqual(
                1, clip.boneTracks.Count,
                "Nor are they deleted to tidy up the binding — those are authored keys, and this is "
                + "not entitled to trade them for a neater asset.");
        }

        [Test]
        public void AStrandedBoneTrackIsShownOnThePartItCouldNotMergeInto()
        {
            rig.targets[0].displayName = BoneName;
            AuthoringTestAssets.AddTransformTrack(
                clip, HeadTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                clip.transformTracks[0], 0.5f, new float3(9f, 9f, 9f), 0f,
                new float3(1f, 1f, 1f), Interpolation.Linear);
            clip.boneTracks.Add(new BoneTrack { boneName = BoneName });
            clip.boneTracks[0].keys.Add(new BoneKey { normalizedTime = 0f });

            ClipComponentModel.PromoteToRigTarget(clip, rig, Bone);
            ClipObjectRef claimed = ClipObjectRef.Bone(BoneName, HeadTargetId, 0u, BonePath, true);

            ClipComponentModel.CollectInstances(clip, rig, claimed, instances);

            Assert.AreEqual(2, instances.Count, "Its transform, and the track left behind.");
            Assert.AreEqual(ClipComponentKind.Transform, instances[0].kind);
            Assert.AreEqual(
                ClipComponentKind.BoneTransform, instances[1].kind,
                "A track that animates the object with no way to see it is worse than a stack with "
                + "two transform blocks in it.");
            Assert.IsTrue(
                ClipComponentModel.IsPrimaryTransform(ClipComponentKind.Transform, claimed));
            Assert.IsFalse(
                ClipComponentModel.IsPrimaryTransform(ClipComponentKind.BoneTransform, claimed),
                "Which is what gives the leftover a remove button and the object's own transform "
                + "none.");
        }

        [Test]
        public void ANodeIsResolvedToItsPartByPath()
        {
            rig.targets[0].sourceNodePath = BonePath;

            Assert.AreEqual(
                HeadTargetId, ClipComponentModel.ResolveTargetIdForNode(rig, BonePath));
            Assert.AreEqual(
                0u, ClipComponentModel.ResolveTargetIdForNode(rig, "Root/Somewhere/Else"),
                "By the path the rig recorded, never by a name that happens to match — two planes "
                + "called Plane is the ordinary case.");
        }

        // -----------------------------------------------------------------------------------
        // Billboard.
        // -----------------------------------------------------------------------------------

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
            Assert.AreEqual(2, instances.Count, "Its transform, and the billboard just added.");

            int billboardIndex = IndexOfKind(ClipComponentKind.Billboard);
            Assert.AreNotEqual(-1, billboardIndex);
            Assert.AreEqual(
                0, instances[billboardIndex].index, "The instance addresses the rig's root list.");
        }

        [Test]
        public void ANodeThatIsNotARootCarriesNoBillboardComponent()
        {
            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.AreEqual(-1, IndexOfKind(ClipComponentKind.Billboard));
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
            Assert.IsTrue(
                ClipComponentModel.Remove(clip, rig, instances[IndexOfKind(ClipComponentKind.Billboard)]));

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

            Assert.AreEqual(3, instances.Count, "Its transform, and both flipbooks.");
            Assert.AreEqual(ClipComponentKind.Flipbook, instances[1].kind);
            Assert.AreEqual(ClipComponentKind.Flipbook, instances[2].kind);
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
            int socketIndex = IndexOfKind(ClipComponentKind.Socket);
            Assert.AreNotEqual(-1, socketIndex, "Only the socket following this node.");
            Assert.AreEqual(1, instances[socketIndex].index);

            ClipComponentModel.CollectInstances(clip, rig, Head, instances);
            Assert.AreEqual(
                -1, IndexOfKind(ClipComponentKind.Socket),
                "The head follows nothing — the socket is the hand's.");
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

            Assert.IsTrue(
                ClipComponentModel.Remove(clip, rig, instances[IndexOfKind(ClipComponentKind.Socket)]));
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
                track, 0f, float3.zero, 0f, new float3(1f, 1f, 1f), Interpolation.Linear);

            Assert.AreEqual(1, ClipComponentModel.KeyCount(clip, Head, instances[0]));
        }

        [Test]
        public void AnUnkeyedTransformCountsNoKeysRatherThanThrowing()
        {
            ClipComponentModel.CollectInstances(clip, rig, Head, instances);

            Assert.AreEqual(
                0, ClipComponentModel.KeyCount(clip, Head, instances[0]),
                "The component is present with no track behind it, and −1 must read as empty "
                + "rather than index the list.");
        }
    }
}
