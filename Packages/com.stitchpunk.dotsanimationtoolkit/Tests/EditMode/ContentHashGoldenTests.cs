// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
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
        private const ulong ExpectedContentHash = 0x7262FF88711EB9F9UL;

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
        public void TheFrozenSet_StampsTheSchemaVersionTheGoldenValueWasRecordedUnder()
        {
            // A golden hash means nothing without the layout version it belongs to: the same bytes
            // under a different layout are a different blob.
            ClipSetAsset frozenSet = BuildFrozenSet();

            registryScope.Build(frozenSet);

            Assert.AreEqual(
                2,
                registryScope.Registry.Value.schemaVersion,
                "The golden value above was recorded under schema version 2. A bump must be paired " +
                "with a re-recorded constant, never landed on its own.");
        }

        /// <summary>
        /// A small set that still touches every part of the canonical stream: two clips out of id
        /// order, two targets out of id order, a transform track with a non-default easing and a
        /// negative scale, a sprite track in each frame mode, an event, and a VAT frame range.
        /// Every id and value is a literal — nothing minted, nothing derived from a name — so the
        /// hash is reproducible on any machine and in any session.
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
                bodyTrack, 0f, new float3(0f, 0f, 0f), 0f, new float2(1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                bodyTrack, 1f, new float3(2f, -1f, 0.5f), 90f, new float2(-1.5f, 2f), Interpolation.EaseInOut);
            SpriteTrack headSliceTrack = AuthoringTestAssets.AddSpriteTrack(
                walkClip, HeadTargetId, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(headSliceTrack, 0f, 2, float4.zero);
            AuthoringTestAssets.AddSpriteKey(headSliceTrack, 1f, -1, float4.zero);
            AuthoringTestAssets.AddEvent(walkClip, 0.5f, 16u, -2, 1.5f);

            ClipAsset idleClip = assets.CreateClip("GoldenIdle", rig, IdleClipId, 0.5f);
            idleClip.defaultLoop = LoopMode.Loop;
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
