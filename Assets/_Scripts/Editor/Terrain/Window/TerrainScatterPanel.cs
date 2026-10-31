#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TerrainScatterPanel : TerrainToolPanel
{
    const string DefaultSetFolder = "Assets/Terrain/Profiles";
    const string DefaultSetPath = DefaultSetFolder + "/DefaultScatterSet.asset";
    const string ScatterChildName = "Scatter";

    delegate TerrainScatterRuleData RuleEdit(TerrainScatterRuleData rule);

    readonly ObjectField setField = new ObjectField("Scatter Set");
    readonly VisualElement emptyState = new VisualElement();
    readonly VisualElement mainGroup = new VisualElement();
    readonly VisualElement ruleListContainer = new VisualElement();
    readonly VisualElement detailGroup = new VisualElement();
    readonly VisualElement prefabListContainer = new VisualElement();
    readonly Slider densitySlider = new Slider("Density per 100 m2", 0f, 20f);
    readonly Slider spacingSlider = new Slider("Min spacing", 0.5f, 20f);
    readonly Toggle blocksNavigationToggle = new Toggle("Blocks navigation");
    readonly IntegerField seedField = new IntegerField("Seed");
    readonly Button placeButton = new Button();
    readonly Button paintButton = new Button();
    readonly Button eraseButton = new Button();
    IVisualElementScheduledItem refreshTimer;
    string lastStatus = string.Empty;

    public override string DisplayName => "Scatter";
    public override string StatusText => "Scatter: " + SelectedRuleLabel();

    public TerrainScatterPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Scatter");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Rules place props by ground type and slope. Hand-placed props are locked against regeneration.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        setField.objectType = typeof(TerrainScatterSet);
        setField.allowSceneObjects = false;
        setField.AddToClassList("terrain-row");
        setField.RegisterValueChangedCallback(OnSetChanged);
        Add(setField);

        emptyState.AddToClassList("terrain-empty");
        Label emptyTitle = new Label("No scatter set");
        emptyTitle.AddToClassList("terrain-panel-title");
        Label emptyWhy = new Label("A scatter set holds the rules that decide which props go where. Create one to start.");
        emptyWhy.AddToClassList("terrain-panel-hint");
        Button emptyCreateButton = new Button(CreateDefaultSet);
        emptyCreateButton.text = "Create Scatter Set";
        emptyCreateButton.AddToClassList("terrain-primary-button");
        emptyState.Add(emptyTitle);
        emptyState.Add(emptyWhy);
        emptyState.Add(emptyCreateButton);
        Add(emptyState);

        mainGroup.Add(ruleListContainer);

        VisualElement ruleButtonRow = new VisualElement();
        ruleButtonRow.AddToClassList("terrain-segment-row");
        Button addRuleButton = new Button(AddRule);
        addRuleButton.text = "Add Rule";
        addRuleButton.AddToClassList("terrain-segment-button");
        Button removeRuleButton = new Button(RemoveRule);
        removeRuleButton.text = "Remove Rule";
        removeRuleButton.AddToClassList("terrain-segment-button");
        ruleButtonRow.Add(addRuleButton);
        ruleButtonRow.Add(removeRuleButton);
        mainGroup.Add(ruleButtonRow);

        densitySlider.showInputField = true;
        densitySlider.AddToClassList("terrain-row");
        densitySlider.RegisterValueChangedCallback(changeEvent => EditSelectedRule("Set Scatter Density", rule =>
        {
            rule.densityPer100SquareMetres = changeEvent.newValue;
            return rule;
        }));
        spacingSlider.showInputField = true;
        spacingSlider.AddToClassList("terrain-row");
        spacingSlider.RegisterValueChangedCallback(changeEvent => EditSelectedRule("Set Scatter Spacing", rule =>
        {
            rule.minSpacingMetres = changeEvent.newValue;
            return rule;
        }));
        blocksNavigationToggle.AddToClassList("terrain-row");
        blocksNavigationToggle.RegisterValueChangedCallback(changeEvent => EditSelectedRule("Set Scatter Navigation", rule =>
        {
            rule.blocksNavigation = changeEvent.newValue;
            return rule;
        }));
        detailGroup.Add(densitySlider);
        detailGroup.Add(spacingSlider);
        detailGroup.Add(blocksNavigationToggle);

        Label prefabHint = new Label("Add prefabs to the rule in the asset inspector");
        prefabHint.AddToClassList("terrain-panel-hint");
        detailGroup.Add(prefabListContainer);
        detailGroup.Add(prefabHint);
        mainGroup.Add(detailGroup);

        seedField.AddToClassList("terrain-row");
        seedField.SetValueWithoutNotify(TerrainScatterBrush.seed);
        seedField.RegisterValueChangedCallback(changeEvent => TerrainScatterBrush.seed = changeEvent.newValue);
        mainGroup.Add(seedField);

        VisualElement toolRow = new VisualElement();
        toolRow.AddToClassList("terrain-segment-row");
        ConfigureToolButton(placeButton, "Place", TerrainBrushTool.ScatterPlace);
        ConfigureToolButton(paintButton, "Paint", TerrainBrushTool.ScatterPaint);
        ConfigureToolButton(eraseButton, "Erase", TerrainBrushTool.ScatterErase);
        toolRow.Add(placeButton);
        toolRow.Add(paintButton);
        toolRow.Add(eraseButton);
        mainGroup.Add(toolRow);

        Button scatterTileButton = new Button(ScatterActiveTile);
        scatterTileButton.text = "Scatter Tile";
        scatterTileButton.AddToClassList("terrain-primary-button");
        mainGroup.Add(scatterTileButton);

        VisualElement tileButtonRow = new VisualElement();
        tileButtonRow.AddToClassList("terrain-segment-row");
        Button regenerateButton = new Button(RegenerateActiveTile);
        regenerateButton.text = "Regenerate";
        regenerateButton.AddToClassList("terrain-segment-button");
        Button clearButton = new Button(ClearActiveTileUnlocked);
        clearButton.text = "Clear Unlocked";
        clearButton.AddToClassList("terrain-segment-button");
        Button lockButton = new Button(LockActiveTile);
        lockButton.text = "Lock All";
        lockButton.AddToClassList("terrain-segment-button");
        tileButtonRow.Add(regenerateButton);
        tileButtonRow.Add(clearButton);
        tileButtonRow.Add(lockButton);
        mainGroup.Add(tileButtonRow);

        Add(mainGroup);
    }

    public override void OnShown()
    {
        BrushState.activeTool = TerrainBrushTool.ScatterPlace;
        if (refreshTimer == null) refreshTimer = schedule.Execute(Refresh).Every(250);
        else refreshTimer.Resume();
        Refresh();
    }

    public override void OnHidden()
    {
        if (refreshTimer != null) refreshTimer.Pause();
        TerrainBrushTool tool = BrushState.activeTool;
        if (tool == TerrainBrushTool.ScatterPlace || tool == TerrainBrushTool.ScatterPaint || tool == TerrainBrushTool.ScatterErase)
            BrushState.activeTool = TerrainBrushTool.None;
    }

    void ConfigureToolButton(Button button, string label, TerrainBrushTool tool)
    {
        button.text = label;
        button.AddToClassList("terrain-segment-button");
        button.clicked += () =>
        {
            BrushState.activeTool = tool;
            Refresh();
        };
    }

    string SelectedRuleLabel()
    {
        TerrainScatterSet set = BrushState.activeScatterSet;
        if (set == null || set.rules == null || BrushState.scatterRuleIndex < 0 || BrushState.scatterRuleIndex >= set.rules.Count) return "none";
        return set.rules[BrushState.scatterRuleIndex].label;
    }

    void Refresh()
    {
        if (BrushState.activeScatterSet == null) BrushState.activeScatterSet = FindFirstSet();
        TerrainScatterSet set = BrushState.activeScatterSet;
        setField.SetValueWithoutNotify(set);
        emptyState.style.display = set == null ? DisplayStyle.Flex : DisplayStyle.None;
        mainGroup.style.display = set != null ? DisplayStyle.Flex : DisplayStyle.None;

        placeButton.EnableInClassList("terrain-segment-button--active", BrushState.activeTool == TerrainBrushTool.ScatterPlace);
        paintButton.EnableInClassList("terrain-segment-button--active", BrushState.activeTool == TerrainBrushTool.ScatterPaint);
        eraseButton.EnableInClassList("terrain-segment-button--active", BrushState.activeTool == TerrainBrushTool.ScatterErase);

        if (set != null)
        {
            int ruleCount = set.rules != null ? set.rules.Count : 0;
            BrushState.scatterRuleIndex = Mathf.Clamp(BrushState.scatterRuleIndex, 0, Mathf.Max(0, ruleCount - 1));
            RebuildRuleList(set);
            RefreshDetail(set);
        }

        string status = StatusText;
        if (status != lastStatus)
        {
            lastStatus = status;
            RaiseStatusChanged();
        }
    }

    void RebuildRuleList(TerrainScatterSet set)
    {
        ruleListContainer.Clear();
        for (int ruleIndex = 0; ruleIndex < set.rules.Count; ruleIndex++)
        {
            int capturedIndex = ruleIndex;
            TerrainScatterRuleData rule = set.rules[ruleIndex];
            int prefabCount = rule.prefabs != null ? rule.prefabs.Count : 0;
            Button row = new Button(() => SelectRule(capturedIndex));
            row.text = rule.label + ", " + prefabCount + " prefabs, density " + rule.densityPer100SquareMetres.ToString("0.#");
            row.AddToClassList("terrain-list-row");
            if (ruleIndex == BrushState.scatterRuleIndex) row.AddToClassList("terrain-list-row--selected");
            ruleListContainer.Add(row);
        }
    }

    void RefreshDetail(TerrainScatterSet set)
    {
        bool hasRule = BrushState.scatterRuleIndex >= 0 && BrushState.scatterRuleIndex < set.rules.Count;
        detailGroup.style.display = hasRule ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasRule) return;

        TerrainScatterRuleData rule = set.rules[BrushState.scatterRuleIndex];
        densitySlider.SetValueWithoutNotify(rule.densityPer100SquareMetres);
        spacingSlider.SetValueWithoutNotify(rule.minSpacingMetres);
        blocksNavigationToggle.SetValueWithoutNotify(rule.blocksNavigation);

        prefabListContainer.Clear();
        if (rule.prefabs == null) return;
        foreach (TerrainScatterPrefab entry in rule.prefabs)
        {
            Label prefabLabel = new Label((entry.prefab != null ? entry.prefab.name : "none") + ", weight " + entry.weight.ToString("0.##"));
            prefabLabel.AddToClassList("terrain-label");
            prefabListContainer.Add(prefabLabel);
        }
    }

    void SelectRule(int ruleIndex)
    {
        BrushState.scatterRuleIndex = ruleIndex;
        Refresh();
    }

    static TerrainScatterSet FindFirstSet()
    {
        string[] guids = AssetDatabase.FindAssets("t:TerrainScatterSet");
        if (guids.Length == 0) return null;
        return AssetDatabase.LoadAssetAtPath<TerrainScatterSet>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    void OnSetChanged(ChangeEvent<Object> changeEvent)
    {
        BrushState.activeScatterSet = changeEvent.newValue as TerrainScatterSet;
        BrushState.scatterRuleIndex = 0;
        Refresh();
    }

    void CreateDefaultSet()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Terrain")) AssetDatabase.CreateFolder("Assets", "Terrain");
        if (!AssetDatabase.IsValidFolder(DefaultSetFolder)) AssetDatabase.CreateFolder("Assets/Terrain", "Profiles");
        TerrainScatterSet created = ScriptableObject.CreateInstance<TerrainScatterSet>();
        created.rules.Add(TerrainScatterSet.CreateRule("New rule"));
        AssetDatabase.CreateAsset(created, DefaultSetPath);
        AssetDatabase.SaveAssets();
        BrushState.activeScatterSet = created;
        BrushState.scatterRuleIndex = 0;
        Refresh();
    }

    void AddRule()
    {
        TerrainScatterSet set = BrushState.activeScatterSet;
        if (set == null) return;
        Undo.RecordObject(set, "Add Scatter Rule");
        set.rules.Add(TerrainScatterSet.CreateRule("New rule"));
        EditorUtility.SetDirty(set);
        BrushState.scatterRuleIndex = set.rules.Count - 1;
        Refresh();
    }

    void RemoveRule()
    {
        TerrainScatterSet set = BrushState.activeScatterSet;
        if (set == null || BrushState.scatterRuleIndex < 0 || BrushState.scatterRuleIndex >= set.rules.Count) return;
        Undo.RecordObject(set, "Remove Scatter Rule");
        set.rules.RemoveAt(BrushState.scatterRuleIndex);
        EditorUtility.SetDirty(set);
        Refresh();
    }

    void EditSelectedRule(string undoName, RuleEdit edit)
    {
        TerrainScatterSet set = BrushState.activeScatterSet;
        if (set == null || BrushState.scatterRuleIndex < 0 || BrushState.scatterRuleIndex >= set.rules.Count) return;
        Undo.RecordObject(set, undoName);
        set.rules[BrushState.scatterRuleIndex] = edit(set.rules[BrushState.scatterRuleIndex]);
        EditorUtility.SetDirty(set);
    }

    void ScatterActiveTile()
    {
        TerrainTile tile;
        if (!TerrainTilesPanel.TryGetActiveTile(out tile) || BrushState.activeScatterSet == null) return;
        TerrainScatterGenerator.Generate(tile, BrushState.activeScatterSet, TerrainScatterBrush.seed, null);
    }

    void RegenerateActiveTile()
    {
        TerrainTile tile;
        if (!TerrainTilesPanel.TryGetActiveTile(out tile) || BrushState.activeScatterSet == null) return;
        TerrainScatterGenerator.Regenerate(tile, BrushState.activeScatterSet, TerrainScatterBrush.seed);
    }

    void ClearActiveTileUnlocked()
    {
        TerrainTile tile;
        if (!TerrainTilesPanel.TryGetActiveTile(out tile)) return;
        TerrainScatterGenerator.ClearUnlocked(tile.transform.Find(ScatterChildName));
    }

    void LockActiveTile()
    {
        TerrainTile tile;
        if (!TerrainTilesPanel.TryGetActiveTile(out tile)) return;
        TerrainScatterGenerator.LockAll(tile.transform.Find(ScatterChildName));
    }
}
#endif
