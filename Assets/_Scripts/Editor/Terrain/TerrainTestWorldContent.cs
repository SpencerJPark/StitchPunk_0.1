#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

// Fills the Create Test World grid: hills and one mountain, roads, a river with a ford and a bridge, props, spawn markers
public static class TerrainTestWorldContent
{
    const string RoadProfilePath = "Assets/ScriptableObjects/Terrain/Roads/DirtTrack_RoadProfile.asset";
    const string WaterMaterialPath = "Assets/PaintedGround/Materials/M_PaintedWater.mat";
    const string RoadsRootName = "TestWorldRoads";
    const string WaterRootName = "TestWorldWater";
    const string GenerationProfileName = "TestWorldGeneration.asset";
    const string WaterProfileName = "TestWorldWater.asset";
    const float RiverBaseX = 170f;
    const float MountainHeightMetres = 200f;
    const float MountainRadiusMetres = 150f;
    const int GenerationSeed = 20261107;
    const int ScatterSeed = 4711;
    const int DecalSeed = 1337;
    const int MarkerSeed = 99;
    const int RoadKnotCount = 9;
    const float RoadWidthMetres = 4f;

    struct WorldLayout
    {
        public int tilesPerSide;
        public float roadHalfExtent;
        public Vector2 mountainCentre;
    }

    public static void PopulateTestWorld(int tilesPerSide)
    {
        int half = tilesPerSide / 2;
        WorldLayout layout = new WorldLayout
        {
            tilesPerSide = tilesPerSide,
            roadHalfExtent = half * TerrainTile.TileSizeMetres + 30f,
            mountainCentre = new Vector2(half * TerrainTile.TileSizeMetres - 20f, half * TerrainTile.TileSizeMetres - 20f)
        };
        Scene worldScene = EditorSceneManager.GetSceneByPath(TerrainTestWorldBuilder.TestWorldScenePath);
        if (!worldScene.IsValid()) worldScene = EditorSceneManager.GetActiveScene();

        try
        {
            ReportStep("Generating hills and the mountain", 0.05f);
            GenerateTerrain(layout);
            ReportStep("Building roads", 0.35f);
            Transform roadsRoot = CreateRoadNetwork(layout, worldScene);
            ReportStep("Building the river", 0.4f);
            CreateRiverWithCrossings(layout, worldScene, roadsRoot);
            ReportStep("Baking water", 0.45f);
            TerrainWaterBaker.RebakeAllWater(true);
            ReportStep("Baking roads", 0.55f);
            TerrainRoadBaker.RebakeAllRoads(true);
            ReportStep("Scattering props", 0.7f);
            ScatterProps(layout);
            ScatterDecals(layout);
            ReportStep("Placing spawn markers", 0.8f);
            PlaceSpawnMarkers(layout);
            ReportStep("Baking nav", 0.9f);
            TerrainNavBaker.BakeAllOpenTiles();
            ReportStep("Saving", 0.97f);
            EditorSceneManager.MarkSceneDirty(worldScene);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static void ReportStep(string label, float progress01)
    {
        EditorUtility.DisplayProgressBar("Create Test World", label, progress01);
    }

    // The generator only has world-space noise, so the centre is gentle rather than exactly flat
    static void GenerateTerrain(WorldLayout layout)
    {
        TerrainGenerationProfile profile = CreateGenerationProfile(layout);
        if (profile == null) return;

        GameObject regionObject = new GameObject("TestWorldRegion");
        TerrainRegion region = regionObject.AddComponent<TerrainRegion>();
        float boundsHalf = (layout.tilesPerSide / 2) * TerrainTile.TileSizeMetres + 49f;
        region.boundsMin = new Vector3(-boundsHalf, -50f, -boundsHalf);
        region.boundsMax = new Vector3(boundsHalf, 600f, boundsHalf);
        region.mode = TerrainRegionMode.Procedural;
        region.profile = profile;
        TerrainGenerateTool.Generate(region, true, (progress, label) => ReportStep("Generating: " + label, 0.05f + progress * 0.25f));
    }

    static TerrainGenerationProfile CreateGenerationProfile(WorldLayout layout)
    {
        TerrainGenerationProfile profile = ScriptableObject.CreateInstance<TerrainGenerationProfile>();
        profile.seed = GenerationSeed;
        profile.heightLayers = new List<TerrainHeightLayer>
        {
            new TerrainHeightLayer { frequencyPerMetre = 0.006f, amplitudeMetres = 7f, octaves = 3, lacunarity = 2f, gain = 0.45f },
            new TerrainHeightLayer { frequencyPerMetre = 0.02f, amplitudeMetres = 1.2f, octaves = 2, lacunarity = 2f, gain = 0.5f }
        };
        profile.heightRemap = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.45f, 0.1f), new Keyframe(1f, 1f));
        profile.mountainCentreWorld = layout.mountainCentre;
        profile.mountainRadiusMetres = MountainRadiusMetres;
        profile.mountainHeightMetres = MountainHeightMetres;
        profile.mountainProfile = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        return CreateAssetInTestFolder(profile, GenerationProfileName);
    }

