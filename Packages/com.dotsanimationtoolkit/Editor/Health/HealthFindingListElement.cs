// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Lists Health findings as flat single-line rows (severity dot, code, title on the left;
    /// the target asset name muted and right-aligned) and reports selection for a detail panel to act on.</summary>
    public sealed class HealthFindingListElement : VisualElement
    {
        private readonly List<HealthFinding> findings = new List<HealthFinding>();
        private readonly ListView findingListView;
        private readonly Label emptyLabel;
        private readonly Label titleLabel;
        private HealthFinding selectedFinding;

        public event Action<HealthFinding> FindingSelected;

        public HealthFinding SelectedFinding => selectedFinding;

        public HealthFindingListElement()
        {
            name = "health-finding-list";
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;

            Add(ToolkitChrome.MakePaneHeader("Findings", out titleLabel, out _));

            findingListView = new ListView();
            findingListView.name = "health-finding-list-view";
            findingListView.style.flexGrow = 1f;
            // A flat single-line row.
            findingListView.fixedItemHeight = 22f;
            findingListView.selectionType = SelectionType.Single;
            findingListView.makeItem = MakeFindingRow;
            findingListView.bindItem = BindFindingRow;
            findingListView.itemsSource = findings;
            findingListView.selectionChanged += OnFindingSelectionChanged;
            findingListView.AddToClassList("toolkit-list-surface");
            Add(findingListView);

            emptyLabel = ToolkitChrome.MakeHint("No findings.");
            emptyLabel.name = "health-finding-empty";
            Add(emptyLabel);

            RefreshEmptyState();
        }

        public void SetFindings(IReadOnlyList<HealthFinding> findings)
        {
            this.findings.Clear();
            if (findings != null)
            {
                this.findings.AddRange(findings);
            }

            titleLabel.text = "Findings (" + this.findings.Count + ")";

            // The panel re-selects immediately after calling this, so the old selection is cleared
            // silently here rather than through ClearSelection, which would fire FindingSelected(null)
            // for a frame before the panel's own re-selection lands.
            findingListView.SetSelectionWithoutNotify(Array.Empty<int>());
            selectedFinding = null;

            findingListView.Rebuild();
            RefreshEmptyState();
        }

        public void SelectFinding(HealthFinding finding)
        {
            if (finding == null)
            {
                findingListView.ClearSelection();
                return;
            }

            int index = findings.IndexOf(finding);
            if (index < 0)
            {
                findingListView.ClearSelection();
                return;
            }

            findingListView.SetSelection(index);
            findingListView.ScrollToItem(index);
        }

        private void OnFindingSelectionChanged(IEnumerable<object> selectedItems)
        {
            HealthFinding selected = null;
            foreach (object selectedItem in selectedItems)
            {
                selected = selectedItem as HealthFinding;
                break;
            }

            selectedFinding = selected;
            FindingSelected?.Invoke(selected);
        }

        private void RefreshEmptyState()
        {
            findingListView.style.display = findings.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = findings.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement MakeFindingRow()
        {
            VisualElement itemSlot = ToolkitChrome.MakeListRowSlot("health-finding-row", out VisualElement row);

            VisualElement dot = ToolkitChrome.MakeSeverityDot(Color.clear);
            dot.name = "health-finding-dot";
            dot.style.marginRight = 6f;
            row.Add(dot);

            Label titleLabel = new Label();
            titleLabel.name = "health-finding-title";
            titleLabel.AddToClassList("toolkit-list-row__title");
            row.Add(titleLabel);

            Label assetLabel = new Label();
            assetLabel.name = "health-finding-asset";
            assetLabel.AddToClassList("toolkit-list-row__meta");
            row.Add(assetLabel);

            return itemSlot;
        }

        private void BindFindingRow(VisualElement element, int index)
        {
            HealthFinding finding = findings[index];

            VisualElement row = element.Q<VisualElement>("health-finding-row");
            row.tooltip = finding.message;

            VisualElement dot = row.Q<VisualElement>("health-finding-dot");
            dot.style.backgroundColor = SeverityColor(finding.severity); // colour from data

            Label titleLabel = row.Q<Label>("health-finding-title");
            string title = string.IsNullOrEmpty(finding.title) ? finding.message : finding.title;
            titleLabel.text = finding.code + "  " + title;

            Label assetLabel = row.Q<Label>("health-finding-asset");
            assetLabel.text = finding.target != null ? finding.target.name : "(missing)";
        }

        private static Color SeverityColor(HealthSeverity severity)
        {
            switch (severity)
            {
                case HealthSeverity.Error:
                    return ToolkitPalette.Error;
                case HealthSeverity.Warning:
                    return ToolkitPalette.Warning;
                default:
                    return ToolkitPalette.Accent;
            }
        }
    }
}
