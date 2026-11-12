#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

// Seed in, explorable world out: recipe -> terrain -> roads, river, crossings -> bakes, props, markers, nav
public static class TerrainRandomWorldBuilder
{
    const string GenerationProfileName = "RandomWorldGeneration.asset";
    const string WaterProfileName = "RandomWorldWater.asset";
    const string RoadsRootName = "RandomWorldRoads";
    const string WaterRootName = "RandomWorldWater";
    const int PersistentMarkerCount = 2;

    [MenuItem("Stitch Punk/Terrain/Create Random World (7 x 7)")]
    static void CreateSevenBySevenFromMenu()
    {
        BuildFromMenu(7);
    }

    [MenuItem("Stitch Punk/Terrain/Create Random World (11 x 11)")]
    static void CreateElevenByElevenFromMenu()
    {
        BuildFromMenu(11);
    }

    static void BuildFromMenu(int tilesPerSide)
    {
        int seed = (int)(System.DateTime.Now.Ticks & 0x7FFFFFFF);
        string message = "This builds a NEW " + tilesPerSide + " x " + tilesPerSide + " random world (seed " + seed + ") at "
            + TerrainTestWorldBuilder.RandomWorldTarget.scenePath + ".\n\nThe folder " + TerrainTestWorldBuilder.RandomWorldTarget.tileFolder
            + " will be deleted and replaced. CampLoop is never touched.";
        if (!EditorUtility.DisplayDialog("Create Random World", message, "Create", "Cancel")) return;
        string report = BuildRandomWorld(tilesPerSide, seed);
        Debug.Log("[Terrain] " + report + " (seed " + seed + ")");
    }

    public static string BuildRandomWorld(int tilesPerSide, int seed)
    {
        return TerrainTestWorldBuilder.BuildWorld(TerrainTestWorldBuilder.RandomWorldTarget, tilesPerSide, size => PopulateRandomWorld(size, seed));
    }

    static void ReportStep(string label, float progress01)
    {
        EditorUtility.DisplayProgressBar("Create Random World", label, progress01);
    }

    static void PopulateRandomWorld(int tilesPerSide, int seed)
    {
        Vector2Int tileRange = TerrainTestWorldBuilder.TileRangeForSide(tilesPerSide);
        Scene worldScene = EditorSceneManager.GetSceneByPath(TerrainTestWorldBuilder.ActiveTarget.scenePath);
        if (!worldScene.IsValid()) worldScene = EditorSceneManager.GetActiveScene();

        try
        {
            ReportStep("Planning the world", 0.02f);
            TerrainGenerationProfile profile = ScriptableObject.CreateInstance<TerrainGenerationProfile>();
            profile.heightLayers = new List<TerrainHeightLayer>
            {
                new TerrainHeightLayer { frequencyPerMetre = 0.006f, amplitudeMetres = 7f, octaves = 3, lacunarity = 2f, gain = 0.45f },
                new TerrainHeightLayer { frequencyPerMetre = 0.02f, amplitudeMetres = 1.2f, octaves = 2, lacunarity = 2f, gain = 0.5f }
            };
            profile.heightRemap = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.45f, 0.1f), new Keyframe(1f, 1f));
            TerrainWorldRecipe recipe = TerrainWorldRecipeBuilder.Build(seed, tilesPerSide, profile);
            profile = TerrainTestWorldContent.CreateAssetInTestFolder(profile, GenerationProfileName);
            if (profile == null) return;

            ReportStep("Generating terrain", 0.05f);
            GameObject regionObject = new GameObject("RandomWorldRegion");
            SceneManager.MoveGameObjectToScene(regionObject, worldScene);
            TerrainRegion region = regionObject.AddComponent<TerrainRegion>();
            float boundsHalf = (tilesPerSide / 2) * TerrainTile.TileSizeMetres + 49f;
            region.boundsMin = new Vector3(-boundsHalf, -50f, -boundsHalf);
            region.boundsMax = new Vector3(boundsHalf, 600f, boundsHalf);
            region.mode = TerrainRegionMode.Procedural;
            region.profile = profile;
            TerrainGenerateTool.Generate(region, true, (progress, label) => ReportStep("Generating: " + label, 0.05f + progress * 0.25f));

