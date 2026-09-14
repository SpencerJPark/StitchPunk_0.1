using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    public sealed class WorktreeLegend : VisualElement
    {
        private readonly Label firstUseHintLabel;

        public WorktreeLegend()
        {
            AddToClassList("worktree-legend");
            pickingMode = PickingMode.Ignore;

            Label titleLabel = new Label("KEY");
            titleLabel.AddToClassList("worktree-legend__title");
            titleLabel.pickingMode = PickingMode.Ignore;
            Add(titleLabel);

            this.AddLegendRow("worktree-legend__swatch--crimson", "Open in your Editor");
            this.AddLegendRow("worktree-legend__swatch--teal", "Ready to act on");
            this.AddLegendRow("worktree-legend__swatch--locked", "Locked or waiting");

            this.firstUseHintLabel = new Label("Click a tile to see its details and actions.");
            this.firstUseHintLabel.AddToClassList("worktree-legend__hint");
            this.firstUseHintLabel.pickingMode = PickingMode.Ignore;
            Add(this.firstUseHintLabel);
        }

        public void SetFirstUseHintVisible(bool isVisible)
        {
            this.firstUseHintLabel.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void AddLegendRow(string swatchModifierClass, string labelText)
        {
            VisualElement rowElement = new VisualElement();
            rowElement.AddToClassList("worktree-legend__row");
            rowElement.pickingMode = PickingMode.Ignore;
            rowElement.style.flexDirection = FlexDirection.Row;
            rowElement.style.alignItems = Align.Center;

            VisualElement swatchElement = new VisualElement();
            swatchElement.AddToClassList("worktree-legend__swatch");
            swatchElement.AddToClassList(swatchModifierClass);
            swatchElement.pickingMode = PickingMode.Ignore;
            swatchElement.style.width = 12;
            swatchElement.style.height = 12;
            rowElement.Add(swatchElement);

            Label rowLabel = new Label(labelText);
            rowLabel.AddToClassList("worktree-legend__label");
            rowLabel.pickingMode = PickingMode.Ignore;
            rowElement.Add(rowLabel);

            Add(rowElement);
        }
    }
}