    static TAsset CreateAssetInTestFolder<TAsset>(TAsset asset, string fileName) where TAsset : ScriptableObject
    {
        string assetPath = TerrainTestWorldBuilder.TestWorldTileFolder + "/" + fileName;
        if (!AssetDatabase.IsValidFolder(TerrainTestWorldBuilder.TestWorldTileFolder))
        {
            Debug.LogWarning("[Terrain] Test world folder missing, cannot create " + assetPath);
            return null;
        }
        AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(asset, assetPath);
        return asset;
    }

    static Vector3 MainRoadPoint(float fraction, WorldLayout layout)
    {
        float worldX = Mathf.Lerp(-layout.roadHalfExtent, layout.roadHalfExtent, fraction);
        return new Vector3(worldX, 0f, MainRoadZAt(worldX));
    }

    static float MainRoadZAt(float worldX)
    {
        return 14f * Mathf.Sin(worldX * 0.011f);
    }

    static Vector3 BranchPoint(float fraction, WorldLayout layout)
    {
        Vector3 start = new Vector3(60f, 0f, MainRoadZAt(60f));
        Vector2 mountainFootDirection = new Vector2(layout.mountainCentre.x, layout.mountainCentre.y).normalized;
        Vector2 foot = layout.mountainCentre - mountainFootDirection * (MountainRadiusMetres * 0.65f);
        Vector3 end = new Vector3(foot.x, 0f, foot.y);
        Vector3 along = Vector3.Lerp(start, end, fraction);
        Vector3 direction = (end - start).normalized;
        Vector3 sideways = new Vector3(-direction.z, 0f, direction.x);
        return along + sideways * (12f * Mathf.Sin(fraction * Mathf.PI * 2f));
    }

    static Vector3 LoopPoint(float fraction)
    {
        float angle = fraction * Mathf.PI * 2f;
        float radius = 70f + 6f * Mathf.Sin(angle * 3f);
        return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
    }

    static float RiverXAt(float worldZ)
    {
        return RiverBaseX + 15f * Mathf.Sin(worldZ * 0.012f);
    }

    static Transform CreateRoadNetwork(WorldLayout layout, Scene worldScene)
    {
        TerrainRoadProfile roadProfile = AssetDatabase.LoadAssetAtPath<TerrainRoadProfile>(RoadProfilePath);
        if (roadProfile == null)
        {
            Debug.LogWarning("[Terrain] Road profile missing at " + RoadProfilePath + "; roads skipped.");
            return null;
        }

        GameObject rootObject = new GameObject(RoadsRootName);
        SceneManager.MoveGameObjectToScene(rootObject, worldScene);
        CreateRoad("MainRoad", rootObject.transform, roadProfile, false, fraction => MainRoadPoint(fraction, layout));
        CreateRoad("MountainBranch", rootObject.transform, roadProfile, false, fraction => BranchPoint(fraction, layout));
        CreateRoad("CampLoop", rootObject.transform, roadProfile, true, fraction => LoopPoint(fraction));
        return rootObject.transform;
    }

