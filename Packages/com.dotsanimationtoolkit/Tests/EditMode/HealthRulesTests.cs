// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Confirms the cross-asset Health rules that compare a profile's rig against its clip sets and clips.</summary>
    public sealed class HealthRulesTests
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

        [Test]
        public void H04_FlagsProfileWhoseClipSetHasAnotherRig()
        {
            RigAsset rigA = assets.CreateRig("RigA", 1001UL, new uint[] { 1u });
            RigAsset rigB = assets.CreateRig("RigB", 1002UL, new uint[] { 1u });
            ClipAsset clip = assets.CreateClip("Clip", 2001UL, 1f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rigA, 3001UL, clip);
            VatTextureSetAsset textures = assets.CreateVatTextureSet("Vat", 4001UL);
            textures.sourceRigKey = rigB.StableId;
            clipSet.vatTextures = textures;
            ActorProfileAsset profile = assets.CreateProfile(rigA, clipSet, 2);

            HealthScanContext context = new HealthScanContext();
            context.clips = new List<ClipAsset> { clip };
            context.clipSets = new List<ClipSetAsset> { clipSet };
            context.rigs = new List<RigAsset> { rigA, rigB };
            context.profiles = new List<ActorProfileAsset> { profile };
            context.vatTextureSets = new List<VatTextureSetAsset> { textures };

            List<HealthFinding> findings = new List<HealthFinding>();
            ProfileHealthValidation.EvaluateProfileRigMismatchingClipSetRig(context, findings);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(HealthFinding.ProfileRigDiffersFromClipSetRigCode, findings[0].code);
            Assert.AreSame(profile, findings[0].target);

            textures.sourceRigKey = rigA.StableId;
            List<HealthFinding> secondFindings = new List<HealthFinding>();
            ProfileHealthValidation.EvaluateProfileRigMismatchingClipSetRig(context, secondFindings);

            Assert.AreEqual(0, secondFindings.Count);
        }

        [Test]
        public void H10_FlagsClipWithNoTagOnRoster()
        {
            RigAsset rig = assets.CreateRig("Rig", 5001UL, new uint[] { 1u });
            rig.targets[0].tagId = 100u;
            ClipAsset clip = assets.CreateClip("Clip", 6001UL, 1f);
            TransformTrack track = AuthoringTestAssets.AddTransformTrack(
                clip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            track.tagId = 200u;
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 7001UL, clip);
            ActorProfileAsset profile = assets.CreateProfile(rig, clipSet, 2);

            HealthScanContext context = new HealthScanContext();
            context.clips = new List<ClipAsset> { clip };
            context.clipSets = new List<ClipSetAsset> { clipSet };
            context.rigs = new List<RigAsset> { rig };
            context.profiles = new List<ActorProfileAsset> { profile };

            List<HealthFinding> findings = new List<HealthFinding>();
            TagAndKeyValidation.EvaluateClipsPosingNothingOnRig(context, findings);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(HealthFinding.ClipPosesNothingOnRigCode, findings[0].code);
            Assert.AreSame(clip, findings[0].target);

            track.tagId = 100u;
            List<HealthFinding> secondFindings = new List<HealthFinding>();
            TagAndKeyValidation.EvaluateClipsPosingNothingOnRig(context, secondFindings);

            Assert.AreEqual(0, secondFindings.Count);
        }
    }
}
