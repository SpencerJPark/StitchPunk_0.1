// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A small coloured dot plus label showing whether a VAT bake is Fresh, Stale or Unbaked,
    /// with the reason surfaced as a tooltip.
    /// </summary>
    public sealed class VatFreshnessBadgeElement : VisualElement
    {
        private readonly VisualElement dot;
        private readonly Label label;

        public VatFreshnessBadgeElement()
        {
            name = "vat-freshness-badge";
            AddToClassList("toolkit-chip");
            style.flexShrink = 0f;

            dot = ToolkitChrome.MakeSeverityDot(ToolkitPalette.Clean);
            dot.name = "vat-freshness-dot";
            Add(dot);

            label = new Label { name = "vat-freshness-label" };
            Add(label);
        }

        public void Refresh(VatBakeFreshness freshness, string reason)
        {
            bool isStale = freshness == VatBakeFreshness.Stale;
            bool isBroken = freshness != VatBakeFreshness.Fresh && freshness != VatBakeFreshness.Stale;

            switch (freshness)
            {
                case VatBakeFreshness.Fresh:
                    label.text = "Fresh";
                    dot.style.backgroundColor = ToolkitPalette.Clean; // colour from data
                    break;
                case VatBakeFreshness.Stale:
                    label.text = "Stale";
                    dot.style.backgroundColor = ToolkitPalette.Warning; // colour from data
                    break;
                case VatBakeFreshness.Unbaked:
                default:
                    label.text = "Unbaked";
                    dot.style.backgroundColor = ToolkitPalette.Error; // colour from data
                    break;
            }

            label.EnableInClassList("toolkit-text--warning", isStale);
            label.EnableInClassList("toolkit-text--error", isBroken);

            tooltip = reason ?? string.Empty;
        }
    }
}