            ReportStep("Opening tiles", 0.32f);
            for (int coordinateZ = tileRange.x; coordinateZ <= tileRange.y; coordinateZ++)
            {
                for (int coordinateX = tileRange.x; coordinateX <= tileRange.y; coordinateX++)
                    TerrainTilesPanel.OpenTileForEdit(new Vector2Int(coordinateX, coordinateZ));
            }

            ReportStep("Building roads", 0.36f);
            Transform roadsRoot = CreateRoads(recipe, worldScene);
            ReportStep("Building the river", 0.4f);
            CreateRiverWithCrossings(recipe, worldScene);

            ReportStep("Baking water", 0.45f);
            TerrainWaterBaker.RebakeAllWater(true);
            ReportStep("Baking roads", 0.55f);
            TerrainRoadBaker.RebakeAllRoads(true);
            ReportStep("Scattering props", 0.7f);
            TerrainTestWorldContent.ScatterProps(seed * 31 + 7);
            TerrainTestWorldContent.ScatterDecals(seed * 17 + 3);
            ReportStep("Placing spawn markers", 0.8f);
            for (int index = 0; index < recipe.locations.Count; index++)
            {
                Vector2 location = recipe.locations[index];
                TerrainTestWorldContent.PlaceOneMarker(new Vector3(location.x, 0f, location.y), index < PersistentMarkerCount, index);
            }
            ReportStep("Baking nav", 0.88f);
            TerrainNavBaker.BakeAllOpenTiles();

