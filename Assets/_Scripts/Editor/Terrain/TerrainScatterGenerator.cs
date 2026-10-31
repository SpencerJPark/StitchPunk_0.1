#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TerrainScatterGenerator
{
    private const string ScatterChildName = "Scatter";
    private const string NavigationWallLayerName = "PathfindingWalls";

    public static List<TerrainScatterRule> RulesFrom(TerrainScatterSet set)
    {
        List<TerrainScatterRule> rules = new List<TerrainScatterRule>();
        if (set == null || set.rules == null)
        {
            return rules;
        }
        for (int ruleIndex = 0; ruleIndex < set.rules.Count; ruleIndex++)
        {
            TerrainScatterRuleData data = set.rules[ruleIndex];
            TerrainScatterRule rule = new TerrainScatterRule();
            rule.onDirt = data.onDirt;
            rule.onGrass = data.onGrass;
            rule.onForest = data.onForest;
            rule.onStone = data.onStone;
            rule.slopeRangeDegrees = data.slopeRangeDegrees;
            rule.heightRange = data.heightRange;
            rule.minSpacingMetres = data.minSpacingMetres;
            rule.scaleRange = data.scaleRange;
            rule.yawJitterDegrees = data.yawJitterDegrees;
            rule.densityPer100SquareMetres = data.densityPer100SquareMetres;
            rule.clusterNoiseScale = data.clusterNoiseScale;
            rule.clusterThreshold = data.clusterThreshold;
            rule.alignToNormal = data.alignToNormal;
            rules.Add(rule);
        }
        return rules;
    }

    public static int GenerateWithSampler(Transform scatterParent, TerrainScatterSet set, ITerrainScatterSampler sampler, Rect worldRectXZ, int seed)
    {
        if (scatterParent == null || sampler == null || set == null || set.rules == null || set.rules.Count == 0)
        {
            Debug.LogWarning("TerrainScatterGenerator: nothing to scatter (missing parent, sampler or empty set).");
            return 0;
        }

        List<TerrainScatterObstacle> obstacles = new List<TerrainScatterObstacle>();
        for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
        {
            Transform child = scatterParent.GetChild(childIndex);
            TerrainScatterMarker marker = child.GetComponent<TerrainScatterMarker>();
            if (marker == null || !marker.locked)
            {
                continue;
            }
            TerrainScatterObstacle obstacle = new TerrainScatterObstacle();
            obstacle.position = child.position;
            obstacle.radius = marker.footprintRadiusMetres;
            obstacles.Add(obstacle);
        }

        List<TerrainScatterRule> rules = RulesFrom(set);
        List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(rules, sampler, worldRectXZ, seed, obstacles);

        int navigationLayer = -1;
        bool warnedAboutLayer = false;
        int created = 0;
        for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
        {
            TerrainScatterPlacement placement = placements[placementIndex];
            System.Random pickRandom = new System.Random(unchecked(seed * 73856093) ^ (placementIndex * 19349663));
            GameObject prefab = set.PickPrefab(placement.ruleIndex, (float)pickRandom.NextDouble());
            if (prefab == null)
            {
                continue;
            }

            TerrainScatterRuleData ruleData = set.rules[placement.ruleIndex];
            GameObject instance = InstantiateScatterInstance(scatterParent, prefab, placement, ruleData);
            if (instance == null)
            {
                continue;
            }

            if (ruleData.blocksNavigation)
            {
                if (navigationLayer < 0)
                {
                    navigationLayer = LayerMask.NameToLayer(NavigationWallLayerName);
                }
                if (navigationLayer >= 0)
                {
                    SetLayerRecursively(instance, navigationLayer);
                }
                else if (!warnedAboutLayer)
                {
                    warnedAboutLayer = true;
                    Debug.LogWarning("TerrainScatterGenerator: layer '" + NavigationWallLayerName + "' not found; navigation-blocking props keep their layer.");
                }
            }
            created++;
        }
        return created;
    }

    public static int Generate(TerrainTile tile, TerrainScatterSet set, int seed, Rect? worldRegion)
    {
        if (tile == null)
        {
            return 0;
        }
        Transform scatterParent = GetOrCreateScatterParent(tile);
        TerrainTileSampler sampler = new TerrainTileSampler(tile);
        Rect rect = sampler.WorldRect;
        if (worldRegion.HasValue)
        {
            Rect region = worldRegion.Value;
            float xMin = Mathf.Max(rect.xMin, region.xMin);
            float yMin = Mathf.Max(rect.yMin, region.yMin);
            float xMax = Mathf.Min(rect.xMax, region.xMax);
            float yMax = Mathf.Min(rect.yMax, region.yMax);
            rect = new Rect(xMin, yMin, Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
        }

        int created = GenerateWithSampler(scatterParent, set, sampler, rect, TerrainDecalScatterer.SeedFor(seed, tile.tileCoordinate));
        EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
        return created;
    }

    public static int ClearUnlocked(Transform scatterParent)
    {
        if (scatterParent == null)
        {
            return 0;
        }
        List<GameObject> doomed = new List<GameObject>();
        for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
        {
            Transform child = scatterParent.GetChild(childIndex);
            TerrainScatterMarker marker = child.GetComponent<TerrainScatterMarker>();
            if (marker != null && !marker.locked)
            {
                doomed.Add(child.gameObject);
            }
        }
        for (int doomedIndex = 0; doomedIndex < doomed.Count; doomedIndex++)
        {
            Undo.DestroyObjectImmediate(doomed[doomedIndex]);
        }
        return doomed.Count;
    }

    public static int LockAll(Transform scatterParent)
    {
        if (scatterParent == null)
        {
            return 0;
        }
        int changed = 0;
        for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
        {
            TerrainScatterMarker marker = scatterParent.GetChild(childIndex).GetComponent<TerrainScatterMarker>();
            if (marker == null || marker.locked)
            {
                continue;
            }
            Undo.RecordObject(marker, "Lock scatter");
            marker.locked = true;
            EditorUtility.SetDirty(marker);
            changed++;
        }
        return changed;
    }

    public static int Regenerate(TerrainTile tile, TerrainScatterSet set, int seed)
    {
        if (tile == null)
        {
            return 0;
        }
        ClearUnlocked(GetOrCreateScatterParent(tile));
        return Generate(tile, set, seed, null);
    }

    public static GameObject PlaceOne(TerrainTile tile, TerrainScatterSet set, int ruleIndex, Vector2 worldXZ, System.Random random)
    {
        if (tile == null || set == null || set.rules == null || random == null || ruleIndex < 0 || ruleIndex >= set.rules.Count || tile.tileData == null)
        {
            return null;
        }
        GameObject prefab = set.PickPrefab(ruleIndex, (float)random.NextDouble());
        if (prefab == null)
        {
            return null;
        }

        TerrainScatterRuleData ruleData = set.rules[ruleIndex];
        TerrainScatterPlacement placement = new TerrainScatterPlacement();
        placement.ruleIndex = ruleIndex;
        placement.position = new Vector3(worldXZ.x, tile.tileData.SampleBilinear(worldXZ.x, worldXZ.y), worldXZ.y);
        placement.normal = Vector3.up;
        placement.yawDegrees = (float)(random.NextDouble() * 2.0 - 1.0) * ruleData.yawJitterDegrees;
        placement.scale = Mathf.Lerp(ruleData.scaleRange.x, ruleData.scaleRange.y, (float)random.NextDouble());

        GameObject instance = InstantiateScatterInstance(GetOrCreateScatterParent(tile), prefab, placement, ruleData);
        if (instance == null)
        {
            return null;
        }
        instance.GetComponent<TerrainScatterMarker>().locked = true;
        Undo.RegisterCreatedObjectUndo(instance, "Place scatter");
        EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
        return instance;
    }

    public static int EraseInRadius(TerrainTile tile, Vector2 worldXZ, float radiusMetres, bool includeLocked)
    {
        if (tile == null)
        {
            return 0;
        }
        Transform scatterParent = tile.transform.Find(ScatterChildName);
        if (scatterParent == null)
        {
            return 0;
        }
        float radiusSquared = radiusMetres * radiusMetres;
        List<GameObject> doomed = new List<GameObject>();
        for (int childIndex = 0; childIndex < scatterParent.childCount; childIndex++)
        {
            Transform child = scatterParent.GetChild(childIndex);
            TerrainScatterMarker marker = child.GetComponent<TerrainScatterMarker>();
            if (marker == null || (marker.locked && !includeLocked))
            {
                continue;
            }
            Vector2 childXZ = new Vector2(child.position.x, child.position.z);
            if ((childXZ - worldXZ).sqrMagnitude <= radiusSquared)
            {
                doomed.Add(child.gameObject);
            }
        }
        for (int doomedIndex = 0; doomedIndex < doomed.Count; doomedIndex++)
        {
            Undo.DestroyObjectImmediate(doomed[doomedIndex]);
        }
        return doomed.Count;
    }

    private static GameObject InstantiateScatterInstance(Transform scatterParent, GameObject prefab, TerrainScatterPlacement placement, TerrainScatterRuleData ruleData)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scatterParent.gameObject.scene) as GameObject;
        if (instance == null)
        {
            return null;
        }
        Vector3 prefabScale = prefab.transform.localScale;
        instance.transform.SetParent(scatterParent, true);
        Quaternion yaw = Quaternion.Euler(0f, placement.yawDegrees, 0f);
        Quaternion rotation = ruleData.alignToNormal ? Quaternion.FromToRotation(Vector3.up, placement.normal) * yaw : yaw;
        instance.transform.SetPositionAndRotation(placement.position, rotation);
        instance.transform.localScale = prefabScale * placement.scale;
        instance.name = prefab.name;

        TerrainScatterMarker marker = instance.AddComponent<TerrainScatterMarker>();
        marker.locked = false;
        marker.ruleIndex = placement.ruleIndex;
        marker.footprintRadiusMetres = ruleData.minSpacingMetres * 0.5f;
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
        Undo.RegisterCreatedObjectUndo(created, "Create scatter parent");
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
