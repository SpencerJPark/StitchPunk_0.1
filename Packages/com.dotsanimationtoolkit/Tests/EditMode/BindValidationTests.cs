// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Confirms BindValidation folds V36 into a code another rule already reports and still surfaces the V01 bind failure.</summary>
    public sealed class BindValidationTests
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
        public void H11_SkipsStaleVatBakeAndCodesOtherRulesReport()
        {
            RigAsset rig = assets.CreateRig("Rig", 8001UL, new uint[] { 1u });
            ClipAsset shortClip = assets.CreateClip("ShortClip", 9001UL, 0f);
            ClipAsset taggedClip = assets.CreateClip("TaggedClip", 9002UL, 1f);
            TransformTrack taggedTrack = AuthoringTestAssets.AddTransformTrack(
                taggedClip, 1u, TrackBlendOp.Override, AnimatedChannels.PositionXY);
            taggedTrack.tagId = 900u;
            TargetTagRegistry registry = assets.Create<TargetTagRegistry>("Tags");
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, 10001UL, shortClip, taggedClip);
            ActorProfileAsset profile = assets.CreateProfile(rig, clipSet, 2);

            HealthScanContext context = new HealthScanContext();
            context.profiles = new List<ActorProfileAsset> { profile };
            context.clips = new List<ClipAsset> { shortClip, taggedClip };
            context.clipSets = new List<ClipSetAsset> { clipSet };
            context.rigs = new List<RigAsset> { rig };
            context.targetTags = registry;

            List<HealthFinding> findings = new List<HealthFinding>();
            BindValidation.EvaluateProfileBinds(context, findings);

            for (int findingIndex = 0; findingIndex < findings.Count; findingIndex++)
            {
                Assert.AreEqual(HealthFinding.ClipSetDoesNotBindToProfileRigCode, findings[findingIndex].code);
            }

            bool hasV01Title = false;
            bool hasV36Title = false;
            for (int findingIndex = 0; findingIndex < findings.Count; findingIndex++)
            {
                string title = findings[findingIndex].title;
                if (title.StartsWith("V01"))
                {
                    hasV01Title = true;
                }
                if (title.StartsWith("V36"))
                {
                    hasV36Title = true;
                }
            }

            Assert.IsTrue(hasV01Title);
            Assert.IsFalse(hasV36Title);

            Assert.IsTrue(BindValidation.IsCodeReportedByAnotherRule(ValidationCode.V08));
        }
    }
}
