// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The identity half of the module M1 acceptance list (architecture sections 3.4, 8 M1):
    /// ids are auto-assigned on creation, are random rather than name-derived, and survive renames,
    /// list reordering, and a full serialize/deserialize round trip. Every id-stability claim in the
    /// architecture is asserted here against a real mutation of the asset, not against a comment.
    /// </summary>
    public sealed class StableIdentityTests
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

        // -----------------------------------------------------------------------------------
        // Generation.
        // -----------------------------------------------------------------------------------

        [Test]
        public void CreateInstance_MintsANonZeroStableId_OnEveryIdentityBearingAssetType()
        {
            ClipAsset clip = assets.Create<ClipAsset>("Clip");
            RigAsset rig = assets.Create<RigAsset>("Rig");
            ClipSetAsset clipSet = assets.Create<ClipSetAsset>("Set");
            VatTextureSetAsset vatTextureSet = assets.Create<VatTextureSetAsset>("VatSet");

            Assert.IsTrue(clip.Id.IsValid, "A newly created ClipAsset must carry a valid ClipId.");
            Assert.AreNotEqual(0UL, rig.StableId, "A newly created RigAsset must carry a stable id.");
            Assert.AreNotEqual(0UL, clipSet.StableId, "A newly created ClipSetAsset must carry a stable id.");
            Assert.AreNotEqual(0UL, vatTextureSet.SetKey, "A newly created VatTextureSetAsset must carry a set key.");
        }

        [Test]
        public void CreateInstance_MintsDistinctIds_ForTwoAssetsSharingTheSameName()
        {
            ClipAsset firstClip = assets.Create<ClipAsset>("Walk");
            ClipAsset secondClip = assets.Create<ClipAsset>("Walk");

            Assert.AreNotEqual(
                firstClip.Id.Value,
                secondClip.Id.Value,
                "Ids are random, never name-derived, so identically named assets must not collide.");
        }

        [Test]
        public void EnsureStableIds_IsIdempotent_AndNeverReplacesAnExistingId()
        {
            ClipAsset clip = assets.Create<ClipAsset>("Clip");
            ulong originalId = clip.Id.Value;

            clip.EnsureStableIds();
            clip.EnsureStableIds();

            Assert.AreEqual(originalId, clip.Id.Value, "Re-running id assignment must be a no-op.");
        }

        [Test]
        public void EnsureStableIds_AssignsIdsToTargetRows_ThatWereAddedWithoutOne()
        {
            RigAsset rig = assets.Create<RigAsset>("Rig");
            rig.targets.Add(new RigTargetDefinition { displayName = "Head" });
            rig.targets.Add(new RigTargetDefinition { displayName = "Body" });

            rig.EnsureStableIds();

            Assert.IsTrue(rig.targets[0].Id.IsValid, "A new target row must be given a valid TargetId.");
            Assert.IsTrue(rig.targets[1].Id.IsValid, "A new target row must be given a valid TargetId.");
            Assert.AreNotEqual(
                rig.targets[0].Id.Value,
                rig.targets[1].Id.Value,
                "Two target rows in one rig must not receive the same id.");
        }

        [Test]
        public void Fold_ExclusiveOrsTheGuidHalves_AndIsDeterministic()
        {
            byte[] guidBytes = new byte[16]
            {
                0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08
            };
            Guid guid = new Guid(guidBytes);

            // Low half 0x8877665544332211 xor high half 0x0807060504030201.
            Assert.AreEqual(0x8070605040302010UL, StableIdUtility.Fold(guid), "Fold must xor the two halves.");
            Assert.AreEqual(
                StableIdUtility.Fold(guid),
                StableIdUtility.Fold(new Guid(guidBytes)),
                "Fold must be a pure function of the GUID bytes.");
        }

        [Test]
        public void NewStableIds_AreNeverZero_AndAreOverwhelminglyDistinct()
        {
            HashSet<ulong> assetIds = new HashSet<ulong>();
            for (int drawIndex = 0; drawIndex < 256; drawIndex++)
            {
                ulong assetId = StableIdUtility.NewAssetStableId();
                Assert.AreNotEqual(0UL, assetId, "0 is reserved as none/invalid and must never be minted.");
                assetIds.Add(assetId);

                uint targetId = StableIdUtility.NewTargetStableId();
                Assert.AreNotEqual(0u, targetId, "0 is reserved as none/invalid and must never be minted.");
            }
            Assert.AreEqual(256, assetIds.Count, "256 draws of a 64-bit random id must not repeat.");
        }

        [Test]
        public void NewClipIdAndNewTargetId_WrapTheGeneratedValues()
        {
            ClipId clipId = StableIdUtility.NewClipId();
            TargetId targetId = StableIdUtility.NewTargetId();

            Assert.IsTrue(clipId.IsValid, "A freshly minted ClipId must be valid.");
            Assert.IsTrue(targetId.IsValid, "A freshly minted TargetId must be valid.");
        }

        // -----------------------------------------------------------------------------------
        // Stability under rename, reorder, and re-serialization.
        // -----------------------------------------------------------------------------------

        [Test]
        public void RenamingAssetsAndTargets_LeavesEveryStableIdUnchanged()
        {
            RigAsset rig = assets.CreateRig("Rig", 0x1111UL, 2, new uint[] { 7u, 3u });
            ClipAsset clip = assets.CreateClip("Walk", rig, 0x2222UL, 1f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 0x3333UL, clip);

            rig.name = "RenamedRig";
            clip.name = "Run";
            clipSet.name = "RenamedSet";
            rig.targets[0].displayName = "CompletelyDifferentName";
            rig.targets[1].displayName = "AlsoRenamed";

            Assert.AreEqual(0x1111UL, rig.StableId, "Renaming a rig must not touch its id.");
            Assert.AreEqual(0x2222UL, clip.Id.Value, "Renaming a clip must not touch its id.");
            Assert.AreEqual(0x3333UL, clipSet.StableId, "Renaming a set must not touch its id.");
            Assert.AreEqual(7u, rig.targets[0].Id.Value, "Renaming a target must not touch its id.");
            Assert.AreEqual(3u, rig.targets[1].Id.Value, "Renaming a target must not touch its id.");
        }

        [Test]
        public void ReorderingTargetAndClipLists_LeavesEveryStableIdUnchanged()
        {
            RigAsset rig = assets.CreateRig("Rig", 0x1111UL, 2, new uint[] { 7u, 3u, 9u });
            ClipAsset walkClip = assets.CreateClip("Walk", rig, 0x2222UL, 1f);
            ClipAsset runClip = assets.CreateClip("Run", rig, 0x4444UL, 2f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 0x3333UL, walkClip, runClip);

            rig.targets.Reverse();
            clipSet.clips.Reverse();

            Assert.AreEqual(9u, rig.targets[0].Id.Value, "Reordering must not renumber target rows.");
            Assert.AreEqual(3u, rig.targets[1].Id.Value, "Reordering must not renumber target rows.");
            Assert.AreEqual(7u, rig.targets[2].Id.Value, "Reordering must not renumber target rows.");
            Assert.AreEqual(0x4444UL, clipSet.clips[0].Id.Value, "Reordering must not renumber clips.");
            Assert.AreEqual(0x2222UL, clipSet.clips[1].Id.Value, "Reordering must not renumber clips.");
        }

        [Test]
        public void SerializationRoundTrip_PreservesA64BitClipId_IncludingItsHighBit()
        {
            // 0xF0E1... has the high bit set, so it exceeds long.MaxValue. That is the case a
            // serializer which quietly routes ulong through a signed 64-bit field mangles, and it
            // is why this goes through Unity's serializer rather than Object.Instantiate — an
            // in-memory native clone never visits the text representation where that happens.
            ClipAsset clip = assets.Create<ClipAsset>("Clip");
            clip.stableId = 0xF0E1D2C3B4A59687UL;

            string serializedClip = EditorJsonUtility.ToJson(clip);
            ClipAsset revivedClip = assets.Create<ClipAsset>("RevivedClip");
            EditorJsonUtility.FromJsonOverwrite(serializedClip, revivedClip);

            Assert.AreEqual(
                0xF0E1D2C3B4A59687UL,
                revivedClip.Id.Value,
                "A serialize/deserialize round trip must preserve all 64 bits of a clip id, " +
                "including ids above long.MaxValue.");
        }

        [Test]
        public void SerializationRoundTrip_PreservesRigAndTargetIds()
        {
            // 0x8000000000000001 is long.MaxValue + 2, and 0xFFFFFFFF is uint.MaxValue: the two
            // boundary values a signed round trip corrupts.
            RigAsset rig = assets.CreateRig("Rig", 0x8000000000000001UL, 1, new uint[] { 0xFFFFFFFFu, 1u });

            string serializedRig = EditorJsonUtility.ToJson(rig);
            RigAsset revivedRig = assets.Create<RigAsset>("RevivedRig");
            EditorJsonUtility.FromJsonOverwrite(serializedRig, revivedRig);

            Assert.AreEqual(0x8000000000000001UL, revivedRig.StableId, "The rig id must survive re-serialization.");
            Assert.AreEqual(2, revivedRig.targets.Count, "The target rows must survive re-serialization.");
            Assert.AreEqual(0xFFFFFFFFu, revivedRig.targets[0].Id.Value, "A 32-bit target id must survive intact.");
            Assert.AreEqual(1u, revivedRig.targets[1].Id.Value, "A 32-bit target id must survive intact.");
        }

        [Test]
        public void DuplicatingAnAsset_CopiesTheIdRatherThanMintingANewOne()
        {
            ClipAsset clip = assets.Create<ClipAsset>("Clip");
            ulong originalId = clip.Id.Value;

            ClipAsset clonedClip = UnityEngine.Object.Instantiate(clip);
            assets.Track(clonedClip);

            // Architecture section 3.4: duplication copies the id; the editor's import-time
            // collision postprocessor is what separates the copy, not the runtime assignment hook.
            Assert.AreEqual(originalId, clonedClip.Id.Value, "Duplication must copy the id, not regenerate it.");
        }

        [Test]
        public void AfterRenamingAndReordering_TheRebuiltRegistryStillResolvesEveryOriginalId()
        {
            RigAsset rig = assets.CreateRig("Rig", 0x1111UL, 2, new uint[] { 7u, 3u });
            ClipAsset walkClip = assets.CreateClip("Walk", rig, 0x2222UL, 1f);
            ClipAsset runClip = assets.CreateClip("Run", rig, 0x4444UL, 2f);
            AuthoringTestAssets.AddTransformTrack(walkClip, 7u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                walkClip.transformTracks[0], 0f, new float3(1f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformTrack(runClip, 3u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                runClip.transformTracks[0], 0f, new float3(2f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 0x3333UL, walkClip, runClip);

            rig.name = "RenamedRig";
            walkClip.name = "Sprint";
            rig.targets.Reverse();
            clipSet.clips.Reverse();

            BlobAssetReferenceScope registryScope = new BlobAssetReferenceScope();
            try
            {
                registryScope.Build(clipSet);

                int walkIndex;
                int runIndex;
                Assert.IsTrue(
                    ClipRegistryUtil.TryResolveClip(ref registryScope.Registry.Value, new ClipId(0x2222UL), out walkIndex),
                    "The renamed clip must still resolve by its original id.");
                Assert.IsTrue(
                    ClipRegistryUtil.TryResolveClip(ref registryScope.Registry.Value, new ClipId(0x4444UL), out runIndex),
                    "The reordered clip must still resolve by its original id.");
                Assert.AreEqual(
                    0x2222UL,
                    registryScope.Registry.Value.clips[walkIndex].clipId,
                    "Resolution must land on the clip that owns the id.");
                Assert.AreEqual(
                    0x4444UL,
                    registryScope.Registry.Value.clips[runIndex].clipId,
                    "Resolution must land on the clip that owns the id.");
            }
            finally
            {
                registryScope.Dispose();
            }
        }

        // -----------------------------------------------------------------------------------
        // Amendment A14: a minted id must be reported so the editor layer can persist it.
        //
        // Minting without persisting is the silent failure this contract exists to close: the id
        // lives only in memory, so the asset is re-minted on the next load and changes identity
        // every session, rotting every baked reference to it.
        // -----------------------------------------------------------------------------------

        [Test]
        public void AnAssetThatMintsItsOwnId_ReportsThatTheIdIsNotYetPersisted()
        {
            // CreateInstance raises Awake, which mints because the serialized id is still 0.
            RigAsset freshRig = assets.Create<RigAsset>("FreshRig");

            Assert.AreNotEqual(0UL, freshRig.StableId, "A fresh asset must mint an id.");
            Assert.IsTrue(
                freshRig.HasUnpersistedStableId,
                "An asset that minted an id must say so, or the editor layer has no way to know " +
                "it must be saved — and the id is re-minted differently on the next load.");
        }

        [Test]
        public void EveryIdentityBearingAssetType_ReportsItsMint()
        {
            IStableIdMintReporter[] freshAssets =
            {
                assets.Create<RigAsset>("FreshRig"),
                assets.Create<ClipAsset>("FreshClip"),
                assets.Create<ClipSetAsset>("FreshSet"),
                assets.Create<VatTextureSetAsset>("FreshVatSet")
            };
            foreach (IStableIdMintReporter freshAsset in freshAssets)
            {
                Assert.IsTrue(
                    freshAsset.HasUnpersistedStableId,
                    freshAsset.GetType().Name + " mints an id, so it must report it as unpersisted.");
            }
        }

        [Test]
        public void MarkingTheIdPersisted_ClearsTheReport_AndDoesNotChangeTheId()
        {
            RigAsset freshRig = assets.Create<RigAsset>("FreshRig");
            ulong mintedId = freshRig.StableId;

            freshRig.MarkStableIdPersisted();

            Assert.IsFalse(
                freshRig.HasUnpersistedStableId,
                "Once the editor layer has saved the asset, the report must clear.");
            Assert.AreEqual(
                mintedId,
                freshRig.StableId,
                "Clearing the report must not disturb the id itself.");
        }

        [Test]
        public void AnAssetLoadedWithAnExistingId_DoesNotMintAndReportsNothingToPersist()
        {
            // The load case: a stored id is already present, so nothing is minted and nothing
            // needs saving. A false report here would dirty every asset on every load.
            RigAsset storedRig = assets.CreateRig("StoredRig", 0xABCDEFUL, 1, new uint[] { 4u });
            storedRig.MarkStableIdPersisted();

            storedRig.EnsureStableIds();

            Assert.AreEqual(0xABCDEFUL, storedRig.StableId, "An existing id must never be replaced.");
            Assert.IsFalse(
                storedRig.HasUnpersistedStableId,
                "An asset that already had an id has nothing to persist.");
        }
    }
}
