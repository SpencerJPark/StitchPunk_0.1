#if UNITY_EDITOR
using System.Collections.Generic;
using PaintedGround;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TerrainDecalScatterer
{
    private const string DecalsChildName = "Decals";
    private const float DecalDepthMetres = 1.5f;
    private const string ProfilesFolder = "Assets/Terrain/Profiles";
    private const string DefaultDecalSetPath = ProfilesFolder + "/DefaultDecalSet.asset";
    private const string AtlasPath = "Assets/PaintedGround/Textures/Decals/T_DecalAtlas.png";
    private const string MultiplyMaterialPath = "Assets/PaintedGround/Materials/M_Decal_Multiply.mat";
    private const string NormalMaterialPath = "Assets/PaintedGround/Materials/M_Decal_Normal.mat";

    public static int SeedFor(int baseSeed, Vector2Int tileCoordinate)
    {
        return baseSeed * 73856093 ^ tileCoordinate.x * 19349663 ^ tileCoordinate.y * 83492791;
    }

    public static List<TerrainScatterRule> RulesFrom(TerrainDecalSet set)
    {
        List<TerrainScatterRule> rules = new List<TerrainScatterRule>();
        foreach (TerrainDecalEntry entry in set.entries)
        {
            TerrainScatterRule rule = new TerrainScatterRule();
            rule.onDirt = entry.onDirt;
            rule.onGrass = entry.onGrass;
            rule.onForest = entry.onForest;
            rule.onStone = entry.onStone;
            rule.slopeRangeDegrees = new Vector2(0f, entry.maxSlopeDegrees);
            rule.heightRange = new Vector2(-1000f, 5000f);
            rule.minSpacingMetres = entry.sizeRangeMetres.y * 0.6f;
            rule.scaleRange = entry.sizeRangeMetres;
            rule.yawJitterDegrees = 360f;
            rule.densityPer100SquareMetres = entry.densityPer100SquareMetres;
            rule.clusterNoiseScale = 0f;
            rule.clusterThreshold = 0f;
            rule.alignToNormal = false;
            rules.Add(rule);
        }
        return rules;
    }

    public static int ScatterWithSampler(Transform decalParent, TerrainDecalSet set, ITerrainScatterSampler sampler, Rect worldRectXZ, int seed)
    {
        if (!HasUsableAssets(set))
        {
            return 0;
        }

        List<TerrainScatterObstacle> obstacles = new List<TerrainScatterObstacle>();
        for (int childIndex = 0; childIndex < decalParent.childCount; childIndex++)
        {
            Transform child = decalParent.GetChild(childIndex);
            TerrainDecalMarker marker = child.GetComponent<TerrainDecalMarker>();
            if (marker != null && marker.locked)
            {
                TerrainScatterObstacle obstacle = new TerrainScatterObstacle();
                obstacle.position = child.position;
                obstacle.radius = child.localScale.x * 0.5f;
                obstacles.Add(obstacle);
            }
        }

        List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(RulesFrom(set), sampler, worldRectXZ, seed, obstacles);
        System.Random opacityRandom = new System.Random(seed ^ 0x5bd1e995);
        Mesh cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        int created = 0;
        for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
        {
            TerrainScatterPlacement placement = placements[placementIndex];
            TerrainDecalEntry entry = set.entries[placement.ruleIndex];
            float opacityBlend = (float)opacityRandom.NextDouble();
            CreateDecal(set, entry, cubeMesh, decalParent, placement.position, placement.yawDegrees, placement.scale,
                Mathf.Lerp(entry.opacityRange.x, entry.opacityRange.y, opacityBlend), placement.ruleIndex, false);
            created++;
        }
        return created;
    }

    public static int Scatter(TerrainTile tile, TerrainDecalSet set, int seed, Rect? worldRegion)
    {
        Transform decalParent = FindOrCreateChild(tile.transform, DecalsChildName);
        TerrainTileSampler sampler = new TerrainTileSampler(tile);
        Rect rect = sampler.WorldRect;
        if (worldRegion.HasValue)
        {
            Rect region = worldRegion.Value;
            float minX = Mathf.Max(rect.xMin, region.xMin);
            float minZ = Mathf.Max(rect.yMin, region.yMin);
            float maxX = Mathf.Min(rect.xMax, region.xMax);
            float maxZ = Mathf.Min(rect.yMax, region.yMax);
            rect = new Rect(minX, minZ, Mathf.Max(0f, maxX - minX), Mathf.Max(0f, maxZ - minZ));
        }

        int created = ScatterWithSampler(decalParent, set, sampler, rect, SeedFor(seed, tile.tileCoordinate));
        EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
        return created;
    }

    public static int ClearUnlocked(Transform decalParent)
    {
        List<GameObject> doomed = new List<GameObject>();
        for (int childIndex = 0; childIndex < decalParent.childCount; childIndex++)
        {
            Transform child = decalParent.GetChild(childIndex);
            TerrainDecalMarker marker = child.GetComponent<TerrainDecalMarker>();
            bool shouldDestroy = marker != null ? !marker.locked : child.GetComponent<PaintedDecal>() != null;
            if (shouldDestroy)
            {
                doomed.Add(child.gameObject);
            }
        }
        foreach (GameObject decalObject in doomed)
        {
            Undo.DestroyObjectImmediate(decalObject);
        }
        return doomed.Count;
    }

    public static int LockAll(Transform decalParent)
    {
        int lockedCount = 0;
        TerrainDecalMarker[] markers = decalParent.GetComponentsInChildren<TerrainDecalMarker>(true);
        foreach (TerrainDecalMarker marker in markers)
        {
            Undo.RecordObject(marker, "Lock Decals");
            marker.locked = true;
            lockedCount++;
        }
        return lockedCount;
    }

    public static int Regenerate(TerrainTile tile, TerrainDecalSet set, int seed)
    {
        Transform decalParent = FindOrCreateChild(tile.transform, DecalsChildName);
        ClearUnlocked(decalParent);
        return Scatter(tile, set, seed, null);
    }

    public static GameObject PlaceOne(TerrainTile tile, TerrainDecalSet set, int entryIndex, Vector2 worldXZ, System.Random random)
    {
        if (!HasUsableAssets(set) || entryIndex < 0 || entryIndex >= set.entries.Count)
        {
            return null;
        }

        TerrainDecalEntry entry = set.entries[entryIndex];
        Transform decalParent = FindOrCreateChild(tile.transform, DecalsChildName);
        float height = tile.tileData != null ? tile.tileData.SampleBilinear(worldXZ.x, worldXZ.y) : 0f;
        float size = Mathf.Lerp(entry.sizeRangeMetres.x, entry.sizeRangeMetres.y, (float)random.NextDouble());
        float opacity = Mathf.Lerp(entry.opacityRange.x, entry.opacityRange.y, (float)random.NextDouble());
        float yaw = (float)random.NextDouble() * 360f;
        Mesh cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        GameObject decalObject = CreateDecal(set, entry, cubeMesh, decalParent, new Vector3(worldXZ.x, height, worldXZ.y), yaw, size, opacity, entryIndex, true);
        Undo.RegisterCreatedObjectUndo(decalObject, "Place Decal");
        return decalObject;
    }

    public static int EraseInRadius(TerrainTile tile, Vector2 worldXZ, float radiusMetres, bool includeLocked)
    {
        Transform decalParent = tile.transform.Find(DecalsChildName);
        if (decalParent == null)
        {
            return 0;
        }

        List<GameObject> doomed = new List<GameObject>();
        for (int childIndex = 0; childIndex < decalParent.childCount; childIndex++)
        {
            Transform child = decalParent.GetChild(childIndex);
            Vector2 childXZ = new Vector2(child.position.x, child.position.z);
            if (Vector2.Distance(childXZ, worldXZ) > radiusMetres)
            {
                continue;
            }
            TerrainDecalMarker marker = child.GetComponent<TerrainDecalMarker>();
            if (marker != null && marker.locked && !includeLocked)
            {
                continue;
            }
            doomed.Add(child.gameObject);
        }
        foreach (GameObject decalObject in doomed)
        {
            Undo.DestroyObjectImmediate(decalObject);
        }
        return doomed.Count;
    }

    public static TerrainDecalSet CreateDefaultDecalSetAsset()
    {
        TerrainDecalSet existing = AssetDatabase.LoadAssetAtPath<TerrainDecalSet>(DefaultDecalSetPath);
        if (existing != null)
        {
            return existing;
        }

        EnsureFolder("Assets/Terrain");
        EnsureFolder(ProfilesFolder);
        TerrainDecalSet set = ScriptableObject.CreateInstance<TerrainDecalSet>();
        set.atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        set.atlasGrid = new Vector2Int(4, 2);
        set.multiplyMaterial = AssetDatabase.LoadAssetAtPath<Material>(MultiplyMaterialPath);
        set.normalMaterial = AssetDatabase.LoadAssetAtPath<Material>(NormalMaterialPath);
        set.FillWithDefaultRules();
        AssetDatabase.CreateAsset(set, DefaultDecalSetPath);
        AssetDatabase.SaveAssets();
        return set;
    }

    private static GameObject CreateDecal(TerrainDecalSet set, TerrainDecalEntry entry, Mesh cubeMesh, Transform decalParent, Vector3 worldPosition, float yawDegrees, float sizeMetres, float opacity, int entryId, bool locked)
    {
        Material material = entry.multiplyBlend ? set.multiplyMaterial : set.normalMaterial;
        PaintedDecal decal = PaintedDecal.Create(material, cubeMesh, worldPosition, yawDegrees, sizeMetres, DecalDepthMetres, entry.atlasTile, decalParent, "Decal_" + entry.label);
        decal.transform.position = worldPosition;
        decal.opacity = opacity;
        decal.tint = entry.tint;
        decal.Apply();
        TerrainDecalMarker marker = decal.gameObject.AddComponent<TerrainDecalMarker>();
        marker.locked = locked;
        marker.entryId = entryId;
        return decal.gameObject;
    }

    private static bool HasUsableAssets(TerrainDecalSet set)
    {
        if (set != null && set.atlas != null && set.multiplyMaterial != null && set.normalMaterial != null)
        {
            return true;
        }
        Debug.LogWarning("TerrainDecalScatterer: decal set, atlas or materials are missing; nothing scattered.");
        return false;
    }

    private static Transform FindOrCreateChild(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            return existing;
        }
        GameObject created = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(created, "Create " + childName);
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }
        int separatorIndex = folderPath.LastIndexOf('/');
        AssetDatabase.CreateFolder(folderPath.Substring(0, separatorIndex), folderPath.Substring(separatorIndex + 1));
    }
}
#endif
