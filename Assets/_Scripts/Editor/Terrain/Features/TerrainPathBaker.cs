#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

// Places along-path features (fence posts, segment spans) into a tile; runs after TerrainAreaFiller.FillTile on the same tile.
public static class TerrainPathBaker
{
    private const string ScatterChildName = "Scatter";
    private const string NavigationWallLayerName = "PathfindingWalls";
    private const float MaximumChordMetres = 2f;
    private const float FallbackPrefabLengthMetres = 1f;
    private const int MinimumKnotCount = 2;

    public static int BakeTile(TerrainTile tile, IReadOnlyList<TerrainPath> allPaths)
    {
        TerrainTileData tileData = tile.tileData;
        if (tileData == null)
        {
            return 0;
        }

        int placedCount = 0;
        Rect tileRect = new Rect(
            tile.tileCoordinate.x * TerrainTileData.TileSizeMetres - 50f,
            tile.tileCoordinate.y * TerrainTileData.TileSizeMetres - 50f,
            TerrainTileData.TileSizeMetres,
            TerrainTileData.TileSizeMetres);

        using (TerrainGeneratedLockWatcher.SuppressLocking())
        {
            Transform scatterParent = GetOrCreateScatterParent(tile);
            ClearUnlockedPathInstances(scatterParent, tileData);

            int interactableUsed = CountMarkers(scatterParent);
            int capRemaining = TerrainPlatformProfile.Active.maxInteractablePropsPerTile - interactableUsed;
            int navigationLayer = LayerMask.NameToLayer(NavigationWallLayerName);
            bool skippedDressing = false;
            bool skippedForCap = false;

            for (int pathIndex = 0; pathIndex < allPaths.Count; pathIndex++)
            {
                TerrainPath path = allPaths[pathIndex];
                if (path == null || path.recipe == null)
                {
                    continue;
                }

                TerrainAlongPathPlacer placer = path.recipe.placer as TerrainAlongPathPlacer;
                if (placer == null)
                {
                    continue;
                }

                List<int> edgeStartKnotIndices = new List<int>();
                List<Vector2> polylineLocalXZ = BuildLocalPolyline(path, edgeStartKnotIndices);
                if (polylineLocalXZ.Count < 2)
                {
                    continue;
                }

                List<int> gapEdgeIndices = new List<int>();
                for (int edgeIndex = 0; edgeIndex < edgeStartKnotIndices.Count; edgeIndex++)
                {
                    if (path.gapKnotIndices.Contains(edgeStartKnotIndices[edgeIndex]))
                    {
                        gapEdgeIndices.Add(edgeIndex);
                    }
                }

                List<TerrainPlacementResult> results = TerrainFeaturePlacement.PlanAlongPath(
                    polylineLocalXZ, path.closedLoop, gapEdgeIndices, placer, path.recipe.placeables.Count, path.seed);

                float prefabLengthZ = placer.segmentPrefab != null ? MeasurePrefabLengthZ(placer.segmentPrefab) : FallbackPrefabLengthMetres;
                float pathYaw = path.transform.eulerAngles.y;

                for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                {
                    TerrainPlacementResult result = results[resultIndex];
                    Vector3 startWorldFlat = path.transform.TransformPoint(new Vector3(result.localXZ.x, 0f, result.localXZ.y));
                    Vector2 startWorldXZ = new Vector2(startWorldFlat.x, startWorldFlat.z);
                    if (!TerrainFeaturePlacement.IsOwnedByTile(startWorldXZ, tileRect))
                    {
                        continue;
                    }

                    float startHeight = tileData.SampleBilinear(startWorldXZ.x, startWorldXZ.y) + TerrainTileData.GroundTopHeightMetres;
                    Vector3 startPoint = new Vector3(startWorldXZ.x, startHeight, startWorldXZ.y);

                    if (result.isSegment)
                    {
                        if (placer.segmentPrefab == null)
                        {
                            continue;
                        }

                        if (capRemaining <= 0)
                        {
                            skippedForCap = true;
                            continue;
                        }

                        Vector3 endWorldFlat = path.transform.TransformPoint(new Vector3(result.segmentEndLocalXZ.x, 0f, result.segmentEndLocalXZ.y));
                        // The end post may lie in another tile whose heights this tile data cannot sample, so it reuses the start height there.
                        float endHeight = tileRect.Contains(new Vector2(endWorldFlat.x, endWorldFlat.z))
                            ? tileData.SampleBilinear(endWorldFlat.x, endWorldFlat.z) + TerrainTileData.GroundTopHeightMetres
                            : startHeight;
                        Vector3 endPoint = new Vector3(endWorldFlat.x, endHeight, endWorldFlat.z);
                        Vector3 direction = endPoint - startPoint;
                        float chordLength = direction.magnitude;
                        if (chordLength < 0.001f)
                        {
                            continue;
                        }

                        Vector3 prefabScale = placer.segmentPrefab.transform.localScale;
                        prefabScale.z *= chordLength / prefabLengthZ;
                        GameObject segmentInstance = InstantiateMarkedInstance(
                            scatterParent, placer.segmentPrefab, (startPoint + endPoint) * 0.5f,
                            Quaternion.LookRotation(direction), prefabScale, -1, path.featureId);
                        if (segmentInstance != null)
                        {
                            placedCount++;
                            capRemaining--;
                        }

                        continue;
                    }

                    TerrainPlaceable placeable = path.recipe.placeables[result.placeableIndex];
                    if (placeable.tier == TerrainScatterTier.Dressing)
                    {
                        skippedDressing = true;
                        continue;
                    }

                    if (placeable.prefab == null)
                    {
                        continue;
                    }

                    if (capRemaining <= 0)
                    {
                        skippedForCap = true;
                        continue;
                    }

                    GameObject instance = InstantiateMarkedInstance(
                        scatterParent, placeable.prefab, startPoint,
                        Quaternion.Euler(0f, result.yawDegrees + pathYaw, 0f),
                        placeable.prefab.transform.localScale * result.scale, result.placeableIndex, path.featureId);
                    if (instance == null)
                    {
                        continue;
                    }

                    if (placeable.blocksNavigation && navigationLayer >= 0)
                    {
                        SetLayerRecursively(instance, navigationLayer);
                    }

                    placedCount++;
                    capRemaining--;
                }
            }

            if (skippedDressing)
            {
                Debug.LogWarning("TerrainPathBaker: cards on paths arrive with TerrainCardSource; dressing placeables skipped on tile " + tile.tileCoordinate);
            }

            if (skippedForCap)
            {
                Debug.LogWarning("TerrainPathBaker: interactable cap reached on tile " + tile.tileCoordinate + "; some path placements were skipped");
            }
        }

        return placedCount;
    }

