// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Confirms HealthScan orders findings with stale VAT bakes pinned first and errors ahead of notes.</summary>
    public sealed class HealthScanTests
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
        public void Run_OrdersErrorsFirst()
        {
            RigAsset unusedRig = assets.CreateRig("UnusedRig", 0x1000UL, new uint[] { 1u });
            unusedRig.MarkStableIdPersisted();

            ClipAsset clipWithUnknownEventKey = assets.CreateClip("ClipWithUnknownEventKey", 0x2000UL, 1f);
            clipWithUnknownEventKey.MarkStableIdPersisted();
            AuthoringTestAssets.AddEvent(clipWithUnknownEventKey, 0.5f, 5000u, 0, 0f);

            AnimEventKeyRegistry emptyEventKeys = assets.Create<AnimEventKeyRegistry>("EmptyEventKeys");

            HealthScanContext context = new HealthScanContext();
            context.rigs = new List<RigAsset> { unusedRig };
            context.clips = new List<ClipAsset> { clipWithUnknownEventKey };
            context.eventKeys = emptyEventKeys;

            List<HealthFinding> findings = HealthScan.Run(context);

            int firstErrorIndex = -1;
            int firstNoteIndex = -1;
            for (int findingIndex = 0; findingIndex < findings.Count; findingIndex++)
            {
                if (firstErrorIndex < 0 && findings[findingIndex].severity == HealthSeverity.Error)
                {
                    firstErrorIndex = findingIndex;
                }
                if (firstNoteIndex < 0 && findings[findingIndex].severity == HealthSeverity.Note)
                {
                    firstNoteIndex = findingIndex;
                }
            }

            Assert.GreaterOrEqual(firstErrorIndex, 0, "Expected an H08 event-key-not-in-registry error to fire.");
            Assert.GreaterOrEqual(firstNoteIndex, 0, "Expected an H05 rig-used-by-no-profile note to fire.");
            Assert.Less(firstErrorIndex, firstNoteIndex);
        }

        [Test]
        public void Run_PinsStaleVatBakesAboveOtherErrors()
        {
            ClipSetAsset brokenSet = assets.CreateSet("BrokenSet", null, 0x3333UL, (ClipAsset)null);
            brokenSet.MarkStableIdPersisted();

            VatTextureSetAsset staleTextures = assets.CreateVatTextureSet("StaleTextures", 0x4444UL);
            staleTextures.sourceRigKey = 0x5555UL;
            staleTextures.MarkStableIdPersisted();

            ClipSetAsset bakedSet = assets.CreateSet("BakedSet", null, 0x6666UL);
            bakedSet.MarkStableIdPersisted();
            bakedSet.vatTextures = staleTextures;

            HealthScanContext context = new HealthScanContext();
            context.clipSets = new List<ClipSetAsset> { brokenSet, bakedSet };
            context.vatTextureSets = new List<VatTextureSetAsset> { staleTextures };

            List<HealthFinding> findings = HealthScan.Run(context);

            Assert.AreEqual(HealthFinding.StaleOrUnbakedVatSetCode, findings[0].code);

            bool hasNullClipFinding = false;
            for (int findingIndex = 0; findingIndex < findings.Count; findingIndex++)
            {
                if (findings[findingIndex].code == HealthFinding.ClipSetListsNullClipCode)
                {
                    hasNullClipFinding = true;
                    break;
                }
            }
            Assert.IsTrue(hasNullClipFinding, "Expected an H02 clip-set-lists-null-clip finding.");
        }
    }
}
