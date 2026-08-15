// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The construction half of the module M1 acceptance list: that
    /// <see cref="ClipRegistryBuilder"/> populates the architecture section 4.2 schema with the
    /// section 4.5 canonical ordering and value conversions, and the section 4.6 conservative
    /// bounds. Every expectation here is derived from the architecture, not from the builder's own
    /// output.
    /// </summary>
    public sealed class ClipRegistryBuilderTests
    {
        private const float FloatTolerance = 1e-5f;

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

        // -----------------------------------------------------------------------------------
        // Canonical ordering and the id indirection.
        // -----------------------------------------------------------------------------------

        [Test]
        public void Build_OrdersClipsByAscendingClipId_AndResolvesEveryOneOfThem()
        {
            ulong[] authoredClipIds = new ulong[] { 0x50UL, 0x02UL, 0x100UL, 0x09UL };
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset[] clips = new ClipAsset[authoredClipIds.Length];
            for (int clipIndex = 0; clipIndex < authoredClipIds.Length; clipIndex++)
            {
                clips[clipIndex] = CreateKeyedClip(rig, "Clip" + clipIndex, authoredClipIds[clipIndex], 7u);
            }
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clips);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            ulong[] expectedAscendingIds = new ulong[] { 0x02UL, 0x09UL, 0x50UL, 0x100UL };
            Assert.AreEqual(4, registry.clips.Length, "Every authored clip must be baked.");
            for (int denseIndex = 0; denseIndex < expectedAscendingIds.Length; denseIndex++)
            {
                Assert.AreEqual(
                    expectedAscendingIds[denseIndex],
                    registry.sortedClipIds[denseIndex],
                    "sortedClipIds must be ascending, independent of authoring list order.");
                Assert.AreEqual(
                    expectedAscendingIds[denseIndex],
                    registry.clips[denseIndex].clipId,
                    "clips must be stored in the same ascending-id order as sortedClipIds, so a " +
                    "clip's dense index is its position in both arrays.");
            }

            for (int clipIndex = 0; clipIndex < authoredClipIds.Length; clipIndex++)
            {
                int resolvedIndex;
                Assert.IsTrue(
                    ClipRegistryUtil.TryResolveClip(
                        ref registry, new ClipId(authoredClipIds[clipIndex]), out resolvedIndex),
                    "Every baked clip must resolve through the section 4.3 lookup contract.");
                Assert.AreEqual(
                    authoredClipIds[clipIndex],
                    registry.clips[resolvedIndex].clipId,
                    "Resolution must land on the clip that owns the id.");
            }
        }

        [Test]
        public void Build_AssignsDenseTargetIndicesByAscendingTargetId()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 9u, 2u, 5u });
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 9u);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            Assert.AreEqual(3, registry.sortedTargetIds.Length, "Every target row must be baked.");
            Assert.AreEqual(2u, registry.sortedTargetIds[0], "Target ids must be ascending.");
            Assert.AreEqual(5u, registry.sortedTargetIds[1], "Target ids must be ascending.");
            Assert.AreEqual(9u, registry.sortedTargetIds[2], "Target ids must be ascending.");
            Assert.AreEqual(
                2,
                registry.clips[0].transformTracks[0].targetIndex,
                "A track's dense target index is its target's position in the sorted id array.");

            int resolvedTargetIndex;
            Assert.IsTrue(
                ClipRegistryUtil.ResolveTargetIndex(ref registry, new TargetId(9u), out resolvedTargetIndex),
                "Every baked target must resolve through the section 4.3 lookup contract.");
            Assert.AreEqual(2, resolvedTargetIndex, "Target resolution must agree with the dense order.");
        }

        [Test]
        public void Build_SortsTracksByDenseTargetIndex_AndKeepsBothTracksThatShareATarget()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u, 3u });
            ClipAsset clip = assets.CreateClip("Walk", rig, 0x10UL, 1f);
            AddSingleKeyTrack(clip, 7u, AnimatedChannels.PositionXY);
            AddSingleKeyTrack(clip, 3u, AnimatedChannels.Rotation);
            AddSingleKeyTrack(clip, 7u, AnimatedChannels.Scale);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;
            ref ClipBlob clipBlob = ref registry.clips[0];

            // Dense target order is 3 then 7, so the middle authored track sorts first.
            Assert.AreEqual(3, clipBlob.transformTracks.Length, "Every authored track must be kept.");
            Assert.AreEqual(0, clipBlob.transformTracks[0].targetIndex, "Tracks sort by dense target index.");
            Assert.AreEqual(1, clipBlob.transformTracks[1].targetIndex, "Tracks sort by dense target index.");
            Assert.AreEqual(1, clipBlob.transformTracks[2].targetIndex, "Tracks sort by dense target index.");
            Assert.AreEqual(
                AnimatedChannels.Rotation,
                clipBlob.transformTracks[0].channels,
                "The track on the lower dense target index comes first.");
            Assert.AreEqual(
                AnimatedChannels.PositionXY,
                clipBlob.transformTracks[1].channels,
                "Two tracks on one target keep their authoring list order.");
            Assert.AreEqual(
                AnimatedChannels.Scale,
                clipBlob.transformTracks[2].channels,
                "Two tracks on one target keep their authoring list order.");
        }

        [Test]
        public void Build_SortsEventsByTime_WithAuthoringOrderBreakingTies()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 7u);
            AuthoringTestAssets.AddEvent(clip, 0.9f, 20u, 0, 0f);
            AuthoringTestAssets.AddEvent(clip, 0.1f, 21u, 0, 0f);
            AuthoringTestAssets.AddEvent(clip, 0.5f, 22u, 0, 0f);
            AuthoringTestAssets.AddEvent(clip, 0.5f, 23u, 0, 0f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipBlob clipBlob = ref registryScope.Registry.Value.clips[0];

            Assert.AreEqual(4, clipBlob.events.Length, "Every marker must be baked.");
            Assert.AreEqual(21u, clipBlob.events[0].eventKey, "Markers sort by normalized time.");
            Assert.AreEqual(22u, clipBlob.events[1].eventKey, "Equal times keep authoring order.");
            Assert.AreEqual(23u, clipBlob.events[2].eventKey, "Equal times keep authoring order.");
            Assert.AreEqual(20u, clipBlob.events[3].eventKey, "Markers sort by normalized time.");
        }

        // -----------------------------------------------------------------------------------
        // Canonical values (architecture section 4.5 point 2).
        // -----------------------------------------------------------------------------------

        [Test]
        public void Build_ConvertsKeyRotationFromAuthoredDegreesToBakedRadians()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = assets.CreateClip("Walk", rig, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.Rotation);
            AuthoringTestAssets.AddTransformKey(
                track, 0f, float3.zero, 90f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                track, 1f, float3.zero, -180f, new float3(1f, 1f, 1f), Interpolation.Linear);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref TransformTrackBlob trackBlob = ref registryScope.Registry.Value.clips[0].transformTracks[0];

            Assert.AreEqual(
                math.PI * 0.5f, trackBlob.keys[0].rotation.z, FloatTolerance,
                "90 degrees must bake to pi/2 radians.");
            Assert.AreEqual(
                -math.PI, trackBlob.keys[1].rotation.z, FloatTolerance,
                "-180 degrees must bake to -pi radians.");
        }

        [Test]
        public void Build_ClampsBlendDefaultsToTheClipDuration()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 7u);
            clip.duration = 0.75f;
            clip.defaultBlendIn = 3f;
            clip.defaultBlendOut = 0.25f;
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipBlob clipBlob = ref registryScope.Registry.Value.clips[0];

            Assert.AreEqual(0.75f, clipBlob.defaultBlendIn, FloatTolerance, "Blend-in must clamp to the duration.");
            Assert.AreEqual(0.25f, clipBlob.defaultBlendOut, FloatTolerance, "A blend inside the duration is untouched.");
        }

        [Test]
        public void Build_ResolvesTheUseClipDefaultSentinelToOnce()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset sentinelClip = CreateKeyedClip(rig, "Sentinel", 0x10UL, 7u);
            sentinelClip.defaultLoop = LoopMode.UseClipDefault;
            ClipAsset pingPongClip = CreateKeyedClip(rig, "PingPong", 0x20UL, 7u);
            pingPongClip.defaultLoop = LoopMode.PingPong;
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, sentinelClip, pingPongClip);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            Assert.AreEqual(
                LoopMode.Once,
                registry.clips[0].defaultLoop,
                "A baked default loop mode is always resolved; UseClipDefault would be circular.");
            Assert.AreEqual(
                LoopMode.PingPong,
                registry.clips[1].defaultLoop,
                "An authored loop mode must be baked unchanged.");
        }

        [Test]
        public void Build_StampsTheRegistryHeaderFromTheSetAndTheRig()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 5, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 7u);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 0xDEADBEEFUL, clip);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            Assert.AreEqual(ClipRegistryBuilder.SchemaVersion, registry.schemaVersion, "The schema version must be stamped.");
            Assert.AreEqual(0xDEADBEEFUL, registry.setKey, "The set key must be mirrored from the asset.");
            Assert.AreEqual(0UL, registry.vatSetKey, "A set with no VAT textures must carry a zero VAT key.");
            Assert.AreEqual((byte)5, registry.layerCount, "The layer count must come from the rig.");
            Assert.AreEqual(1, registry.vatInfo.rowsPerFrame, "Rows per frame must stay usable when there is no VAT set.");
        }

        // -----------------------------------------------------------------------------------
        // VAT metadata.
        // -----------------------------------------------------------------------------------

        [Test]
        public void Build_WritesMinusOneFrameStart_ForAClipWithNoVatRange()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 7u);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipBlob clipBlob = ref registryScope.Registry.Value.clips[0];

            Assert.AreEqual(-1, clipBlob.vatFrameStart, "A clip with no VAT range is marked with frame start -1.");
            Assert.AreEqual(0, clipBlob.vatFrameCount, "A clip with no VAT range has no frames.");
        }

        [Test]
        public void Build_MirrorsTheVatSetKeyAddressingParametersAndFrameRange()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 7u);
            clip.vatSource = new VatClipSource();
            VatTextureSetAsset vatTextureSet = assets.CreateVatTextureSet("VatSet", 0xFEEDUL);
            vatTextureSet.clipRanges.Add(new VatClipRange
            {
                clipId = 0x10UL,
                frameStart = 5,
                frameCount = 7,
                fps = 24f,
                bounds = new UnityEngine.Bounds(
                    new UnityEngine.Vector3(0f, 10f, 0f),
                    new UnityEngine.Vector3(2f, 2f, 2f))
            });
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);
            clipSet.vatTextures = vatTextureSet;

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;
            ref ClipBlob clipBlob = ref registry.clips[0];

            Assert.AreEqual(0xFEEDUL, registry.vatSetKey, "The VAT set key links the blob to its textures.");
            Assert.AreEqual(VatFlavor.BoneMatrix, registry.vatInfo.flavor, "The flavor must be mirrored.");
            Assert.AreEqual(9, registry.vatInfo.textureWidth, "The texture width must be mirrored.");
            Assert.AreEqual(1, registry.vatInfo.rowsPerFrame, "Rows per frame must be mirrored.");
            Assert.AreEqual(
                3,
                registry.vatInfo.boneOrVertexCount,
                "A bone-flavor set contributes its bone count.");
            Assert.AreEqual(5, clipBlob.vatFrameStart, "The frame start must be mirrored.");
            Assert.AreEqual(7, clipBlob.vatFrameCount, "The frame count must be mirrored.");
            Assert.AreEqual(24f, clipBlob.vatFps, FloatTolerance, "The sample rate must be mirrored.");
            Assert.GreaterOrEqual(
                clipBlob.offsetBounds.Max.y,
                11f,
                "Section 4.6: a VAT clip's measured bounds must be unioned into its local bounds.");
        }

        // -----------------------------------------------------------------------------------
        // Bounds at bake (architecture section 4.6).
        // -----------------------------------------------------------------------------------

        [Test]
        public void Build_UnionsScaledKeyExtentsWithTheRestPoseOfUntrackedTargets()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u, 3u });
            ClipAsset clip = assets.CreateClip("Walk", rig, 0x10UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 7u, TrackBlendOp.Override, AnimatedChannels.PositionXY | AnimatedChannels.Scale);
            AuthoringTestAssets.AddTransformKey(
                track, 0f, new float3(1f, 2f, 9f), 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                track, 1f, new float3(-3f, 0f, 0f), 0f, new float3(2f, 1f, 1f), Interpolation.Linear);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipBlob clipBlob = ref registryScope.Registry.Value.clips[0];

            // Key 0: centre (1,2,9) with half-extents 0.5 (scale factor 1).
            // Key 1: centre (-3,0,0) with half-extents 1.0 (scale factor max(|2|,|1|,|1|,1) = 2).
            // Untracked target 3 contributes its rest box, +/-0.5 around the origin.
            // z spans [-1, 9.5] once the first key's z = 9 is included, so centre 4.25, extent 5.25.
            Assert.AreEqual(-1.25f, clipBlob.offsetBounds.Center.x, FloatTolerance, "Bounds centre x.");
            Assert.AreEqual(0.75f, clipBlob.offsetBounds.Center.y, FloatTolerance, "Bounds centre y.");
            Assert.AreEqual(4.25f, clipBlob.offsetBounds.Center.z, FloatTolerance, "Bounds centre z.");
            Assert.AreEqual(2.75f, clipBlob.offsetBounds.Extents.x, FloatTolerance, "Bounds half-extent x.");
            Assert.AreEqual(1.75f, clipBlob.offsetBounds.Extents.y, FloatTolerance, "Bounds half-extent y.");
            Assert.AreEqual(
                5.25f,
                clipBlob.offsetBounds.Extents.z,
                FloatTolerance,
                "A key's z displacement widens the box like any other axis. It did not while " +
                "transform data was 2.5D and z meant draw-layer order; now that a rig can move in " +
                "depth, a box that ignored z would cull a vehicle the moment it drove away from " +
                "the origin plane.");
        }

        [Test]
        public void Build_ClampsNegativeAuthoredTargetExtentsToZero()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            rig.targets[0].boundsExtents = new float3(-1f, 0.5f, 0.25f);
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 7u);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            Assert.AreEqual(0f, registry.targetBoundsExtents[0].x, FloatTolerance, "Negative extents clamp to zero.");
            Assert.AreEqual(0.5f, registry.targetBoundsExtents[0].y, FloatTolerance, "Valid extents are untouched.");
            Assert.AreEqual(0.25f, registry.targetBoundsExtents[0].z, FloatTolerance, "Valid extents are untouched.");
        }

        [Test]
        public void Build_ProducesAZeroBox_WhenTheRigDeclaresNoTargets()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[0]);
            ClipAsset clip = assets.CreateClip("Empty", rig, 0x10UL, 1f);
            AuthoringTestAssets.AddEvent(clip, 0f, 16u, 0, 0f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            Assert.AreEqual(0, registry.sortedTargetIds.Length, "A rig with no targets bakes no dense targets.");
            Assert.AreEqual(0f, registry.clips[0].offsetBounds.Extents.x, FloatTolerance, "Empty bounds must be a zero box.");
            Assert.AreEqual(0f, registry.clips[0].offsetBounds.Extents.y, FloatTolerance, "Empty bounds must be a zero box.");
            Assert.AreEqual(0f, registry.clips[0].offsetBounds.Extents.z, FloatTolerance, "Empty bounds must be a zero box.");
            Assert.AreEqual(0f, registry.clips[0].offsetBounds.Center.x, FloatTolerance, "Empty bounds must sit at the origin.");
        }

        [Test]
        public void Build_CopiesTheAssetNameIntoTheClipDebugName()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, "WalkForwardFast", 0x10UL, 7u);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);

            Assert.AreEqual(
                "WalkForwardFast",
                registryScope.Registry.Value.clips[0].debugName.ToString(),
                "The debug name is the asset name, for logs and the inspector only.");
        }

        [Test]
        public void Build_TruncatesAnOverlongAssetNameInsteadOfThrowing()
        {
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, new string('N', 200), 0x10UL, 7u);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);

            Assert.LessOrEqual(
                registryScope.Registry.Value.clips[0].debugName.Length,
                61,
                "A name longer than the fixed string must be truncated, never overflow it.");
            // Length alone would pass if truncation kept a single character, so pin the content:
            // the surviving text must be the name's leading run, not an arbitrary fragment.
            Assert.AreEqual(
                new string('N', registryScope.Registry.Value.clips[0].debugName.Length),
                registryScope.Registry.Value.clips[0].debugName.ToString(),
                "Truncation must keep the leading characters rather than drop or reorder them.");
            Assert.GreaterOrEqual(
                registryScope.Registry.Value.clips[0].debugName.Length,
                60,
                "Truncation must fill the fixed string, not discard most of the name.");
        }

        // -----------------------------------------------------------------------------------
        // Branches and shapes the acceptance fixtures above never reach.
        // -----------------------------------------------------------------------------------

        [Test]
        public void Build_UsesTheVertexCount_ForAVertexFlavorVatSet()
        {
            // Every other VAT fixture is bone-flavored, so the vertex branch of BuildVatInfo was
            // never executed — the flavor that feeds the vertex-position VAT path.
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipAsset clip = CreateKeyedClip(rig, "Walk", 0x10UL, 7u);
            clip.vatSource = new VatClipSource();
            VatTextureSetAsset vatTextureSet = assets.CreateVatTextureSet("VatSet", 0xFEEDUL);
            vatTextureSet.flavor = VatFlavor.VertexPosition;
            vatTextureSet.vertexCount = 1024;
            vatTextureSet.rowsPerFrame = 3;
            vatTextureSet.clipRanges.Add(new VatClipRange
            {
                clipId = 0x10UL,
                frameStart = 0,
                frameCount = 4,
                fps = 30f,
                bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one)
            });
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);
            clipSet.vatTextures = vatTextureSet;

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            Assert.AreEqual(
                VatFlavor.VertexPosition, registry.vatInfo.flavor, "The vertex flavor must be mirrored.");
            Assert.AreEqual(
                1024,
                registry.vatInfo.boneOrVertexCount,
                "A vertex-flavor set contributes its vertex count, not its bone count.");
            Assert.AreEqual(3, registry.vatInfo.rowsPerFrame, "Rows per frame must be mirrored.");
        }

        [Test]
        public void Build_SortsSpriteTracksByDenseTargetIndex_KeepingAuthoringOrderOnTies()
        {
            // Transform track ordering is asserted above; the sprite path had no direct assertion.
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u, 3u });
            ClipAsset clip = assets.CreateClip("Walk", rig, 0x10UL, 1f);
            SpriteTrack highTargetTrack = AuthoringTestAssets.AddSpriteTrack(
                clip, 7u, SpriteFrameMode.Slice);
            AuthoringTestAssets.AddSpriteKey(highTargetTrack, 0f, 1, float4.zero);
            SpriteTrack lowTargetTrack = AuthoringTestAssets.AddSpriteTrack(
                clip, 3u, SpriteFrameMode.AtlasRect);
            AuthoringTestAssets.AddSpriteKey(lowTargetTrack, 0f, -1, new float4(0f, 0f, 1f, 1f));
            SpriteTrack secondHighTargetTrack = AuthoringTestAssets.AddSpriteTrack(
                clip, 7u, SpriteFrameMode.AtlasRect);
            AuthoringTestAssets.AddSpriteKey(secondHighTargetTrack, 0f, -1, new float4(0f, 0f, 0.5f, 0.5f));
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL, clip);

            registryScope.Build(clipSet);
            ref ClipBlob clipBlob = ref registryScope.Registry.Value.clips[0];

            // Dense target order is 3 then 7, so the second authored track sorts first.
            Assert.AreEqual(3, clipBlob.spriteTracks.Length, "Every authored sprite track must be kept.");
            Assert.AreEqual(0, clipBlob.spriteTracks[0].targetIndex, "Sprite tracks sort by dense target index.");
            Assert.AreEqual(1, clipBlob.spriteTracks[1].targetIndex, "Sprite tracks sort by dense target index.");
            Assert.AreEqual(1, clipBlob.spriteTracks[2].targetIndex, "Sprite tracks sort by dense target index.");
            Assert.AreEqual(
                SpriteFrameMode.Slice,
                clipBlob.spriteTracks[1].mode,
                "Two sprite tracks on one target keep their authoring list order.");
            Assert.AreEqual(
                SpriteFrameMode.AtlasRect,
                clipBlob.spriteTracks[2].mode,
                "Two sprite tracks on one target keep their authoring list order.");
        }

        [Test]
        public void Build_ProducesAnEmptyRegistry_ForASetWithNoClips()
        {
            // The degenerate shape a new ClipSetAsset has before any clip is added. It must bake
            // rather than throw, and must still resolve cleanly as "nothing here".
            RigAsset rig = assets.CreateRig("Rig", 1UL, 1, new uint[] { 7u });
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 2UL);

            registryScope.Build(clipSet);
            ref ClipRegistryBlob registry = ref registryScope.Registry.Value;

            Assert.AreEqual(0, registry.clips.Length, "An empty set bakes an empty clip array.");
            Assert.AreEqual(0, registry.sortedClipIds.Length, "An empty set bakes an empty id array.");
            Assert.AreEqual(1, registry.sortedTargetIds.Length, "The rig's targets still bake.");

            int resolvedIndex;
            Assert.IsFalse(
                ClipRegistryUtil.TryResolveClip(ref registry, new ClipId(0x10UL), out resolvedIndex),
                "Resolving against an empty registry must fail rather than read out of bounds.");
            Assert.AreEqual(-1, resolvedIndex, "A failed resolve reports -1.");
        }

        // -----------------------------------------------------------------------------------
        // Fixture helpers.
        // -----------------------------------------------------------------------------------

        private ClipAsset CreateKeyedClip(RigAsset rig, string assetName, ulong clipId, uint targetId)
        {
            ClipAsset clip = assets.CreateClip(assetName, rig, clipId, 1f);
            AddSingleKeyTrack(clip, targetId, AnimatedChannels.PositionXY);
            return clip;
        }

        private static void AddSingleKeyTrack(ClipAsset clip, uint targetId, AnimatedChannels channels)
        {
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, targetId, TrackBlendOp.Override, channels);
            AuthoringTestAssets.AddTransformKey(
                track, 0f, float3.zero, 0f, new float3(1f, 1f, 1f), Interpolation.Linear);
        }
    }
}