    private static void ClearUnlockedPathInstances(Transform scatterParent, TerrainTileData tileData)
    {
        HashSet<string> areaFeatureIds = new HashSet<string>();
        if (tileData.areaEntries != null)
        {
            for (int entryIndex = 0; entryIndex < tileData.areaEntries.Count; entryIndex++)
            {
                areaFeatureIds.Add(tileData.areaEntries[entryIndex].featureId);
            }
        }

        List<GameObject> doomed = new List<GameObject>();
        for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
        {
            Transform child = scatterParent.GetChild(childIndex);
            TerrainScatterMarker marker = child.GetComponent<TerrainScatterMarker>();
            if (marker != null
                && !string.IsNullOrEmpty(marker.sourceFeatureId)
                && !marker.locked
                && !areaFeatureIds.Contains(marker.sourceFeatureId))
            {
                doomed.Add(child.gameObject);
            }
        }

        for (int doomedIndex = 0; doomedIndex < doomed.Count; doomedIndex++)
        {
            Object.DestroyImmediate(doomed[doomedIndex]);
        }
    }

    private static int CountMarkers(Transform scatterParent)
    {
        int markerCount = 0;
        for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
        {
            if (scatterParent.GetChild(childIndex).GetComponent<TerrainScatterMarker>() != null)
            {
                markerCount++;
            }
        }

        return markerCount;
    }

