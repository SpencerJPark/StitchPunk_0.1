// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class VatBakeClipBuilderTests
    {
        [Test]
        public void Build_ATargetedTrackWins_OverTheClipWideSource()
        {
            AnimationClip walkClip = new AnimationClip();
            AnimationClip capeClip = new AnimationClip();

            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.boneTracks = new List<BoneTrack> { new BoneTrack() };
            clip.vatSource = new VatClipSource { sourceClip = walkClip };
            clip.vatTracks = new List<VatTrack> { new VatTrack { targetId = 7u, sourceClip = capeClip } };

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.clips = new List<ClipAsset> { clip };

            List<VatBakeSource> sources = new List<VatBakeSource>
            {
                new VatBakeSource { TargetId = 0u, DisplayName = "Root" },
                new VatBakeSource { TargetId = 7u, DisplayName = "Cape" },
                new VatBakeSource { TargetId = 9u, DisplayName = "Body" }
            };

            VatBakePlan plan = VatBakeClipBuilder.Build(clipSet, sources);

            VatBakeSourcePlan capePlan = plan.Sources.Find(sourcePlan => sourcePlan.Source.TargetId == 7u);
            VatBakeSourcePlan bodyPlan = plan.Sources.Find(sourcePlan => sourcePlan.Source.TargetId == 9u);

            Assert.IsNotNull(capePlan);
            Assert.AreEqual(capeClip, capePlan.Clips[0].animationClip);
            Assert.IsNull(capePlan.Clips[0].boneTracks);

            Assert.IsNotNull(bodyPlan);
            Assert.AreEqual(walkClip, bodyPlan.Clips[0].animationClip);
            Assert.AreEqual(clip.boneTracks, bodyPlan.Clips[0].boneTracks);
        }

        [Test]
        public void Build_APartNoClipAnimates_IsSkippedByName_NotDropped()
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.boneTracks = new List<BoneTrack>();
            clip.vatSource = null;
            clip.vatTracks = new List<VatTrack>();

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.clips = new List<ClipAsset> { clip };

            List<VatBakeSource> sources = new List<VatBakeSource>
            {
                new VatBakeSource { TargetId = 9u, DisplayName = "Antenna" }
            };

            VatBakePlan plan = VatBakeClipBuilder.Build(clipSet, sources);

            Assert.IsFalse(plan.Sources.Exists(sourcePlan => sourcePlan.Source.TargetId == 9u));
            Assert.Contains("Antenna", plan.SkippedPartNames);
        }

        [Test]
        public void Build_ATrackNamingNoSource_IsReportedOnce_NotPerSource()
        {
            AnimationClip missingTargetClip = new AnimationClip();

            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.boneTracks = new List<BoneTrack>();
            clip.vatSource = null;
            clip.vatTracks = new List<VatTrack> { new VatTrack { targetId = 42u, sourceClip = missingTargetClip } };

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.clips = new List<ClipAsset> { clip };

            List<VatBakeSource> sources = new List<VatBakeSource>
            {
                new VatBakeSource { TargetId = 1u, DisplayName = "A" },
                new VatBakeSource { TargetId = 2u, DisplayName = "B" },
                new VatBakeSource { TargetId = 3u, DisplayName = "C" }
            };

            VatBakePlan plan = VatBakeClipBuilder.Build(clipSet, sources);

            Assert.AreEqual(1, plan.UnknownTrackTargets.Count);
        }

        [Test]
        public void Build_NothingBakeable_ReportsHasAnythingToBakeFalse()
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            clip.boneTracks = new List<BoneTrack>();
            clip.vatSource = null;
            clip.vatTracks = new List<VatTrack>();

            ClipSetAsset clipSet = ScriptableObject.CreateInstance<ClipSetAsset>();
            clipSet.clips = new List<ClipAsset> { clip };

            List<VatBakeSource> sources = new List<VatBakeSource>
            {
                new VatBakeSource { TargetId = 9u, DisplayName = "Antenna" }
            };

            VatBakePlan plan = VatBakeClipBuilder.Build(clipSet, sources);

            Assert.IsFalse(plan.HasAnythingToBake);
            Assert.AreEqual(0, plan.Sources.Count);
        }
    }
}