            ReportStep("Saving", 0.94f);
            EditorSceneManager.MarkSceneDirty(worldScene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            ReportStep("Closing tiles", 0.97f);
            for (int coordinateZ = tileRange.x; coordinateZ <= tileRange.y; coordinateZ++)
            {
                for (int coordinateX = tileRange.x; coordinateX <= tileRange.y; coordinateX++)
                    TerrainTilesPanel.SaveAndCloseTile(new Vector2Int(coordinateX, coordinateZ));
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static Transform CreateRoads(TerrainWorldRecipe recipe, Scene worldScene)
    {
        TerrainRoadProfile roadProfile = AssetDatabase.LoadAssetAtPath<TerrainRoadProfile>(TerrainTestWorldContent.RoadProfilePath);
        if (roadProfile == null)
        {
            Debug.LogWarning("[Terrain] Road profile missing at " + TerrainTestWorldContent.RoadProfilePath + "; roads skipped.");
            return null;
        }

        GameObject rootObject = new GameObject(RoadsRootName);
        SceneManager.MoveGameObjectToScene(rootObject, worldScene);
        for (int roadIndex = 0; roadIndex < recipe.roads.Count; roadIndex++)
        {
            if (recipe.roads[roadIndex].Count < 2) continue;
            TerrainTestWorldContent.CreateRoad("RandomRoad_" + roadIndex, rootObject.transform, roadProfile, recipe.roads[roadIndex]);
        }
        return rootObject.transform;
    }

    static void CreateRiverWithCrossings(TerrainWorldRecipe recipe, Scene worldScene)
    {
        if (recipe.river == null || recipe.river.Count < 2) return;

        TerrainWaterProfile waterProfile = ScriptableObject.CreateInstance<TerrainWaterProfile>();
        waterProfile.waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(TerrainTestWorldContent.WaterMaterialPath);
        if (waterProfile.waterMaterial == null) Debug.LogWarning("[Terrain] Water material missing at " + TerrainTestWorldContent.WaterMaterialPath + "; the river has no material.");
        waterProfile = TerrainTestWorldContent.CreateAssetInTestFolder(waterProfile, WaterProfileName);
        if (waterProfile == null) return;

        GameObject waterRoot = new GameObject(WaterRootName);
        SceneManager.MoveGameObjectToScene(waterRoot, worldScene);
        GameObject riverObject = new GameObject("RandomWorldRiver");
        riverObject.transform.SetParent(waterRoot.transform, false);
        SplineContainer container = riverObject.AddComponent<SplineContainer>();
        TerrainRiver river = riverObject.AddComponent<TerrainRiver>();
        river.profile = waterProfile;
        List<Vector3> riverPoints = recipe.river;
        TerrainTestWorldContent.FillSpline(container.Spline, riverPoints.Count, false, knotIndex => new Vector3(riverPoints[knotIndex].x, 0f, riverPoints[knotIndex].z));

        int crossingIndex = 0;
        foreach (List<Vector3> roadPolyline in recipe.roads)
        {
            for (int roadSegment = 0; roadSegment + 1 < roadPolyline.Count; roadSegment++)
            {
                for (int riverSegment = 0; riverSegment + 1 < riverPoints.Count; riverSegment++)
                {
                    Vector3 crossingPoint;
                    if (!TryIntersectInGroundPlane(roadPolyline[roadSegment], roadPolyline[roadSegment + 1], riverPoints[riverSegment], riverPoints[riverSegment + 1], out crossingPoint)) continue;
                    TerrainCrossingKind kind = crossingIndex % 2 == 0 ? TerrainCrossingKind.Bridge : TerrainCrossingKind.Ford;
                    CreateCrossing("RandomCrossing_" + crossingIndex, kind, waterRoot.transform, crossingPoint, roadPolyline[roadSegment + 1] - roadPolyline[roadSegment]);
                    crossingIndex++;
                }
            }
        }
    }

    static void CreateCrossing(string crossingName, TerrainCrossingKind kind, Transform parent, Vector3 crossingPoint, Vector3 roadDirection)
    {
        float yawDegrees = -Mathf.Atan2(roadDirection.z, roadDirection.x) * Mathf.Rad2Deg;
        GameObject crossingObject = new GameObject(crossingName);
        crossingObject.transform.SetParent(parent, false);
        crossingObject.transform.position = crossingPoint;
        crossingObject.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        TerrainCrossing crossing = crossingObject.AddComponent<TerrainCrossing>();
        crossing.kind = kind;
        crossing.footprintMetres = new Vector2(30f, TerrainTestWorldContent.RoadWidthMetres + 2f);
    }

    static bool TryIntersectInGroundPlane(Vector3 firstStart, Vector3 firstEnd, Vector3 secondStart, Vector3 secondEnd, out Vector3 intersection)
    {
        intersection = Vector3.zero;
        float firstDeltaX = firstEnd.x - firstStart.x;
        float firstDeltaZ = firstEnd.z - firstStart.z;
        float secondDeltaX = secondEnd.x - secondStart.x;
        float secondDeltaZ = secondEnd.z - secondStart.z;
        float denominator = firstDeltaX * secondDeltaZ - firstDeltaZ * secondDeltaX;
        if (Mathf.Abs(denominator) < 1e-5f) return false;

        float offsetX = secondStart.x - firstStart.x;
        float offsetZ = secondStart.z - firstStart.z;
        float alongFirst = (offsetX * secondDeltaZ - offsetZ * secondDeltaX) / denominator;
        float alongSecond = (offsetX * firstDeltaZ - offsetZ * firstDeltaX) / denominator;
        if (alongFirst < 0f || alongFirst > 1f || alongSecond < 0f || alongSecond > 1f) return false;

        intersection = new Vector3(firstStart.x + firstDeltaX * alongFirst, 0f, firstStart.z + firstDeltaZ * alongFirst);
        return true;
    }
}
#endif