    static void CreateRoad(string roadName, Transform parent, TerrainRoadProfile roadProfile, bool closed, Func<float, Vector3> pointAtFraction)
    {
        GameObject roadObject = new GameObject(roadName);
        roadObject.transform.SetParent(parent, false);
        SplineContainer container = roadObject.AddComponent<SplineContainer>();
        TerrainRoad road = roadObject.AddComponent<TerrainRoad>();
        road.profile = roadProfile;
        road.priority = 0;
        road.widthOverrideMetres = RoadWidthMetres;

        // A closed loop repeats no end knot: the spline wraps back to the first itself
        int knotCount = closed ? RoadKnotCount - 1 : RoadKnotCount;
        float fractionStep = closed ? 1f / knotCount : 1f / (knotCount - 1);
        FillSpline(container.Spline, knotCount, closed, knotIndex => pointAtFraction(knotIndex * fractionStep));
    }

    static void FillSpline(Spline spline, int knotCount, bool closed, Func<int, Vector3> pointAtKnot)
    {
        spline.Clear();
        for (int knotIndex = 0; knotIndex < knotCount; knotIndex++)
        {
            Vector3 point = pointAtKnot(knotIndex);
            spline.Add(new BezierKnot(new float3(point.x, point.y, point.z)), TangentMode.AutoSmooth);
        }
        spline.Closed = closed;
    }

    static void CreateRiverWithCrossings(WorldLayout layout, Scene worldScene, Transform roadsRoot)
    {
        TerrainWaterProfile waterProfile = ScriptableObject.CreateInstance<TerrainWaterProfile>();
        waterProfile.waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        if (waterProfile.waterMaterial == null) Debug.LogWarning("[Terrain] Water material missing at " + WaterMaterialPath + "; the river has no material.");
        waterProfile = CreateAssetInTestFolder(waterProfile, WaterProfileName);
        if (waterProfile == null) return;

        GameObject waterRoot = new GameObject(WaterRootName);
        SceneManager.MoveGameObjectToScene(waterRoot, worldScene);

        GameObject riverObject = new GameObject("TestWorldRiver");
        riverObject.transform.SetParent(waterRoot.transform, false);
        SplineContainer container = riverObject.AddComponent<SplineContainer>();
        TerrainRiver river = riverObject.AddComponent<TerrainRiver>();
        river.profile = waterProfile;
        FillSpline(container.Spline, RoadKnotCount, false, knotIndex =>
        {
            float worldZ = Mathf.Lerp(-layout.roadHalfExtent, layout.roadHalfExtent, knotIndex / (float)(RoadKnotCount - 1));
            return new Vector3(RiverXAt(worldZ), 0f, worldZ);
        });

        if (roadsRoot == null)
        {
            Debug.LogWarning("[Terrain] No roads were built, so the ford and bridge are skipped.");
            return;
        }
        CreateCrossingWhereRoadMeetsRiver("TestWorldBridge", TerrainCrossingKind.Bridge, waterRoot.transform, fraction => MainRoadPoint(fraction, layout));
        CreateCrossingWhereRoadMeetsRiver("TestWorldFord", TerrainCrossingKind.Ford, waterRoot.transform, fraction => BranchPoint(fraction, layout));
    }

    static void CreateCrossingWhereRoadMeetsRiver(string crossingName, TerrainCrossingKind kind, Transform parent, Func<float, Vector3> roadPointAtFraction)
    {
        const int scanSteps = 1000;
        float bestFraction = 0f;
        float bestDistance = float.MaxValue;
        for (int step = 0; step <= scanSteps; step++)
        {
            float fraction = step / (float)scanSteps;
            Vector3 point = roadPointAtFraction(fraction);
            float distance = Mathf.Abs(point.x - RiverXAt(point.z));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestFraction = fraction;
            }
        }

        Vector3 crossingPoint = roadPointAtFraction(bestFraction);
        Vector3 ahead = roadPointAtFraction(Mathf.Min(1f, bestFraction + 0.01f));
        Vector3 behind = roadPointAtFraction(Mathf.Max(0f, bestFraction - 0.01f));
        Vector3 tangent = ahead - behind;
        float yawDegrees = -Mathf.Atan2(tangent.z, tangent.x) * Mathf.Rad2Deg;

