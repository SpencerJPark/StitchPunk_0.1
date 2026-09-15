// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Linq;
using NUnit.Framework;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Covers StatsSnapshotFormatting.ToMarkdown's LOD row sampled marker.</summary>
    public sealed class StatsSnapshotFormattingTests
    {
        [Test]
        public void ToMarkdown_MarksSampledLod()
        {
            ToolkitStatsSample sampledSample = new ToolkitStatsSample
            {
                worldAvailable = true,
                worldName = "Default World",
                lodLevel0Count = 180,
                lodLevel1Count = 120,
                lodLevel2Count = 80,
                lodLevel3Count = 32,
                lodSampled = true,
                timingsAvailable = false,
            };
            ToolkitStatsSample unsampledSample = sampledSample;
            unsampledSample.lodSampled = false;

            DateTime capturedAt = new DateTime(2026, 9, 15, 14, 2, 11);

            string sampledMarkdown = StatsSnapshotFormatting.ToMarkdown(in sampledSample, "0.53.0", capturedAt);
            string unsampledMarkdown = StatsSnapshotFormatting.ToMarkdown(in unsampledSample, "0.53.0", capturedAt);

            string sampledLodLine = sampledMarkdown.Split('\n').First(line => line.StartsWith("| LOD 0 / 1 / 2 / 3 |", StringComparison.Ordinal));
            string unsampledLodLine = unsampledMarkdown.Split('\n').First(line => line.StartsWith("| LOD 0 / 1 / 2 / 3 |", StringComparison.Ordinal));

            Assert.IsTrue(sampledLodLine.EndsWith("(sampled) |", StringComparison.Ordinal));
            Assert.IsFalse(unsampledLodLine.Contains("sampled"));
        }
    }
}
