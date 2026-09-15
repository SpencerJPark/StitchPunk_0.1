// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Lists Health findings as two-line boxed rows (severity dot, code, title on line one;
    /// the target asset name on line two) and reports selection for a detail panel to act on.</summary>
    public sealed class HealthFindingListElement : VisualElement
    {
        private readonly List<HealthFinding> findings = new List<HealthFinding>();
        private readonly ListView findingListView;
        private readonly Label emptyLabel;
        private HealthFinding selectedFinding;

        public event Action<HealthFinding> FindingSelected;

        public HealthFinding SelectedFinding => selectedFinding;

        public HealthFindingListElement()
        {
            name = "health-finding-list";
            style.flexGrow = 1f;

            findingListView = new ListView();
            findingListView.name = "health-finding-list-view";
            findingListView.style.flexGrow = 1f;
            findingListView.fixedItemHeight = 42f;
            findingListView.selectionType = SelectionType.Single;
            findingListView.makeItem = MakeFindingRow;
            findingListView.bindItem = BindFindingRow;
            findingListView.itemsSource = findings;
            findingListView.selectionChanged += OnFindingSelectionChanged;
            Add(findingListView);

            emptyLabel = new Label("No findings.");
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
            // ListView tags whatever makeItem returns with its own internal item classes and forces
            // this outer slot's margin to zero, so the boxed row that wants the row-to-row gap has to
            // live one level deeper, as a plain child Unity's pooling never touches (see
            // ToolkitCatalogColumn.MakeRow for the same trap, verified there).
            VisualElement itemSlot = new VisualElement();
            itemSlot.style.backgroundColor = new StyleColor(Color.clear);

            VisualElement row = new VisualElement();
            row.name = "health-finding-row";
            row.AddToClassList("toolkit-box");
            row.style.marginTop = 4f;
            row.style.marginBottom = 4f;
            row.style.marginLeft = 0f;
            row.style.marginRight = 0f;

            VisualElement firstLine = new VisualElement();
            firstLine.name = "health-finding-line-1";
            firstLine.style.flexDirection = FlexDirection.Row;
            firstLine.style.alignItems = Align.Center;

            VisualElement dot = new VisualElement();
            dot.name = "health-finding-dot";
            dot.style.width = 8f;
            dot.style.height = 8f;
            dot.style.borderTopLeftRadius = 4f;
            dot.style.borderTopRightRadius = 4f;
            dot.style.borderBottomLeftRadius = 4f;
            dot.style.borderBottomRightRadius = 4f;
            dot.style.marginRight = 6f;
            firstLine.Add(dot);

            Label codeLabel = new Label();
            codeLabel.name = "health-finding-code";
            codeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            codeLabel.style.marginRight = 6f;
            firstLine.Add(codeLabel);

            Label titleLabel = new Label();
            titleLabel.name = "health-finding-title";
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.flexShrink = 1f;
            titleLabel.style.overflow = Overflow.Hidden;
            titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            firstLine.Add(titleLabel);

            row.Add(firstLine);

            Label assetLabel = new Label();
            assetLabel.name = "health-finding-asset";
            assetLabel.style.opacity = 0.6f;
            assetLabel.style.overflow = Overflow.Hidden;
            assetLabel.style.textOverflow = TextOverflow.Ellipsis;
            assetLabel.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(assetLabel);

            itemSlot.Add(row);
            return itemSlot;
        }

        private void BindFindingRow(VisualElement element, int index)
        {
            HealthFinding finding = findings[index];

            VisualElement row = element.Q<VisualElement>("health-finding-row");
            row.tooltip = finding.message;

            VisualElement dot = row.Q<VisualElement>("health-finding-dot");
            dot.style.backgroundColor = SeverityColor(finding.severity);

            Label codeLabel = row.Q<Label>("health-finding-code");
            codeLabel.text = finding.code;

            Label titleLabel = row.Q<Label>("health-finding-title");
            titleLabel.text = string.IsNullOrEmpty(finding.title) ? finding.message : finding.title;

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
