// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of what a prefab restructure does and does not break.
    /// </summary>
    /// <remarks>
    /// Two tests, because there are exactly two ways this can be wrong and both are silent. It can
    /// under-report — a broken bone track that never reaches the panel is animation data quietly
    /// not baking — or it can over-report, listing id-bound tracks that no prefab edit can touch,
    /// which trains the user to dismiss the panel without reading it.
    /// </remarks>
    public sealed class BindingReconcilerTests
    {
        private readonly List<BrokenBinding> findings = new List<BrokenBinding>();
        private readonly List<Object> createdAssets = new List<Object>();

        [TearDown]
        public void DisposeFixture()
        {
            for (int assetIndex = 0; assetIndex < createdAssets.Count; assetIndex++)
            {
                if (createdAssets[assetIndex] != null)
                {
                    Object.DestroyImmediate(createdAssets[assetIndex]);
                }
            }
            createdAssets.Clear();
            findings.Clear();
        }

        private TAsset Create<TAsset>() where TAsset : ScriptableObject
        {
            TAsset asset = ScriptableObject.CreateInstance<TAsset>();
            createdAssets.Add(asset);
            return asset;
        }

        /// <summary>
        /// A renamed bone is reported, with enough detail for the panel to state the cost.
        /// </summary>
        /// <remarks>
        /// Catches: reporting the finding but losing the key count, which turns the delete prompt
        /// from "40 keys will be lost" into an unqualified "delete this?".
        /// </remarks>
        [Test]
        public void RenamedBone_IsReportedWithItsKeyCount()
        {
            ClipAsset clip = Create<ClipAsset>();
            clip.name = "Walk";
            clip.boneTracks = new List<BoneTrack>
            {
                new BoneTrack
                {
                    boneName = "Spine_01",
                    keys = new List<BoneKey>
                    {
                        new BoneKey { normalizedTime = 0f },
                        new BoneKey { normalizedTime = 1f }
                    }
                }
            };

            RigAsset rig = Create<RigAsset>();
            ClipSetAsset clipSet = Create<ClipSetAsset>();
            clipSet.clips = new List<ClipAsset> { clip };

            HashSet<string> availableNames = new HashSet<string> { "Root", "Spine_1", "Head" };
            BindingReconciler.Collect(rig, clipSet, availableNames, findings);

            Assert.AreEqual(1, findings.Count, "The renamed bone must be reported exactly once.");
            Assert.AreEqual(BrokenBindingKind.BoneTrack, findings[0].kind);
            Assert.AreEqual("Spine_01", findings[0].missingName);
            Assert.AreEqual(2, findings[0].keyCount, "The panel states what a delete would cost.");

            // Remapping is the non-destructive fix, and it must actually re-point the track.
            Assert.IsTrue(BindingReconciler.Remap(findings[0], rig, "Spine_1"));
            Assert.AreEqual("Spine_1", clip.boneTracks[0].boneName);

            BindingReconciler.Collect(rig, clipSet, availableNames, findings);
            Assert.AreEqual(0, findings.Count, "A remapped binding must stop being reported.");
        }

        /// <summary>
        /// Id-bound tracks survive a restructure and are never reported.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The premise the whole reconciler rests on. Transform and sprite tracks bind to a rig
        /// target's stable id, not to a name or a hierarchy path, so renaming the part they drive
        /// changes nothing they depend on — the target keeps its id and the tracks keep pointing at
        /// it.
        /// </para>
        /// <para>
        /// The rig target itself is still reported, because the <em>preview</em> finds its rest pose
        /// by name. Catches an implementation that conflates the two and either deletes working
        /// tracks or stays silent about a part that will preview at the origin.
        /// </para>
        /// </remarks>
        [Test]
        public void RenamedPart_ReportsTheRestPoseOnly_AndLeavesIdBoundTracksAlone()
        {
            // The id is set explicitly rather than minted, matching the other authoring fixtures:
            // the point under test is that it survives a rename, so it has to be a value the
            // assertions can name.
            const uint torsoId = 4242u;
            RigAsset rig = Create<RigAsset>();
            rig.targets = new List<RigTargetDefinition>
            {
                new RigTargetDefinition { displayName = "Torso", stableId = torsoId }
            };

            ClipAsset clip = Create<ClipAsset>();
            clip.name = "Idle";
            clip.transformTracks = new List<TransformTrack>
            {
                new TransformTrack { targetId = torsoId, keys = new List<TransformKey>() }
            };
            clip.spriteTracks = new List<SpriteTrack>
            {
                new SpriteTrack { targetId = torsoId, keys = new List<SpriteKey>() }
            };

            ClipSetAsset clipSet = Create<ClipSetAsset>();
            clipSet.clips = new List<ClipAsset> { clip };

            // The prefab now calls it something else entirely.
            HashSet<string> availableNames = new HashSet<string> { "Root", "Chest" };
            BindingReconciler.Collect(rig, clipSet, availableNames, findings);

            Assert.AreEqual(1, findings.Count, "Only the name-bound rest pose breaks.");
            Assert.AreEqual(BrokenBindingKind.RigTargetRestPose, findings[0].kind);
            Assert.IsFalse(
                BindingReconciler.IsDeletable(findings[0].kind),
                "A rig target carries the id every track binds to, so the panel must not offer to "
                + "delete it.");

            // Renaming the target fixes the preview without disturbing the id the tracks use.
            Assert.IsTrue(BindingReconciler.Remap(findings[0], rig, "Chest"));
            Assert.AreEqual(torsoId, rig.targets[0].Id.Value, "Remapping a name must not remint the id.");
            Assert.AreEqual(torsoId, clip.transformTracks[0].targetId);
            Assert.AreEqual(torsoId, clip.spriteTracks[0].targetId);

            BindingReconciler.Collect(rig, clipSet, availableNames, findings);
            Assert.AreEqual(0, findings.Count);
        }
    }
}
