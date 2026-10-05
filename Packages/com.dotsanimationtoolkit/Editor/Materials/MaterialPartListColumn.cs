// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab's left column: one row per rig part with a check mark for its material inputs.</summary>
    public sealed class MaterialPartListColumn : VisualElement
    {
        private const string RowBoxName = "materials-part-row-box";
        private const string RowTitleName = "materials-part-row-title";
        private const string RowMetaName = "materials-part-row-meta";
        private const string RowMarkName = "materials-part-row-mark";

        private readonly List<PartInputReport> reports = new List<PartInputReport>();
        private readonly ListView partsListView;
        private readonly VisualElement emptyState;

        public event Action<PartInputReport> PartSelected;
        public event Action RefreshRequested;

        public PartInputReport SelectedReport { get; private set; }

        public MaterialPartListColumn()
        {
            AddToClassList("toolkit-column");

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            Label titleLabel = new Label("Parts");
            titleLabel.AddToClassList("toolkit-pane-title");
            header.Add(titleLabel);

            VisualElement headerActions = new VisualElement();
            headerActions.AddToClassList("toolkit-pane-actions");
            Button refreshButton = ToolkitChrome.MakeIconSquare(
                () => RefreshRequested?.Invoke(), "d_Refresh", "Run the check again");
            refreshButton.name = "materials-parts-refresh-button";
            headerActions.Add(refreshButton);
            header.Add(headerActions);
            Add(header);

            partsListView = new ListView();
            partsListView.name = "materials-parts-list";
            partsListView.fixedItemHeight = 22f;
            partsListView.selectionType = SelectionType.Single;
            partsListView.style.flexGrow = 1f;
            partsListView.makeItem = MakeRow;
            partsListView.bindItem = BindRow;
            partsListView.itemsSource = reports;
            partsListView.selectionChanged += OnListSelectionChanged;
            partsListView.AddToClassList("toolkit-list-surface");
            Add(partsListView);

            emptyState = ToolkitChrome.MakeEmptyState(
                "materials-parts-empty", "No parts", "Pick a rig with targets to check its parts.", null, null);
            Add(emptyState);
            UpdateEmptyState();
        }

        public void SetReports(IReadOnlyList<PartInputReport> newReports, bool isClipSetBound)
        {
            reports.Clear();
            if (newReports != null)
            {
                for (int reportIndex = 0; reportIndex < newReports.Count; reportIndex++)
                {
                    reports.Add(newReports[reportIndex]);
                }
            }

            SelectedReport = null;
            partsListView.ClearSelection();
            partsListView.RefreshItems();
            UpdateEmptyState();
        }

        public void SetSelected(PartInputReport report)
        {
            SelectedReport = report;
            int reportIndex = report != null ? reports.IndexOf(report) : -1;
            if (reportIndex >= 0)
            {
                partsListView.SetSelectionWithoutNotify(new[] { reportIndex });
            }
            else
            {
                partsListView.ClearSelection();
            }

            partsListView.RefreshItems();
        }

        private void UpdateEmptyState()
        {
            bool hasReports = reports.Count > 0;
            emptyState.style.display = hasReports ? DisplayStyle.None : DisplayStyle.Flex;
            partsListView.style.display = hasReports ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement MakeRow()
        {
            VisualElement itemSlot = ToolkitChrome.MakeListRowSlot(RowBoxName, out VisualElement row);

            Label titleLabel = new Label();
            titleLabel.name = RowTitleName;
            titleLabel.AddToClassList("toolkit-list-row__title");
            row.Add(titleLabel);

            Label metaLabel = new Label();
            metaLabel.name = RowMetaName;
            metaLabel.AddToClassList("toolkit-list-row__meta");
            row.Add(metaLabel);

            Label markLabel = new Label();
            markLabel.name = RowMarkName;
            markLabel.AddToClassList("toolkit-list-row__mark");
            row.Add(markLabel);

            return itemSlot;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (index < 0 || index >= reports.Count)
            {
                return;
            }

            PartInputReport report = reports[index];
            VisualElement row = element.Q<VisualElement>(RowBoxName);
            row.userData = report;

            Label titleLabel = row.Q<Label>(RowTitleName);
            titleLabel.text = report.DisplayName;

            Label metaLabel = row.Q<Label>(RowMetaName);
            metaLabel.text = report.Materials.Count > 0 && report.Materials[0] != null
                ? report.Materials[0].name
                : "no material";

            Label markLabel = row.Q<Label>(RowMarkName);
            markLabel.text = MarkTextFor(report.State);
            markLabel.EnableInClassList("toolkit-text--error", report.HasMissingInputs);

            row.tooltip = report.DisplayName + "\n" + metaLabel.text;
            row.EnableInClassList("toolkit-list-row--selected", report == SelectedReport);
        }

        private static string MarkTextFor(PartInputState state)
        {
            switch (state)
            {
                case PartInputState.MissingInputs:
                case PartInputState.NoMaterial:
                    return "✗";
                case PartInputState.AllPresent:
                    return "✓";
                default:
                    return "–";
            }
        }

        private void OnListSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (object selectedItem in selectedItems)
            {
                PartInputReport report = selectedItem as PartInputReport;
                SelectedReport = report;
                partsListView.RefreshItems();
                PartSelected?.Invoke(report);
                return;
            }
        }
    }
}
