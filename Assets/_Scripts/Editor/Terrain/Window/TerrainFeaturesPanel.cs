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
        Add(pane);
        body.Add(contentHost);
    }

    public override void OnShown()
    {
        TerrainFeatureTool.SelectionChanged += Refresh;
        TerrainFeatureBaker.BakeFinished += Refresh;
        Selection.selectionChanged += Refresh;
        if (drawingPollTimer == null) drawingPollTimer = schedule.Execute(RefreshIfDrawingStateChanged).Every(DrawingPollIntervalMilliseconds);
        else drawingPollTimer.Resume();
        Refresh();
    }

    public override void OnHidden()
    {
        TerrainFeatureTool.SelectionChanged -= Refresh;
        TerrainFeatureBaker.BakeFinished -= Refresh;
        Selection.selectionChanged -= Refresh;
        if (drawingPollTimer != null) drawingPollTimer.Pause();
        TerrainFeatureTool.CancelDrawing();
        armedRecipe = null;
        DestroyThumbnailSprites();
    }

    void RefreshIfDrawingStateChanged()
    {
        if (TerrainFeatureTool.IsDrawing == lastKnownDrawing) return;
        Refresh();
    }

    void Refresh()
    {
        lastKnownDrawing = TerrainFeatureTool.IsDrawing;
        if (!lastKnownDrawing) armedRecipe = null;

        DestroyThumbnailSprites();
        contentHost.Clear();

        TerrainArea[] areas = Object.FindObjectsByType<TerrainArea>(FindObjectsInactive.Include);
        areaCount = areas.Length;

        contentHost.Add(BuildRecipeGrid());
        contentHost.Add(BuildFeatureList(areas));

        TerrainArea selectedArea = TerrainFeatureTool.SelectedFeature as TerrainArea;
        if (selectedArea != null) contentHost.Add(BuildSelectedFeatureCard(selectedArea));

        RaiseStatusChanged();
    }

    VisualElement BuildRecipeGrid()
    {
        VisualElement gridBody;
        VisualElement card = TerrainUi.MakeCard("Draw a feature", out gridBody);

        List<TerrainAreaRecipe> recipes = FindAllRecipes();
        if (recipes.Count == 0)
        {
            gridBody.Add(TerrainUi.MakeEmpty("No recipes yet", "A recipe says what a feature looks like. Make one with Assets > Create > Stitch Punk > Terrain > Forest Recipe, then click it here to draw an outline.", null, null));
            return card;
        }

        gridBody.Add(TerrainUi.MakeHint("Click a recipe to draw its outline in the Scene view, or the box to drag out a rectangle."));

        VisualElement tileWrap = new VisualElement();
        tileWrap.AddToClassList("terrain-feature-grid");
        foreach (TerrainAreaRecipe recipe in recipes)
            tileWrap.Add(BuildRecipeTile(recipe));
        gridBody.Add(tileWrap);

        if (TerrainFeatureTool.IsDrawing && armedRecipe != null)
            gridBody.Add(ToolkitChrome.MakeBadge("Drawing " + armedRecipe.name, ToolkitStatusTone.Neutral));

        return card;
    }

    VisualElement BuildRecipeTile(TerrainAreaRecipe recipe)
    {
        bool isArmed = TerrainFeatureTool.IsDrawing && armedRecipe == recipe;

        VisualElement tile = new VisualElement();
        tile.AddToClassList("terrain-swatch");
        tile.AddToClassList("terrain-feature-tile");
        tile.EnableInClassList("terrain-swatch--selected", isArmed);
        tile.tooltip = "Draw an outline for " + recipe.name;

        VisualElement chip = new VisualElement();
        chip.AddToClassList("terrain-swatch-chip");
        ApplyThumbnail(chip, recipe);
        tile.Add(chip);

        Label nameLabel = new Label(recipe.name);
        nameLabel.AddToClassList("toolkit-hint");
        nameLabel.AddToClassList("terrain-feature-tile-name");
        tile.Add(nameLabel);

        Button boxButton = TerrainUi.MakeIconSquare(BoxIconName, "Drag out a rectangle for " + recipe.name, () => ArmBox(recipe));
        boxButton.AddToClassList("terrain-feature-tile-box");
        boxButton.RegisterCallback<ClickEvent>((ClickEvent clickEvent) => clickEvent.StopPropagation());
        tile.Add(boxButton);

        tile.RegisterCallback<ClickEvent>((ClickEvent clickEvent) => ArmOutline(recipe));
        return tile;
    }

    void ApplyThumbnail(VisualElement chip, TerrainAreaRecipe recipe)
    {
        TerrainForestRecipe forestRecipe = recipe as TerrainForestRecipe;
        if (forestRecipe != null && forestRecipe.cardAtlas != null && forestRecipe.cardAtlas.material != null
            && forestRecipe.cardAtlas.spriteUvRects != null && forestRecipe.cardAtlas.spriteUvRects.Count > 0)
        {
            Texture2D atlasTexture = forestRecipe.cardAtlas.material.mainTexture as Texture2D;
            if (atlasTexture != null)
            {
                Rect uvRect = forestRecipe.cardAtlas.spriteUvRects[0];
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

        if (recipe.groundLayer != null)
        {
            Color32 layerColour = TerrainSplatCodec.ColourOfLayer(recipe.groundLayer);
            chip.style.backgroundColor = new Color(layerColour.r / 255f, layerColour.g / 255f, layerColour.b / 255f, 1f);
        }
    }

    VisualElement BuildFeatureList(TerrainArea[] areas)
    {
        VisualElement listBody;
        VisualElement card = TerrainUi.MakeCard("In the loaded scenes", out listBody);

        if (areas.Length == 0)
        {
            listBody.Add(TerrainUi.MakeHint("No features yet. Draw one from a recipe above."));
            return card;
        }

        TerrainArea selectedArea = TerrainFeatureTool.SelectedFeature as TerrainArea;
        foreach (TerrainArea area in areas)
        {
            TerrainArea capturedArea = area;
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-list-row");
            row.AddToClassList("terrain-road-row");
            row.EnableInClassList("toolkit-list-row--selected", area == selectedArea);
            Label nameLabel = new Label(area.name);
            nameLabel.AddToClassList("terrain-road-name");
            Label recipeLabel = new Label(area.recipe != null ? area.recipe.name : "no recipe");
            recipeLabel.AddToClassList("toolkit-hint");
            row.Add(nameLabel);
            row.Add(recipeLabel);
            row.RegisterCallback<ClickEvent>((ClickEvent clickEvent) => SelectAndFrame(capturedArea));
            listBody.Add(row);
        }
        return card;
    }

    VisualElement BuildSelectedFeatureCard(TerrainArea area)
    {
        VisualElement cardBody;
        VisualElement card = TerrainUi.MakeCard("Selected feature", out cardBody);

        ObjectField recipeField = new ObjectField();
        recipeField.objectType = typeof(TerrainAreaRecipe);
        recipeField.allowSceneObjects = false;
        recipeField.SetValueWithoutNotify(area.recipe);
        recipeField.RegisterValueChangedCallback((ChangeEvent<Object> changeEvent) =>
        {
            Undo.RecordObject(area, "Change feature recipe");
            area.recipe = changeEvent.newValue as TerrainAreaRecipe;
            EditorUtility.SetDirty(area);
            TerrainFeatureEvents.Raise(area);
            Refresh();
        });
        cardBody.Add(ToolkitChrome.MakePropertyRow("Recipe", recipeField, "What this feature grows and paints"));

        VisualElement seedRow = new VisualElement();
        seedRow.AddToClassList("terrain-seed-row");
        IntegerField seedField = new IntegerField();
        seedField.SetValueWithoutNotify(area.seed);
        seedField.RegisterValueChangedCallback((ChangeEvent<int> changeEvent) =>
        {
            Undo.RecordObject(area, "Change feature seed");
            area.seed = changeEvent.newValue;
            EditorUtility.SetDirty(area);
            TerrainFeatureEvents.Raise(area);
        });
        seedRow.Add(seedField);
        seedRow.Add(TerrainUi.MakeIconSquare(RerollIconName, "Roll a new random seed and regenerate", () => RerollSeed(area)));
        cardBody.Add(ToolkitChrome.MakePropertyRow("Seed", seedRow, "Same seed, same scatter"));

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

        VisualElement badgeRow = new VisualElement();
        badgeRow.AddToClassList("terrain-badge-row");
        badgeRow.Add(ToolkitChrome.MakeBadge(CountLockedMarkers(area) + " locked", ToolkitStatusTone.Neutral));
        cardBody.Add(badgeRow);

        cardBody.Add(TerrainUi.MakeSecondary("Regenerate", RerollIconName, "Regenerate this feature's scatter, keeping locked items", () => TerrainFeatureEvents.Raise(area)));

        int staleTileCount = TerrainFeatureBaker.StaleTiles.Count;
        if (staleTileCount > 0)
        {
            Label staleLabel = new Label(staleTileCount + " tiles need a rebake");
            staleLabel.AddToClassList("terrain-warning");
            cardBody.Add(staleLabel);
            cardBody.Add(TerrainUi.MakePrimary("Bake stale tiles", null, "Rebake every tile a feature has changed", () => TerrainFeatureBaker.RebakeAllFeatures(true)));
        }

        return card;
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

    static List<TerrainAreaRecipe> FindAllRecipes()
    {
        List<TerrainAreaRecipe> recipes = new List<TerrainAreaRecipe>();
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainAreaRecipe"))
        {
            TerrainAreaRecipe recipe = AssetDatabase.LoadAssetAtPath<TerrainAreaRecipe>(AssetDatabase.GUIDToAssetPath(guid));
            if (recipe != null) recipes.Add(recipe);
        }
        return recipes;
    }

    void ArmOutline(TerrainAreaRecipe recipe)
    {
        armedRecipe = recipe;
        TerrainFeatureTool.BeginDrawOutline(recipe);
        Refresh();
    }

    void ArmBox(TerrainAreaRecipe recipe)
    {
        armedRecipe = recipe;
        TerrainFeatureTool.BeginDragBox(recipe);
        Refresh();
    }

    void SelectAndFrame(TerrainArea area)
    {
        Selection.activeObject = area.gameObject;
        TerrainFeatureTool.FrameSelected();
        Refresh();
    }

    static void RerollSeed(TerrainArea area)
    {
        Undo.RecordObject(area, "Reroll feature seed");
        area.seed = Random.Range(1, int.MaxValue);
        EditorUtility.SetDirty(area);
        TerrainFeatureEvents.Raise(area);
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
