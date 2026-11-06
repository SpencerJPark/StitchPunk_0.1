using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;

// Second-pass bake: one ribbon mesh per road spline per tile, stored as sub-assets of the tile's TerrainTileData.
public static class TerrainRoadRibbonBaker
{
    public const string RibbonRootName = "Ribbons";
    public const string RibbonMeshPrefix = "Ribbon_";

    private const float SampleSpacingMetres = 0.5f;
    private const float TileHalfSizeMetres = 50f;

    public static int BakeTileRibbons(TerrainTile tile)
    {
        TerrainTileData tileData = tile.tileData;
        if (tileData == null)
        {
            return 0;
        }

        Vector2 tileCenter = TerrainTile.CenterOfTile(tile.tileCoordinate);
        Rect tileRect = new Rect(tileCenter.x - TileHalfSizeMetres, tileCenter.y - TileHalfSizeMetres, TerrainTileData.TileSizeMetres, TerrainTileData.TileSizeMetres);

        TerrainRoad[] allRoads = Object.FindObjectsByType<TerrainRoad>(FindObjectsInactive.Exclude);
        List<TerrainRoad> ribbonRoads = new List<TerrainRoad>();
        foreach (TerrainRoad road in allRoads)
        {
            if (road.profile == null || road.profile.ribbonStyle == null)
            {
                continue;
            }

            Bounds bounds = road.WorldBounds();
            bool overlapsTile = bounds.max.x >= tileRect.xMin && bounds.min.x < tileRect.xMax
                && bounds.max.z >= tileRect.yMin && bounds.min.z < tileRect.yMax;
            if (overlapsTile)
            {
                ribbonRoads.Add(road);
            }
        }

        Dictionary<Vector2Int, TerrainTileData> tileDataCache = new Dictionary<Vector2Int, TerrainTileData>();
        System.Func<float, float, float> groundHeightAt = (float worldX, float worldZ) =>
        {
            Vector2Int owner = new Vector2Int(Mathf.FloorToInt((worldX + TileHalfSizeMetres) / TerrainTileData.TileSizeMetres),
                Mathf.FloorToInt((worldZ + TileHalfSizeMetres) / TerrainTileData.TileSizeMetres));
            TerrainTileData ownerData;
            if (!tileDataCache.TryGetValue(owner, out ownerData))
            {
                TerrainTile openTile = TerrainTileCreator.FindTileAt(owner);
                ownerData = openTile != null && openTile.tileData != null
                    ? openTile.tileData
                    : AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(owner));
                tileDataCache[owner] = ownerData;
            }
            return ownerData == null ? 0f : ownerData.SampleBilinear(worldX, worldZ);
        };

        TerrainCrossing[] crossings = Object.FindObjectsByType<TerrainCrossing>(FindObjectsInactive.Exclude);
        System.Func<float, float, TerrainRibbonCrossingState> crossingAt = (float worldX, float worldZ) =>
        {
            TerrainRibbonCrossingState state = TerrainRibbonCrossingState.None;
            foreach (TerrainCrossing crossing in crossings)
            {
                if (!crossing.ContainsXZ(worldX, worldZ))
                {
                    continue;
                }

                if (crossing.kind == TerrainCrossingKind.Bridge)
                {
                    return TerrainRibbonCrossingState.Bridge;
                }
                state = TerrainRibbonCrossingState.Ford;
            }
            return state;
        };

        Dictionary<TerrainRoad, List<List<Vector2>>> centrelineCache = new Dictionary<TerrainRoad, List<List<Vector2>>>();

