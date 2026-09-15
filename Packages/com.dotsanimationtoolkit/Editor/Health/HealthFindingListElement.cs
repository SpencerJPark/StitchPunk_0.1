// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Lists Health findings as rows: severity dot, code, message, and a locate button naming the asset.</summary>
    public sealed class HealthFindingListElement : VisualElement
    {
        private readonly List<HealthFinding> findings = new List<HealthFinding>();
        private readonly ListView findingListView;
        private readonly Label emptyLabel;

        public HealthFindingListElement()
        {
            name = "health-finding-list";
            style.flexGrow = 1f;

            findingListView = new ListView();
            findingListView.name = "health-finding-list-view";
            findingListView.style.flexGrow = 1f;
            findingListView.fixedItemHeight = 22f;
            findingListView.selectionType = SelectionType.None;
            findingListView.makeItem = MakeFindingRow;
            findingListView.bindItem = BindFindingRow;
            findingListView.itemsSource = findings;
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

            findingListView.Rebuild();
            RefreshEmptyState();
        }

        private void RefreshEmptyState()
        {
            findingListView.style.display = findings.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = findings.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement MakeFindingRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            VisualElement dot = new VisualElement();
            dot.name = "health-finding-dot";
            dot.style.width = 8f;
            dot.style.height = 8f;
            dot.style.borderTopLeftRadius = 4f;
            dot.style.borderTopRightRadius = 4f;
            dot.style.borderBottomLeftRadius = 4f;
            dot.style.borderBottomRightRadius = 4f;
            dot.style.marginLeft = 4f;
            dot.style.marginRight = 6f;
            row.Add(dot);

            Label codeLabel = new Label();
            codeLabel.name = "health-finding-code";
            codeLabel.style.width = 34f;
            codeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(codeLabel);

            Label messageLabel = new Label();
            messageLabel.name = "health-finding-message";
            messageLabel.style.flexGrow = 1f;
            messageLabel.style.flexShrink = 1f;
            messageLabel.style.overflow = Overflow.Hidden;
            messageLabel.style.textOverflow = TextOverflow.Ellipsis;
            messageLabel.style.whiteSpace = WhiteSpace.NoWrap;
            row.Add(messageLabel);

            Button locateButton = new Button();
            locateButton.name = "health-finding-locate";
            locateButton.tooltip = "Select and ping this asset";
            locateButton.clicked += () =>
            {
                HealthFinding rowFinding = row.userData as HealthFinding;
                if (rowFinding == null)
                {
                    return;
                }

                if (rowFinding.target != null)
                {
                    Selection.activeObject = rowFinding.target;
                    EditorGUIUtility.PingObject(rowFinding.target);
                }

                UnityEngine.Object secondaryTarget = rowFinding.secondaryTarget;
                if (secondaryTarget != null)
                {
                    // Pinging both at once shows only the second flash, so the first is delayed
                    // long enough for the target ping to register before it starts.
                    row.schedule.Execute(() => EditorGUIUtility.PingObject(secondaryTarget)).StartingIn(800);
                }
            };
            row.Add(locateButton);

            return row;
        }

        private void BindFindingRow(VisualElement element, int index)
        {
            HealthFinding finding = findings[index];
            element.userData = finding;

            VisualElement dot = element.Q<VisualElement>("health-finding-dot");
            dot.style.backgroundColor = SeverityColor(finding.severity);

            Label codeLabel = element.Q<Label>("health-finding-code");
            codeLabel.text = finding.code;

            Label messageLabel = element.Q<Label>("health-finding-message");
            messageLabel.text = finding.message;
            messageLabel.tooltip = finding.message;

            Button locateButton = element.Q<Button>("health-finding-locate");
            locateButton.text = finding.target != null ? "▸ " + finding.target.name : "▸ (missing)";
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