        GameObject crossingObject = new GameObject(crossingName);
        crossingObject.transform.SetParent(parent, false);
        crossingObject.transform.position = crossingPoint;
        crossingObject.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        TerrainCrossing crossing = crossingObject.AddComponent<TerrainCrossing>();
        crossing.kind = kind;
        crossing.footprintMetres = new Vector2(30f, RoadWidthMetres + 2f);
    }

    static void ScatterProps(WorldLayout layout)
    {
        TerrainScatterSet scatterSet = FindFirstAsset<TerrainScatterSet>();
        if (scatterSet == null)
        {
            Debug.LogWarning("[Terrain] No TerrainScatterSet asset in the project; trees and rocks skipped.");
            return;
        }
        foreach (TerrainTile tile in UnityEngine.Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
            TerrainScatterGenerator.Generate(tile, scatterSet, ScatterSeed, null);
    }

    static void ScatterDecals(WorldLayout layout)
    {
        TerrainDecalSet decalSet = FindFirstAsset<TerrainDecalSet>();
        if (decalSet == null)
        {
            Debug.LogWarning("[Terrain] No TerrainDecalSet asset in the project; decals skipped.");
            return;
        }
        foreach (TerrainTile tile in UnityEngine.Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
            TerrainDecalScatterer.Scatter(tile, decalSet, DecalSeed, null);
    }

    static TAsset FindFirstAsset<TAsset>() where TAsset : UnityEngine.Object
    {
        string[] guids = AssetDatabase.FindAssets("t:" + typeof(TAsset).Name);
        if (guids.Length == 0) return null;
        return AssetDatabase.LoadAssetAtPath<TAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }

    static void PlaceSpawnMarkers(WorldLayout layout)
    {
        System.Random random = new System.Random(MarkerSeed);
        const int onDemandMarkerCount = 8;
        const int persistentMarkerCount = 2;
        for (int index = 0; index < onDemandMarkerCount + persistentMarkerCount; index++)
        {
            bool persistent = index >= onDemandMarkerCount;
            // Evenly spread bearings, so markers never pile up on one side
            float angle = (index / (float)(onDemandMarkerCount + persistentMarkerCount)) * Mathf.PI * 2f + (float)random.NextDouble() * 0.3f;
            float minDistance = 130f;
            float maxDistance = Mathf.Max(minDistance + 1f, layout.roadHalfExtent - 70f);
            float distance = Mathf.Lerp(minDistance, maxDistance, (float)random.NextDouble());
            Vector3 position = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            PlaceOneMarker(position, persistent, index);
        }
    }

    static void PlaceOneMarker(Vector3 position, bool persistent, int index)
    {
        Vector2Int coordinate = TerrainTile.CoordinateContaining(position);
        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile == null && TerrainTilesPanel.OpenTileForEdit(coordinate)) tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile == null)
        {
            Debug.LogWarning("[Terrain] No open tile at " + coordinate + "; spawn marker " + index + " skipped.");
            return;
        }

        float groundHeight = tile.tileData != null ? tile.tileData.SampleBilinear(position.x, position.z) + TerrainTileCreator.GroundTopHeight : 0f;
        GameObject markerObject = new GameObject(persistent ? "CampMarker_" + index : "SpawnMarker_" + index);
        markerObject.transform.SetParent(tile.transform, false);
        markerObject.transform.position = new Vector3(position.x, groundHeight, position.z);
        TerrainSpawnMarkerAuthoring marker = markerObject.AddComponent<TerrainSpawnMarkerAuthoring>();
        marker.persistent = persistent;
        marker.unitType = persistent || index % 2 == 0 ? UnitType.Bandit : UnitType.MaleRotter;
        marker.count = persistent ? 4 : 3;
        marker.radiusMetres = persistent ? 12f : 8f;
        marker.respawnAfterGameDays = persistent ? 3f : 0f;
        EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
    }
}
#endif
