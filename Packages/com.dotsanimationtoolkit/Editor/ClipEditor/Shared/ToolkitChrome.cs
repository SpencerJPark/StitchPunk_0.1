// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public enum ToolkitStatusTone
    {
        Neutral,
        Warning,
        Error
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
        private const string StatusRowClassName = "toolkit-status-row";
        private const string StatusRowFooterClassName = "toolkit-status-row--footer";
        private const string StatusClassName = "toolkit-status";
        private const string StatusActionsClassName = "toolkit-status-actions";
        private const string StatusWarningClassName = "toolkit-text--warning";
        private const string StatusErrorClassName = "toolkit-text--error";
        private const string BoxClassName = "toolkit-box";
        private const string ListRowClassName = "toolkit-list-row";
        private const string SeverityDotClassName = "toolkit-severity-dot";

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
            primaryActionButton.AddToClassList(PrimaryActionClassName);
            return primaryActionButton;
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
            // A margin on this outer slot is a no-op, so the boxed row that actually wants the gap
            // has to live one level deeper, as a plain child Unity's pooling never touches.
            VisualElement slot = new VisualElement();
            // Unity also paints its own hover/selected background straight onto this slot (verified
            // live: unity-collection-view__item--selected resolves a solid grey fill across the
            // WHOLE slot, gap margin included) -- an inline override beats that USS state styling
            // unconditionally, so the slot itself never shades and only the boxed row below reacts.
            slot.style.backgroundColor = Color.clear; // colour from data

            row = new VisualElement { name = rowElementName };
            row.AddToClassList(BoxClassName);
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
    }
}
