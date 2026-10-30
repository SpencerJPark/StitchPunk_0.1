#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// Stitch Punk > Terrain > Create Tile / Adopt Selected Ground As Tile
public static class TerrainTileCreator
{
    // Top face of every tile sits here, matching the original CampLoop Ground
    public const float GroundTopHeight = -0.03f;
    const string GroundLayerName = "Ground";

    public static TerrainTile FindTileAt(Vector2Int coordinate)
    {
        foreach (TerrainTile existingTile in Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
            if (existingTile.tileCoordinate == coordinate) return existingTile;
        return null;
    }

    public static Vector2Int FirstFreeNeighbourOfExistingTiles()
    {
        Vector2Int[] neighbourOffsets = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        foreach (TerrainTile existingTile in Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
        {
            foreach (Vector2Int offset in neighbourOffsets)
            {
                Vector2Int candidate = existingTile.tileCoordinate + offset;
                if (FindTileAt(candidate) == null) return candidate;
            }
        }
        return Vector2Int.zero;
    }

    public static TerrainTile CreateTile(Vector2Int coordinate, TerrainSplatLayer fill)
    {
        TerrainTile existingTile = FindTileAt(coordinate);
        if (existingTile != null)
        {
            Debug.LogWarning("[Terrain] A tile already exists at " + coordinate + ": " + existingTile.name, existingTile);
            return existingTile;
        }

        GameObject tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(tileObject, "Create Terrain Tile");
        tileObject.name = "Ground_Tile_" + coordinate.x + "_" + coordinate.y;
        Vector2 center = TerrainTile.CenterOfTile(coordinate);
        tileObject.transform.position = new Vector3(center.x, GroundTopHeight - 0.5f, center.y);
        tileObject.transform.localScale = new Vector3(TerrainTile.TileSizeMetres, 1f, TerrainTile.TileSizeMetres);
        int groundLayer = LayerMask.NameToLayer(GroundLayerName);
        if (groundLayer >= 0) tileObject.layer = groundLayer;
        tileObject.isStatic = true;

        TerrainTile tile = tileObject.AddComponent<TerrainTile>();
        tile.tileCoordinate = coordinate;
        TerrainTileAssets.EnsureAssets(tile, fill);
        Selection.activeGameObject = tileObject;
        return tile;
    }

    [MenuItem("Stitch Punk/Terrain/Create Tile...")]
    static void OpenCreateTileWindow()
    {
        TerrainTileCreatorWindow.Open();
    }

    // Turns an existing ground object (collider + mesh) into a tile on the grid
    [MenuItem("Stitch Punk/Terrain/Adopt Selected Ground As Tile")]
    static void AdoptSelectedGround()
    {
        GameObject groundObject = Selection.activeGameObject;
        if (groundObject == null || groundObject.GetComponent<MeshRenderer>() == null)
        {
            EditorUtility.DisplayDialog("Terrain", "Select a ground object with a MeshRenderer.", "OK");
            return;
        }

        TerrainTile tile = groundObject.GetComponent<TerrainTile>();
        if (tile == null) tile = Undo.AddComponent<TerrainTile>(groundObject);

        Vector2Int coordinate = TerrainTile.CoordinateContaining(groundObject.transform.position);
        TerrainTile occupant = FindTileAt(coordinate);
        if (occupant != null && occupant != tile)
        {
            EditorUtility.DisplayDialog("Terrain", "Tile " + coordinate + " is already " + occupant.name + ".", "OK");
            return;
        }

        Undo.RecordObject(groundObject.transform, "Adopt Ground As Tile");
        Vector2 center = TerrainTile.CenterOfTile(coordinate);
        groundObject.transform.position = new Vector3(center.x, groundObject.transform.position.y, center.y);
        tile.tileCoordinate = coordinate;
        TerrainTileAssets.EnsureAssets(tile, TerrainSplatLayer.Grass);
        EditorSceneDirty(groundObject);
    }

    static void EditorSceneDirty(GameObject tileObject)
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tileObject.scene);
    }
}

public class TerrainTileCreatorWindow : EditorWindow
{
    public static void Open()
    {
        TerrainTileCreatorWindow window = GetWindow<TerrainTileCreatorWindow>(true, "Create Terrain Tile");
        window.minSize = new Vector2(320f, 110f);
    }

    void CreateGUI()
    {
        Vector2IntField coordinateField = new Vector2IntField("Tile (x, z)");
        coordinateField.value = TerrainTileCreator.FirstFreeNeighbourOfExistingTiles();
        EnumField fillField = new EnumField("Fill", TerrainSplatLayer.Grass);
        Button createButton = new Button(() =>
        {
            TerrainTileCreator.CreateTile(coordinateField.value, (TerrainSplatLayer)fillField.value);
            Close();
        });
        createButton.text = "Create Tile";
        createButton.style.height = 28f;
        createButton.style.marginTop = 8f;

        rootVisualElement.style.paddingLeft = 8f;
        rootVisualElement.style.paddingRight = 8f;
        rootVisualElement.style.paddingTop = 8f;
        rootVisualElement.Add(coordinateField);
        rootVisualElement.Add(fillField);
        rootVisualElement.Add(createButton);
    }
}
#endif
