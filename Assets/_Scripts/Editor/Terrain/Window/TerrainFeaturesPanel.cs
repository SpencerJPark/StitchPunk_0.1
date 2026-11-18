#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DotsAnimationToolkit.Editor;

public sealed class TerrainFeaturesPanel : TerrainToolPanel
{
    const int DrawingPollIntervalMilliseconds = 400;
    const string BoxIconName = "PreMatCube";
    const string RerollIconName = "Refresh";

    static readonly TerrainKeyHint[] Hints =
    {
        new TerrainKeyHint { keys = "Click", action = "add point" },
        new TerrainKeyHint { keys = "Enter", action = "close" },
        new TerrainKeyHint { keys = "Esc", action = "cancel" },
        new TerrainKeyHint { keys = "Backspace", action = "remove last point" },
        new TerrainKeyHint { keys = "Delete", action = "remove knot" },
        new TerrainKeyHint { keys = "Ctrl+Click", action = "insert knot" }
    };

    readonly VisualElement contentHost = new VisualElement();
    readonly List<Sprite> thumbnailSprites = new List<Sprite>();
    IVisualElementScheduledItem drawingPollTimer;
    TerrainAreaRecipe armedRecipe;
    bool lastKnownDrawing;
    bool hierarchyChangedPending;
    bool pickerOpen;
    bool choosingPlacerKind;
    int areaCount;

    public override string DisplayName => "Features";
    public override IReadOnlyList<TerrainKeyHint> KeyHints => Hints;

    public override string StatusText
    {
        get
        {
            if (TerrainFeatureTool.IsDrawing && armedRecipe != null) return "Drawing " + armedRecipe.name;
            return areaCount + " areas, " + TerrainFeatureBaker.StaleTiles.Count + " stale tiles";
        }
    }

    public TerrainFeaturesPanel()
    {
        style.flexGrow = 1f;

        VisualElement actions;
        VisualElement body;
        VisualElement pane = TerrainUi.MakePane("Features", out actions, out body);
        actions.Add(TerrainUi.MakePrimary("New feature", ToolkitIcons.Plus, "Pick a recipe, then draw the feature in the Scene view", TogglePicker));
        Add(pane);
        body.Add(contentHost);
    }

    public override void OnShown()
    {
        TerrainFeatureTool.SelectionChanged += Refresh;
        TerrainFeatureBaker.BakeFinished += Refresh;
        Selection.selectionChanged += Refresh;
        EditorApplication.hierarchyChanged += MarkHierarchyChanged;
        if (drawingPollTimer == null) drawingPollTimer = schedule.Execute(RefreshIfDrawingStateChanged).Every(DrawingPollIntervalMilliseconds);
        else drawingPollTimer.Resume();
        Refresh();
    }

    public override void OnHidden()
    {
        TerrainFeatureTool.SelectionChanged -= Refresh;
        TerrainFeatureBaker.BakeFinished -= Refresh;
        Selection.selectionChanged -= Refresh;
        EditorApplication.hierarchyChanged -= MarkHierarchyChanged;
        hierarchyChangedPending = false;
        if (drawingPollTimer != null) drawingPollTimer.Pause();
        TerrainFeatureTool.CancelDrawing();
        armedRecipe = null;
        DestroyThumbnailSprites();
    }

    void MarkHierarchyChanged()
    {
        hierarchyChangedPending = true;
    }

    void RefreshIfDrawingStateChanged()
    {
        if (hierarchyChangedPending)
        {
            hierarchyChangedPending = false;
            Refresh();
            return;
        }
        if (TerrainFeatureTool.IsDrawing == lastKnownDrawing) return;
        Refresh();
    }

