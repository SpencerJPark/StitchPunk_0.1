#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TerrainForestRecipeConverter
{
    public static void CopyForestInto(TerrainForestRecipe forest, TerrainFeatureRecipe target)
    {
        target.displayName = forest.name;
        target.category = TerrainFeatureCategory.Areas;
        target.supportedShapes = TerrainFeatureShapeSupport.ClosedArea;
        target.defaultShape = TerrainFeatureShapeKind.Curve;
        target.paintsGround = forest.paintsGround;
        target.groundLayer = forest.groundLayer;

        TerrainFillPlacer fill = new TerrainFillPlacer();
        fill.densityPer100SquareMetres = 0f;
        fill.minSpacingMetres = float.MaxValue;
        fill.maxSlopeDegrees = 0f;
        bool foundRule = false;
        target.placeables = new List<TerrainPlaceable>();

        List<TerrainScatterRuleData> rules = forest.scatterSet != null ? forest.scatterSet.rules : new List<TerrainScatterRuleData>();
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            TerrainScatterRuleData rule = rules[ruleIndex];
            if (rule.densityPer100SquareMetres <= 0f)
            {
                continue;
            }
            if (!foundRule)
            {
                fill.scaleRange = rule.scaleRange;
                foundRule = true;
            }
            fill.densityPer100SquareMetres += rule.densityPer100SquareMetres;
            fill.minSpacingMetres = Mathf.Min(fill.minSpacingMetres, rule.minSpacingMetres);
            fill.maxSlopeDegrees = Mathf.Max(fill.maxSlopeDegrees, rule.slopeRangeDegrees.y);

            float prefabWeightSum = 0f;
            int prefabCount = rule.prefabs != null ? rule.prefabs.Count : 0;
            for (int prefabIndex = 0; prefabIndex < prefabCount; prefabIndex++)
            {
                prefabWeightSum += Mathf.Max(0f, rule.prefabs[prefabIndex].weight);
            }

            if (prefabCount == 0)
            {
                target.placeables.Add(BuildPlaceable(rule, null, rule.densityPer100SquareMetres));
                continue;
            }
            if (prefabWeightSum <= 0f)
            {
                continue;
            }
            for (int prefabIndex = 0; prefabIndex < prefabCount; prefabIndex++)
            {
                TerrainScatterPrefab scatterPrefab = rule.prefabs[prefabIndex];
                if (scatterPrefab.weight <= 0f)
                {
                    continue;
                }
                float placeableDensity = rule.densityPer100SquareMetres * scatterPrefab.weight / prefabWeightSum;
                target.placeables.Add(BuildPlaceable(rule, scatterPrefab.prefab, placeableDensity));
            }
        }

        if (!foundRule)
        {
            fill.minSpacingMetres = 2f;
            fill.maxSlopeDegrees = 35f;
        }
        fill.cardAtlas = forest.cardAtlas;
        fill.interiorDemotionMetres = forest.interiorDemotionMetres;
        fill.clearingNoiseScaleMetres = forest.clearingNoiseScaleMetres;
        fill.clearingThreshold01 = forest.clearingThreshold01;
        target.placer = fill;
    }

    private static TerrainPlaceable BuildPlaceable(TerrainScatterRuleData rule, GameObject prefab, float weight)
    {
        TerrainPlaceable placeable = new TerrainPlaceable();
        placeable.prefab = prefab;
        placeable.weight = weight;
        placeable.tier = rule.tier;
        placeable.cardAtlasEntry = rule.cardAtlasEntry;
        placeable.cardSizeMetres = rule.cardSizeMetres;
        placeable.blocksNavigation = rule.blocksNavigation;
        return placeable;
    }

    [MenuItem("Stitch Punk/Terrain/Convert Forest Recipes")]
    public static void ConvertAllForestRecipes()
    {
        string[] guids = AssetDatabase.FindAssets("t:TerrainForestRecipe");
        TerrainArea[] areas = Object.FindObjectsByType<TerrainArea>(FindObjectsInactive.Include);
        for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
        {
            string forestPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
            TerrainForestRecipe forest = AssetDatabase.LoadAssetAtPath<TerrainForestRecipe>(forestPath);
            if (forest == null)
            {
                continue;
            }
            string featurePath = Path.GetDirectoryName(forestPath).Replace('\\', '/') + "/" + forest.name + "Feature.asset";
            TerrainFeatureRecipe feature = AssetDatabase.LoadAssetAtPath<TerrainFeatureRecipe>(featurePath);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<TerrainFeatureRecipe>();
                AssetDatabase.CreateAsset(feature, featurePath);
            }
            CopyForestInto(forest, feature);
            EditorUtility.SetDirty(feature);

            int repointedCount = 0;
            for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
            {
                if (areas[areaIndex].recipe != forest)
                {
                    continue;
                }
                Undo.RecordObject(areas[areaIndex], "Convert Forest Recipe");
                areas[areaIndex].recipe = feature;
                EditorUtility.SetDirty(areas[areaIndex]);
                repointedCount++;
            }
            Debug.Log("Converted " + forestPath + " -> " + featurePath + ": " + feature.placeables.Count + " placeables, " + repointedCount + " areas repointed");
        }
        AssetDatabase.SaveAssets();
    }
}
#endif
