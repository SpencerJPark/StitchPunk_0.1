#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class TerrainUi
{
    public const string StylesheetPath = "Assets/_Scripts/Editor/Terrain/Window/TerrainEditor.uss";

    private const int WideRangeThreshold = 20;
    private const string RailClassName = "terrain-ui-rail";
    private const string RailButtonClassName = "terrain-ui-rail-button";
    private const string RailButtonActiveClassName = "terrain-ui-rail-button--active";
    private const string SliderFieldClassName = "terrain-ui-slider-field";
    private const string NumberFieldClassName = "terrain-ui-number-field";
    private const string PaneBodyClassName = "terrain-ui-pane-body";
    private const string PaneClassName = "terrain-ui-pane";
    private const string SegmentedFitClassName = "terrain-ui-segmented-fit";
    private const string IconSquareClassName = "terrain-ui-icon-square";

    public static void ApplyStyles(VisualElement root)
    {
        if (root == null)
        {
            return;
        }

        StyleSheet terrainStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesheetPath);
        if (terrainStyleSheet != null && !root.styleSheets.Contains(terrainStyleSheet))
        {
            root.styleSheets.Add(terrainStyleSheet);
        }

        ToolkitChrome.AddToolkitStyleSheets(root);
    }

    // Blender-style navigation: a vertical rail of icon buttons down the left edge of the panel, so the full width stays free for the
    // controls. Names live in the tooltips and the pane title; an icon that fails to resolve falls back to the first letter.
    public static VisualElement MakeTabRail(IReadOnlyList<string> labels, IReadOnlyList<string> iconNames, Action<int> onSelected, out Action<int> setSelectedIndex)
    {
        VisualElement rail = new VisualElement();
        rail.AddToClassList(RailClassName);

        List<Button> buttons = new List<Button>();
        Action<int> applyActive = (int activeIndex) =>
        {
            for (int buttonIndex = 0; buttonIndex < buttons.Count; buttonIndex++)
            {
                buttons[buttonIndex].EnableInClassList(RailButtonActiveClassName, buttonIndex == activeIndex);
            }
        };

        for (int labelIndex = 0; labelIndex < labels.Count; labelIndex++)
        {
            int capturedIndex = labelIndex;
            string iconName = iconNames != null && labelIndex < iconNames.Count ? iconNames[labelIndex] : null;
            string fallbackText = labels[labelIndex].Substring(0, 1);
            Button button = ToolkitIcons.MakeIconButton(() =>
            {
                applyActive(capturedIndex);
                onSelected?.Invoke(capturedIndex);
            }, iconName, labels[labelIndex], fallbackText);
            ToolkitChrome.StyleButton(button, ToolkitButtonVariant.Ghost);
            button.AddToClassList(RailButtonClassName);
            buttons.Add(button);
            rail.Add(button);
        }

        setSelectedIndex = applyActive;
        applyActive(0);
        return rail;
    }

    public static VisualElement MakePane(string title, out VisualElement actions, out VisualElement body)
    {
        VisualElement pane = new VisualElement();
        pane.AddToClassList(PaneClassName);

        Label titleLabel;
        VisualElement header = ToolkitChrome.MakePaneHeader(title, out titleLabel, out actions);
        pane.Add(header);

        ScrollView scrollBody = new ScrollView(ScrollViewMode.Vertical);
        scrollBody.AddToClassList(PaneBodyClassName);
        pane.Add(scrollBody);
        body = scrollBody;
        return pane;
    }

    public static VisualElement MakeCard(string title, out VisualElement body)
    {
        VisualElement headerActions;
        return ToolkitChrome.MakeCard(null, title, out body, out headerActions);
    }

    public static VisualElement MakeSliderRow(string label, float minimum, float maximum, Func<float> read, Action<float> write, string tooltip, out Action refresh)
    {
        // The toolkit property row owns the label column, so sliders, fields and pickers share one aligned grid (R08).
        VisualElement field = new VisualElement();
        field.AddToClassList(SliderFieldClassName);

        Slider slider = new Slider(minimum, maximum);
        slider.showInputField = false;
        slider.style.flexGrow = 1f;

        FloatField numberField = new FloatField();
        numberField.AddToClassList(NumberFieldClassName);
        numberField.formatString = (maximum - minimum) >= WideRangeThreshold ? "F0" : "F2";

        slider.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) =>
        {
            numberField.SetValueWithoutNotify(changeEvent.newValue);
            write?.Invoke(changeEvent.newValue);
        });
        numberField.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) =>
        {
            float clamped = Mathf.Clamp(changeEvent.newValue, minimum, maximum);
            slider.SetValueWithoutNotify(clamped);
            numberField.SetValueWithoutNotify(clamped);
            write?.Invoke(clamped);
        });

        field.Add(slider);
        field.Add(numberField);
        VisualElement row = ToolkitChrome.MakePropertyRow(label, field, tooltip);

        refresh = () =>
        {
            float current = read();
            slider.SetValueWithoutNotify(current);
            numberField.SetValueWithoutNotify(current);
        };
        refresh();
        return row;
    }

    public static VisualElement MakeToggleRow(string label, Func<bool> read, Action<bool> write, string tooltip, out Action refresh)
    {
        Toggle toggle = new Toggle();
        toggle.RegisterValueChangedCallback((ChangeEvent<bool> changeEvent) => write?.Invoke(changeEvent.newValue));
        VisualElement row = ToolkitChrome.MakePropertyRow(label, toggle, tooltip);

        refresh = () => toggle.SetValueWithoutNotify(read());
        refresh();
        return row;
    }

    public static VisualElement MakeSegmented(IReadOnlyList<string> labels, int selectedIndex, Action<int> onSelected)
    {
        VisualElement segmented = ToolkitChrome.MakeSegmentedControl(null, labels, selectedIndex, onSelected);
        segmented.AddToClassList(SegmentedFitClassName);
        return segmented;
    }

    public static Button MakePrimary(string text, string iconName, string tooltip, Action onClick)
    {
        return ToolkitChrome.MakePrimaryAction(onClick, iconName, tooltip, text);
    }

    public static Button MakeSecondary(string text, string iconName, string tooltip, Action onClick)
    {
        return ToolkitChrome.MakeSecondaryAction(onClick, iconName, tooltip, text);
    }

    public static Button MakeGhost(string text, string iconName, string tooltip, Action onClick)
    {
        return ToolkitChrome.MakeGhostAction(onClick, iconName, tooltip, text);
    }

    public static Button MakeDestructive(string text, string iconName, string tooltip, Action onClick)
    {
        return ToolkitChrome.MakeDestructiveAction(onClick, iconName, tooltip, text);
    }

    public static Label MakeHint(string text)
    {
        return ToolkitChrome.MakeHint(text);
    }

    public static VisualElement MakeEmpty(string title, string why, string actionText, Action onAction)
    {
        return ToolkitChrome.MakeEmptyState(null, title, why, actionText, onAction);
    }

    public static Button MakeIconSquare(string iconName, string tooltip, Action onClick)
    {
        Button button = ToolkitIcons.MakeIconButton(onClick, iconName, tooltip, null);
        ToolkitChrome.StyleButton(button, ToolkitButtonVariant.Ghost);
        button.AddToClassList(IconSquareClassName);
        return button;
    }
}
#endif