    void Refresh()
    {
        // Sync first: a change here re-enters Refresh via SelectionChanged, which must finish before this rebuild starts.
        TerrainFeatureTool.SyncWithCurrentSelection();
        lastKnownDrawing = TerrainFeatureTool.IsDrawing;
        if (!lastKnownDrawing) armedRecipe = null;

        DestroyThumbnailSprites();
        contentHost.Clear();

        List<Component> features = new List<Component>();
        features.AddRange(Object.FindObjectsByType<TerrainArea>(FindObjectsInactive.Include));
        features.AddRange(Object.FindObjectsByType<TerrainPath>(FindObjectsInactive.Include));
        areaCount = features.Count;

        bool noRecipesAtAll = FindAssetsOfType<TerrainFeatureRecipe>().Count == 0;
        if (noRecipesAtAll && features.Count == 0)
        {
            contentHost.Add(TerrainUi.MakeEmpty("No recipes yet", "A recipe says what a feature looks like and how it is placed. Make one to start drawing features.", "Create recipe...", () => { pickerOpen = true; choosingPlacerKind = true; Refresh(); }));
            if (pickerOpen) contentHost.Add(BuildPicker());
            RaiseStatusChanged();
            return;
        }

        if (pickerOpen) contentHost.Add(BuildPicker());
        contentHost.Add(BuildFeatureList(features));

        Component selectedFeature = TerrainFeatureTool.SelectedFeature;
        if (selectedFeature != null) contentHost.Add(BuildSelectedFeatureCard(selectedFeature));

        RaiseStatusChanged();
    }

    enum PlacerKind
    {
        Fill,
        AlongPath,
        Rows
    }

    VisualElement BuildPicker()
    {
        VisualElement pickerBody;
        VisualElement pickerCard = TerrainUi.MakeCard("New feature", out pickerBody);

        if (choosingPlacerKind)
        {
            pickerBody.Add(TerrainUi.MakeHint("How does the new recipe place things?"));
            pickerBody.Add(TerrainUi.MakeSecondary("Fill an area", null, "Scatter placeables across a drawn area", () => CreateRecipeAsset(PlacerKind.Fill)));
            pickerBody.Add(TerrainUi.MakeSecondary("Along a path", null, "Repeat placeables along a line, such as a fence", () => CreateRecipeAsset(PlacerKind.AlongPath)));
            pickerBody.Add(TerrainUi.MakeSecondary("In rows", null, "Plant placeables in ordered rows", () => CreateRecipeAsset(PlacerKind.Rows)));
            pickerBody.Add(TerrainUi.MakeGhost("Back", null, "Return to the recipe list", () => { choosingPlacerKind = false; Refresh(); }));
            return pickerCard;
        }

        List<TerrainFeatureRecipe> featureRecipes = FindAssetsOfType<TerrainFeatureRecipe>();

        TerrainFeatureCategory[] categories = (TerrainFeatureCategory[])System.Enum.GetValues(typeof(TerrainFeatureCategory));
        string[] categoryTitles = { "Areas", "Lines and fences", "Ground cover" };
        foreach (TerrainFeatureCategory category in categories)
        {
            List<TerrainFeatureRecipe> inCategory = featureRecipes.FindAll((TerrainFeatureRecipe recipe) => recipe.category == category);
            if (inCategory.Count == 0) continue;

            Label header = new Label(categoryTitles[(int)category]);
            header.AddToClassList("toolkit-hint");
            pickerBody.Add(header);
            foreach (TerrainFeatureRecipe recipe in inCategory)
            {
                TerrainFeatureRecipe capturedRecipe = recipe;
                pickerBody.Add(BuildPickerRow(recipe, RecipeDisplayName(recipe), ShapeMeta(recipe.supportedShapes), () => PickFeatureRecipe(capturedRecipe)));
            }
        }

        Button createRecipeButton = TerrainUi.MakeSecondary("Create recipe...", ToolkitIcons.Plus, "Make a new feature recipe asset", () => { choosingPlacerKind = true; Refresh(); });
        createRecipeButton.AddToClassList("terrain-ellipsis-button");
        pickerBody.Add(createRecipeButton);
        return pickerCard;
    }

    VisualElement BuildPickerRow(TerrainFeatureRecipe recipe, string title, string meta, System.Action onPick)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("toolkit-list-row");
        row.AddToClassList("terrain-road-row");

