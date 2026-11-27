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
    public const string WallsChildName = "TerraceWalls";
    public const string WallsMeshName = "TerraceWallsMesh";
    public const string RampMeshNamePrefix = "TerraceRampMesh_";
    public const string MaterialFolder = "Assets/PaintedGround/Materials/Terraces";

    const string CliffRibbonShaderName = "Painted/CliffEdgeRibbon";
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
                if (TerraceLevelField.ResolvedLevel(data, cellX, cellZ) != 0)
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
            footprints.Add(new TerraceRampFootprint(ramp.CentreXZ, ramp.YawDegrees, ramp.widthMetres, ramp.runMetres, lowerHeight, upperHeight, ramp.kind, ramp.risesToTheRight));
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
        TerrainWorldProfile world = TerrainWorldProfile.FindOrCreateInProject();
        float topRoundMetres = world != null ? world.terraceTopRoundMetres : 0.2f;
        float footRoundMetres = world != null ? world.terraceFootRoundMetres : 0.2f;

        // The tile mesh's submesh 1 (cliff profile) never draws on the GPU, in or out of a subscene; the tile renders
        // submesh 0 only, the profile renders from its own child mesh, and the tile collider keeps the combined mesh.
        if (tileRenderer != null)
            tileRenderer.sharedMaterials = new Material[] { groundMaterial };

        ApplyWallsChild(tile, tileMesh, wallMaterial, persist);
        List<TerraceOutlineChain> dressingChains = new List<TerraceOutlineChain>();
        // CE-D2: with tile cliff edges the ground shader draws rim and foot art, so only ramp walls keep the old lip dressing.
        if (chains != null && (world == null || !world.terraceUseTileCliffEdges))
            dressingChains.AddRange(chains);

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

                ApplyRampChild(tile, rampCount, footprint, groundMaterial, wallMaterial, topRoundMetres, footRoundMetres, persist);
                List<TerraceOutlineChain> rampChains = TerraceRampMeshBuilder.BuildSideWallOutlineChains(in footprint, tile.transform.position, topRoundMetres, footRoundMetres);
                if (rampChains != null)
                    dressingChains.AddRange(rampChains);
                rampCount++;
            }
        }

        ApplyDressingChild(tile, dressingChains, groundMaterial, persist);
        RemoveRampChildrenFrom(tile, rampCount, persist);
    }

    static void ApplyWallsChild(TerrainTile tile, Mesh tileMesh, Material wallMaterial, bool persist)
    {
        Transform existingChild = tile.transform.Find(WallsChildName);
        Mesh wallsMesh = BuildWallsMeshFromProfileSubMesh(tileMesh);
        if (wallsMesh == null)
        {
            if (existingChild != null)
                DestroyChildAndMeshes(existingChild.gameObject, persist);
            if (persist)
                RemoveSubAssetByName(tile.tileData, WallsMeshName);
            return;
        }

        Transform child = existingChild != null ? existingChild : CreateChild(tile.transform, WallsChildName);
        MeshFilter filter = GetOrAdd<MeshFilter>(child.gameObject);
        MeshRenderer meshRenderer = GetOrAdd<MeshRenderer>(child.gameObject);
        filter.sharedMesh = ResolveMeshForChild(tile, filter.sharedMesh, wallsMesh, WallsMeshName, persist);
        meshRenderer.sharedMaterials = new Material[] { wallMaterial };
    }

    // Compacts submesh 1 of the combined tile mesh into a standalone single-submesh mesh, keeping the float4 uv0.
    static Mesh BuildWallsMeshFromProfileSubMesh(Mesh tileMesh)
    {
        if (tileMesh == null || tileMesh.subMeshCount < 2)
            return null;
        int[] profileIndices = tileMesh.GetTriangles(1);
        if (profileIndices.Length == 0)
            return null;

        Vector3[] tilePositions = tileMesh.vertices;
        Vector3[] tileNormals = tileMesh.normals;
        List<Vector4> tileUvs = new List<Vector4>();
        tileMesh.GetUVs(0, tileUvs);
        bool hasNormals = tileNormals != null && tileNormals.Length == tilePositions.Length;
        bool hasUvs = tileUvs.Count == tilePositions.Length;

        Dictionary<int, int> tileToWallsVertex = new Dictionary<int, int>();
        List<Vector3> wallsPositions = new List<Vector3>();
        List<Vector3> wallsNormals = new List<Vector3>();
        List<Vector4> wallsUvs = new List<Vector4>();
        int[] wallsIndices = new int[profileIndices.Length];
        for (int indexPosition = 0; indexPosition < profileIndices.Length; indexPosition++)
        {
            int tileVertex = profileIndices[indexPosition];
            if (!tileToWallsVertex.TryGetValue(tileVertex, out int wallsVertex))
            {
                wallsVertex = wallsPositions.Count;
                tileToWallsVertex.Add(tileVertex, wallsVertex);
                wallsPositions.Add(tilePositions[tileVertex]);
                wallsNormals.Add(hasNormals ? tileNormals[tileVertex] : Vector3.up);
                wallsUvs.Add(hasUvs ? tileUvs[tileVertex] : Vector4.zero);
            }
            wallsIndices[indexPosition] = wallsVertex;
        }

        Mesh wallsMesh = new Mesh();
        wallsMesh.indexFormat = wallsPositions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        wallsMesh.SetVertices(wallsPositions);
        wallsMesh.SetNormals(wallsNormals);
        wallsMesh.SetUVs(0, wallsUvs);
        wallsMesh.SetTriangles(wallsIndices, 0);
        wallsMesh.RecalculateBounds();
        return wallsMesh;
    }

    static void ApplyDressingChild(TerrainTile tile, IReadOnlyList<TerraceOutlineChain> chains, Material groundMaterial, bool persist)
    {
        Transform existingChild = tile.transform.Find(DressingChildName);
        Mesh dressingMesh = null;
        Material lipMaterial = null;
        Material footMaterial = null;
        if (chains != null && chains.Count > 0 && tile.tileData != null)
        {
            lipMaterial = GetOrCreateRibbonMaterial("M_CliffRibbonLip.mat", 0f);
            footMaterial = GetOrCreateRibbonMaterial("M_CliffRibbonFoot.mat", 1f);
            if (lipMaterial != null && footMaterial != null)
            {
                Vector2 tileMin = tile.tileData.WorldMinCorner();
                Func<Vector2, int> edgeSetIndexAtWorldXZ = BuildEdgeSetSampler(tile, tileMin);
                dressingMesh = TerraceDressingBuilder.BuildLipAndFootMesh(chains, edgeSetIndexAtWorldXZ, tileMin);
            }
        }

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

    static Material GetOrCreateRibbonMaterial(string fileName, float ribbonSide)
    {
        string assetPath = MaterialFolder + "/" + fileName;
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null)
            return existing;

        Shader shader = Shader.Find(CliffRibbonShaderName);
        if (shader == null)
        {
            Debug.LogError("TerraceTileAssembly: shader '" + CliffRibbonShaderName + "' not found; cliff ribbon materials were not created.");
            return null;
        }

        EnsureMaterialFolderExists();
        Material material = new Material(shader);
        material.name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        material.SetFloat("_RibbonSide", ribbonSide);
        AssetDatabase.CreateAsset(material, assetPath);
        return material;
    }

    // Set per biome: Grass 0 (GrassOverDirt), Forest 1 (ForestOverGrass), Stone and Dirt 2 (StoneOverDirt); the tile's composite mask decides the biome.
    static Func<Vector2, int> BuildEdgeSetSampler(TerrainTile tile, Vector2 tileMin)
    {
        TerrainTileData tileData = tile.tileData;
        string baseMaskPath = tileData.baseMask != null ? AssetDatabase.GetAssetPath(tileData.baseMask) : null;
        if (string.IsNullOrEmpty(baseMaskPath))
            baseMaskPath = TerrainTileAssets.MaskPath(tile.tileCoordinate);
        if (string.IsNullOrEmpty(baseMaskPath) || !System.IO.File.Exists(baseMaskPath))
            return null;

        int resolution;
        Color32[] baseMask = TerrainSplatCodec.ReadPng(baseMaskPath, out resolution);
        Color32[] areaMask = TerrainCompositor.ReadAreaMask(tileData, resolution);
        Color32[] areaLayerColours = TerrainCompositor.AreaLayerColoursFor(tileData);
        Color32[] paintOverlay = ReadOverlayIfSameResolution(TerrainTileAssets.PaintOverlayPath(tile.tileCoordinate), resolution);
        Color32[] surfaceOverlay = ReadOverlayIfSameResolution(TerrainTileAssets.SurfaceOverlayPath(tile.tileCoordinate), resolution);
        Color32[] roadMask = ReadOverlayIfSameResolution(TerrainTileAssets.RoadMaskPath(tile.tileCoordinate), resolution);
        Color32[] composite = new Color32[resolution * resolution];
        TerrainCompositor.CompositeTexels(baseMask, areaMask, areaLayerColours, paintOverlay, surfaceOverlay, roadMask,
            resolution, new RectInt(0, 0, resolution, resolution), composite);

        return (Vector2 worldXZ) =>
        {
            int texelX = Mathf.Clamp(Mathf.FloorToInt((worldXZ.x - tileMin.x) / TerrainTileData.TileSizeMetres * resolution), 0, resolution - 1);
            int texelZ = Mathf.Clamp(Mathf.FloorToInt((worldXZ.y - tileMin.y) / TerrainTileData.TileSizeMetres * resolution), 0, resolution - 1);
            Color32 texel = composite[texelZ * resolution + texelX];
            if (texel.r >= texel.g && texel.r >= texel.b && texel.r > 127)
                return 0;
            if (texel.g >= texel.b && texel.g > 127)
                return 1;
            return 2;
        };
    }

    static Color32[] ReadOverlayIfSameResolution(string assetPath, int expectedResolution)
    {
        if (!System.IO.File.Exists(assetPath))
            return null;
        int resolution;
        Color32[] pixels = TerrainSplatCodec.ReadPng(assetPath, out resolution);
        return resolution == expectedResolution ? pixels : null;
    }

    static void ApplyRampChild(TerrainTile tile, int rampIndex, TerraceRampFootprint footprint, Material groundMaterial, Material wallMaterial, float topRoundMetres, float footRoundMetres, bool persist)
    {
        string childName = RampChildPrefix + rampIndex;
        Transform existingChild = tile.transform.Find(childName);
        Transform child = existingChild != null ? existingChild : CreateChild(tile.transform, childName);
        MeshFilter filter = GetOrAdd<MeshFilter>(child.gameObject);
        MeshRenderer meshRenderer = GetOrAdd<MeshRenderer>(child.gameObject);
        MeshCollider meshCollider = GetOrAdd<MeshCollider>(child.gameObject);

        Mesh rampMesh = TerraceRampMeshBuilder.BuildRampMesh(in footprint, tile.transform.position, topRoundMetres, footRoundMetres);
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
            TerrainMeshCopy.CopyGeometryInto(newMesh, persisted);
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

        Transform walls = tile.transform.Find(WallsChildName);
        if (walls != null)
            DestroyChildAndMeshes(walls.gameObject, persist);

        RemoveRampChildrenFrom(tile, 0, persist);

        if (persist)
        {
            RemoveSubAssetByName(tile.tileData, DressingMeshName);
            RemoveSubAssetByName(tile.tileData, WallsMeshName);
        }

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
