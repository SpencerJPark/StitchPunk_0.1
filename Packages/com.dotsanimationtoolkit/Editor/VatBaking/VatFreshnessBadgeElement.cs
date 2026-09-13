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
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0f;

            dot = new VisualElement { name = "vat-freshness-dot" };
            dot.style.width = 10f;
            dot.style.height = 10f;
            dot.style.borderTopLeftRadius = 5f;
            dot.style.borderTopRightRadius = 5f;
            dot.style.borderBottomLeftRadius = 5f;
            dot.style.borderBottomRightRadius = 5f;
            dot.style.marginRight = 4f;
            Add(dot);

            label = new Label { name = "vat-freshness-label" };
            Add(label);
        }

        public void Refresh(VatBakeFreshness freshness, string reason)
        {
            switch (freshness)
            {
                case VatBakeFreshness.Fresh:
                    label.text = "Fresh";
                    dot.style.backgroundColor = ToolkitPalette.Clean;
                    label.style.color = ToolkitPalette.Clean;
                    break;
                case VatBakeFreshness.Stale:
                    label.text = "Stale";
                    dot.style.backgroundColor = ToolkitPalette.Warning;
                    label.style.color = ToolkitPalette.Warning;
                    break;
                case VatBakeFreshness.Unbaked:
                default:
                    label.text = "Unbaked";
                    dot.style.backgroundColor = ToolkitPalette.Error;
                    label.style.color = ToolkitPalette.Error;
                    break;
            }

            tooltip = reason ?? string.Empty;
        }
    }
}
