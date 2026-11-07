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
    private const string TabListBarClassName = "toolkit-tablist-bar";
    private const string TabListClassName = "toolkit-tablist";
    private const string TabClassName = "toolkit-tablist__tab";
    private const string TabActiveClassName = "toolkit-tablist__tab--active";
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

    public static VisualElement MakeTabBar(IReadOnlyList<string> labels, Action<int> onSelected, out Action<int> setSelectedIndex)
    {
        Toolbar tabBar = new Toolbar();
        tabBar.AddToClassList(TabListBarClassName);

        VisualElement tabList = new VisualElement();
        tabList.AddToClassList(TabListClassName);
        tabBar.Add(tabList);

        List<ToolbarToggle> toggles = new List<ToolbarToggle>();
        bool isApplying = false;

        Action<int> applyActive = (int activeIndex) =>
        {
            isApplying = true;
            for (int toggleIndex = 0; toggleIndex < toggles.Count; toggleIndex++)
            {
                bool isActive = toggleIndex == activeIndex;
                toggles[toggleIndex].SetValueWithoutNotify(isActive);
                toggles[toggleIndex].EnableInClassList(TabActiveClassName, isActive);
            }
            isApplying = false;
        };

        for (int labelIndex = 0; labelIndex < labels.Count; labelIndex++)
        {
            int capturedIndex = labelIndex;
            ToolbarToggle toggle = new ToolbarToggle { text = labels[labelIndex] };
            toggle.AddToClassList(TabClassName);
            toggle.RegisterValueChangedCallback((ChangeEvent<bool> changeEvent) =>
            {
                if (isApplying)
                {
                    return;
                }
                applyActive(capturedIndex);
                onSelected?.Invoke(capturedIndex);
            });
            toggles.Add(toggle);
            tabList.Add(toggle);
        }

        setSelectedIndex = applyActive;
        applyActive(0);
        return tabBar;
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
