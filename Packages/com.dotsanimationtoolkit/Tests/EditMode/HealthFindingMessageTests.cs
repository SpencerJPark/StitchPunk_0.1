// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Guards the wording of the stale/unbaked VAT finding message against a re-concatenated clause.</summary>
    public sealed class HealthFindingMessageTests
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
        public void H06_MessageIsOneSentence_WithNoRepeatedClause()
        {
            ClipAsset clip = assets.CreateClip("Clip", 9001UL, 1f);
            BoneTrack boneTrack = new BoneTrack
            {
                boneName = "Spine"
            };
            boneTrack.keys.Add(new BoneKey());
            clip.boneTracks.Add(boneTrack);
            ClipSetAsset clipSet = assets.CreateSet("UnbakedSet", null, 9002UL, clip);

            HealthScanContext context = new HealthScanContext();
            context.clips = new List<ClipAsset> { clip };
            context.clipSets = new List<ClipSetAsset> { clipSet };

            List<HealthFinding> findings = new List<HealthFinding>();
            VatFreshnessValidation.EvaluateStaleOrUnbakedVatSets(context, findings);

            Assert.AreEqual(1, findings.Count);
            string message = findings[0].message;
            string lowerCasedMessage = message.ToLowerInvariant();

            int notBakedOccurrenceCount = 0;
            int searchStartIndex = 0;
            while (true)
            {
                int foundIndex = lowerCasedMessage.IndexOf("not baked", searchStartIndex, StringComparison.Ordinal);
                if (foundIndex < 0)
                {
                    break;
                }
                notBakedOccurrenceCount++;
                searchStartIndex = foundIndex + "not baked".Length;
            }

            Assert.LessOrEqual(notBakedOccurrenceCount, 1,
                "the finding message should state 'not baked' at most once, not repeat the clause");
            StringAssert.DoesNotContain(": Not baked:", message);
            StringAssert.Contains(clipSet.name, message);
        }
    }
}