        VisualElement chip = new VisualElement();
        chip.AddToClassList("terrain-swatch-chip");
        ApplyThumbnail(chip, recipe);
        row.Add(chip);

        Label nameLabel = new Label(title);
        nameLabel.AddToClassList("terrain-road-name");
        nameLabel.AddToClassList("terrain-ellipsis-label");
        nameLabel.tooltip = title;
        row.Add(nameLabel);
        Label metaLabel = new Label(meta);
        metaLabel.AddToClassList("toolkit-hint");
        metaLabel.AddToClassList("terrain-ellipsis-label");
        metaLabel.AddToClassList("terrain-picker-meta");
        metaLabel.tooltip = meta;
        row.Add(metaLabel);

        row.RegisterCallback<ClickEvent>((ClickEvent clickEvent) => onPick());
        return row;
    }

    static string RecipeDisplayName(TerrainFeatureRecipe recipe)
    {
        return string.IsNullOrEmpty(recipe.displayName) ? recipe.name : recipe.displayName;
    }

    static string ShapeMeta(TerrainFeatureShapeSupport shapes)
    {
        List<string> parts = new List<string>();
        if ((shapes & TerrainFeatureShapeSupport.ClosedArea) != 0) parts.Add("area");
        if ((shapes & TerrainFeatureShapeSupport.ClosedLoop) != 0) parts.Add("loop");
        if ((shapes & TerrainFeatureShapeSupport.OpenPath) != 0) parts.Add("line");
        return string.Join(", ", parts);
    }

    void PickFeatureRecipe(TerrainFeatureRecipe recipe)
    {
        pickerOpen = false;
        armedRecipe = recipe;
        TerrainFeatureTool.BeginDraw(recipe, recipe.defaultShape);
        Refresh();
    }

    void CreateRecipeAsset(PlacerKind placerKind)
    {
        string assetPath = EditorUtility.SaveFilePanelInProject("New feature recipe", "NewFeatureRecipe", "asset", "Where to save the new feature recipe");
        if (string.IsNullOrEmpty(assetPath)) return;

        TerrainFeatureRecipe recipe = ScriptableObject.CreateInstance<TerrainFeatureRecipe>();
        recipe.displayName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        switch (placerKind)
        {
            case PlacerKind.Fill:
                recipe.placer = new TerrainFillPlacer();
                recipe.category = TerrainFeatureCategory.Areas;
                recipe.supportedShapes = TerrainFeatureShapeSupport.ClosedArea;
                recipe.defaultShape = TerrainFeatureShapeKind.Curve;
                break;
            case PlacerKind.Rows:
                recipe.placer = new TerrainRowsPlacer();
                recipe.category = TerrainFeatureCategory.Areas;
                recipe.supportedShapes = TerrainFeatureShapeSupport.ClosedArea;
                recipe.defaultShape = TerrainFeatureShapeKind.Box;
                break;
            default:
                recipe.placer = new TerrainAlongPathPlacer();
                recipe.category = TerrainFeatureCategory.LinesAndFences;
                recipe.supportedShapes = TerrainFeatureShapeSupport.OpenPath | TerrainFeatureShapeSupport.ClosedLoop;
                recipe.defaultShape = TerrainFeatureShapeKind.Straight;
                break;
        }
        AssetDatabase.CreateAsset(recipe, assetPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = recipe;
        EditorGUIUtility.PingObject(recipe);
        choosingPlacerKind = false;
        Refresh();
    }

    void ApplyThumbnail(VisualElement chip, TerrainFeatureRecipe recipe)
    {
        if (recipe.thumbnail != null)
        {
            chip.style.backgroundImage = new StyleBackground(recipe.thumbnail);
            return;
        }

        TerrainFillPlacer fillPlacer = recipe.placer as TerrainFillPlacer;
        TerrainCardAtlas cardAtlas = fillPlacer != null ? fillPlacer.cardAtlas : null;
        if (cardAtlas != null && cardAtlas.material != null
            && cardAtlas.spriteUvRects != null && cardAtlas.spriteUvRects.Count > 0)
        {
            Texture2D atlasTexture = cardAtlas.material.mainTexture as Texture2D;
            if (atlasTexture != null)
            {
                Rect uvRect = cardAtlas.spriteUvRects[0];
                Rect pixelRect = new Rect(uvRect.x * atlasTexture.width, uvRect.y * atlasTexture.height, uvRect.width * atlasTexture.width, uvRect.height * atlasTexture.height);
                if (pixelRect.width >= 1f && pixelRect.height >= 1f)
                {
                    Sprite thumbnailSprite = Sprite.Create(atlasTexture, pixelRect, new Vector2(0.5f, 0.5f));
                    thumbnailSprite.hideFlags = HideFlags.HideAndDontSave;
                    thumbnailSprites.Add(thumbnailSprite);
                    chip.style.backgroundImage = new StyleBackground(thumbnailSprite);
                    return;
                }
            }
        }

        Color32 layerColour = TerrainSplatCodec.ColourOfLayer(recipe.groundLayer);
        chip.style.backgroundColor = new Color(layerColour.r / 255f, layerColour.g / 255f, layerColour.b / 255f, 1f);
    }

    static TerrainAreaRecipe RecipeOf(Component feature)
    {
        TerrainArea area = feature as TerrainArea;
        if (area != null) return area.recipe;
        TerrainPath path = feature as TerrainPath;
        return path != null ? path.recipe : null;
    }

    static string RecipeNameOf(Component feature)
    {
        TerrainAreaRecipe recipe = RecipeOf(feature);
        if (recipe == null) return "no recipe";
        TerrainFeatureRecipe featureRecipe = recipe as TerrainFeatureRecipe;
        return featureRecipe != null ? RecipeDisplayName(featureRecipe) : recipe.name;
    }

    VisualElement BuildFeatureList(List<Component> features)
    {
        VisualElement listBody;
        VisualElement card = TerrainUi.MakeCard("In scene", out listBody);

        if (features.Count == 0)
        {
            listBody.Add(TerrainUi.MakeHint("No features yet. Press New feature to draw one."));
            return card;
        }

        Component selectedFeature = TerrainFeatureTool.SelectedFeature;
        foreach (Component feature in features)
        {
            Component capturedFeature = feature;
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-list-row");
            row.AddToClassList("terrain-road-row");
            row.EnableInClassList("toolkit-list-row--selected", feature == selectedFeature);
            Label nameLabel = new Label(feature.name);
            nameLabel.AddToClassList("terrain-road-name");
            nameLabel.AddToClassList("terrain-ellipsis-label");
            nameLabel.tooltip = feature.name;
            string recipeName = RecipeNameOf(feature);
            Label recipeLabel = new Label(recipeName);
            recipeLabel.AddToClassList("toolkit-hint");
            recipeLabel.AddToClassList("terrain-ellipsis-label");
            recipeLabel.tooltip = recipeName;
            row.Add(nameLabel);
            row.Add(recipeLabel);
            row.RegisterCallback<ClickEvent>((ClickEvent clickEvent) => SelectAndFrame(capturedFeature));
            listBody.Add(row);
        }
        return card;
    }

    VisualElement BuildSelectedFeatureCard(Component feature)
    {
        VisualElement cardBody;
        VisualElement card = TerrainUi.MakeCard("Selected", out cardBody);

        // Below this content width the label column starves the field column, so rows stack.
        const float NarrowCardContentWidth = 170f;
        card.RegisterCallback<GeometryChangedEvent>((GeometryChangedEvent geometryEvent) =>
        {
            bool shouldBeNarrow = cardBody.contentRect.width < NarrowCardContentWidth;
            if (shouldBeNarrow != card.ClassListContains("terrain-card--narrow"))
                card.EnableInClassList("terrain-card--narrow", shouldBeNarrow);
        });

        TerrainArea area = feature as TerrainArea;
        TerrainPath path = feature as TerrainPath;

        ObjectField recipeField = new ObjectField();
        recipeField.objectType = typeof(TerrainAreaRecipe);
        recipeField.allowSceneObjects = false;
        recipeField.AddToClassList("terrain-ellipsis-field");
        recipeField.SetValueWithoutNotify(RecipeOf(feature));
        recipeField.RegisterValueChangedCallback((ChangeEvent<Object> changeEvent) =>
        {
            Undo.RecordObject(feature, "Change feature recipe");
            if (area != null) area.recipe = changeEvent.newValue as TerrainAreaRecipe;
            else if (path != null) path.recipe = changeEvent.newValue as TerrainFeatureRecipe;
            EditorUtility.SetDirty(feature);
            TerrainFeatureEvents.Raise(feature);
            Refresh();
        });
        cardBody.Add(ToolkitChrome.MakePropertyRow("Recipe", recipeField, "What this feature grows and paints"));

        VisualElement seedRow = new VisualElement();
        seedRow.AddToClassList("terrain-seed-row");
        IntegerField seedField = new IntegerField();
        seedField.SetValueWithoutNotify(area != null ? area.seed : path.seed);
        seedField.RegisterValueChangedCallback((ChangeEvent<int> changeEvent) =>
        {
            Undo.RecordObject(feature, "Change feature seed");
            SetSeed(feature, changeEvent.newValue);
            EditorUtility.SetDirty(feature);
            TerrainFeatureEvents.Raise(feature);
        });
        seedRow.Add(seedField);
        seedRow.Add(TerrainUi.MakeIconSquare(RerollIconName, "Roll a new random seed and regenerate", () => RerollSeed(feature)));
        cardBody.Add(ToolkitChrome.MakePropertyRow("Seed", seedRow, "Same seed, same scatter"));

        if (area != null) AddAreaControls(cardBody, area);
        AddPlacerReadout(cardBody, RecipeOf(feature) as TerrainFeatureRecipe);

        Button regenerateButton = TerrainUi.MakeSecondary("Regenerate", RerollIconName, "Regenerate this feature's scatter, keeping locked items", () => TerrainFeatureEvents.Raise(feature));
        regenerateButton.AddToClassList("terrain-fit-button");
        cardBody.Add(regenerateButton);

        int staleTileCount = TerrainFeatureBaker.StaleTiles.Count;
        if (staleTileCount > 0)
        {
            Label staleLabel = new Label(staleTileCount + " tiles need a rebake");
            staleLabel.AddToClassList("terrain-warning");
            staleLabel.tooltip = staleLabel.text;
            cardBody.Add(staleLabel);
            Button bakeButton = TerrainUi.MakePrimary("Bake stale tiles", null, "Rebake every tile a feature has changed", () => TerrainFeatureBaker.RebakeAllFeatures(true));
            bakeButton.AddToClassList("terrain-fit-button");
            cardBody.Add(bakeButton);
        }

        return card;
    }

    void AddAreaControls(VisualElement cardBody, TerrainArea area)
    {
        IntegerField priorityField = new IntegerField();
        priorityField.SetValueWithoutNotify(area.priority);
        priorityField.RegisterValueChangedCallback((ChangeEvent<int> changeEvent) =>
        {
            Undo.RecordObject(area, "Change feature priority");
            area.priority = changeEvent.newValue;
            EditorUtility.SetDirty(area);
            TerrainFeatureEvents.Raise(area);
        });
        cardBody.Add(ToolkitChrome.MakePropertyRow("Priority", priorityField, "Where features overlap, the higher priority wins"));

        System.Action densityRefresh;
        cardBody.Add(TerrainUi.MakeSliderRow(
            "Density",
            0.1f,
            5f,
            () => area.densityMultiplier,
            (float newDensity) =>
            {
                Undo.RecordObject(area, "Change feature density");
                area.densityMultiplier = newDensity;
                EditorUtility.SetDirty(area);
                TerrainFeatureEvents.Raise(area);
            },
            "How many items this area plants, relative to the recipe (1 = recipe density). Spacing still limits the maximum.",
            out densityRefresh));

        VisualElement badgeRow = new VisualElement();
        badgeRow.AddToClassList("terrain-badge-row");
        badgeRow.Add(ToolkitChrome.MakeBadge(CountLockedMarkers(area) + " locked", ToolkitStatusTone.Neutral));
        cardBody.Add(badgeRow);
    }

    static void AddPlacerReadout(VisualElement cardBody, TerrainFeatureRecipe recipe)
    {
        if (recipe == null || recipe.placer == null) return;

        TerrainFillPlacer fillPlacer = recipe.placer as TerrainFillPlacer;
        if (fillPlacer != null)
        {
            cardBody.Add(ToolkitChrome.MakePropertyRow("Recipe density", MakeReadoutLabel(fillPlacer.densityPer100SquareMetres.ToString("0.##") + " per 100 m2"), "Set on the recipe asset"));
            return;
        }

        TerrainAlongPathPlacer alongPathPlacer = recipe.placer as TerrainAlongPathPlacer;
        if (alongPathPlacer != null)
        {
            cardBody.Add(ToolkitChrome.MakePropertyRow("Spacing", MakeReadoutLabel(alongPathPlacer.spacingMetres.ToString("0.##") + " m"), "Set on the recipe asset"));
            cardBody.Add(ToolkitChrome.MakePropertyRow("Mode", MakeReadoutLabel(alongPathPlacer.mode.ToString()), "Set on the recipe asset"));
            return;
        }

        TerrainRowsPlacer rowsPlacer = recipe.placer as TerrainRowsPlacer;
        if (rowsPlacer != null)
        {
            string directionText = float.IsNaN(rowsPlacer.rowDirectionDegrees) ? "auto" : rowsPlacer.rowDirectionDegrees.ToString("0.#") + " deg";
            cardBody.Add(ToolkitChrome.MakePropertyRow("Row direction", MakeReadoutLabel(directionText), "Set on the recipe asset"));
        }
    }

    static Label MakeReadoutLabel(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("toolkit-hint");
        label.AddToClassList("terrain-ellipsis-label");
        label.tooltip = text;
        return label;
    }

    static void SetSeed(Component feature, int seed)
    {
        TerrainArea area = feature as TerrainArea;
        if (area != null)
        {
            area.seed = seed;
            return;
        }
        TerrainPath path = feature as TerrainPath;
        if (path != null) path.seed = seed;
    }

    static List<T> FindAssetsOfType<T>() where T : Object
    {
        List<T> assets = new List<T>();
        foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) assets.Add(asset);
        }
        return assets;
    }

    void TogglePicker()
    {
        pickerOpen = !pickerOpen;
        choosingPlacerKind = false;
        Refresh();
    }

    void SelectAndFrame(Component feature)
    {
        Selection.activeObject = feature.gameObject;
        TerrainFeatureTool.FrameSelected();
        Refresh();
    }

    static void RerollSeed(Component feature)
    {
        Undo.RecordObject(feature, "Reroll feature seed");
        SetSeed(feature, Random.Range(1, int.MaxValue));
        EditorUtility.SetDirty(feature);
        TerrainFeatureEvents.Raise(feature);
    }

    static int CountLockedMarkers(TerrainArea area)
    {
        int lockedCount = 0;
        foreach (TerrainScatterMarker marker in Object.FindObjectsByType<TerrainScatterMarker>(FindObjectsInactive.Include))
        {
            if (marker.locked && marker.sourceFeatureId == area.featureId) lockedCount++;
        }
        return lockedCount;
    }

    void DestroyThumbnailSprites()
    {
        foreach (Sprite thumbnailSprite in thumbnailSprites)
        {
            if (thumbnailSprite != null) Object.DestroyImmediate(thumbnailSprite);
        }
        thumbnailSprites.Clear();
    }
}
#endif