        string dataPath = TerrainTileAssets.TileDataPath(tile.tileCoordinate);
        Dictionary<string, Mesh> existingMeshes = new Dictionary<string, Mesh>();
        foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(dataPath))
        {
            Mesh existingMesh = subAsset as Mesh;
            if (existingMesh != null && existingMesh.name.StartsWith(RibbonMeshPrefix))
            {
                existingMeshes[existingMesh.name] = existingMesh;
            }
        }

        Transform ribbonRoot = tile.transform.Find(RibbonRootName);
        bool anythingChanged = false;
        int totalVertexCount = 0;
        HashSet<string> usedNames = new HashSet<string>();

        foreach (TerrainRoad road in ribbonRoads)
        {
            SplineContainer container = road.Container;
            List<List<Vector2>> centrelines = CentrelinesOf(road, centrelineCache);
            for (int splineIndex = 0; splineIndex < centrelines.Count; splineIndex++)
            {
                TerrainRoadRibbonInput input = new TerrainRoadRibbonInput();
                input.centrelineXZ = centrelines[splineIndex];
                input.closedLoop = container.Splines[splineIndex].Closed;
                input.widthMetres = road.EffectiveWidthMetres;
                input.style = road.profile.ribbonStyle;
                input.tileRectXZ = tileRect;
                input.groundHeightAt = groundHeightAt;
                input.crossingAt = crossingAt;
                input.junctionPointsXZ = JunctionPointsOf(road, splineIndex, allRoads, centrelineCache);

                int vertexCount;
                int triangleCount;
                Mesh mesh = TerrainRoadRibbonMeshBuilder.Build(input, tile.transform.position, out vertexCount, out triangleCount);
                if (mesh == null)
                {
                    continue;
                }

                string meshName = RibbonMeshPrefix + road.name + "_" + splineIndex;
                if (usedNames.Contains(meshName))
                {
                    Object.DestroyImmediate(mesh);
                    continue;
                }
                usedNames.Add(meshName);
                totalVertexCount += vertexCount;
                anythingChanged = true;

                Mesh persistedMesh;
                if (existingMeshes.TryGetValue(meshName, out persistedMesh))
                {
                    EditorUtility.CopySerialized(mesh, persistedMesh);
                    persistedMesh.name = meshName;
                    Object.DestroyImmediate(mesh);
                }
                else
                {
                    persistedMesh = mesh;
                    persistedMesh.name = meshName;
                    AssetDatabase.AddObjectToAsset(persistedMesh, tileData);
                }

                if (ribbonRoot == null)
                {
                    GameObject ribbonRootObject = new GameObject(RibbonRootName);
                    ribbonRootObject.transform.SetParent(tile.transform, false);
                    ribbonRoot = ribbonRootObject.transform;
                }

                Transform child = ribbonRoot.Find(meshName);
                if (child == null)
                {
                    GameObject childObject = new GameObject(meshName, typeof(MeshFilter), typeof(MeshRenderer));
                    childObject.transform.SetParent(ribbonRoot, false);
                    child = childObject.transform;
                }

                child.GetComponent<MeshFilter>().sharedMesh = persistedMesh;
                child.GetComponent<MeshRenderer>().sharedMaterial = road.profile.ribbonStyle.material;
            }
        }

        foreach (KeyValuePair<string, Mesh> existing in existingMeshes)
        {
            if (!usedNames.Contains(existing.Key) && existing.Value.vertexCount > 0)
            {
                existing.Value.Clear();
                anythingChanged = true;
            }
        }

        if (ribbonRoot != null)
        {
            List<GameObject> obsoleteChildren = new List<GameObject>();
            foreach (Transform child in ribbonRoot)
            {
                if (!usedNames.Contains(child.name))
                {
                    obsoleteChildren.Add(child.gameObject);
                }
            }
            foreach (GameObject obsoleteChild in obsoleteChildren)
            {
                Object.DestroyImmediate(obsoleteChild);
                anythingChanged = true;
            }

            if (ribbonRoot.childCount == 0)
            {
                Object.DestroyImmediate(ribbonRoot.gameObject);
            }
        }

        if (anythingChanged)
        {
            EditorUtility.SetDirty(tileData);
            EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
            EditorSceneManager.SaveScene(tile.gameObject.scene);
        }

        return totalVertexCount;
    }

    private static List<List<Vector2>> CentrelinesOf(TerrainRoad road, Dictionary<TerrainRoad, List<List<Vector2>>> cache)
    {
        List<List<Vector2>> centrelines;
        if (cache.TryGetValue(road, out centrelines))
        {
            return centrelines;
        }

        centrelines = new List<List<Vector2>>();
        SplineContainer container = road.Container;
        for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
        {
            float length = container.CalculateLength(splineIndex);
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / SampleSpacingMetres));
            List<Vector2> points = new List<Vector2>(steps + 1);
            for (int step = 0; step <= steps; step++)
            {
                Unity.Mathematics.float3 position = container.EvaluatePosition(splineIndex, step / (float)steps);
                points.Add(new Vector2(position.x, position.z));
            }
            centrelines.Add(points);
        }

        cache[road] = centrelines;
        return centrelines;
    }

    private static List<Vector2> JunctionPointsOf(TerrainRoad road, int splineIndex,
        TerrainRoad[] allRoads, Dictionary<TerrainRoad, List<List<Vector2>>> centrelineCache)
    {
        List<Vector2> junctionPoints = null;
        Spline spline = road.Container.Splines[splineIndex];
        foreach (TerrainRoad other in allRoads)
        {
            if (other == road || other.profile == null || other.priority <= road.priority)
            {
                continue;
            }

            float halfWidth = other.EffectiveHalfWidthMetres;
            float halfWidthSquared = halfWidth * halfWidth;
            List<List<Vector2>> otherCentrelines = CentrelinesOf(other, centrelineCache);
            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                Vector3 worldKnot = road.Container.transform.TransformPoint((Vector3)(Unity.Mathematics.float3)spline[knotIndex].Position);
                Vector2 knotXZ = new Vector2(worldKnot.x, worldKnot.z);
                if (IsWithinDistanceOfAnyCentreline(knotXZ, otherCentrelines, halfWidthSquared))
                {
                    if (junctionPoints == null)
                    {
                        junctionPoints = new List<Vector2>();
                    }
                    junctionPoints.Add(knotXZ);
                }
            }
        }
        return junctionPoints;
    }

    private static bool IsWithinDistanceOfAnyCentreline(Vector2 point, List<List<Vector2>> centrelines, float distanceSquared)
    {
        foreach (List<Vector2> centreline in centrelines)
        {
            foreach (Vector2 sample in centreline)
            {
                if ((sample - point).sqrMagnitude <= distanceSquared)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
