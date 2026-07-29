// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The determinism half of the module M1 acceptance list (architecture section 4.5 point 4):
    /// the same source assets must produce the same blob and the same content hash on every build,
    /// and shuffling every authoring list that carries no meaning must change neither.
    /// </summary>
    /// <remarks>
    /// Each fixture compares two things, not one. The content hash proves the
    /// <c>BlobAssetStore</c> dedup key is stable; <see cref="BlobSignature"/> proves the bytes the
    /// key stands for are stable too, field by field and float bit pattern by float bit pattern. A
    /// hash that collapsed to a constant would satisfy the first and is caught by the negative
    /// fixtures, which require a changed input to change the hash.
    /// </remarks>
    public sealed class ClipRegistryDeterminismTests
    {
        private const uint HeadTargetId = 0x20u;
        private const uint BodyTargetId = 0x10u;
        private const uint TailTargetId = 0x30u;
        private const ulong WalkClipId = 0x1000UL;
        private const ulong RunClipId = 0x0800UL;
        private const ulong IdleClipId = 0x9000UL;
        private const ulong RigKey = 0x1UL;
        private const ulong SetKey = 0x2UL;
        private const ulong VatSetKey = 0xFEEDUL;

        private AuthoringTestAssets assets;
        private BlobAssetReferenceScope firstBuild;
        private BlobAssetReferenceScope secondBuild;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
            firstBuild = new BlobAssetReferenceScope();
            secondBuild = new BlobAssetReferenceScope();
        }

        [TearDown]
        public void TearDown()
        {
            firstBuild.Dispose();
            secondBuild.Dispose();
            assets.DestroyAll();
        }

        // -----------------------------------------------------------------------------------
        // Positive determinism.
        // -----------------------------------------------------------------------------------

        [Test]
        public void BuildingTheSameSetTwice_ProducesTheSameHashAndTheSameBlob()
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);
            string firstSignature = BlobSignature.Describe(ref firstBuild.Registry.Value);
            secondBuild.Build(clipSet);
            string secondSignature = BlobSignature.Describe(ref secondBuild.Registry.Value);

            Assert.AreEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "Rebuilding unchanged assets must produce the same dedup hash.");
            Assert.AreEqual(
                firstSignature,
                secondSignature,
                "Rebuilding unchanged assets must produce a field-identical blob.");
        }

        [Test]
        public void ShufflingEveryOrderIndependentAuthoringList_ChangesNeitherTheHashNorTheBlob()
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);
            string beforeShuffle = BlobSignature.Describe(ref firstBuild.Registry.Value);

            // Every list reversed here carries no meaning: clip registration order, target row
            // order, and marker order are all re-derived at bake. Track order within one target is
            // deliberately not shuffled, because architecture section 4.5 makes authoring order the
            // tie-break for two tracks that share a target, and this fixture has none.
            clipSet.clips.Reverse();
            clipSet.rig.targets.Reverse();
            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                clip.transformTracks.Reverse();
                clip.spriteTracks.Reverse();
                clip.events.Reverse();
            }

            secondBuild.Build(clipSet);
            string afterShuffle = BlobSignature.Describe(ref secondBuild.Registry.Value);

            Assert.AreEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "Canonical ordering must make the dedup hash independent of authoring list order.");
            Assert.AreEqual(
                beforeShuffle,
                afterShuffle,
                "Canonical ordering must make the blob itself independent of authoring list order.");
        }

        [Test]
        public void RebakingOneSetFromEquivalentAssets_LandsOnTheSameBlob()
        {
            // What dedup actually buys: the bake is a pure function of content AND set identity, so
            // the same set baked twice — two subscenes, two bake passes, many actors referencing one
            // ClipSetAsset — resolves to a single stored blob.
            //
            // This deliberately does NOT claim that two independently authored sets dedup onto each
            // other. They cannot: the folded set key is the dedup key's last word and setKey is in
            // the hashed stream, so distinct identities always produce distinct keys. That is
            // asserted directly below and by ChangingOnlyTheSetKey_ChangesTheContentHash. Both
            // fixtures here therefore share one identity on purpose — modelling one set, not two.
            ClipSetAsset firstAuthoring = CreateRichSet("Set", SetKey);
            ClipSetAsset secondAuthoring = CreateRichSet("Set", SetKey);

            firstBuild.Build(firstAuthoring);
            secondBuild.Build(secondAuthoring);

            Assert.AreEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "One set baked twice must land on one shared blob.");
            Assert.AreEqual(
                BlobSignature.Describe(ref firstBuild.Registry.Value),
                BlobSignature.Describe(ref secondBuild.Registry.Value),
                "One set baked twice must produce field-identical blobs.");
        }

        [Test]
        public void TwoDistinctSetsWithIdenticalContent_NeverShareABlob()
        {
            // The counterpart, stated explicitly so nobody reads the fixture above as a promise of
            // cross-set sharing. Identical content, different identity: dedup is scoped to a set,
            // which is what stops one set's edits from silently rewriting another's baked data.
            ClipSetAsset firstSet = CreateRichSet("FirstSet", SetKey);
            ClipSetAsset secondSet = CreateRichSet("SecondSet", SetKey + 1UL);

            firstBuild.Build(firstSet);
            secondBuild.Build(secondSet);

            Assert.AreNotEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "Dedup is scoped to one set identity; distinct sets must never collapse together.");
        }

        [Test]
        public void ListingAClipTwice_BakesTheSameBlobAsListingItOnce()
        {
            ClipSetAsset singleListingSet = CreateRichSet("Set", SetKey);
            ClipSetAsset doubleListingSet = CreateRichSet("Set", SetKey);
            doubleListingSet.clips.Add(doubleListingSet.clips[0]);

            firstBuild.Build(singleListingSet);
            secondBuild.Build(doubleListingSet);

            Assert.AreEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "Rule V11 deduplicates at bake, so the duplicate entry must not reach the blob.");
            Assert.AreEqual(
                BlobSignature.Describe(ref firstBuild.Registry.Value),
                BlobSignature.Describe(ref secondBuild.Registry.Value),
                "Rule V11 deduplicates at bake, so the duplicate entry must not reach the blob.");
        }

        // -----------------------------------------------------------------------------------
        // Negative determinism: the hash must actually depend on the content.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ChangingOneKeyPosition_ChangesTheContentHash()
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);

            TransformTrack track = FindClip(clipSet, WalkClipId).transformTracks[0];
            TransformKey movedKey = track.keys[1];
            movedKey.position = new float3(movedKey.position.x + 0.0001f, movedKey.position.y, movedKey.position.z);
            track.keys[1] = movedKey;

            secondBuild.Build(clipSet);

            Assert.AreNotEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "A changed key must change the dedup hash, or stale blobs would be reused.");
        }

        [Test]
        public void ChangingOneEventPayload_ChangesTheContentHash()
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);

            ClipAsset walkClip = FindClip(clipSet, WalkClipId);
            EventMarker retunedMarker = walkClip.events[0];
            retunedMarker.intParam = retunedMarker.intParam + 1;
            walkClip.events[0] = retunedMarker;

            secondBuild.Build(clipSet);

            Assert.AreNotEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "Event payloads are part of the baked content and must reach the hash.");
        }

        [Test]
        public void ChangingOnlyTheSetKey_ChangesTheContentHash()
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);
            clipSet.stableId = SetKey + 1UL;
            secondBuild.Build(clipSet);

            Assert.AreNotEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "The set key is part of the hashed stream and of the dedup key's last word.");
        }

        [Test]
        public void ChangingATargetsAuthoredExtents_ChangesTheContentHash()
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);
            clipSet.rig.targets[0].boundsExtents = clipSet.rig.targets[0].boundsExtents + new float3(1f, 0f, 0f);
            secondBuild.Build(clipSet);

            Assert.AreNotEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                "Authored extents reach the hash through every clip's conservative local bounds.");
        }

        // -----------------------------------------------------------------------------------
        // The load-bearing invariant: a blob that differs must hash differently.
        //
        // Every negative fixture above compares hashes only. That is what let the C2 gate find a
        // real defect: the stream omitted sortedTargetIds, targetBoundsExtents, every vatInfo
        // field and debugName, so an edit confined to them returned a STALE blob from the
        // BlobAssetStore. The fixtures below close that class by asserting the general property —
        // different signature implies different hash — against each field that was missing.
        // Each of them fails against the pre-amendment-A10 stream.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Builds the set, applies <paramref name="mutate"/>, rebuilds, and asserts the blob really
        /// changed and that the dedup key changed with it. Asserting the signature first means a
        /// mutation that silently did nothing fails as "the blob is identical" rather than
        /// masquerading as a hashing bug.
        /// </summary>
        private void AssertMutationChangesBlobAndHash(
            System.Action<ClipSetAsset> mutate,
            string mutationDescription)
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);
            string firstSignature = BlobSignature.Describe(ref firstBuild.Registry.Value);

            mutate(clipSet);

            secondBuild.Build(clipSet);
            string secondSignature = BlobSignature.Describe(ref secondBuild.Registry.Value);

            Assert.AreNotEqual(
                firstSignature,
                secondSignature,
                "Precondition: " + mutationDescription + " must actually change the baked blob.");
            Assert.AreNotEqual(
                firstBuild.ContentHash,
                secondBuild.ContentHash,
                mutationDescription + " changes the blob, so it must change the dedup key. If it " +
                "does not, the BlobAssetStore answers the next bake with a stale blob.");
        }

        [Test]
        public void ChangingATargetId_ChangesTheContentHash()
        {
            // Renumbering must carry the tracks with it: a track pointing at an id the rig no
            // longer defines is a V02 error and Build would refuse the set rather than hash it.
            //
            // Body is the lowest of {0x10, 0x20, 0x30}, and 0x11 is still the lowest, so the dense
            // target order is untouched. That is deliberate — it isolates the id *values* in
            // sortedTargetIds as the only thing that changed, rather than letting a reshuffled
            // dense order change the blob for a different reason.
            AssertMutationChangesBlobAndHash(
                clipSet => RetargetRigTarget(clipSet, BodyTargetId, BodyTargetId + 1u),
                "Renumbering a rig target (dense order unchanged)");
        }

        /// <summary>
        /// Moves a rig target to a new id and repoints every transform and sprite track that
        /// referenced the old id, so the set stays valid under rule V02.
        /// </summary>
        private static void RetargetRigTarget(ClipSetAsset clipSet, uint oldTargetId, uint newTargetId)
        {
            for (int targetIndex = 0; targetIndex < clipSet.rig.targets.Count; targetIndex++)
            {
                if (clipSet.rig.targets[targetIndex].stableId == oldTargetId)
                {
                    clipSet.rig.targets[targetIndex].stableId = newTargetId;
                }
            }
            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
                {
                    if (clip.transformTracks[trackIndex].targetId == oldTargetId)
                    {
                        clip.transformTracks[trackIndex].targetId = newTargetId;
                    }
                }
                for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                {
                    if (clip.spriteTracks[trackIndex].targetId == oldTargetId)
                    {
                        clip.spriteTracks[trackIndex].targetId = newTargetId;
                    }
                }
            }
        }

        [Test]
        public void ChangingTheVatTextureWidth_ChangesTheContentHash()
        {
            // The concrete stale-blob case: VAT textures rebaked at a new width while every clip's
            // frame range stays identical. Everything the old stream hashed is unchanged.
            AssertMutationChangesBlobAndHash(
                clipSet => clipSet.vatTextures.textureWidth = clipSet.vatTextures.textureWidth + 512,
                "Rebaking VAT textures to a different width");
        }

        [Test]
        public void ChangingTheVatBoneCount_ChangesTheContentHash()
        {
            AssertMutationChangesBlobAndHash(
                clipSet => clipSet.vatTextures.boneCount = clipSet.vatTextures.boneCount + 7,
                "Changing the VAT bone count");
        }

        [Test]
        public void RenamingAClip_ChangesTheContentHash()
        {
            // debugName is baked content — it is what logs and the inspector show.
            AssertMutationChangesBlobAndHash(
                clipSet => FindClip(clipSet, WalkClipId).name = "WalkRenamed",
                "Renaming a clip asset");
        }

        // -----------------------------------------------------------------------------------
        // The dedup key's shape (architecture section 4.5 point 3).
        // -----------------------------------------------------------------------------------

        [Test]
        public void TheDedupKey_CarriesTheSchemaVersionAndTheFoldedSetKey()
        {
            ClipSetAsset clipSet = CreateRichSet("Set", SetKey);

            firstBuild.Build(clipSet);

            uint expectedFoldedSetKey = (uint)SetKey ^ (uint)(SetKey >> 32);
            Assert.AreEqual(
                (uint)ClipRegistryBuilder.SchemaVersion,
                firstBuild.ContentHash.Value.z,
                "The dedup key's third word is the schema version, so a layout bump cannot alias.");
            Assert.AreEqual(
                expectedFoldedSetKey,
                firstBuild.ContentHash.Value.w,
                "The dedup key's fourth word is the folded set key.");
            Assert.AreNotEqual(
                0u,
                firstBuild.ContentHash.Value.x | firstBuild.ContentHash.Value.y,
                "The content hash words must actually carry the hashed stream.");
        }

        // -----------------------------------------------------------------------------------
        // The rich fixture.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Builds a set that exercises every field the canonical stream visits: several clips out of
        /// id order, several targets out of id order, transform tracks with easing and negative
        /// scale, both sprite frame modes, markers out of time order, and a VAT frame range.
        /// </summary>
        private ClipSetAsset CreateRichSet(string setName, ulong setKey)
        {
            RigAsset rig = assets.CreateRig(
                "Rig", RigKey, 3, new uint[] { HeadTargetId, BodyTargetId, TailTargetId });
            rig.targets[0].boundsExtents = new float3(0.25f, 0.5f, 0.125f);
            rig.targets[1].boundsExtents = new float3(1f, 1f, 1f);
            rig.targets[2].boundsExtents = new float3(0.75f, 0.25f, 0.5f);

            ClipAsset walkClip = assets.CreateClip("Walk", rig, WalkClipId, 1.5f);
            walkClip.defaultLoop = LoopMode.PingPong;
            walkClip.defaultBlendIn = 0.25f;
            walkClip.defaultBlendOut = 0.5f;
            TransformTrack bodyTrack = AuthoringTestAssets.AddTransformTrack(
                walkClip, BodyTargetId, TrackBlendOp.Override,
                AnimatedChannels.PositionXY | AnimatedChannels.Scale);
            AuthoringTestAssets.AddTransformKey(
                bodyTrack, 0f, new float3(0f, 0f, 0f), 0f, new float2(1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                bodyTrack, 0.5f, new float3(1f, -2f, 0.5f), 45f, new float2(-1.5f, 2f), Interpolation.EaseIn);
            AuthoringTestAssets.AddTransformKey(
                bodyTrack, 1f, new float3(3f, 1f, 0f), -90f, new float2(1f, 1f), Interpolation.Step);
            TransformTrack tailTrack = AuthoringTestAssets.AddTransformTrack(
                walkClip, TailTargetId, TrackBlendOp.Additive, AnimatedChannels.RotationZ);
            AuthoringTestAssets.AddTransformKey(
                tailTrack, 0f, float3.zero, 10f, new float2(1f, 1f), Interpolation.EaseOut);
            AuthoringTestAssets.AddTransformKey(
                tailTrack, 1f, float3.zero, -10f, new float2(1f, 1f), Interpolation.EaseInOut);
            SpriteTrack headSliceTrack = AuthoringTestAssets.AddSpriteTrack(
                walkClip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(headSliceTrack, 0f, 3, float4.zero);
            AuthoringTestAssets.AddSpriteKey(headSliceTrack, 0.5f, -1, float4.zero);
            AuthoringTestAssets.AddSpriteKey(headSliceTrack, 1f, 7, float4.zero);
            // A second sprite track so the shuffle fixture's spriteTracks.Reverse() actually
            // reorders something. With one track per clip it was inert, and the canonical sprite
            // ordering was never exercised by the shuffle at all.
            SpriteTrack tailAtlasTrack = AuthoringTestAssets.AddSpriteTrack(
                walkClip, TailTargetId, SpriteFrameMode.AtlasRect);
            AuthoringTestAssets.AddSpriteKey(
                tailAtlasTrack, 0f, -1, new float4(0.125f, 0.25f, 0.375f, 0.5f));
            AuthoringTestAssets.AddSpriteKey(
                tailAtlasTrack, 1f, -1, new float4(0.5f, 0.375f, 0.25f, 0.125f));
            AuthoringTestAssets.AddEvent(walkClip, 0.9f, 32u, 1, 0.5f);
            AuthoringTestAssets.AddEvent(walkClip, 0.1f, 16u, -3, 2.5f);
            AuthoringTestAssets.AddEvent(walkClip, 0.5f, 20u, 0, 0f);

            ClipAsset runClip = assets.CreateClip("Run", rig, RunClipId, 2f);
            runClip.defaultLoop = LoopMode.Once;
            runClip.defaultBlendIn = 0f;
            runClip.defaultBlendOut = 0f;
            runClip.vatSource = new VatClipSource();
            TransformTrack runHeadTrack = AuthoringTestAssets.AddTransformTrack(
                runClip, HeadTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                runHeadTrack, 0f, new float3(0.5f, 0.25f, 0f), 0f, new float2(1f, 1f), Interpolation.Linear);
            SpriteTrack bodyAtlasTrack = AuthoringTestAssets.AddSpriteTrack(
                runClip, BodyTargetId, SpriteFrameMode.AtlasRect);
            AuthoringTestAssets.AddSpriteKey(
                bodyAtlasTrack, 0f, -1, new float4(0.5f, 0.5f, 0.25f, 0.75f));

            ClipAsset idleClip = assets.CreateClip("Idle", rig, IdleClipId, 0.5f);
            idleClip.defaultLoop = LoopMode.Loop;
            idleClip.defaultBlendIn = 0.1f;
            idleClip.defaultBlendOut = 0.1f;
            AuthoringTestAssets.AddEvent(idleClip, 0f, 99u, 7, -1.5f);

            ClipSetAsset clipSet = assets.CreateSet(setName, rig, setKey, idleClip, walkClip, runClip);

            VatTextureSetAsset vatTextureSet = assets.CreateVatTextureSet("VatSet", VatSetKey);
            vatTextureSet.clipRanges.Add(new VatClipRange
            {
                clipId = RunClipId,
                frameStart = 12,
                frameCount = 31,
                fps = 30f,
                bounds = new UnityEngine.Bounds(
                    new UnityEngine.Vector3(0f, 1f, 0f),
                    new UnityEngine.Vector3(2f, 4f, 2f))
            });
            clipSet.vatTextures = vatTextureSet;
            return clipSet;
        }

        private static ClipAsset FindClip(ClipSetAsset clipSet, ulong clipId)
        {
            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                if (clipSet.clips[clipIndex].Id.Value == clipId)
                {
                    return clipSet.clips[clipIndex];
                }
            }
            Assert.Fail("The fixture must contain a clip with id " + clipId.ToString("X16") + ".");
            return null;
        }
    }
}
