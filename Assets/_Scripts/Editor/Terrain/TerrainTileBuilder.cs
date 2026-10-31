#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns a tile's height data into the mesh and MeshCollider the tile renders and collides with.
public static class TerrainTileBuilder
{
    public const string TerrainMeshName = "TerrainMesh";

    public static Mesh BuildMeshInMemory(TerrainTile tile)
    {
        TerrainTileData tileData = tile.tileData != null ? tile.tileData : TerrainTileAssets.EnsureTileData(tile);
        MakeTransformTerrainReady(tile);

        Mesh newMesh = TerrainMeshBuilder.BuildTileMesh(
            tileData,
            1,
            LookUpNeighbourData,
            TerrainTileCreator.GroundTopHeight);

        MeshFilter meshFilter = tile.GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = tile.gameObject.AddComponent<MeshFilter>();

        Mesh existingMesh = meshFilter.sharedMesh;
        if (IsTerrainSubAssetOf(existingMesh, tile))
        {
            EditorUtility.CopySerialized(newMesh, existingMesh);
            Object.DestroyImmediate(newMesh);
            return existingMesh;
        }

        meshFilter.sharedMesh = newMesh;
        return newMesh;
    }

    public static void Rebuild(TerrainTile tile)
    {
        Mesh mesh = BuildMeshInMemory(tile);
        TerrainTileData tileData = tile.tileData != null ? tile.tileData : TerrainTileAssets.EnsureTileData(tile);
        MeshFilter meshFilter = tile.GetComponent<MeshFilter>();

        // A mesh that is not yet a sub-asset must be saved into the data asset or the subscene cannot bake it.
        if (!IsTerrainSubAssetOf(mesh, tile))
        {
            mesh.name = TerrainMeshName;
            Mesh persistedMesh = FindTerrainSubAsset(tile.tileCoordinate);
            if (persistedMesh != null)
            {
                EditorUtility.CopySerialized(mesh, persistedMesh);
                Object.DestroyImmediate(mesh);
                mesh = persistedMesh;
            }
            else
            {
                AssetDatabase.AddObjectToAsset(mesh, tileData);
            }
            meshFilter.sharedMesh = mesh;
        }

        EnsureMeshCollider(tile, mesh);

        tileData.BumpRevision();
        EditorUtility.SetDirty(tileData);
        EditorUtility.SetDirty(mesh);
        EditorUtility.SetDirty(tile);
        AssetDatabase.SaveAssetIfDirty(tileData);
        EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
    }

    [MenuItem("Stitch Punk/Terrain/Rebuild Selected Tile Mesh")]
    static void RebuildSelectedTile()
    {
        TerrainTile tile = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<TerrainTile>() : null;
        if (tile == null)
        {
            EditorUtility.DisplayDialog("Terrain", "Select a terrain tile.", "OK");
            return;
        }
        Rebuild(tile);
    }

    static TerrainTileData LookUpNeighbourData(Vector2Int coordinate)
    {
        TerrainTile neighbour = TerrainTileCreator.FindTileAt(coordinate);
        return neighbour != null ? neighbour.tileData : null;
    }

    static Mesh FindTerrainSubAsset(Vector2Int coordinate)
    {
        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(TerrainTileAssets.TileDataPath(coordinate));
        foreach (Object asset in allAssets)
        {
            Mesh candidate = asset as Mesh;
            if (candidate != null && candidate.name == TerrainMeshName) return candidate;
        }
        return null;
    }

    static bool IsTerrainSubAssetOf(Mesh mesh, TerrainTile tile)
    {
        if (mesh == null || mesh.name != TerrainMeshName) return false;
        if (!AssetDatabase.Contains(mesh)) return false;
        return AssetDatabase.GetAssetPath(mesh) == TerrainTileAssets.TileDataPath(tile.tileCoordinate);
    }

    static void EnsureMeshCollider(TerrainTile tile, Mesh mesh)
    {
        foreach (Collider existingCollider in tile.GetComponents<Collider>())
            if (!(existingCollider is MeshCollider)) Object.DestroyImmediate(existingCollider);

        MeshCollider meshCollider = tile.GetComponent<MeshCollider>();
        if (meshCollider == null) meshCollider = tile.gameObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = false;
    }

    // The adopted Ground cube carries a big scale; reset it without dragging children along.
    static void MakeTransformTerrainReady(TerrainTile tile)
    {
        Transform tileTransform = tile.transform;
        if (tileTransform.localScale == Vector3.one) return;

        List<Transform> children = new List<Transform>();
        List<Vector3> worldPositions = new List<Vector3>();
        List<Quaternion> worldRotations = new List<Quaternion>();
        List<Vector3> worldScales = new List<Vector3>();
        foreach (Transform child in tileTransform)
        {
            children.Add(child);
            worldPositions.Add(child.position);
            worldRotations.Add(child.rotation);
            worldScales.Add(child.lossyScale);
        }

        Vector2 centre = TerrainTile.CenterOfTile(tile.tileCoordinate);
        tileTransform.position = new Vector3(centre.x, 0f, centre.y);
        tileTransform.rotation = Quaternion.identity;
        tileTransform.localScale = Vector3.one;

        // With an identity parent, local scale equals the recorded lossy scale.
        for (int childIndex = 0; childIndex < children.Count; childIndex++)
        {
            children[childIndex].SetPositionAndRotation(worldPositions[childIndex], worldRotations[childIndex]);
            children[childIndex].localScale = worldScales[childIndex];
        }
    }
}
#endif
