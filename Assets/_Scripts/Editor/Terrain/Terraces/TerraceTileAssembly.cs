#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerraceTileAssembly
{
    public const string DressingChildName = "TerraceDressing";
    public const string RampChildPrefix = "TerraceRamp_";
    public const string DressingMeshName = "TerraceDressingMesh";
    public const string RampMeshNamePrefix = "TerraceRampMesh_";
    public const string MaterialFolder = "Assets/PaintedGround/Materials/Terraces";

    const float DressingFringeSizeMetres = 0.8f;
    const float RampOverlapMarginMetres = 1f;
    const string CliffShaderName = "Painted/Cliff";
    const string WallTexturePath = "Assets/PaintedGround/Textures/Layers/T_Cliff.png";
    const string LipTexturePath = "Assets/PaintedGround/Textures/Edges/T_EdgeFringe.png";

    static readonly Vector2Int[] NeighbourOffsets =
    {
        new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        new Vector2Int(-1, 0), new Vector2Int(1, 0),
        new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
    };

    public static bool TileNeedsTerracedPath(TerrainTileData data, Vector2Int tileCoordinate, Func<Vector2Int, TerrainTileData> neighbourLookup)
    {
        if (TileDataHasAnyTerrace(data))
            return true;

        if (neighbourLookup == null)
            return false;

        for (int offsetIndex = 0; offsetIndex < NeighbourOffsets.Length; offsetIndex++)
        {
            TerrainTileData neighbour = neighbourLookup(tileCoordinate + NeighbourOffsets[offsetIndex]);
            if (TileDataHasAnyTerrace(neighbour))
                return true;
        }
        return false;
    }

    static bool TileDataHasAnyTerrace(TerrainTileData data)
    {
        if (data == null || (data.baseTerraceLevels == null && data.handTerraceLevels == null))
            return false;

        for (int cellZ = 0; cellZ < TerrainTileData.TerraceCellsPerSide; cellZ++)
        {
            for (int cellX = 0; cellX < TerrainTileData.TerraceCellsPerSide; cellX++)
            {
                if (TerraceLevelField.ResolvedLevel(data, cellX, cellZ) > 0)
                    return true;
            }
        }
        return false;
    }

    public static List<TerraceRampFootprint> CollectRampFootprints(TerrainTileData data, Vector2Int tileCoordinate, TerrainWorldProfile world)
    {
        List<TerraceRampFootprint> footprints = new List<TerraceRampFootprint>();
        if (data == null || world == null)
            return footprints;

        Vector2 tileMin = data.WorldMinCorner();
        Vector2 expandedMin = tileMin - new Vector2(RampOverlapMarginMetres, RampOverlapMarginMetres);
        Vector2 expandedMax = tileMin + new Vector2(TerrainTileData.TileSizeMetres, TerrainTileData.TileSizeMetres)
            + new Vector2(RampOverlapMarginMetres, RampOverlapMarginMetres);
        Vector2[] corners = new Vector2[4];

        TerraceRamp[] ramps = UnityEngine.Object.FindObjectsByType<TerraceRamp>(FindObjectsInactive.Exclude);
        for (int rampIndex = 0; rampIndex < ramps.Length; rampIndex++)
        {
            TerraceRamp ramp = ramps[rampIndex];
            if (!ramp.enabled)
                continue;

            ramp.GetFootprintCornersXZ(corners);
            Vector2 cornerMin = corners[0];
            Vector2 cornerMax = corners[0];
            for (int cornerIndex = 1; cornerIndex < 4; cornerIndex++)
            {
                cornerMin = Vector2.Min(cornerMin, corners[cornerIndex]);
                cornerMax = Vector2.Max(cornerMax, corners[cornerIndex]);
            }
            if (cornerMax.x < expandedMin.x || cornerMin.x > expandedMax.x
                || cornerMax.y < expandedMin.y || cornerMin.y > expandedMax.y)
                continue;

            float yawRadians = ramp.YawDegrees * Mathf.Deg2Rad;
            Vector2 forward = new Vector2(Mathf.Sin(yawRadians), Mathf.Cos(yawRadians));
            Vector2 lowerEndCentre = ramp.CentreXZ - forward * (ramp.runMetres * 0.5f);
            Vector2 upperEndCentre = ramp.CentreXZ + forward * (ramp.runMetres * 0.5f);
            float lowerHeight = data.SampleBilinear(lowerEndCentre.x, lowerEndCentre.y)
                + TerrainTileData.GroundTopHeightMetres + ramp.lowerLevel * world.terraceLevelHeightMetres;
            float upperHeight = data.SampleBilinear(upperEndCentre.x, upperEndCentre.y)
                + TerrainTileData.GroundTopHeightMetres + (ramp.lowerLevel + 1) * world.terraceLevelHeightMetres;
            footprints.Add(new TerraceRampFootprint(ramp.CentreXZ, ramp.YawDegrees, ramp.widthMetres, ramp.runMetres, lowerHeight, upperHeight));
        }
        return footprints;
    }

    public static Material GetOrCreateWallMaterial()
    {
        return GetOrCreateMaterial("M_TerraceWall.mat", 0, 1, 0, 1, WallTexturePath, 2000, false);
    }

    public static Material GetOrCreateLipMaterial()
    {
        return GetOrCreateMaterial("M_TerraceLip.mat", 1, 1, 0, 1, LipTexturePath, 2450, false);
    }

    public static Material GetOrCreateFootMaterial()
    {
        return GetOrCreateMaterial("M_TerraceFoot.mat", 2, 5, 10, 0, null, 3000, true);
    }

    static Material GetOrCreateMaterial(
        string fileName, int mode, int sourceBlend, int destinationBlend, int depthWrite,
        string surfaceTexturePath, int renderQueue, bool transparent)
    {
        string assetPath = MaterialFolder + "/" + fileName;
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null)
            return existing;

        Shader shader = Shader.Find(CliffShaderName);
        if (shader == null)
        {
            Debug.LogError("TerraceTileAssembly: shader '" + CliffShaderName + "' not found; terrace materials were not created.");
            return null;
        }

        EnsureMaterialFolderExists();
        Material material = new Material(shader);
        material.name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        material.SetFloat("_Mode", mode);
        material.SetFloat("_SrcBlend", sourceBlend);
        material.SetFloat("_DstBlend", destinationBlend);
        material.SetFloat("_ZWrite", depthWrite);
        material.SetFloat("_Cull", 0f);
        if (surfaceTexturePath != null)
        {
            Texture surfaceTexture = AssetDatabase.LoadAssetAtPath<Texture>(surfaceTexturePath);
            if (surfaceTexture != null)
                material.SetTexture("_SurfaceTex", surfaceTexture);
        }
        material.renderQueue = renderQueue;
        if (transparent)
            material.SetOverrideTag("RenderType", "Transparent");

        AssetDatabase.CreateAsset(material, assetPath);
        return material;
    }

    static void EnsureMaterialFolderExists()
    {
        string[] segments = MaterialFolder.Split('/');
        string current = segments[0];
        for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
        {
            string next = current + "/" + segments[segmentIndex];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[segmentIndex]);
            current = next;
        }
    }

    public static void ApplyTerraceChildren(TerrainTile tile, Mesh tileMesh, IReadOnlyList<TerraceOutlineChain> chains, IReadOnlyList<TerraceRampFootprint> ramps, bool persist)
    {
        if (tile == null)
            return;

        MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
        Material groundMaterial = null;
        if (tileRenderer != null && tileRenderer.sharedMaterials.Length > 0)
            groundMaterial = tileRenderer.sharedMaterials[0];

        Material wallMaterial = GetOrCreateWallMaterial();
        Material lipMaterial = GetOrCreateLipMaterial();
        Material footMaterial = GetOrCreateFootMaterial();

        if (tileRenderer != null)
        {
            bool tileHasWallSubMesh = tileMesh != null && tileMesh.subMeshCount >= 2;
            tileRenderer.sharedMaterials = tileHasWallSubMesh
                ? new Material[] { groundMaterial, wallMaterial }
                : new Material[] { groundMaterial };
        }

        ApplyDressingChild(tile, chains, lipMaterial, footMaterial, persist);

        int rampCount = 0;
        Vector2 tileMin = Vector2.zero;
        Vector2 tileMax = Vector2.zero;
        if (tile.tileData != null)
        {
            tileMin = tile.tileData.WorldMinCorner();
            tileMax = tileMin + new Vector2(TerrainTileData.TileSizeMetres, TerrainTileData.TileSizeMetres);
        }

        if (ramps != null)
        {
            for (int footprintIndex = 0; footprintIndex < ramps.Count; footprintIndex++)
            {
                TerraceRampFootprint footprint = ramps[footprintIndex];
                bool centreInsideTile = footprint.centre.x >= tileMin.x && footprint.centre.x < tileMax.x
                    && footprint.centre.y >= tileMin.y && footprint.centre.y < tileMax.y;
                if (!centreInsideTile)
                    continue;

                ApplyRampChild(tile, rampCount, footprint, groundMaterial, wallMaterial, persist);
                rampCount++;
            }
        }

        RemoveRampChildrenFrom(tile, rampCount, persist);
    }

    static void ApplyDressingChild(TerrainTile tile, IReadOnlyList<TerraceOutlineChain> chains, Material lipMaterial, Material footMaterial, bool persist)
    {
        Transform existingChild = tile.transform.Find(DressingChildName);
        Mesh dressingMesh = null;
        if (chains != null && chains.Count > 0)
            dressingMesh = TerraceDressingBuilder.BuildLipAndFootMesh(chains, DressingFringeSizeMetres);

        if (dressingMesh == null)
        {
            if (existingChild != null)
                DestroyChildAndMeshes(existingChild.gameObject, persist);
            if (persist)
                RemoveSubAssetByName(tile.tileData, DressingMeshName);
            return;
        }

        Transform child = existingChild != null ? existingChild : CreateChild(tile.transform, DressingChildName);
        MeshFilter filter = GetOrAdd<MeshFilter>(child.gameObject);
        MeshRenderer meshRenderer = GetOrAdd<MeshRenderer>(child.gameObject);
        Collider[] colliders = child.GetComponents<Collider>();
        for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            UnityEngine.Object.DestroyImmediate(colliders[colliderIndex]);

        filter.sharedMesh = ResolveMeshForChild(tile, filter.sharedMesh, dressingMesh, DressingMeshName, persist);
        meshRenderer.sharedMaterials = new Material[] { lipMaterial, footMaterial };
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void ApplyRampChild(TerrainTile tile, int rampIndex, TerraceRampFootprint footprint, Material groundMaterial, Material wallMaterial, bool persist)
    {
        string childName = RampChildPrefix + rampIndex;
        Transform existingChild = tile.transform.Find(childName);
        Transform child = existingChild != null ? existingChild : CreateChild(tile.transform, childName);
        MeshFilter filter = GetOrAdd<MeshFilter>(child.gameObject);
        MeshRenderer meshRenderer = GetOrAdd<MeshRenderer>(child.gameObject);
        MeshCollider meshCollider = GetOrAdd<MeshCollider>(child.gameObject);

        Mesh rampMesh = TerraceRampMeshBuilder.BuildRampMesh(in footprint, tile.transform.position);
        Mesh resolved = ResolveMeshForChild(tile, filter.sharedMesh, rampMesh, RampMeshNamePrefix + rampIndex, persist);
        filter.sharedMesh = resolved;
        meshRenderer.sharedMaterials = new Material[] { groundMaterial, wallMaterial };
        meshCollider.convex = false;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = resolved;
    }

    static Mesh ResolveMeshForChild(TerrainTile tile, Mesh previousMesh, Mesh newMesh, string meshName, bool persist)
    {
        if (newMesh == null)
            return previousMesh;

        if (!persist)
        {
            if (previousMesh != null && !AssetDatabase.Contains(previousMesh))
                UnityEngine.Object.DestroyImmediate(previousMesh);
            newMesh.name = meshName;
            return newMesh;
        }

        Mesh persisted = FindSubAssetMesh(tile.tileData, meshName);
        if (persisted != null)
        {
            EditorUtility.CopySerialized(newMesh, persisted);
            persisted.name = meshName;
            UnityEngine.Object.DestroyImmediate(newMesh);
        }
        else
        {
            persisted = newMesh;
            persisted.name = meshName;
            AssetDatabase.AddObjectToAsset(persisted, tile.tileData);
        }
        EditorUtility.SetDirty(persisted);
        return persisted;
    }

    static Mesh FindSubAssetMesh(TerrainTileData tileData, string meshName)
    {
        if (tileData == null)
            return null;

        UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(tileData));
        for (int assetIndex = 0; assetIndex < subAssets.Length; assetIndex++)
        {
            Mesh mesh = subAssets[assetIndex] as Mesh;
            if (mesh != null && mesh.name == meshName)
                return mesh;
        }
        return null;
    }

    static void RemoveSubAssetByName(TerrainTileData tileData, string meshName)
    {
        Mesh stale = FindSubAssetMesh(tileData, meshName);
        if (stale == null)
            return;

        AssetDatabase.RemoveObjectFromAsset(stale);
        UnityEngine.Object.DestroyImmediate(stale);
    }

    static void RemoveStaleRampSubAssets(TerrainTileData tileData, int keepCount)
    {
        if (tileData == null)
            return;

        UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(tileData));
        for (int assetIndex = 0; assetIndex < subAssets.Length; assetIndex++)
        {
            Mesh mesh = subAssets[assetIndex] as Mesh;
            if (mesh == null || !mesh.name.StartsWith(RampMeshNamePrefix, StringComparison.Ordinal))
                continue;

            int meshIndex;
            string suffix = mesh.name.Substring(RampMeshNamePrefix.Length);
            if (int.TryParse(suffix, out meshIndex) && meshIndex >= keepCount)
            {
                AssetDatabase.RemoveObjectFromAsset(mesh);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    static void RemoveRampChildrenFrom(TerrainTile tile, int keepCount, bool persist)
    {
        List<GameObject> doomed = new List<GameObject>();
        for (int childIndex = 0; childIndex < tile.transform.childCount; childIndex++)
        {
            Transform child = tile.transform.GetChild(childIndex);
            if (!child.name.StartsWith(RampChildPrefix, StringComparison.Ordinal))
                continue;

            int rampIndex;
            if (!int.TryParse(child.name.Substring(RampChildPrefix.Length), out rampIndex) || rampIndex >= keepCount)
                doomed.Add(child.gameObject);
        }
        for (int doomedIndex = 0; doomedIndex < doomed.Count; doomedIndex++)
            DestroyChildAndMeshes(doomed[doomedIndex], persist);

        if (persist)
            RemoveStaleRampSubAssets(tile.tileData, keepCount);
    }

    public static void RemoveTerraceChildren(TerrainTile tile, bool persist)
    {
        if (tile == null)
            return;

        Transform dressing = tile.transform.Find(DressingChildName);
        if (dressing != null)
            DestroyChildAndMeshes(dressing.gameObject, persist);

        RemoveRampChildrenFrom(tile, 0, persist);

        if (persist)
            RemoveSubAssetByName(tile.tileData, DressingMeshName);

        MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
        if (tileRenderer != null && tileRenderer.sharedMaterials.Length > 1)
            tileRenderer.sharedMaterials = new Material[] { tileRenderer.sharedMaterials[0] };
    }

    static void DestroyChildAndMeshes(GameObject child, bool persist)
    {
        MeshFilter filter = child.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null && !AssetDatabase.Contains(filter.sharedMesh))
            UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
        UnityEngine.Object.DestroyImmediate(child);
    }

    static Transform CreateChild(Transform parent, string childName)
    {
        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        childObject.transform.localPosition = Vector3.zero;
        childObject.transform.localRotation = Quaternion.identity;
        childObject.transform.localScale = Vector3.one;
        return childObject.transform;
    }

    static ComponentType GetOrAdd<ComponentType>(GameObject target) where ComponentType : Component
    {
        ComponentType existing = target.GetComponent<ComponentType>();
        return existing != null ? existing : target.AddComponent<ComponentType>();
    }
}
#endif
