// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public enum ToolkitStatusTone
    {
        Neutral,
        Warning,
        Error,
        Ok
    }

    public enum ToolkitButtonVariant
    {
        Primary,
        Secondary,
        Ghost,
        Destructive
    }

    /// <summary>
    /// The one place every tab builds its shared chrome elements. Inline styles here are layout
    /// only, except the two lines marked "colour from data".
    /// </summary>
    public static class ToolkitChrome
    {
        private const string ColumnClassName = "toolkit-column";
        private const string PaneHeaderClassName = "toolkit-pane-header";
        private const string PaneTitleClassName = "toolkit-pane-title";
        private const string PaneActionsClassName = "toolkit-pane-actions";
        private const string HeadingClassName = "toolkit-heading";
        private const string HintClassName = "toolkit-hint";
        private const string DetailTitleClassName = "toolkit-detail-title";
        private const string AssetBarClassName = "toolkit-asset-bar";
        private const string AssetBarLabelClassName = "toolkit-asset-bar__label";
        private const string AssetBarSpacerClassName = "toolkit-asset-bar__spacer";
        private const string PrimaryActionClassName = "toolkit-primary-action";
        private const string ActionRunClassName = "toolkit-action-run";
        private const string ActionRunPrimaryClassName = "toolkit-action-run--primary";
        private const string StatusRowClassName = "toolkit-status-row";
        private const string StatusRowFooterClassName = "toolkit-status-row--footer";
        private const string StatusClassName = "toolkit-status";
        private const string StatusActionsClassName = "toolkit-status-actions";
        private const string StatusWarningClassName = "toolkit-text--warning";
        private const string StatusErrorClassName = "toolkit-text--error";
        private const string ListRowClassName = "toolkit-list-row";
        private const string SeverityDotClassName = "toolkit-severity-dot";
        private const string SegmentedClassName = "toolkit-segmented";
        private const string SegmentedItemClassName = "toolkit-segmented__item";
        private const string SegmentedItemOnClassName = "toolkit-segmented__item--on";
        private const string ButtonSecondaryClassName = "toolkit-button--secondary";
        private const string ButtonGhostClassName = "toolkit-button--ghost";
        private const string ButtonDestructiveClassName = "toolkit-button--destructive";
        private const string CardClassName = "toolkit-card";
        private const string CardHeaderClassName = "toolkit-card__header";
        private const string CardTitleClassName = "toolkit-card__title";
        private const string CardActionsClassName = "toolkit-card__actions";
        private const string CardBodyClassName = "toolkit-card__body";
        private const string BadgeClassName = "toolkit-badge";
        private const string BadgeNeutralClassName = "toolkit-badge--neutral";
        private const string BadgeWarningClassName = "toolkit-badge--warning";
        private const string BadgeErrorClassName = "toolkit-badge--error";
        private const string BadgeOkClassName = "toolkit-badge--ok";
        private const string EmptyClassName = "toolkit-empty";
        private const string EmptyTitleClassName = "toolkit-empty__title";
        private const string EmptyWhyClassName = "toolkit-empty__why";
        private const string EmptyActionClassName = "toolkit-empty__action";
        private const string PropertyRowClassName = "toolkit-property-row";
        private const string PropertyRowLabelClassName = "toolkit-property-row__label";
        private const string PropertyRowFieldClassName = "toolkit-property-row__field";

        public static VisualElement MakeColumn(string elementName)
        {
            VisualElement column = new VisualElement { name = elementName };
            column.AddToClassList(ColumnClassName);
            return column;
        }

        public static VisualElement MakePaneHeader(string title, out Label titleLabel, out VisualElement actions)
        {
            VisualElement header = new VisualElement();
            header.AddToClassList(PaneHeaderClassName);

            titleLabel = new Label(title);
            titleLabel.AddToClassList(PaneTitleClassName);

            actions = new VisualElement();
            actions.AddToClassList(PaneActionsClassName);
            actions.AddToClassList(ActionRunClassName);

            header.Add(titleLabel);
            header.Add(actions);
            return header;
        }

        public static Label MakeHeading(string text)
        {
            Label heading = new Label(text);
            heading.AddToClassList(HeadingClassName);
            return heading;
        }

        public static Label MakeHint(string text)
        {
            Label hint = new Label(text);
            hint.AddToClassList(HintClassName);
            return hint;
        }

        public static Label MakeDetailTitle(string text)
        {
            Label detailTitle = new Label(text);
            detailTitle.AddToClassList(DetailTitleClassName);
            return detailTitle;
        }

        public static VisualElement MakeAssetBar(string elementName)
        {
            VisualElement assetBar = new VisualElement { name = elementName };
            assetBar.AddToClassList(AssetBarClassName);
            return assetBar;
        }

        public static Label MakeAssetBarLabel(string text)
        {
            Label assetBarLabel = new Label(text);
            assetBarLabel.AddToClassList(AssetBarLabelClassName);
            return assetBarLabel;
        }

        public static VisualElement MakeAssetBarSpacer()
        {
            VisualElement spacer = new VisualElement();
            spacer.AddToClassList(AssetBarSpacerClassName);
            return spacer;
        }

        public static Button MakePrimaryAction(Action onClick, string iconName, string tooltip, string text)
        {
            Button primaryActionButton = ToolkitIcons.MakeIconTextButton(onClick, iconName, tooltip, text);
            StyleButton(primaryActionButton, ToolkitButtonVariant.Primary);
            return primaryActionButton;
        }

        public static Button MakeSecondaryAction(Action onClick, string iconName, string tooltip, string text)
        {
            Button secondaryActionButton = ToolkitIcons.MakeIconTextButton(onClick, iconName, tooltip, text);
            StyleButton(secondaryActionButton, ToolkitButtonVariant.Secondary);
            return secondaryActionButton;
        }

        public static Button MakeGhostAction(Action onClick, string iconName, string tooltip, string text)
        {
            Button ghostActionButton = ToolkitIcons.MakeIconTextButton(onClick, iconName, tooltip, text);
            StyleButton(ghostActionButton, ToolkitButtonVariant.Ghost);
            return ghostActionButton;
        }

        public static Button MakeDestructiveAction(Action onClick, string iconName, string tooltip, string text)
        {
            Button destructiveActionButton = ToolkitIcons.MakeIconTextButton(onClick, iconName, tooltip, text);
            StyleButton(destructiveActionButton, ToolkitButtonVariant.Destructive);
            return destructiveActionButton;
        }

        public static VisualElement MakeStatusRow(out Label statusLabel, out VisualElement actions, bool isFooter)
        {
            VisualElement statusRow = new VisualElement();
            statusRow.AddToClassList(StatusRowClassName);
            if (isFooter)
            {
                statusRow.AddToClassList(StatusRowFooterClassName);
            }

            statusLabel = new Label();
            statusLabel.AddToClassList(StatusClassName);

            actions = new VisualElement();
            actions.AddToClassList(StatusActionsClassName);

            statusRow.Add(statusLabel);
            statusRow.Add(actions);
            return statusRow;
        }

        public static void SetStatus(Label statusLabel, string text, ToolkitStatusTone tone)
        {
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.text = text;
            statusLabel.EnableInClassList(StatusWarningClassName, tone == ToolkitStatusTone.Warning);
            statusLabel.EnableInClassList(StatusErrorClassName, tone == ToolkitStatusTone.Error);
        }

        public static VisualElement MakeListRowSlot(string rowElementName, out VisualElement row)
        {
            // ListView (FixedHeight virtualization) tags whatever makeItem returns with its own
            // internal item classes and forcibly zeroes ITS margin to keep the fixed-slot math
            // exact (verified live: an 8px inline margin set directly on that root read back as 0).
            // A margin on this outer slot is a no-op, so the row that actually wants the gap
            // has to live one level deeper, as a plain child Unity's pooling never touches.
            VisualElement slot = new VisualElement();
            // Unity also paints its own hover/selected background straight onto this slot (verified
            // live: unity-collection-view__item--selected resolves a solid grey fill across the
            // WHOLE slot, gap margin included) -- an inline override beats that USS state styling
            // unconditionally, so the slot itself never shades and only the row below reacts.
            slot.style.backgroundColor = Color.clear; // colour from data

            row = new VisualElement { name = rowElementName };
            row.AddToClassList(ListRowClassName);
            slot.Add(row);
            return slot;
        }

        public static VisualElement MakeSeverityDot(Color fill)
        {
            VisualElement severityDot = new VisualElement();
            severityDot.AddToClassList(SeverityDotClassName);
            severityDot.style.backgroundColor = fill; // colour from data
            return severityDot;
        }

        public const string ToolkitTokensStyleSheetPath = "Packages/com.dotsanimationtoolkit/Editor/ClipEditor/Shared/ToolkitTokens.uss";
        public const string ToolkitComponentsStyleSheetPath = "Packages/com.dotsanimationtoolkit/Editor/ClipEditor/Shared/ToolkitComponents.uss";

        // Call after the window's own style sheet so the shared layer applies later and wins ties.
        public static void AddToolkitStyleSheets(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            StyleSheet tokensStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ToolkitTokensStyleSheetPath);
            StyleSheet componentsStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ToolkitComponentsStyleSheetPath);

            if (tokensStyleSheet != null && !root.styleSheets.Contains(tokensStyleSheet))
            {
                root.styleSheets.Add(tokensStyleSheet);
            }

            if (componentsStyleSheet != null && !root.styleSheets.Contains(componentsStyleSheet))
            {
                root.styleSheets.Add(componentsStyleSheet);
            }
        }

        public static VisualElement MakeSegmentedControl(string elementName, IReadOnlyList<string> labels, int selectedIndex, Action<int> onSelected)
        {
            VisualElement segmented = new VisualElement { name = elementName };
            segmented.AddToClassList(SegmentedClassName);

            if (labels != null)
            {
                for (int itemIndex = 0; itemIndex < labels.Count; itemIndex++)
                {
                    int capturedIndex = itemIndex;
                    Button segmentButton = new Button(() =>
                    {
                        SetSegmentedSelection(segmented, capturedIndex);
                        onSelected?.Invoke(capturedIndex);
                    })
                    {
                        name = elementName + "-item-" + capturedIndex.ToString(),
                        text = labels[itemIndex]
                    };
                    segmentButton.AddToClassList(SegmentedItemClassName);
                    segmented.Add(segmentButton);
                }
            }

            SetSegmentedSelection(segmented, selectedIndex);
            return segmented;
        }

        public static void SetSegmentedSelection(VisualElement segmented, int selectedIndex)
        {
            if (segmented == null)
            {
                return;
            }

            for (int childIndex = 0; childIndex < segmented.childCount; childIndex++)
            {
                segmented[childIndex].EnableInClassList(SegmentedItemOnClassName, childIndex == selectedIndex);
            }
        }

        public static bool HasButtonVariant(Button button)
        {
            return button != null
                && (button.ClassListContains(PrimaryActionClassName)
                    || button.ClassListContains(ButtonSecondaryClassName)
                    || button.ClassListContains(ButtonGhostClassName)
                    || button.ClassListContains(ButtonDestructiveClassName));
        }

        public static void StyleButton(Button button, ToolkitButtonVariant variant)
        {
            if (button == null)
            {
                return;
            }

            button.RemoveFromClassList(PrimaryActionClassName);
            button.RemoveFromClassList(ButtonSecondaryClassName);
            button.RemoveFromClassList(ButtonGhostClassName);
            button.RemoveFromClassList(ButtonDestructiveClassName);

            switch (variant)
            {
                case ToolkitButtonVariant.Primary:
                    button.AddToClassList(PrimaryActionClassName);
                    // A primary button inside a shared action run gets the run's own accent class,
                    // registered here once rather than by every panel that builds an action run.
                    button.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        if (button.parent != null && button.parent.ClassListContains(ActionRunClassName))
                        {
                            button.parent.AddToClassList(ActionRunPrimaryClassName);
                        }
                    });
                    break;
                case ToolkitButtonVariant.Secondary:
                    button.AddToClassList(ButtonSecondaryClassName);
                    break;
                case ToolkitButtonVariant.Ghost:
                    button.AddToClassList(ButtonGhostClassName);
                    break;
                case ToolkitButtonVariant.Destructive:
                    button.AddToClassList(ButtonGhostClassName);
                    button.AddToClassList(ButtonDestructiveClassName);
                    break;
            }
        }

        public static VisualElement MakeCard(string elementName, string title, out VisualElement body, out VisualElement headerActions)
        {
            VisualElement card = new VisualElement { name = elementName };
            card.AddToClassList(CardClassName);

            VisualElement header = new VisualElement();
            header.AddToClassList(CardHeaderClassName);

            Label headerTitle = new Label(title);
            headerTitle.AddToClassList(CardTitleClassName);

            headerActions = new VisualElement();
            headerActions.AddToClassList(CardActionsClassName);
            headerActions.AddToClassList(ActionRunClassName);

            header.Add(headerTitle);
            header.Add(headerActions);

            body = new VisualElement();
            body.AddToClassList(CardBodyClassName);

            card.Add(header);
            card.Add(body);
            return card;
        }

        public static Label MakeBadge(string text, ToolkitStatusTone tone)
        {
            Label badge = new Label(text);
            badge.AddToClassList(BadgeClassName);

            switch (tone)
            {
                case ToolkitStatusTone.Warning:
                    badge.AddToClassList(BadgeWarningClassName);
                    break;
                case ToolkitStatusTone.Error:
                    badge.AddToClassList(BadgeErrorClassName);
                    break;
                case ToolkitStatusTone.Ok:
                    badge.AddToClassList(BadgeOkClassName);
                    break;
                default:
                    badge.AddToClassList(BadgeNeutralClassName);
                    break;
            }

            return badge;
        }

        public static VisualElement MakeEmptyState(string elementName, string title, string why, string actionText, Action onAction)
        {
            VisualElement empty = new VisualElement { name = elementName };
            empty.AddToClassList(EmptyClassName);

            Label emptyTitle = new Label(title);
            emptyTitle.AddToClassList(EmptyTitleClassName);

            Label emptyWhy = new Label(why);
            emptyWhy.AddToClassList(EmptyWhyClassName);

            empty.Add(emptyTitle);
            empty.Add(emptyWhy);

            if (actionText != null)
            {
                Button button = new Button(onAction) { text = actionText };
                button.AddToClassList(EmptyActionClassName);
                StyleButton(button, ToolkitButtonVariant.Secondary);
                empty.Add(button);
            }

            return empty;
        }

        public static VisualElement MakePropertyRow(string labelText, VisualElement field, string tooltip)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(PropertyRowClassName);

            Label label = new Label(labelText);
            label.AddToClassList(PropertyRowLabelClassName);
            // R01: an ellipsis may only hide text a tooltip can recover. The column now widens to
            // its longest label, but past the cap a label still ellipsizes, so the fallback stays.
            label.tooltip = string.IsNullOrEmpty(tooltip) ? labelText : tooltip;
            label.RegisterCallback<AttachToPanelEvent>(attachEvent =>
                label.schedule.Execute(() => AlignPropertyLabelColumn(label)));

            if (field != null)
            {
                field.AddToClassList(PropertyRowFieldClassName);
            }

            row.Add(label);
            row.Add(field);
            return row;
        }

        // One label column per stack of cards (or per parent, outside a card), sized to its longest label
        // between the USS floor and a cap, so "Default Linear Damping" reads whole instead of
        // every tab sharing one 112px column sized for the shortest words.
        private const float PropertyLabelColumnMinimumWidth = 112f;
        private const float PropertyLabelColumnMaximumWidth = 176f;

        private static void AlignPropertyLabelColumn(Label attachedLabel)
        {
            VisualElement row = attachedLabel.parent;
            if (row == null || attachedLabel.panel == null)
            {
                return;
            }

            // Cards stacked in one column share a column, so their fields line up card to card.
            VisualElement labelGroup = row.parent;
            for (VisualElement ancestor = row.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (ancestor.ClassListContains(CardClassName))
                {
                    labelGroup = ancestor.parent ?? ancestor;
                    break;
                }
            }

            if (labelGroup == null)
            {
                return;
            }

            List<Label> groupLabels = labelGroup.Query<Label>(className: PropertyRowLabelClassName).ToList();
            float widestLabelWidth = PropertyLabelColumnMinimumWidth;
            foreach (Label groupLabel in groupLabels)
            {
                Vector2 measuredSize = groupLabel.MeasureTextSize(groupLabel.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
                // A ceiling, not a round: a fractional measure truncated down ellipsizes the last glyph.
                widestLabelWidth = Mathf.Max(widestLabelWidth, Mathf.Ceil(measuredSize.x) + 2f);
            }

            float columnWidth = Mathf.Min(widestLabelWidth, PropertyLabelColumnMaximumWidth);
            foreach (Label groupLabel in groupLabels)
            {
                groupLabel.style.width = columnWidth;
                groupLabel.style.minWidth = columnWidth;
                groupLabel.style.maxWidth = columnWidth;
            }
        }
    }
}
