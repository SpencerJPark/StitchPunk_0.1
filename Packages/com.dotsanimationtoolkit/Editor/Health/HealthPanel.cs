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
    /// <summary>The Health tab: Scan, per-severity counts and filters, a text filter and the findings list; rescans shortly after toolkit assets change.</summary>
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

        private readonly Label errorCountLabel;
        private readonly Label warningCountLabel;
        private readonly Label noteCountLabel;
        private readonly ToolbarSearchField filterField;
        private readonly ToolbarToggle errorsToggle;
        private readonly ToolbarToggle warningsToggle;
        private readonly ToolbarToggle notesToggle;
        private readonly HealthFindingListElement findingListElement;

        private bool isBound;
        private double dueTimeSinceStartup = -1.0;

        public HealthPanel()
        {
            name = "health-panel";
            style.flexGrow = 1f;

            Toolbar toolbar = new Toolbar();

            Button scanButton = new Button(Scan);
            scanButton.name = "health-scan-button";
            scanButton.tooltip = "Scan every toolkit asset";
            scanButton.text = "Scan";
            toolbar.Add(scanButton);

            errorCountLabel = new Label();
            errorCountLabel.name = "health-count-errors";
            toolbar.Add(BuildCountRow(errorCountLabel, ToolkitPalette.Error));

            warningCountLabel = new Label();
            warningCountLabel.name = "health-count-warnings";
            toolbar.Add(BuildCountRow(warningCountLabel, ToolkitPalette.Warning));

            noteCountLabel = new Label();
            noteCountLabel.name = "health-count-notes";
            toolbar.Add(BuildCountRow(noteCountLabel, ToolkitPalette.Accent));

            filterField = new ToolbarSearchField();
            filterField.name = "health-filter-field";
            filterField.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(filterField);

            errorsToggle = new ToolbarToggle();
            errorsToggle.name = "health-filter-errors";
            errorsToggle.text = "Errors";
            errorsToggle.value = true;
            errorsToggle.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(errorsToggle);

            warningsToggle = new ToolbarToggle();
            warningsToggle.name = "health-filter-warnings";
            warningsToggle.text = "Warnings";
            warningsToggle.value = true;
            warningsToggle.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(warningsToggle);

            notesToggle = new ToolbarToggle();
            notesToggle.name = "health-filter-notes";
            notesToggle.text = "Notes";
            notesToggle.value = true;
            notesToggle.RegisterValueChangedCallback(OnFilterChanged);
            toolbar.Add(notesToggle);

            Add(toolbar);

            findingListElement = new HealthFindingListElement();
            findingListElement.style.flexGrow = 1f;
            findingListElement.FixApplied += onFixApplied => Scan();
            Add(findingListElement);
        }

        private static VisualElement BuildCountRow(Label countLabel, Color dotColor)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginLeft = 4f;
            row.style.marginRight = 4f;

            VisualElement dot = new VisualElement();
            dot.style.width = 8f;
            dot.style.height = 8f;
            dot.style.borderTopLeftRadius = 4f;
            dot.style.borderTopRightRadius = 4f;
            dot.style.borderBottomLeftRadius = 4f;
            dot.style.borderBottomRightRadius = 4f;
            dot.style.backgroundColor = dotColor;
            dot.style.marginRight = 4f;
            row.Add(dot);

            row.Add(countLabel);
            return row;
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

            errorCountLabel.text = errorCount + " errors";
            warningCountLabel.text = warningCount + " warnings";
            noteCountLabel.text = noteCount + " notes";
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
