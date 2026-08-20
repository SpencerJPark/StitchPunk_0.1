// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Pins the bake's content hash to a literal value (architecture section 4.5, amendment A10).
    ///
    /// Every other determinism fixture is <em>relative</em>: it builds twice and compares the two
    /// results. That catches non-determinism inside one run, but it cannot notice the hash function
    /// itself changing — if a Collections upgrade altered xxHash3, or an edit reordered the canonical
    /// stream, both sides of every comparison would move together and the whole suite would stay
    /// green while every previously baked subscene silently became unreadable.
    ///
    /// This fixture is the absolute reference that closes that hole. It owns a frozen input so it
    /// cannot drift when the shared fixtures evolve: changing the fixture below changes the expected
    /// value, which is exactly the review that a hash-format change deserves.
    ///
    /// When this fails, exactly one of two things is true. Either the change to the stream was
    /// deliberate — in which case bump <c>ClipRegistryBuilder.SchemaVersion</c> and update the
    /// constant below in the same commit, so old baked data cannot be mistaken for new — or it was
    /// accidental, and the diff that caused it is a bake-compatibility break that needs reverting.
    /// </summary>
    public sealed class ContentHashGoldenTests
    {
        private const ulong RigKey = 0x0000000000000101UL;
        private const ulong SetKey = 0x0000000000000202UL;
        private const ulong VatSetKey = 0x0000000000000303UL;
        private const ulong WalkClipId = 0x0000000000001001UL;
        private const ulong IdleClipId = 0x0000000000002002UL;
        private const uint HeadTargetId = 0x00000011u;
        private const uint BodyTargetId = 0x00000022u;

        /// <summary>
        /// The expected 64-bit content hash of <see cref="BuildFrozenSet"/> under schema version 2,
        /// recorded 2026-07-29 against Collections 6.5.0 / Unity 6000.5. The low word is
        /// <c>0x711EB9F9</c> and the high word <c>0x7262FF88</c>, which is how it appears in the
        /// dedup key's first two words.
        ///
        /// Do not "fix" a failure here by pasting in a new number on its own — read the failure
        /// message first and decide which of the two cases you are in.
        /// </summary>
        // Re-recorded for schema version 4, which added ClipBlob.vatTargetRanges (multi-source VAT).
        // Re-recorded again for schema version 5, which added per-key Bezier handles to transform
        // keys, a per-track baseIndex and a per-key SpriteIndexMode to sprite tracks, and put
        // sliceSpace into the hash stream for the first time.
        // Re-recorded again for schema version 6, which made transform rotation three Euler angles
        // and transform scale a float3.
        // Re-recorded again for schema version 7 (A45, event windows), which appended
        // EventMarkerBlob.windowSeconds to the struct and to the canonical hash stream.
        // Re-recorded again for schema version 8 (A44, hierarchical billboarding), which appended
        // ClipBlob.billboardTracks to the struct and to the canonical hash stream. The frozen set
        // authors no billboard track, so the stream gains only the array's zero length — which is
        // the point: an empty array still has to be in the stream, or a clip that gained its first
        // billboard track would hash identically to the clip that had none.
        private const ulong ExpectedContentHash = 0x12D592565545DA14UL;

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
        public void TheFrozenSet_HashesToItsRecordedGoldenValue()
        {
            ClipSetAsset frozenSet = BuildFrozenSet();

            registryScope.Build(frozenSet);

            ulong actualContentHash =
                ((ulong)registryScope.ContentHash.Value.y << 32) | registryScope.ContentHash.Value.x;

            Assert.AreEqual(
                ExpectedContentHash,
                actualContentHash,
                "The canonical hash stream changed. If that was deliberate, bump " +
                "ClipRegistryBuilder.SchemaVersion and set ExpectedContentHash to 0x" +
                actualContentHash.ToString("X16") + "UL in the same commit. If it was not " +
                "deliberate, the change that caused it breaks compatibility with every subscene " +
                "already baked against schema version " + ClipRegistryBuilder.SchemaVersion + ".");
        }

        [Test]
        public void TryComputeContentHash_MatchesTheKeyBuildProduces_ForTheSameAsset()
        {
            // The whole point of the standalone entry point is that a baker can probe the
            // BlobAssetStore before deciding to build. If it disagreed with Build by even one bit,
            // every probe would miss and the store would fill with duplicates — silently, because
            // nothing else compares the two.
            ClipSetAsset frozenSet = BuildFrozenSet();

            Unity.Entities.Hash128 probedHash;
            bool probed = ClipRegistryBuilder.TryComputeContentHash(frozenSet, out probedHash);
            registryScope.Build(frozenSet);

            Assert.IsTrue(probed, "A bakeable set must yield a key.");
            Assert.AreEqual(
                registryScope.ContentHash,
                probedHash,
                "The probed key must be byte-identical to the one Build produces, or the canonical " +
                "TryGet/build/TryAdd pattern silently stops deduplicating.");
            Assert.AreEqual(
                ExpectedContentHash,
                ((ulong)probedHash.Value.y << 32) | probedHash.Value.x,
                "And it must be the same golden value, reached without allocating a blob.");
        }

        [Test]
        public void TryComputeContentHash_IsPureAndRepeatable_AcrossManyProbes()
        {
            // It builds a blob internally to hash it, allocated with Allocator.Temp and released
            // before returning — the property that makes it safe to call on a store hit. Repeating
            // it proves the probe is a pure function of the asset and exercises that alloc/release
            // path many times over. It does NOT by itself prove the absence of a leak: a Persistent
            // allocation left dangling would be reported by the leak detector at the end of the
            // run, not by this assertion.
            ClipSetAsset frozenSet = BuildFrozenSet();

            Unity.Entities.Hash128 firstHash;
            Assert.IsTrue(ClipRegistryBuilder.TryComputeContentHash(frozenSet, out firstHash));
            for (int probeIndex = 0; probeIndex < 64; probeIndex++)
            {
                Unity.Entities.Hash128 repeatedHash;
                Assert.IsTrue(ClipRegistryBuilder.TryComputeContentHash(frozenSet, out repeatedHash));
                Assert.AreEqual(firstHash, repeatedHash, "Probing must be a pure function of the asset.");
            }
        }

        [Test]
        public void TryComputeContentHash_ReportsFailure_ForANullSetAndForAnInvalidOne()
        {
            // Both false branches. A set that cannot bake has no key, and saying so is what lets a
            // baker skip it rather than throw mid-bake.
            Unity.Entities.Hash128 nullSetHash;
            Assert.IsFalse(
                ClipRegistryBuilder.TryComputeContentHash(null, out nullSetHash),
                "A null set has no key.");
            Assert.AreEqual(default(Unity.Entities.Hash128), nullSetHash, "A failed probe reports no key.");

            // A clip whose track points at a target the rig does not define is a V02 error.
            ClipSetAsset invalidSet = BuildFrozenSet();
            invalidSet.clips[0].transformTracks.Clear();
            TransformTrack orphanedTrack = AuthoringTestAssets.AddTransformTrack(
                invalidSet.clips[0], 0xDEADu, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                orphanedTrack, 0f, new float3(0f, 0f, 0f), 0f, new float3(1f, 1f, 1f),
                Interpolation.Linear);

            Unity.Entities.Hash128 invalidSetHash;
            Assert.IsFalse(
                ClipRegistryBuilder.TryComputeContentHash(invalidSet, out invalidSetHash),
                "A set carrying validation errors is not bakeable, so it has no key.");
            Assert.AreEqual(default(Unity.Entities.Hash128), invalidSetHash, "A failed probe reports no key.");
        }

        [Test]
        public void TheFrozenSet_StampsTheSchemaVersionTheGoldenValueWasRecordedUnder()
        {
            // A golden hash means nothing without the layout version it belongs to: the same bytes
            // under a different layout are a different blob.
            ClipSetAsset frozenSet = BuildFrozenSet();

            registryScope.Build(frozenSet);

            Assert.AreEqual(
                8,
                registryScope.Registry.Value.schemaVersion,
                "The golden value above was recorded under schema version 8. A bump must be paired " +
                "with a re-recorded constant, never landed on its own.");
        }

        /// <summary>
        /// A small set that still touches every part of the canonical stream: two clips out of id
        /// order, two targets out of id order, a transform track with a non-default easing and a
        /// negative scale, a sprite track in each frame mode, an event, and a VAT frame range.
        /// Every id and numeric value is a literal — nothing minted, nothing random — so the hash
        /// is reproducible on any machine and in any session. Note that the clip <em>names</em> are
        /// part of the hashed stream: <c>debugName</c> is <c>clip.name</c>, so renaming a clip here
        /// moves the golden value. That is a fixture edit, not a format change — re-record the
        /// constant, and do NOT bump <c>SchemaVersion</c> for it.
        /// </summary>
        private ClipSetAsset BuildFrozenSet()
        {
            RigAsset rig = assets.CreateRig(
                "GoldenRig", RigKey, 2, new uint[] { BodyTargetId, HeadTargetId });
            rig.targets[0].boundsExtents = new float3(0.5f, 1.25f, 0.25f);
            rig.targets[1].boundsExtents = new float3(0.75f, 0.75f, 0.5f);

            ClipAsset walkClip = assets.CreateClip("GoldenWalk", rig, WalkClipId, 1.25f);
            walkClip.defaultLoop = LoopMode.PingPong;
            walkClip.defaultBlendIn = 0.125f;
            walkClip.defaultBlendOut = 0.25f;
            walkClip.vatSource = new VatClipSource();
            TransformTrack bodyTrack = AuthoringTestAssets.AddTransformTrack(
                walkClip, BodyTargetId, TrackBlendOp.Override,
                AnimatedChannels.PositionXY | AnimatedChannels.Scale);
            AuthoringTestAssets.AddTransformKey(
                bodyTrack, 0f, new float3(0f, 0f, 0f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                bodyTrack, 1f, new float3(2f, -1f, 0.5f), 90f, new float3(-1.5f, 2f, 1f), Interpolation.EaseInOut);
            SpriteTrack headSliceTrack = AuthoringTestAssets.AddSpriteTrack(
                walkClip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(headSliceTrack, 0f, 2, float4.zero);
            AuthoringTestAssets.AddSpriteKey(headSliceTrack, 1f, -1, float4.zero);
            AuthoringTestAssets.AddEvent(walkClip, 0.5f, 16u, -2, 1.5f);

            ClipAsset idleClip = assets.CreateClip("GoldenIdle", rig, IdleClipId, 0.5f);
            // Restated rather than inherited from AuthoringTestAssets: these two are in the hashed
            // stream, so leaving them at the shared helper's defaults would let an unrelated edit to
            // that helper move the golden value and demand a schema bump for no real format change.
            idleClip.defaultLoop = LoopMode.Loop;
            idleClip.defaultBlendIn = 0.1f;
            idleClip.defaultBlendOut = 0.2f;
            SpriteTrack bodyAtlasTrack = AuthoringTestAssets.AddSpriteTrack(
                idleClip, BodyTargetId, SpriteFrameMode.AtlasRect);
            AuthoringTestAssets.AddSpriteKey(
                bodyAtlasTrack, 0f, -1, new float4(0.25f, 0.5f, 0.125f, 0.375f));

            // Authored highest-id-first so the canonical clip sort has something to normalise.
            ClipSetAsset clipSet = assets.CreateSet("GoldenSet", rig, SetKey, idleClip, walkClip);

            VatTextureSetAsset vatTextureSet = assets.CreateVatTextureSet("GoldenVatSet", VatSetKey);
            vatTextureSet.flavor = VatFlavor.BoneMatrix;
            vatTextureSet.boneCount = 12;
            vatTextureSet.textureWidth = 64;
            vatTextureSet.rowsPerFrame = 1;
            vatTextureSet.clipRanges.Add(new VatClipRange
            {
                clipId = WalkClipId,
                frameStart = 8,
                frameCount = 16,
                fps = 30f,
                bounds = new UnityEngine.Bounds(
                    new UnityEngine.Vector3(0f, 1f, 0f),
                    new UnityEngine.Vector3(2f, 3f, 2f))
            });
            clipSet.vatTextures = vatTextureSet;
            return clipSet;
        }
    }
}
