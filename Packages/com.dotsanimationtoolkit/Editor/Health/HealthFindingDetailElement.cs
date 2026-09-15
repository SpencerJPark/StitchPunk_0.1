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
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;

            scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            Add(scrollView);

            emptyLabel = ToolkitChrome.MakeHint("Select a finding to see what's wrong and how to fix it.");
            emptyLabel.name = "health-finding-detail-empty";
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

            Label findingTitleLabel = ToolkitChrome.MakeDetailTitle(finding.title);
            findingTitleLabel.style.whiteSpace = WhiteSpace.Normal;
            findingTitleLabel.style.marginBottom = 6f;
            scrollView.Add(findingTitleLabel);

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
            VisualElement headerRow = ToolkitChrome.MakePaneHeader(finding.code, out _, out VisualElement actions);

            Label severityLabel = new Label(SeverityWord(finding.severity));
            severityLabel.AddToClassList(SeverityClassName(finding.severity));
            actions.Add(severityLabel);

            return headerRow;
        }

        private VisualElement BuildAffectedSection(HealthFinding finding)
        {
            VisualElement section = MakeSectionBox();
            section.Add(MakeSectionHeader("Affected"));

            VisualElement body = new VisualElement();
            body.AddToClassList("toolkit-box__body");
            section.Add(body);

            List<UnityEngine.Object> distinctAssets = CollectDistinctAssets(finding);
            foreach (UnityEngine.Object asset in distinctAssets)
            {
                body.Add(BuildAffectedAssetRow(asset));
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
            row.AddToClassList("toolkit-box__row");

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
                Button openButton = ToolkitIcons.MakeIconTextButton(() => AssetDatabase.OpenAsset(asset), "editicon.sml", "Open", "Open");
                openButton.style.marginLeft = 4f;
                row.Add(openButton);
            }

            return row;
        }

        private VisualElement BuildFixSection(HealthFinding finding)
        {
            VisualElement section = MakeSectionBox();
            section.Add(MakeSectionHeader("How to fix"));

            VisualElement body = new VisualElement();
            body.AddToClassList("toolkit-box__body");
            section.Add(body);

            if (finding.actions == null || finding.actions.Count == 0)
            {
                Label noFixLabel = new Label("No automatic fix; locate the asset and edit it.");
                noFixLabel.style.whiteSpace = WhiteSpace.Normal;
                body.Add(noFixLabel);
                return section;
            }

            for (int actionIndex = 0; actionIndex < finding.actions.Count; actionIndex++)
            {
                body.Add(BuildActionRow(finding, finding.actions[actionIndex]));
            }

            return section;
        }

        private VisualElement BuildActionRow(HealthFinding finding, HealthFindingAction action)
        {
            VisualElement rowContainer = new VisualElement();
            rowContainer.style.marginBottom = 6f;

            string actionIconName = ResolveActionIconName(action.label);
            Button actionButton = actionIconName == null
                ? new Button { text = action.label }
                : ToolkitIcons.MakeIconTextButton(() => RunAction(finding, action), actionIconName, action.description, action.label);
            actionButton.name = "health-finding-action";
            actionButton.style.flexGrow = 1f;
            if (action.isDestructive)
            {
                actionButton.style.backgroundColor = ToolkitPalette.Error; // colour from data
            }

            if (actionIconName == null)
            {
                actionButton.clicked += () => RunAction(finding, action);
            }

            rowContainer.Add(actionButton);

            Label descriptionLabel = new Label(action.description);
            descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            descriptionLabel.AddToClassList("toolkit-text--dim");
            rowContainer.Add(descriptionLabel);

            return rowContainer;
        }

        // Action labels are data-driven (baked into each HealthRule), so only the verbs that show
        // up verbatim today get an icon; anything else stays a bare-word button.
        private static string ResolveActionIconName(string actionLabel)
        {
            if (string.IsNullOrEmpty(actionLabel))
            {
                return null;
            }

            if (actionLabel.StartsWith("Rebake", StringComparison.Ordinal))
            {
                return "d_Refresh";
            }

            if (actionLabel.StartsWith("Delete", StringComparison.Ordinal))
            {
                return ToolkitIcons.Trash;
            }

            return null;
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
            section.AddToClassList("toolkit-box");
            section.style.marginBottom = 6f;
            return section;
        }

        private static VisualElement MakeSectionHeader(string headerText)
        {
            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-box__header");

            Label title = new Label(headerText);
            title.AddToClassList("toolkit-box__title");
            header.Add(title);

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

        private static string SeverityClassName(HealthSeverity severity)
        {
            switch (severity)
            {
                case HealthSeverity.Error:
                    return "toolkit-text--error";
                case HealthSeverity.Warning:
                    return "toolkit-text--warning";
                default:
                    return "toolkit-text--dim";
            }
        }
    }
}
