// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The disk round-trip tier (architecture section 11.1), the debt amendment A36 left behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why this tier exists.</strong> <c>ClipAsset.vatSource</c> is a plain
    /// <c>[Serializable]</c> class field, and Unity cannot serialize null for a field of that shape —
    /// every clip asset that had ever been saved and re-read came back with a default-constructed
    /// <c>VatClipSource</c> instead of null. Validation rule V07 tested <c>clip.vatSource == null</c>
    /// to mean "this clip has no VAT source", which was true in memory and false on disk. The result:
    /// every real clip in the project read as VAT-sourced, V07 failed on any set without a texture
    /// set, <see cref="ClipRegistryBuilder.Build"/> threw, no registry baked at all, and every actor
    /// in the game froze in its rest pose. 221 passing fixtures did not see it because every one of
    /// them constructed its input in memory rather than building a real, saved asset.
    /// </para>
    /// <para>
    /// The lesson recorded in the handoff: a suite that never leaves memory has no coverage of the
    /// serializer, and the serializer is part of the authoring contract. Every fixture here therefore
    /// does the same four things: build an asset graph in memory, write it to disk with
    /// <see cref="AssetDatabase.CreateAsset"/>, force Unity to forget the in-memory instances with
    /// <see cref="Resources.UnloadAsset"/> so the reload is not just handed back the object already
    /// sitting in memory, and only then read it back with <see cref="AssetDatabase.LoadAssetAtPath"/>
    /// and assert. Anything that never crosses the serializer is not what this tier is for — that is
    /// what the rest of the EditMode suite already covers.
    /// </para>
    /// </remarks>
    public sealed class DiskRoundTripTests
    {
        private const float FloatTolerance = 1e-5f;

        // A subfolder of the package's own Tests/EditMode folder is guaranteed to already be a valid
        // asset folder (this file lives there), so folder creation never has to walk and create
        // intermediate parents the way a host-project temp path would.
        private const string TestsFolderPath =
            "Packages/com.stitchpunk.dotsanimationtoolkit/Tests/EditMode";

        private string scratchFolderPath;

        [SetUp]
        public void SetUp()
        {
            string scratchFolderName = "DiskRoundTripScratch_" + Guid.NewGuid().ToString("N");
            string createdFolderGuid = AssetDatabase.CreateFolder(TestsFolderPath, scratchFolderName);
            Assert.IsFalse(
                string.IsNullOrEmpty(createdFolderGuid),
                "Failed to create the scratch folder the disk round-trip fixtures write into.");
            scratchFolderPath = AssetDatabase.GUIDToAssetPath(createdFolderGuid);
        }

        // Robust to a failing test: TearDown runs whether or not the test's own assertions threw,
        // and the guard below tolerates SetUp itself never having produced a folder.
        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(scratchFolderPath) && AssetDatabase.IsValidFolder(scratchFolderPath))
            {
                AssetDatabase.DeleteAsset(scratchFolderPath);
            }
            scratchFolderPath = null;
            AssetDatabase.Refresh();
        }

        // -----------------------------------------------------------------------------------
        // Fixture 1 — the regression that would have caught the shipping blocker.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches exactly amendment A36: a clip with no VAT source, saved and reloaded, must still
        /// validate as having none, and the registry must still build. Before the fix,
        /// <c>vatSource</c> came back a default-constructed <see cref="VatClipSource"/> rather than
        /// null, V07 fired on every such clip, and <see cref="ClipRegistryBuilder.Build"/> threw for
        /// every clip set in the game — this is the exact failure mode, reproduced against a real
        /// saved asset instead of an in-memory fixture that cannot see it.
        /// </summary>
        [Test]
        public void ReloadedClipWithNoVatSource_StillValidatesAsHavingNone_AndTheRegistryStillBuilds()
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.name = "Rig";
            rig.stableId = 0x1111000000000001UL;
            rig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });
            rig.targets.Add(new RigTargetDefinition
            {
                displayName = "Body",
                stableId = 0x0AAA0001u,
                kind = TargetKind.Quad,
                boundsExtents = new float3(0.5f, 0.5f, 0.5f)
            });

            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.name = "Idle";
            clip.stableId = 0x2222000000000001UL;
            clip.rig = rig;
            clip.duration = 1f;
            // vatSource is deliberately left at its default (null) — the exact authoring state A36
            // silently corrupted on disk.

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.name = "Set";
            clipSet.stableId = 0x3333000000000001UL;
            clipSet.rig = rig;
            clipSet.clips.Add(clip);

            SaveAsset(rig);
            SaveAsset(clip);
            string clipSetPath = SaveAsset(clipSet);

            CommitAndForceReload(rig, clip, clipSet);

            ClipSetAsset reloadedSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(clipSetPath);
            Assert.IsNotNull(reloadedSet, "The clip set must reload from disk.");
            Assert.AreEqual(1, reloadedSet.clips.Count, "The reloaded set must still register its one clip.");
            ClipAsset reloadedClip = reloadedSet.clips[0];
            Assert.IsNotNull(reloadedClip, "The reloaded set's clip reference must not be null.");

            // The A36 trap itself: vatSource very likely comes back non-null (a default-constructed
            // VatClipSource), because Unity cannot serialize null for a plain [Serializable] class
            // field. What must be true regardless is that it names no source clip.
            if (reloadedClip.vatSource != null)
            {
                Assert.IsNull(
                    reloadedClip.vatSource.sourceClip,
                    "A clip saved with no VAT source must not read back naming a real source clip.");
            }

            List<ValidationMessage> messages = ClipValidation.ValidateSet(reloadedSet);
            foreach (ValidationMessage message in messages)
            {
                Assert.AreNotEqual(
                    ValidationCode.V07,
                    message.code,
                    "A36 regression: a clip with no VAT source must not fail V07 after a real disk " +
                    "round trip. " + message.text);
            }

            BlobAssetReference<ClipRegistryBlob> registry = default;
            Unity.Entities.Hash128 contentHash = default;
            try
            {
                Assert.DoesNotThrow(
                    () => ClipRegistryBuilder.Build(reloadedSet, out registry, out contentHash),
                    "ClipRegistryBuilder.Build must succeed for a set whose only clip has no VAT " +
                    "source, even after a real serialize/deserialize round trip (amendment A36). " +
                    "Before the fix this threw ClipValidationException for every clip set in the " +
                    "game and no registry baked at all.");
            }
            finally
            {
                if (registry.IsCreated)
                {
                    registry.Dispose();
                }
            }
        }

        // -----------------------------------------------------------------------------------
        // Fixture 2 — stable ids are the binding contract; they must survive intact.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: a stable id silently regenerated on load. Ids are what tracks, clip sets, and the
        /// baked registry bind to — a rig, clip, set, or target id that changed shape across a real
        /// save/reload would rebind every track in the project to the wrong part or the wrong clip,
        /// which is exactly the kind of failure an in-memory fixture (identical instance, identical
        /// id, trivially) cannot exercise.
        /// </summary>
        [Test]
        public void StableIds_OnRig_Clip_Set_AndTargets_SurviveARealSaveAndReload()
        {
            const ulong RigStableId = 0x8000000000000001UL; // long.MaxValue + 2: the signed-overflow case.
            const ulong ClipStableId = 0xF0E1D2C3B4A59687UL; // High bit set.
            const ulong SetStableId = 0x4444000000000001UL;
            const uint FirstTargetId = 0xFFFFFFFFu; // uint.MaxValue.
            const uint SecondTargetId = 0x00000001u;

            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.name = "Rig";
            rig.stableId = RigStableId;
            rig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });
            rig.targets.Add(new RigTargetDefinition { displayName = "Head", stableId = FirstTargetId, kind = TargetKind.Quad });
            rig.targets.Add(new RigTargetDefinition { displayName = "Body", stableId = SecondTargetId, kind = TargetKind.Quad });

            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.name = "Walk";
            clip.stableId = ClipStableId;
            clip.rig = rig;
            clip.duration = 1f;
            AuthoringTestAssets.AddTransformTrack(clip, FirstTargetId, TrackBlendOp.Override, AnimatedChannels.PositionXY);

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.name = "Set";
            clipSet.stableId = SetStableId;
            clipSet.rig = rig;
            clipSet.clips.Add(clip);

            SaveAsset(rig);
            SaveAsset(clip);
            string clipSetPath = SaveAsset(clipSet);

            CommitAndForceReload(rig, clip, clipSet);

            ClipSetAsset reloadedSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(clipSetPath);
            Assert.IsNotNull(reloadedSet);
            RigAsset reloadedRig = reloadedSet.rig;
            ClipAsset reloadedClip = reloadedSet.clips[0];

            Assert.AreEqual(RigStableId, reloadedRig.StableId, "The rig id must survive the round trip, including its high bit.");
            Assert.AreEqual(SetStableId, reloadedSet.StableId, "The set id must survive the round trip.");
            Assert.AreEqual(ClipStableId, reloadedClip.Id.Value, "The clip id must survive the round trip, including its high bit.");
            Assert.AreEqual(2, reloadedRig.targets.Count, "Both target rows must survive the round trip.");
            Assert.AreEqual(FirstTargetId, reloadedRig.targets[0].Id.Value, "A target id at uint.MaxValue must survive intact.");
            Assert.AreEqual(SecondTargetId, reloadedRig.targets[1].Id.Value, "A second target id must survive intact.");
            Assert.AreEqual(
                FirstTargetId,
                reloadedClip.transformTracks[0].targetId,
                "The track's targetId must still bind to the same target id after reload.");
        }

        // -----------------------------------------------------------------------------------
        // Fixture 3 — the positive case, so fixture 1 cannot be satisfied by a resolver that
        // simply never detects a VAT source at all.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: a fix for A36 that goes too far and stops detecting a real VAT source at all — for
        /// example, treating any reloaded <c>vatSource</c> as absent regardless of its contents. A
        /// clip that genuinely names a source clip, reloaded from disk with a set that references no
        /// texture set for it, must still fail V07: that is proof the reloaded instance is read as
        /// VAT-sourced, not merely proof that V07 stayed quiet.
        /// </summary>
        [Test]
        public void ReloadedClipWithARealVatSource_StillReadsAsVatSourced()
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.name = "Rig";
            rig.stableId = 0x5555000000000001UL;
            rig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });
            rig.targets.Add(new RigTargetDefinition { displayName = "Body", stableId = 0x0BBB0001u, kind = TargetKind.Quad });

            AnimationClip sourceAnimationClip = new AnimationClip();
            sourceAnimationClip.name = "SourceAnim";

            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.name = "Attack";
            clip.stableId = 0x6666000000000001UL;
            clip.rig = rig;
            clip.duration = 1f;
            clip.vatSource = new VatClipSource
            {
                sourceClip = sourceAnimationClip,
                sampleFps = 30f,
                loopSafe = false
            };

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.name = "Set";
            clipSet.stableId = 0x7777000000000001UL;
            clipSet.rig = rig;
            clipSet.clips.Add(clip);
            // vatTextures is deliberately left null: the set references no texture set for a clip
            // that names one, so a correctly-detecting reload must fail V07.

            SaveAsset(sourceAnimationClip, "SourceAnim.anim");
            SaveAsset(rig);
            SaveAsset(clip);
            string clipSetPath = SaveAsset(clipSet);

            CommitAndForceReload(sourceAnimationClip, rig, clip, clipSet);

            ClipSetAsset reloadedSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(clipSetPath);
            Assert.IsNotNull(reloadedSet);
            ClipAsset reloadedClip = reloadedSet.clips[0];

            Assert.IsNotNull(reloadedClip.vatSource, "A clip authored with a VAT source must not read back with none.");
            Assert.IsNotNull(
                reloadedClip.vatSource.sourceClip,
                "A clip authored with a real VAT source clip must not read back naming none.");
            Assert.AreEqual(
                "SourceAnim",
                reloadedClip.vatSource.sourceClip.name,
                "The reloaded VAT source must still name the same Unity AnimationClip asset.");

            List<ValidationMessage> messages = ClipValidation.ValidateSet(reloadedSet);
            bool foundExpectedV07Error = false;
            foreach (ValidationMessage message in messages)
            {
                if (message.code == ValidationCode.V07 && message.severity == ValidationSeverity.Error)
                {
                    foundExpectedV07Error = true;
                }
            }
            Assert.IsTrue(
                foundExpectedV07Error,
                "A clip that genuinely has a VAT source, reloaded from disk, must still fail V07 " +
                "against a set with no matching texture set coverage — proof the reload is read as " +
                "VAT-sourced rather than the fix having simply stopped detecting VAT sources at all.");
        }

        // -----------------------------------------------------------------------------------
        // Fixture 4 — the content hash must not move across a reload, or dedup and rebake both break.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: any field the hash covers reading back even slightly differently shaped (a
        /// different array length, a reordered list, a coerced float) than what was hashed before the
        /// save. <see cref="ClipRegistryBuilder.TryComputeContentHash"/> keys the
        /// <c>BlobAssetStore</c>; if the hash moved on every domain reload, every actor sharing a
        /// <see cref="ClipSetAsset"/> would silently stop deduplicating and rebake its registry from
        /// scratch each time the editor reloads.
        /// </summary>
        [Test]
        public void ContentHash_IsIdenticalBeforeSavingAndAfterAFullReload()
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.name = "Rig";
            rig.stableId = 0x9999000000000001UL;
            rig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });
            rig.targets.Add(new RigTargetDefinition
            {
                displayName = "Body",
                stableId = 0x0CCC0001u,
                kind = TargetKind.Quad,
                boundsExtents = new float3(0.75f, 1.25f, 0.5f)
            });

            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.name = "Idle";
            clip.stableId = 0xAAAA000000000001UL;
            clip.rig = rig;
            clip.duration = 2f;
            clip.defaultLoop = LoopMode.Loop;
            TransformTrack transformTrack = AuthoringTestAssets.AddTransformTrack(
                clip, 0x0CCC0001u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            AuthoringTestAssets.AddTransformKey(
                transformTrack, 0f, new float3(1f, 2f, 0f), 45f, new float2(1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                transformTrack, 1f, new float3(-1f, -2f, 0f), -45f, new float2(1f, 1f), Interpolation.Linear);

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.name = "Set";
            clipSet.stableId = 0xBBBB000000000001UL;
            clipSet.rig = rig;
            clipSet.clips.Add(clip);

            bool computedBeforeSave = ClipRegistryBuilder.TryComputeContentHash(
                clipSet, out Unity.Entities.Hash128 hashBeforeSave);
            Assert.IsTrue(computedBeforeSave, "The set must validate cleanly before it is ever written to disk.");

            SaveAsset(rig);
            SaveAsset(clip);
            string clipSetPath = SaveAsset(clipSet);

            CommitAndForceReload(rig, clip, clipSet);

            ClipSetAsset reloadedSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(clipSetPath);
            Assert.IsNotNull(reloadedSet);

            bool computedAfterReload = ClipRegistryBuilder.TryComputeContentHash(
                reloadedSet, out Unity.Entities.Hash128 hashAfterReload);
            Assert.IsTrue(computedAfterReload, "The reloaded set must still validate cleanly.");

            Assert.AreEqual(
                hashBeforeSave,
                hashAfterReload,
                "The content hash must be identical before saving and after a full disk round trip, " +
                "or the BlobAssetStore stops deduplicating across every domain reload.");
        }

        // -----------------------------------------------------------------------------------
        // Fixture 5 — track data (the actual authored curves) must survive byte for byte.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Catches: a transform or sprite track losing keys, reordering them, or coercing a value
        /// during serialization — the fixtures above cover identity and detection, but never actually
        /// compare authored curve data against what comes back.
        /// </summary>
        [Test]
        public void TransformAndSpriteTrackData_SurvivesARealSaveAndReload()
        {
            const uint TransformTargetId = 0x0DDD0001u;
            const uint SpriteTargetId = 0x0DDD0002u;

            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.name = "Rig";
            rig.stableId = 0xCCCC000000000001UL;
            rig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });
            rig.targets.Add(new RigTargetDefinition { displayName = "Arm", stableId = TransformTargetId, kind = TargetKind.Quad });
            rig.targets.Add(new RigTargetDefinition { displayName = "Face", stableId = SpriteTargetId, kind = TargetKind.Quad });

            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.name = "Swing";
            clip.stableId = 0xDDDD000000000001UL;
            clip.rig = rig;
            clip.duration = 1f;

            TransformTrack transformTrack = AuthoringTestAssets.AddTransformTrack(
                clip, TransformTargetId, TrackBlendOp.Additive, AnimatedChannels.PositionXY | AnimatedChannels.RotationZ);
            AuthoringTestAssets.AddTransformKey(
                transformTrack, 0f, new float3(0f, 0f, 0f), 0f, new float2(1f, 1f), Interpolation.Linear);
            AuthoringTestAssets.AddTransformKey(
                transformTrack, 0.5f, new float3(1.5f, -2.5f, 3f), 90f, new float2(1.2f, 0.8f), Interpolation.EaseInOut);
            AuthoringTestAssets.AddTransformKey(
                transformTrack, 1f, new float3(-1f, 2f, -3f), 180f, new float2(0.5f, 0.5f), Interpolation.Step);

            SpriteTrack spriteTrack = AuthoringTestAssets.AddSpriteTrack(clip, SpriteTargetId, SpriteFrameMode.AtlasRect);
            AuthoringTestAssets.AddSpriteKey(spriteTrack, 0f, -1, new float4(1f, 1f, 0f, 0f));
            AuthoringTestAssets.AddSpriteKey(spriteTrack, 0.25f, 3, new float4(0.5f, 0.5f, 0.25f, 0.5f));
            AuthoringTestAssets.AddSpriteKey(spriteTrack, 0.75f, 7, new float4(0.25f, 0.25f, 0.75f, 0.75f));

            SaveAsset(rig);
            string clipAssetPath = SaveAsset(clip);

            CommitAndForceReload(rig, clip);

            ClipAsset reloadedClip = AssetDatabase.LoadAssetAtPath<ClipAsset>(clipAssetPath);
            Assert.IsNotNull(reloadedClip);

            Assert.AreEqual(1, reloadedClip.transformTracks.Count, "The transform track must survive the round trip.");
            TransformTrack reloadedTransformTrack = reloadedClip.transformTracks[0];
            Assert.AreEqual(TransformTargetId, reloadedTransformTrack.targetId);
            Assert.AreEqual(TrackBlendOp.Additive, reloadedTransformTrack.blendOp);
            Assert.AreEqual(AnimatedChannels.PositionXY | AnimatedChannels.RotationZ, reloadedTransformTrack.channels);
            Assert.AreEqual(3, reloadedTransformTrack.keys.Count, "All three transform keys must survive.");

            AssertTransformKey(reloadedTransformTrack.keys[0], 0f, new float3(0f, 0f, 0f), 0f, new float2(1f, 1f), Interpolation.Linear);
            AssertTransformKey(reloadedTransformTrack.keys[1], 0.5f, new float3(1.5f, -2.5f, 3f), 90f, new float2(1.2f, 0.8f), Interpolation.EaseInOut);
            AssertTransformKey(reloadedTransformTrack.keys[2], 1f, new float3(-1f, 2f, -3f), 180f, new float2(0.5f, 0.5f), Interpolation.Step);

            Assert.AreEqual(1, reloadedClip.spriteTracks.Count, "The sprite track must survive the round trip.");
            SpriteTrack reloadedSpriteTrack = reloadedClip.spriteTracks[0];
            Assert.AreEqual(SpriteTargetId, reloadedSpriteTrack.targetId);
            Assert.AreEqual(SpriteFrameMode.AtlasRect, reloadedSpriteTrack.mode);
            Assert.AreEqual(3, reloadedSpriteTrack.keys.Count, "All three sprite keys must survive.");

            AssertSpriteKey(reloadedSpriteTrack.keys[0], 0f, -1, new float4(1f, 1f, 0f, 0f));
            AssertSpriteKey(reloadedSpriteTrack.keys[1], 0.25f, 3, new float4(0.5f, 0.5f, 0.25f, 0.5f));
            AssertSpriteKey(reloadedSpriteTrack.keys[2], 0.75f, 7, new float4(0.25f, 0.25f, 0.75f, 0.75f));
        }

        // -----------------------------------------------------------------------------------
        // Helpers.
        // -----------------------------------------------------------------------------------

        /// <summary>Writes a ScriptableObject to the current test's scratch folder and returns its asset path.</summary>
        /// <summary>
        /// Writes <paramref name="asset"/> to the scratch folder under <em>its own object name</em>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The file name is derived rather than passed, because <see cref="AssetDatabase.CreateAsset"/>
        /// renames the object to match the file. A fixture that sets <c>clip.name = "Idle"</c> and
        /// then saves it as <c>Clip.asset</c> gets an object called "Clip" back — and
        /// <c>ClipBlob.debugName</c> is baked from that name and folded into the content hash, so the
        /// hash legitimately differs before and after the round trip.
        /// </para>
        /// <para>
        /// That presented as a serializer defect when it was a naming slip in the fixture. Deriving
        /// the name makes the mismatch unrepresentable rather than relying on every future fixture
        /// remembering to keep the two in step.
        /// </para>
        /// </remarks>
        private string SaveAsset(UnityEngine.Object asset)
        {
            return SaveAsset(asset, asset.name + ".asset");
        }

        /// <summary>
        /// Writes <paramref name="asset"/> under an explicit file name.
        /// </summary>
        /// <remarks>
        /// Only for assets whose extension is dictated by their type — an <see cref="AnimationClip"/>
        /// must be written as <c>.anim</c> or Unity will not import it as one. Prefer the
        /// name-deriving overload everywhere else, so object name and file name cannot drift.
        /// </remarks>
        private string SaveAsset(UnityEngine.Object asset, string fileName)
        {
            string assetPath = scratchFolderPath + "/" + fileName;
            AssetDatabase.CreateAsset(asset, assetPath);
            return assetPath;
        }

        /// <summary>
        /// Flushes every created asset to disk and then forces Unity to drop the in-memory instances
        /// that created them, so a following <see cref="AssetDatabase.LoadAssetAtPath"/> genuinely
        /// deserializes fresh objects rather than handing back the ones still held in
        /// <paramref name="originals"/>. Without the unload step the "reload" would silently pass by
        /// reading data that was never written through the serializer at all.
        /// </summary>
        private static void CommitAndForceReload(params UnityEngine.Object[] originals)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            for (int assetIndex = 0; assetIndex < originals.Length; assetIndex++)
            {
                if (originals[assetIndex] != null)
                {
                    Resources.UnloadAsset(originals[assetIndex]);
                }
            }
        }

        private static void AssertTransformKey(
            TransformKey key,
            float normalizedTime,
            float3 position,
            float rotationDegrees,
            float2 scale,
            Interpolation interpolation)
        {
            Assert.AreEqual(normalizedTime, key.normalizedTime, FloatTolerance);
            Assert.AreEqual(position.x, key.position.x, FloatTolerance);
            Assert.AreEqual(position.y, key.position.y, FloatTolerance);
            Assert.AreEqual(position.z, key.position.z, FloatTolerance);
            Assert.AreEqual(rotationDegrees, key.rotationZ, FloatTolerance);
            Assert.AreEqual(scale.x, key.scale.x, FloatTolerance);
            Assert.AreEqual(scale.y, key.scale.y, FloatTolerance);
            Assert.AreEqual(interpolation, key.interpolation);
        }

        private static void AssertSpriteKey(SpriteKey key, float normalizedTime, int sliceIndex, float4 atlasRect)
        {
            Assert.AreEqual(normalizedTime, key.normalizedTime, FloatTolerance);
            Assert.AreEqual(sliceIndex, key.sliceIndex);
            Assert.AreEqual(atlasRect.x, key.atlasRect.x, FloatTolerance);
            Assert.AreEqual(atlasRect.y, key.atlasRect.y, FloatTolerance);
            Assert.AreEqual(atlasRect.z, key.atlasRect.z, FloatTolerance);
            Assert.AreEqual(atlasRect.w, key.atlasRect.w, FloatTolerance);
        }
    }
}