    // Polyline in the path transform's local XZ; a closed loop does not repeat its first point. edgeStartKnotIndices gets one entry per edge.
    private static List<Vector2> BuildLocalPolyline(TerrainPath path, List<int> edgeStartKnotIndices)
    {
        List<Vector2> polyline = new List<Vector2>();
        SplineContainer container = path.Container;
        if (container == null || container.Splines.Count == 0 || container.Splines[0].Count < MinimumKnotCount)
        {
            return polyline;
        }

        Spline spline = new Spline(container.Splines[0]);
        spline.Closed = path.closedLoop;
        float worldScale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(path.transform.lossyScale.x), Mathf.Abs(path.transform.lossyScale.z)));
        int curveCount = path.closedLoop ? spline.Count : spline.Count - 1;

        for (int curveIndex = 0; curveIndex < curveCount; curveIndex++)
        {
            BezierCurve curve = spline.GetCurve(curveIndex);
            float curveLengthMetres = CurveUtility.ApproximateLength(curve) * worldScale;
            int chordCount = Mathf.Max(1, Mathf.CeilToInt(curveLengthMetres / MaximumChordMetres));
            for (int chordIndex = 0; chordIndex < chordCount; chordIndex++)
            {
                float3 point = CurveUtility.EvaluatePosition(curve, chordIndex / (float)chordCount);
                polyline.Add(new Vector2(point.x, point.z));
                edgeStartKnotIndices.Add(curveIndex);
            }
        }

        if (!path.closedLoop)
        {
            float3 lastPoint = CurveUtility.EvaluatePosition(spline.GetCurve(curveCount - 1), 1f);
            polyline.Add(new Vector2(lastPoint.x, lastPoint.z));
        }

        return polyline;
    }

    // Mesh bounds in the prefab root's own space at scale 1; Renderer.bounds on a prefab asset is not reliable.
    private static float MeasurePrefabLengthZ(GameObject prefab)
    {
        MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        Matrix4x4 rootWorldToLocal = prefab.transform.worldToLocalMatrix;
        bool hasBounds = false;
        Bounds combined = new Bounds();
        for (int filterIndex = 0; filterIndex < meshFilters.Length; filterIndex++)
        {
            Mesh mesh = meshFilters[filterIndex].sharedMesh;
            if (mesh == null)
            {
                continue;
            }

            Matrix4x4 meshToRoot = rootWorldToLocal * meshFilters[filterIndex].transform.localToWorldMatrix;
            Bounds meshBounds = mesh.bounds;
            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                    (cornerIndex & 1) == 0 ? -1f : 1f,
                    (cornerIndex & 2) == 0 ? -1f : 1f,
                    (cornerIndex & 4) == 0 ? -1f : 1f));
                Vector3 cornerInRoot = meshToRoot.MultiplyPoint3x4(corner);
                if (!hasBounds)
                {
                    combined = new Bounds(cornerInRoot, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(cornerInRoot);
                }
            }
        }

        if (!hasBounds || combined.size.z < 0.001f)
        {
            return FallbackPrefabLengthMetres;
        }

        return combined.size.z;
    }

    private static GameObject InstantiateMarkedInstance(
        Transform scatterParent,
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector3 localScale,
        int placeableIndex,
        string sourceFeatureId)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scatterParent.gameObject.scene) as GameObject;
        if (instance == null)
        {
            return null;
        }

        instance.transform.SetParent(scatterParent, true);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = localScale;
        instance.name = prefab.name;

        TerrainScatterMarker marker = instance.GetComponent<TerrainScatterMarker>();
        if (marker == null)
        {
            marker = instance.AddComponent<TerrainScatterMarker>();
        }

        marker.locked = false;
        marker.ruleIndex = placeableIndex;
        marker.sourceFeatureId = sourceFeatureId;
        return instance;
    }

    private static Transform GetOrCreateScatterParent(TerrainTile tile)
    {
        Transform existing = tile.transform.Find(ScatterChildName);
        if (existing != null)
        {
            return existing;
        }

        GameObject created = new GameObject(ScatterChildName);
        created.transform.SetParent(tile.transform, false);
        return created.transform;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int childIndex = 0; childIndex < root.transform.childCount; childIndex++)
        {
            SetLayerRecursively(root.transform.GetChild(childIndex).gameObject, layer);
        }
    }
}
#endif
