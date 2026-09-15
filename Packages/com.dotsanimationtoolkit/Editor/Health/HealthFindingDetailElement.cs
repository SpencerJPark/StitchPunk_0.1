// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Health tab's right panel: the selected finding's title, explanation, affected assets and fix actions.</summary>
    public sealed class HealthFindingDetailElement : VisualElement
    {
        public event Action<HealthFinding, HealthFindingAction> ActionRan;

        private readonly ScrollView scrollView;
        private readonly Label emptyLabel;

        public HealthFindingDetailElement()
        {
            name = "health-finding-detail";
            style.flexGrow = 1f;

            scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            Add(scrollView);

            emptyLabel = new Label("Select a finding to see what's wrong and how to fix it.");
            emptyLabel.name = "health-finding-detail-empty";
            emptyLabel.style.whiteSpace = WhiteSpace.Normal;
            emptyLabel.style.marginTop = 12f;
            emptyLabel.style.marginLeft = 12f;
            Add(emptyLabel);
        }

        public void SetFinding(HealthFinding finding)
        {
            scrollView.Clear();

            if (finding == null)
            {
                scrollView.style.display = DisplayStyle.None;
                emptyLabel.style.display = DisplayStyle.Flex;
                return;
            }

            scrollView.style.display = DisplayStyle.Flex;
            emptyLabel.style.display = DisplayStyle.None;

            scrollView.Add(BuildHeaderRow(finding));

            Label titleLabel = new Label(finding.title);
            titleLabel.style.fontSize = 14f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.marginBottom = 6f;
            scrollView.Add(titleLabel);

            Label messageLabel = new Label(finding.message);
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.marginBottom = 4f;
            scrollView.Add(messageLabel);

            Label detailLabel = new Label(finding.detail);
            detailLabel.style.whiteSpace = WhiteSpace.Normal;
            detailLabel.style.marginBottom = 10f;
            scrollView.Add(detailLabel);

            scrollView.Add(BuildAffectedSection(finding));
            scrollView.Add(BuildFixSection(finding));
        }

        private static VisualElement BuildHeaderRow(HealthFinding finding)
        {
            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.marginBottom = 4f;

            Label codeLabel = new Label(finding.code);
            codeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            codeLabel.style.marginRight = 8f;
            headerRow.Add(codeLabel);

            Label severityLabel = new Label(SeverityWord(finding.severity));
            severityLabel.style.color = SeverityColor(finding.severity);
            headerRow.Add(severityLabel);

            return headerRow;
        }

        private VisualElement BuildAffectedSection(HealthFinding finding)
        {
            VisualElement section = MakeSectionBox();
            section.Add(MakeSectionHeader("Affected"));

            List<UnityEngine.Object> distinctAssets = CollectDistinctAssets(finding);
            foreach (UnityEngine.Object asset in distinctAssets)
            {
                section.Add(BuildAffectedAssetRow(asset));
            }

            return section;
        }

        private static List<UnityEngine.Object> CollectDistinctAssets(HealthFinding finding)
        {
            List<UnityEngine.Object> distinctAssets = new List<UnityEngine.Object>();
            AddDistinctAsset(distinctAssets, finding.target);
            AddDistinctAsset(distinctAssets, finding.secondaryTarget);
            if (finding.relatedAssets != null)
            {
                for (int relatedIndex = 0; relatedIndex < finding.relatedAssets.Count; relatedIndex++)
                {
                    AddDistinctAsset(distinctAssets, finding.relatedAssets[relatedIndex]);
                }
            }

            return distinctAssets;
        }

        private static void AddDistinctAsset(List<UnityEngine.Object> distinctAssets, UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return;
            }

            for (int assetIndex = 0; assetIndex < distinctAssets.Count; assetIndex++)
            {
                if (distinctAssets[assetIndex] == asset)
                {
                    return;
                }
            }

            distinctAssets.Add(asset);
        }

        private VisualElement BuildAffectedAssetRow(UnityEngine.Object asset)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;

            Button locateButton = new Button();
            locateButton.text = "▸ " + asset.name;
            locateButton.tooltip = AssetDatabase.GetAssetPath(asset);
            locateButton.style.flexGrow = 1f;
            locateButton.clicked += () =>
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            };
            row.Add(locateButton);

            if (asset is ClipAsset || asset is CutsceneAsset || asset is ActorProfileAsset)
            {
                Button openButton = new Button();
                openButton.text = "Open";
                openButton.style.marginLeft = 4f;
                openButton.clicked += () => AssetDatabase.OpenAsset(asset);
                row.Add(openButton);
            }

            return row;
        }

        private VisualElement BuildFixSection(HealthFinding finding)
        {
            VisualElement section = MakeSectionBox();
            section.Add(MakeSectionHeader("How to fix"));

            if (finding.actions == null || finding.actions.Count == 0)
            {
                Label noFixLabel = new Label("No automatic fix; locate the asset and edit it.");
                noFixLabel.style.whiteSpace = WhiteSpace.Normal;
                section.Add(noFixLabel);
                return section;
            }

            for (int actionIndex = 0; actionIndex < finding.actions.Count; actionIndex++)
            {
                section.Add(BuildActionRow(finding, finding.actions[actionIndex]));
            }

            return section;
        }

        private VisualElement BuildActionRow(HealthFinding finding, HealthFindingAction action)
        {
            VisualElement rowContainer = new VisualElement();
            rowContainer.style.marginBottom = 6f;

            Button actionButton = new Button();
            actionButton.name = "health-finding-action";
            actionButton.text = action.label;
            actionButton.style.flexGrow = 1f;
            if (action.isDestructive)
            {
                actionButton.style.backgroundColor = ToolkitPalette.Error;
            }

            actionButton.clicked += () => RunAction(finding, action);
            rowContainer.Add(actionButton);

            Label descriptionLabel = new Label(action.description);
            descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            descriptionLabel.style.opacity = 0.7f;
            descriptionLabel.style.fontSize = 11f;
            rowContainer.Add(descriptionLabel);

            return rowContainer;
        }

        private void RunAction(HealthFinding finding, HealthFindingAction action)
        {
            if (action.buildConfirmation != null)
            {
                string confirmationText = action.buildConfirmation();
                string confirmButtonText = action.isDestructive ? "Move to Trash" : "OK";
                if (!EditorUtility.DisplayDialog(action.label, confirmationText, confirmButtonText, "Cancel"))
                {
                    return;
                }
            }

            action.run?.Invoke();
            ActionRan?.Invoke(finding, action);
        }

        private static VisualElement MakeSectionBox()
        {
            VisualElement section = new VisualElement();
            section.style.borderTopWidth = 1f;
            section.style.borderBottomWidth = 1f;
            section.style.borderLeftWidth = 1f;
            section.style.borderRightWidth = 1f;
            section.style.borderTopColor = ToolkitPalette.BoxBorder;
            section.style.borderBottomColor = ToolkitPalette.BoxBorder;
            section.style.borderLeftColor = ToolkitPalette.BoxBorder;
            section.style.borderRightColor = ToolkitPalette.BoxBorder;
            section.style.backgroundColor = ToolkitPalette.BoxFill;
            section.style.borderTopLeftRadius = 3f;
            section.style.borderTopRightRadius = 3f;
            section.style.borderBottomLeftRadius = 3f;
            section.style.borderBottomRightRadius = 3f;
            section.style.paddingTop = 6f;
            section.style.paddingBottom = 6f;
            section.style.paddingLeft = 6f;
            section.style.paddingRight = 6f;
            section.style.marginBottom = 6f;
            return section;
        }

        private static Label MakeSectionHeader(string headerText)
        {
            Label header = new Label(headerText);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4f;
            return header;
        }

        private static string SeverityWord(HealthSeverity severity)
        {
            switch (severity)
            {
                case HealthSeverity.Error:
                    return "Error";
                case HealthSeverity.Warning:
                    return "Warning";
                default:
                    return "Note";
            }
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
