// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Health tab: a Scan button and status, per-severity toggle filters, a search field, and a findings list paired with a detail panel; rescans shortly after toolkit assets change.</summary>
    public sealed class HealthPanel : VisualElement, IDisposable
    {
        private const double DebounceSeconds = 0.5;

        public event Action<ClipSetAsset, RigAsset> RebakeRequested;
        public event Action FindingsChanged;

        private readonly List<HealthFinding> latestFindings = new List<HealthFinding>();
        private readonly List<HealthFinding> filteredFindings = new List<HealthFinding>();

        public IReadOnlyList<HealthFinding> LatestFindings
        {
            get { return latestFindings; }
        }

        public int StaleVatBakeCount
        {
            get
            {
                int staleCount = 0;
                foreach (HealthFinding finding in latestFindings)
                {
                    if (finding.IsPinnedFirst)
                    {
                        staleCount++;
                    }
                }

                return staleCount;
            }
        }

        // Every Error finding, the pinned H06 included; the window shows it on the tab as "Health (n)".
        public int ErrorCount
        {
            get
            {
                int errorCount = 0;
                foreach (HealthFinding finding in latestFindings)
                {
                    if (finding.severity == HealthSeverity.Error)
                    {
                        errorCount++;
                    }
                }

                return errorCount;
            }
        }

        // Committed clip edits call this: AssetReferenceIndex.Dirtied only fires on asset changes.
        public void RequestRescan()
        {
            OnAssetReferenceIndexDirtied();
        }

        private readonly Button scanButton;
        private readonly Label scanStatusLabel;
        private readonly ToolbarSearchField filterField;
        private readonly ToolbarToggle errorsToggle;
        private readonly ToolbarToggle warningsToggle;
        private readonly ToolbarToggle notesToggle;
        private readonly HealthFindingListElement findingListElement;
        private readonly HealthFindingDetailElement findingDetailElement;

        private bool isBound;
        private double dueTimeSinceStartup = -1.0;

        // Selection survives a rescan by identity (code + target); falls back to the row index when the finding is gone.
        private string rememberedSelectedCode;
        private UnityEngine.Object rememberedSelectedTarget;
        private int rememberedSelectedIndex = -1;

        public HealthPanel()
        {
            name = "health-panel";
            style.flexGrow = 1f;

            VisualElement toolbar = ToolkitChrome.MakeAssetBar("health-asset-bar");

            scanButton = ToolkitChrome.MakePrimaryAction(Scan, "d_Refresh", "Scan every toolkit asset", "Scan project");
            scanButton.name = "health-scan-button";
            toolbar.Add(scanButton);

            scanStatusLabel = new Label("not scanned yet");
            scanStatusLabel.name = "health-scan-status";
            scanStatusLabel.AddToClassList("toolkit-text--dim");
            scanStatusLabel.style.marginLeft = 8f;
            scanStatusLabel.style.marginRight = 8f;
            toolbar.Add(scanStatusLabel);

            errorsToggle = new ToolbarToggle();
            errorsToggle.name = "health-filter-errors";
            errorsToggle.AddToClassList("clip-editor__bar-action");
            errorsToggle.value = true;
            errorsToggle.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(errorsToggle);

            warningsToggle = new ToolbarToggle();
            warningsToggle.name = "health-filter-warnings";
            warningsToggle.AddToClassList("clip-editor__bar-action");
            warningsToggle.value = true;
            warningsToggle.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(warningsToggle);

            notesToggle = new ToolbarToggle();
            notesToggle.name = "health-filter-notes";
            notesToggle.AddToClassList("clip-editor__bar-action");
            notesToggle.value = true;
            notesToggle.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(notesToggle);

            UpdateCounts();

            toolbar.Add(ToolkitChrome.MakeAssetBarSpacer());

            filterField = new ToolbarSearchField();
            filterField.name = "health-filter-field";
            filterField.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(filterField);

            Add(toolbar);

            CoverPaneSplitView bodySplitView = new CoverPaneSplitView("Health.Findings", 0, 360f, TwoPaneSplitViewOrientation.Horizontal);
            bodySplitView.style.flexGrow = 1f;

            findingListElement = new HealthFindingListElement();
            findingListElement.style.flexGrow = 1f;
            findingListElement.FindingSelected += OnFindingSelected;
            bodySplitView.Add(findingListElement);

            findingDetailElement = new HealthFindingDetailElement();
            findingDetailElement.style.flexGrow = 1f;
            findingDetailElement.ActionRan += OnFindingActionRan;
            bodySplitView.Add(findingDetailElement);

            Add(bodySplitView);
        }

        public void Bind()
        {
            if (isBound)
            {
                return;
            }

            isBound = true;
            AssetReferenceIndex.Dirtied += OnAssetReferenceIndexDirtied;
            Scan();
        }

        public void Scan()
        {
            HealthScanContext context = HealthScanContext.FromProject(RaiseRebakeRequested);
            latestFindings.Clear();
            latestFindings.AddRange(HealthScan.Run(context));
            UpdateCounts();
            UpdateScanStatusLabel();
            ApplyFilter();
            FindingsChanged?.Invoke();
        }

        private void UpdateCounts()
        {
            int errorCount = 0;
            int warningCount = 0;
            int noteCount = 0;
            foreach (HealthFinding finding in latestFindings)
            {
                switch (finding.severity)
                {
                    case HealthSeverity.Error:
                        errorCount++;
                        break;
                    case HealthSeverity.Warning:
                        warningCount++;
                        break;
                    default:
                        noteCount++;
                        break;
                }
            }

            errorsToggle.text = BuildSeverityToggleText(errorCount, ToolkitPalette.Error, "Error", "Errors");
            warningsToggle.text = BuildSeverityToggleText(warningCount, ToolkitPalette.Warning, "Warning", "Warnings");
            notesToggle.text = BuildSeverityToggleText(noteCount, ToolkitPalette.Accent, "Note", "Notes");
        }

        private static string BuildSeverityToggleText(int count, Color dotColor, string singularLabel, string pluralLabel)
        {
            string hexColor = ColorUtility.ToHtmlStringRGB(dotColor);
            string countLabel = count == 1 ? singularLabel : pluralLabel;
            return "<color=#" + hexColor + ">●</color> " + count + " " + countLabel;
        }

        private void UpdateScanStatusLabel()
        {
            int findingCount = latestFindings.Count;
            string countLabel = findingCount == 1 ? "finding" : "findings";
            scanStatusLabel.text = "last scan " + DateTime.Now.ToString("HH:mm") + " · " + findingCount + " " + countLabel;
        }

        private void ApplyFilter()
        {
            filteredFindings.Clear();
            string searchText = filterField.value ?? string.Empty;
            searchText = searchText.Trim();

            foreach (HealthFinding finding in latestFindings)
            {
                if (!IsSeverityEnabled(finding.severity))
                {
                    continue;
                }

                if (searchText.Length > 0 && !MatchesSearch(finding, searchText))
                {
                    continue;
                }

                filteredFindings.Add(finding);
            }

            findingListElement.SetFindings(filteredFindings);
            RestoreSelection();
        }

        private void RestoreSelection()
        {
            if (filteredFindings.Count == 0)
            {
                rememberedSelectedIndex = -1;
                findingDetailElement.SetFinding(null);
                return;
            }

            HealthFinding matchByIdentity = null;
            if (rememberedSelectedCode != null)
            {
                foreach (HealthFinding finding in filteredFindings)
                {
                    if (finding.code == rememberedSelectedCode && finding.target == rememberedSelectedTarget)
                    {
                        matchByIdentity = finding;
                        break;
                    }
                }
            }

            if (matchByIdentity != null)
            {
                findingListElement.SelectFinding(matchByIdentity);
                return;
            }

            int clampedIndex = rememberedSelectedIndex < 0 ? 0 : rememberedSelectedIndex;
            if (clampedIndex >= filteredFindings.Count)
            {
                clampedIndex = filteredFindings.Count - 1;
            }

            findingListElement.SelectFinding(filteredFindings[clampedIndex]);
        }

        private void OnFindingSelected(HealthFinding finding)
        {
            findingDetailElement.SetFinding(finding);
            if (finding == null)
            {
                return;
            }

            rememberedSelectedCode = finding.code;
            rememberedSelectedTarget = finding.target;
            rememberedSelectedIndex = filteredFindings.IndexOf(finding);
        }

        private void OnFindingActionRan(HealthFinding finding, HealthFindingAction action)
        {
            Scan();
        }

        private bool IsSeverityEnabled(HealthSeverity severity)
        {
            switch (severity)
            {
                case HealthSeverity.Error:
                    return errorsToggle.value;
                case HealthSeverity.Warning:
                    return warningsToggle.value;
                default:
                    return notesToggle.value;
            }
        }

        private static bool MatchesSearch(HealthFinding finding, string searchText)
        {
            if (finding.message.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (finding.title.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (finding.target != null && finding.target.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private void OnFilterChanged(ChangeEvent<string> changeEvent)
        {
            ApplyFilter();
        }

        private void OnFilterChanged(ChangeEvent<bool> changeEvent)
        {
            ApplyFilter();
        }

        private void RaiseRebakeRequested(ClipSetAsset clipSetAsset, RigAsset rigAsset)
        {
            RebakeRequested?.Invoke(clipSetAsset, rigAsset);
        }

        private void OnAssetReferenceIndexDirtied()
        {
            bool wasAlreadyPending = dueTimeSinceStartup >= 0.0;
            dueTimeSinceStartup = EditorApplication.timeSinceStartup + DebounceSeconds;
            if (!wasAlreadyPending)
            {
                EditorApplication.update += OnEditorUpdate;
            }
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < dueTimeSinceStartup)
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            dueTimeSinceStartup = -1.0;
            Scan();
        }

        public void Dispose()
        {
            AssetReferenceIndex.Dirtied -= OnAssetReferenceIndexDirtied;
            EditorApplication.update -= OnEditorUpdate;
            dueTimeSinceStartup = -1.0;
            isBound = false;
        }
    }
}
