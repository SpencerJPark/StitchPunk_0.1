// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Globalization;
using System.Text;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Formats a ToolkitStatsSample into display strings and a markdown report.</summary>
    public static class StatsSnapshotFormatting
    {
        public const string NoWorldText = "—";

        private const string UnavailableTimingText = "unavailable";

        public static string ToMarkdown(in ToolkitStatsSample sample, string packageVersion, DateTime capturedAt)
        {
            string worldDisplayName = string.IsNullOrEmpty(sample.worldName) ? NoWorldText : sample.worldName;
            string capturedAtText = capturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string notPlayingSuffix = sample.worldAvailable ? string.Empty : " · not playing";
            bool timingRowsAvailable = sample.worldAvailable && sample.timingsAvailable;

            StringBuilder markdownBuilder = new StringBuilder();
            markdownBuilder.Append("## DOTS Animation Toolkit stats");
            markdownBuilder.Append('\n').Append('\n');
            markdownBuilder.Append("Version ").Append(packageVersion)
                .Append(" · captured ").Append(capturedAtText)
                .Append(" · world ").Append(worldDisplayName)
                .Append(notPlayingSuffix);
            markdownBuilder.Append('\n').Append('\n');
            markdownBuilder.Append("| Stat | Value |");
            markdownBuilder.Append('\n');
            markdownBuilder.Append("| --- | --- |");

            AppendRow(markdownBuilder, "Actors", sample.worldAvailable ? sample.actorCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "Layers", sample.worldAvailable ? sample.layerCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "Ragdolling", sample.worldAvailable ? sample.ragdollingActorCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "In cutscene", sample.worldAvailable ? sample.cutscenePlayerCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "LOD 0 / 1 / 2 / 3", sample.worldAvailable ? FormatLodCounts(in sample) : NoWorldText);
            AppendRow(markdownBuilder, "Events this frame", sample.worldAvailable ? sample.eventsThisFrame.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "Actors with pending events", sample.worldAvailable ? sample.pendingEventActorCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "Actors with windows open", sample.worldAvailable ? sample.actorsWithOpenWindowsCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "VAT parts bound", sample.worldAvailable ? sample.vatBoundPartCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "VAT textures", sample.worldAvailable ? sample.vatDistinctTextureCount.ToString(CultureInfo.InvariantCulture) : NoWorldText);
            AppendRow(markdownBuilder, "VAT texture memory", sample.worldAvailable ? FormatMegabytes(sample.vatTextureBytes) : NoWorldText);
            AppendRow(markdownBuilder, "AnimationToolkit group", sample.worldAvailable ? (timingRowsAvailable ? FormatMilliseconds(sample.toolkitGroupMilliseconds) : UnavailableTimingText) : NoWorldText);
            AppendRow(markdownBuilder, "Binding group", sample.worldAvailable ? (timingRowsAvailable ? FormatMilliseconds(sample.bindingGroupMilliseconds) : UnavailableTimingText) : NoWorldText);
            AppendRow(markdownBuilder, "Logic group", sample.worldAvailable ? (timingRowsAvailable ? FormatMilliseconds(sample.logicGroupMilliseconds) : UnavailableTimingText) : NoWorldText);
            AppendRow(markdownBuilder, "Presentation group", sample.worldAvailable ? (timingRowsAvailable ? FormatMilliseconds(sample.presentationGroupMilliseconds) : UnavailableTimingText) : NoWorldText);
            AppendRow(markdownBuilder, "Ragdoll group", sample.worldAvailable ? (timingRowsAvailable ? FormatMilliseconds(sample.ragdollGroupMilliseconds) : UnavailableTimingText) : NoWorldText);

            return markdownBuilder.ToString();
        }

        public static string FormatMegabytes(long bytes)
        {
            double megabytes = bytes / 1048576.0;
            return megabytes.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        public static string FormatMilliseconds(double milliseconds)
        {
            return milliseconds.ToString("0.00", CultureInfo.InvariantCulture) + " ms";
        }

        public static string FormatLodCounts(in ToolkitStatsSample sample)
        {
            string countsText = sample.lodLevel0Count.ToString(CultureInfo.InvariantCulture) + " / " +
                sample.lodLevel1Count.ToString(CultureInfo.InvariantCulture) + " / " +
                sample.lodLevel2Count.ToString(CultureInfo.InvariantCulture) + " / " +
                sample.lodLevel3Count.ToString(CultureInfo.InvariantCulture);
            return sample.lodSampled ? countsText + " (sampled)" : countsText;
        }

        private static void AppendRow(StringBuilder markdownBuilder, string statName, string statValue)
        {
            markdownBuilder.Append('\n');
            markdownBuilder.Append("| ").Append(statName).Append(" | ").Append(statValue).Append(" |");
        }
    }
}
