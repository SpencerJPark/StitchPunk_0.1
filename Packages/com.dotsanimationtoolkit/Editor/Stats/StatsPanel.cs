// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Diagnostics;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Stats tab: actor, event, VAT and timing readouts sampled from the default world.</summary>
    public sealed class StatsPanel : VisualElement, IDisposable
    {
        private const int PollIntervalMilliseconds = 250;
        private const int LodBucketCount = 4;
        private const string NoWorldTooltip =
            "No world yet — enter Play mode and this samples the running game.";
        private const string TimingUnavailableTooltip =
            "This world has no toolkit system timings; the profiler recorder found no samples.";

        private readonly ToolkitStatsCollector collector;
        private readonly Stopwatch pollStopwatch;
        private readonly string packageVersion;

        private readonly Label worldLabel;
        private readonly VisualElement playingDot;
        private readonly Label playingLabel;
        private readonly Label statusLabel;

        private readonly Label actorsValueLabel;
        private readonly Label layersValueLabel;
        private readonly Label ragdollingValueLabel;
        private readonly Label cutsceneValueLabel;
        private readonly Label[] lodValueLabels;
        private readonly VisualElement[] lodBarFills;
        private readonly Label lodSampledLabel;

        private readonly SparklineElement sparkline;
        private readonly Label eventsSummaryLabel;
        private readonly Label pendingValueLabel;
        private readonly Label windowsValueLabel;

        private readonly Label vatPartsValueLabel;
        private readonly Label vatTexturesValueLabel;
        private readonly Label vatMemoryValueLabel;

        private readonly Label timeToolkitValueLabel;
        private readonly Label timeBindingValueLabel;
        private readonly Label timeLogicValueLabel;
        private readonly Label timePresentationValueLabel;
        private readonly Label timeRagdollValueLabel;

        private ToolkitStatsSample lastSample;
        private bool isShowingUnavailableStatus;
        private bool isDisposed;

        public ToolkitStatsSample LastSample
        {
            get { return lastSample; }
        }

        public SparklineElement Sparkline
        {
            get { return sparkline; }
        }

        public StatsPanel()
        {
            name = "stats-panel";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            collector = new ToolkitStatsCollector();
            pollStopwatch = Stopwatch.StartNew();
            packageVersion = ResolvePackageVersion();

            VisualElement headerRow = ToolkitChrome.MakeAssetBar("stats-header-row");
            Add(headerRow);
            headerRow.Add(ToolkitChrome.MakeAssetBarLabel("World"));
            worldLabel = new Label { name = "stats-world-label" };
            worldLabel.AddToClassList("toolkit-text--dim");
            headerRow.Add(worldLabel);
            headerRow.Add(ToolkitChrome.MakeAssetBarSpacer());
            playingDot = ToolkitChrome.MakeSeverityDot(ToolkitPalette.BoxBorder);
            playingDot.name = "stats-playing-dot";
            headerRow.Add(playingDot);
            playingLabel = new Label { name = "stats-playing-label" };
            headerRow.Add(playingLabel);

            VisualElement actorsColumn = ToolkitChrome.MakeColumn("stats-actors-column");
            VisualElement eventsColumn = ToolkitChrome.MakeColumn("stats-events-column");
            VisualElement timingColumn = ToolkitChrome.MakeColumn("stats-timing-column");

            VisualElement actorsBox = ToolkitChrome.MakeCard(
                "stats-actors-card", "Actors", out VisualElement actorsBody, out _);
            actorsValueLabel = MakeValueRow(actorsBody, "Actors", "stats-actors-value");
            layersValueLabel = MakeValueRow(actorsBody, "Layers", "stats-layers-value");
            ragdollingValueLabel = MakeValueRow(actorsBody, "Ragdolling", "stats-ragdolling-value");
            cutsceneValueLabel = MakeValueRow(actorsBody, "In cutscene", "stats-cutscene-value");
            lodValueLabels = new Label[LodBucketCount];
            lodBarFills = new VisualElement[LodBucketCount];
            for (int lodIndex = 0; lodIndex < LodBucketCount; lodIndex++)
            {
                lodValueLabels[lodIndex] = MakeLodRow(
                    actorsBody, "LOD " + lodIndex, "stats-lod" + lodIndex + "-value", out lodBarFills[lodIndex]);
            }
            lodSampledLabel = ToolkitChrome.MakeHint("sampled");
            lodSampledLabel.name = "stats-lod-sampled-label";
            actorsBody.Add(lodSampledLabel);
            actorsColumn.Add(actorsBox);

            VisualElement eventsBox = ToolkitChrome.MakeCard(
                "stats-events-card", "Events / frame", out VisualElement eventsBody, out _);
            sparkline = new SparklineElement();
            eventsBody.Add(sparkline);
            eventsSummaryLabel = new Label { name = "stats-events-summary" };
            eventsBody.Add(eventsSummaryLabel);
            pendingValueLabel = MakeValueRow(eventsBody, "Pending actors", "stats-pending-value");
            windowsValueLabel = MakeValueRow(eventsBody, "Actors with windows open", "stats-windows-value");
            eventsColumn.Add(eventsBox);

            VisualElement vatBox = ToolkitChrome.MakeCard(
                "stats-vat-card", "VAT", out VisualElement vatBody, out _);
            vatPartsValueLabel = MakeValueRow(vatBody, "Parts bound", "stats-vat-parts-value");
            vatTexturesValueLabel = MakeValueRow(vatBody, "Textures", "stats-vat-textures-value");
            vatMemoryValueLabel = MakeValueRow(vatBody, "Texture memory", "stats-vat-memory-value");
            eventsColumn.Add(vatBox);

            VisualElement timingBox = ToolkitChrome.MakeCard(
                "stats-timing-card", "Timing (ms)", out VisualElement timingBody, out _);
            timeToolkitValueLabel = MakeValueRow(timingBody, "AnimationToolkit", "stats-time-toolkit-value");
            timeBindingValueLabel = MakeValueRow(timingBody, "Binding", "stats-time-binding-value");
            timeLogicValueLabel = MakeValueRow(timingBody, "Logic", "stats-time-logic-value");
            timePresentationValueLabel = MakeValueRow(timingBody, "Presentation", "stats-time-presentation-value");
            timeRagdollValueLabel = MakeValueRow(timingBody, "Ragdoll", "stats-time-ragdoll-value");
            timingColumn.Add(timingBox);

            CoverPaneSplitView timingSplitView =
                new CoverPaneSplitView("Stats.Timing", 1, 260f, TwoPaneSplitViewOrientation.Horizontal);
            timingSplitView.style.flexGrow = 1f;
            timingSplitView.Add(eventsColumn);
            timingSplitView.Add(timingColumn);

            CoverPaneSplitView actorsSplitView =
                new CoverPaneSplitView("Stats.Actors", 0, 300f, TwoPaneSplitViewOrientation.Horizontal);
            actorsSplitView.style.flexGrow = 1f;
            actorsSplitView.Add(actorsColumn);
            actorsSplitView.Add(timingSplitView);
            Add(actorsSplitView);

            VisualElement statusFooter = ToolkitChrome.MakeStatusRow(out statusLabel, out VisualElement statusActions, true);
            Button snapshotButton = ToolkitChrome.MakePrimaryAction(
                CopySnapshotToClipboard, "d_SaveAs", "Copy these numbers as a Markdown table", "Snapshot");
            snapshotButton.name = "stats-snapshot-button";
            statusActions.Add(snapshotButton);
            Add(statusFooter);

            RegisterCallback<AttachToPanelEvent>(evt => EditorApplication.update += OnEditorUpdate);
            RegisterCallback<DetachFromPanelEvent>(evt => EditorApplication.update -= OnEditorUpdate);

            RefreshNow();
        }

        public void RefreshNow()
        {
            World world = EditorApplication.isPlaying ? World.DefaultGameObjectInjectionWorld : null;
            bool wasAvailable = lastSample.worldAvailable;
            collector.Sample(world, ref lastSample);

            if (!wasAvailable && lastSample.worldAvailable)
            {
                sparkline.Clear();
            }
            if (lastSample.worldAvailable)
            {
                sparkline.Push(lastSample.eventsThisFrame);
            }

            UpdateLabels();
        }

        public string BuildSnapshotMarkdown()
        {
            return StatsSnapshotFormatting.ToMarkdown(lastSample, packageVersion, DateTime.Now);
        }

        public void CopySnapshotToClipboard()
        {
            EditorGUIUtility.systemCopyBuffer = BuildSnapshotMarkdown();
            isShowingUnavailableStatus = false;
            ToolkitChrome.SetStatus(statusLabel, "Copied a snapshot to the clipboard.", ToolkitStatusTone.Neutral);
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            isDisposed = true;
            EditorApplication.update -= OnEditorUpdate;
            collector.Dispose();
        }

        private void OnEditorUpdate()
        {
            if (pollStopwatch.ElapsedMilliseconds < PollIntervalMilliseconds)
            {
                return;
            }
            pollStopwatch.Restart();
            RefreshNow();
        }

        private void UpdateLabels()
        {
            bool available = lastSample.worldAvailable;
            string noWorldText = StatsSnapshotFormatting.NoWorldText;

            worldLabel.text = available ? lastSample.worldName : noWorldText;

            bool isPlaying = EditorApplication.isPlaying;
            playingLabel.text = isPlaying ? "playing" : "not playing";
            playingDot.style.backgroundColor = isPlaying ? ToolkitPalette.Playing : ToolkitPalette.BoxBorder; // colour from data

            SetValue(actorsValueLabel, available ? lastSample.actorCount.ToString() : noWorldText, available);
            SetValue(layersValueLabel, available ? lastSample.layerCount.ToString() : noWorldText, available);
            SetValue(
                ragdollingValueLabel,
                available ? lastSample.ragdollingActorCount.ToString() : noWorldText,
                available);
            SetValue(
                cutsceneValueLabel, available ? lastSample.cutscenePlayerCount.ToString() : noWorldText, available);

            int[] lodCounts =
            {
                lastSample.lodLevel0Count, lastSample.lodLevel1Count, lastSample.lodLevel2Count, lastSample.lodLevel3Count
            };
            int maxLodCount = 1;
            for (int lodIndex = 0; lodIndex < LodBucketCount; lodIndex++)
            {
                if (lodCounts[lodIndex] > maxLodCount)
                {
                    maxLodCount = lodCounts[lodIndex];
                }
            }
            for (int lodIndex = 0; lodIndex < LodBucketCount; lodIndex++)
            {
                SetValue(lodValueLabels[lodIndex], available ? lodCounts[lodIndex].ToString() : noWorldText, available);
                float share = available ? (float)lodCounts[lodIndex] / maxLodCount : 0f;
                lodBarFills[lodIndex].style.width = Length.Percent(share * 100f);
            }
            lodSampledLabel.style.display =
                available && lastSample.lodSampled ? DisplayStyle.Flex : DisplayStyle.None;

            eventsSummaryLabel.text = string.Format("now {0}   peak {1}", sparkline.Latest, sparkline.Peak);
            SetValue(
                pendingValueLabel, available ? lastSample.pendingEventActorCount.ToString() : noWorldText, available);
            SetValue(
                windowsValueLabel,
                available ? lastSample.actorsWithOpenWindowsCount.ToString() : noWorldText,
                available);

            SetValue(vatPartsValueLabel, available ? lastSample.vatBoundPartCount.ToString() : noWorldText, available);
            SetValue(
                vatTexturesValueLabel,
                available ? lastSample.vatDistinctTextureCount.ToString() : noWorldText,
                available);
            SetValue(
                vatMemoryValueLabel,
                available ? StatsSnapshotFormatting.FormatMegabytes(lastSample.vatTextureBytes) : noWorldText,
                available);

            bool timingsAvailable = lastSample.timingsAvailable;
            string timingFallbackText = available ? "unavailable" : noWorldText;
            SetValue(
                timeToolkitValueLabel,
                timingsAvailable
                    ? StatsSnapshotFormatting.FormatMilliseconds(lastSample.toolkitGroupMilliseconds)
                    : timingFallbackText,
                available);
            SetValue(
                timeBindingValueLabel,
                timingsAvailable
                    ? StatsSnapshotFormatting.FormatMilliseconds(lastSample.bindingGroupMilliseconds)
                    : timingFallbackText,
                available);
            SetValue(
                timeLogicValueLabel,
                timingsAvailable
                    ? StatsSnapshotFormatting.FormatMilliseconds(lastSample.logicGroupMilliseconds)
                    : timingFallbackText,
                available);
            SetValue(
                timePresentationValueLabel,
                timingsAvailable
                    ? StatsSnapshotFormatting.FormatMilliseconds(lastSample.presentationGroupMilliseconds)
                    : timingFallbackText,
                available);
            SetValue(
                timeRagdollValueLabel,
                timingsAvailable
                    ? StatsSnapshotFormatting.FormatMilliseconds(lastSample.ragdollGroupMilliseconds)
                    : timingFallbackText,
                available);
            if (available && !timingsAvailable)
            {
                timeToolkitValueLabel.tooltip = TimingUnavailableTooltip;
                timeBindingValueLabel.tooltip = TimingUnavailableTooltip;
                timeLogicValueLabel.tooltip = TimingUnavailableTooltip;
                timePresentationValueLabel.tooltip = TimingUnavailableTooltip;
                timeRagdollValueLabel.tooltip = TimingUnavailableTooltip;
            }

            if (!available)
            {
                isShowingUnavailableStatus = true;
                ToolkitChrome.SetStatus(
                    statusLabel, "Enter Play mode to read the default world.", ToolkitStatusTone.Neutral);
            }
            else if (isShowingUnavailableStatus)
            {
                isShowingUnavailableStatus = false;
                ToolkitChrome.SetStatus(statusLabel, string.Empty, ToolkitStatusTone.Neutral);
            }
        }

        private static Label MakeValueRow(VisualElement body, string caption, string valueElementName)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-list-row");

            Label captionLabel = new Label(caption);
            captionLabel.AddToClassList("toolkit-list-row__title");
            row.Add(captionLabel);

            Label valueLabel = new Label { name = valueElementName };
            valueLabel.AddToClassList("toolkit-list-row__meta");
            row.Add(valueLabel);

            body.Add(row);
            return valueLabel;
        }

        private static Label MakeLodRow(
            VisualElement body, string caption, string valueElementName, out VisualElement barFill)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-list-row");

            Label captionLabel = new Label(caption);
            captionLabel.AddToClassList("toolkit-list-row__title");
            row.Add(captionLabel);

            VisualElement track = new VisualElement();
            track.style.flexGrow = 1f;
            track.style.marginRight = 6f;
            row.Add(track);

            barFill = new VisualElement();
            barFill.AddToClassList("toolkit-chip__block");
            barFill.AddToClassList("toolkit-chip__block--filled");
            barFill.style.height = 8f;
            barFill.style.width = Length.Percent(0f);
            track.Add(barFill);

            Label valueLabel = new Label { name = valueElementName };
            valueLabel.AddToClassList("toolkit-list-row__meta");
            row.Add(valueLabel);

            body.Add(row);
            return valueLabel;
        }

        private static void SetValue(Label valueLabel, string text, bool available)
        {
            valueLabel.text = text;
            valueLabel.tooltip = available ? string.Empty : NoWorldTooltip;
        }

        private static string ResolvePackageVersion()
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(StatsPanel).Assembly);
            return packageInfo != null ? packageInfo.version : "unknown";
        }
    }
}
