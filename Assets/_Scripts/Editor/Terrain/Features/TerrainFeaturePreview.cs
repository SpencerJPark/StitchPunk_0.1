#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainFeaturePreview
{
    public const int MaxDots = 2000;

    private const double ReplanIntervalSeconds = 0.1;
    private const float DotRadiusMetres = 0.6f;
    private const float FillAlpha = 0.25f;
    private const float PreviewEdgeNoiseMetres = 3f;
    private const float TileSizeMetres = 100f;
    private const float TileCentreOffsetMetres = 50f;
    private const float NormalProbeMetres = 0.5f;
    private const float LiftAboveGroundMetres = 0.05f;

    private static readonly Color DotColour = new Color(0.08f, 0.32f, 0.1f, 1f);

    // Accepts every point as forest so only the area stamp and clearing noise decide where dots land.
    private sealed class PreviewGroundSampler : ITerrainScatterSampler
    {
        private readonly Dictionary<Vector2Int, TerrainTileData> tileDataByCoordinate = new Dictionary<Vector2Int, TerrainTileData>();

        public float HeightAt(float worldX, float worldZ)
        {
            Vector2Int coordinate = new Vector2Int(
                Mathf.FloorToInt((worldX + TileCentreOffsetMetres) / TileSizeMetres),
                Mathf.FloorToInt((worldZ + TileCentreOffsetMetres) / TileSizeMetres));
            TerrainTileData tileData = FindTileData(coordinate);
            if (tileData == null) return 0f;
            return tileData.SampleBilinear(worldX, worldZ) + TerrainTileData.GroundTopHeightMetres;
        }

        public Vector3 NormalAt(float worldX, float worldZ)
        {
            float heightWest = HeightAt(worldX - NormalProbeMetres, worldZ);
            float heightEast = HeightAt(worldX + NormalProbeMetres, worldZ);
            float heightSouth = HeightAt(worldX, worldZ - NormalProbeMetres);
            float heightNorth = HeightAt(worldX, worldZ + NormalProbeMetres);
            return new Vector3(heightWest - heightEast, 2f * NormalProbeMetres, heightSouth - heightNorth).normalized;
        }

        public bool TryGetBiome(float worldX, float worldZ, out float dirt, out float grass, out float forest, out float stone)
        {
            dirt = 0f;
            grass = 0f;
            forest = 1f;
            stone = 0f;
            return true;
        }

        private TerrainTileData FindTileData(Vector2Int coordinate)
        {
            TerrainTileData tileData;
            if (tileDataByCoordinate.TryGetValue(coordinate, out tileData) && tileData != null) return tileData;

            TerrainTile openTile = TerrainTileCreator.FindTileAt(coordinate);
            tileData = openTile != null ? openTile.tileData : null;
            if (tileData == null)
            {
                tileData = AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(coordinate));
            }
            tileDataByCoordinate[coordinate] = tileData;
            return tileData;
        }
    }

    private static readonly PreviewGroundSampler previewGroundSampler = new PreviewGroundSampler();

    private static Vector2[] outlineXZ;
    private static TerrainAreaRecipe recipe;
    private static int seed;
    private static float densityMultiplier = 1f;
    private static double lastPlanTime;
    private static bool replanPending;
    private static List<TerrainScatterPlacement> dotPlacements = new List<TerrainScatterPlacement>();
    private static Vector3[] outlineGroundPoints = new Vector3[0];
    private static Vector3 outlineCentroid;

    public static void SetOutline(IReadOnlyList<Vector2> newOutlineXZ, TerrainAreaRecipe newRecipe, int newSeed)
    {
        SetOutline(newOutlineXZ, newRecipe, newSeed, 1f);
    }

    public static void SetOutline(IReadOnlyList<Vector2> newOutlineXZ, TerrainAreaRecipe newRecipe, int newSeed, float newDensityMultiplier)
    {
        if (newOutlineXZ == null || newOutlineXZ.Count < 3)
        {
            Clear();
            return;
        }

        bool recipeOrSeedChanged = newRecipe != recipe || newSeed != seed || !Mathf.Approximately(newDensityMultiplier, densityMultiplier);
        densityMultiplier = newDensityMultiplier;
        outlineXZ = new Vector2[newOutlineXZ.Count];
        for (int pointIndex = 0; pointIndex < outlineXZ.Length; pointIndex++)
        {
            outlineXZ[pointIndex] = newOutlineXZ[pointIndex];
        }
        recipe = newRecipe;
        seed = newSeed;

        replanPending = true;
        if (recipeOrSeedChanged || EditorApplication.timeSinceStartup - lastPlanTime >= ReplanIntervalSeconds)
        {
            Replan();
        }
    }

    public static void Clear()
    {
        outlineXZ = null;
        recipe = null;
        seed = 0;
        replanPending = false;
        dotPlacements = new List<TerrainScatterPlacement>();
        outlineGroundPoints = new Vector3[0];
    }

    public static void Draw()
    {
        if (outlineXZ == null) return;

        if (replanPending && EditorApplication.timeSinceStartup - lastPlanTime >= ReplanIntervalSeconds)
        {
            Replan();
        }

        TerrainSplatLayer groundLayer = recipe != null ? recipe.groundLayer : TerrainSplatLayer.Forest;
        Color32 layerColour = TerrainSplatCodec.ColourOfLayer(groundLayer);
        Color fillColour = new Color(layerColour.r / 255f, layerColour.g / 255f, layerColour.b / 255f, FillAlpha);
        Color edgeColour = new Color(fillColour.r, fillColour.g, fillColour.b, 1f);

        Handles.color = fillColour;
        for (int pointIndex = 0; pointIndex < outlineGroundPoints.Length; pointIndex++)
        {
            Vector3 current = outlineGroundPoints[pointIndex];
            Vector3 next = outlineGroundPoints[(pointIndex + 1) % outlineGroundPoints.Length];
            Handles.DrawAAConvexPolygon(outlineCentroid, current, next);
        }

        Handles.color = edgeColour;
        Vector3[] closedLine = new Vector3[outlineGroundPoints.Length + 1];
        for (int pointIndex = 0; pointIndex < outlineGroundPoints.Length; pointIndex++)
        {
            closedLine[pointIndex] = outlineGroundPoints[pointIndex];
        }
        closedLine[outlineGroundPoints.Length] = outlineGroundPoints[0];
        Handles.DrawAAPolyLine(2f, closedLine);

        Handles.color = DotColour;
        for (int placementIndex = 0; placementIndex < dotPlacements.Count; placementIndex++)
        {
            Handles.DrawSolidDisc(dotPlacements[placementIndex].position, Vector3.up, DotRadiusMetres);
        }
    }

    private static void Replan()
    {
        lastPlanTime = EditorApplication.timeSinceStartup;
        replanPending = false;

        outlineGroundPoints = new Vector3[outlineXZ.Length];
        Vector3 sum = Vector3.zero;
        float minimumX = float.MaxValue;
        float minimumZ = float.MaxValue;
        float maximumX = float.MinValue;
        float maximumZ = float.MinValue;
        for (int pointIndex = 0; pointIndex < outlineXZ.Length; pointIndex++)
        {
            Vector2 point = outlineXZ[pointIndex];
            outlineGroundPoints[pointIndex] = new Vector3(point.x, previewGroundSampler.HeightAt(point.x, point.y) + LiftAboveGroundMetres, point.y);
            sum += outlineGroundPoints[pointIndex];
            minimumX = Mathf.Min(minimumX, point.x);
            minimumZ = Mathf.Min(minimumZ, point.y);
            maximumX = Mathf.Max(maximumX, point.x);
            maximumZ = Mathf.Max(maximumZ, point.y);
        }
        outlineCentroid = sum / outlineXZ.Length;

        dotPlacements = new List<TerrainScatterPlacement>();
        TerrainForestRecipe forestRecipe = recipe as TerrainForestRecipe;
        if (forestRecipe == null || forestRecipe.scatterSet == null) return;

        TerrainAreaStamp stamp = new TerrainAreaStamp
        {
            outlineXZ = outlineXZ,
            edgeNoiseMetres = PreviewEdgeNoiseMetres,
            seed = seed,
            areaIndex = 1
        };
        TerrainAreaRestrictedSampler restrictedSampler = new TerrainAreaRestrictedSampler(
            previewGroundSampler, stamp, forestRecipe.clearingNoiseScaleMetres, forestRecipe.clearingThreshold01);
        Rect boundsXZ = new Rect(minimumX, minimumZ, maximumX - minimumX, maximumZ - minimumZ);
        List<TerrainScatterRule> previewRules = TerrainScatterGenerator.RulesFrom(forestRecipe.scatterSet);
        float safeMultiplier = densityMultiplier > 0f ? densityMultiplier : 1f;
        for (int ruleIndex = 0; ruleIndex < previewRules.Count; ruleIndex++)
        {
            TerrainScatterRule scaledRule = previewRules[ruleIndex];
            scaledRule.densityPer100SquareMetres *= safeMultiplier;
            previewRules[ruleIndex] = scaledRule;
        }
        List<TerrainScatterPlacement> planned = TerrainScatterPlanner.Plan(
            previewRules, restrictedSampler, boundsXZ, seed, null);

        if (planned.Count <= MaxDots)
        {
            dotPlacements = planned;
            return;
        }
        int stride = Mathf.CeilToInt(planned.Count / (float)MaxDots);
        for (int placementIndex = 0; placementIndex < planned.Count; placementIndex += stride)
        {
            dotPlacements.Add(planned[placementIndex]);
        }
    }
}
#endif
