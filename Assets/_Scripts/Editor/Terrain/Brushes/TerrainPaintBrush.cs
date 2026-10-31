#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainPaintBrush
{
    const int MaximumUndoEntries = 32;
    const string SplatMapPropertyName = "_SplatMap";

    class PaintSession
    {
        public TerrainTile tile;
        public TerrainTileData tileData;
        public int resolution;
        public Color32[] baseMask;
        public Color32[] overlay;
        public Color32[] overlaySnapshot;
        public Color32[] roadMask;
        public Color32[] composite;
        public Color32[] signedDistanceField;
        public Texture2D liveSignedDistanceField;
        public RectInt strokeRect;
        public bool hasStrokeRect;
        public int revisionBefore;
    }

    class PaintUndoEntry
    {
        public Vector2Int coordinate;
        public int revisionBefore;
        public int revisionAfter;
        public RectInt rect;
        public Color32[] beforePixels;
        public Color32[] afterPixels;
    }

    static readonly Dictionary<Vector2Int, PaintSession> sessionsByCoordinate = new Dictionary<Vector2Int, PaintSession>();
    static readonly List<PaintUndoEntry> undoEntries = new List<PaintUndoEntry>();

    static TerrainPaintBrush()
    {
        TerrainBrushOverlay.BrushStroke += OnBrushStroke;
        TerrainBrushOverlay.BrushStrokeEnded += OnBrushStrokeEnded;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        AssemblyReloadEvents.beforeAssemblyReload += DropAllSessions;
    }

    static void OnBrushStroke(BrushHit hit, bool isDragging)
    {
        if (BrushState.activeTool != TerrainBrushTool.Paint) return;
        if (!isDragging) DropAllSessions();

        for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int coordinate = new Vector2Int(hit.tileCoordinate.x + offsetX, hit.tileCoordinate.y + offsetZ);
                if (!BrushOverlapsTile(hit.worldPoint, coordinate)) continue;
                PaintSession session = GetOrCreateSession(coordinate);
                if (session == null) continue;
                StampDisc(session, hit.worldPoint);
            }
        }
    }

    static Vector2 TileMinCorner(Vector2Int coordinate)
    {
        return new Vector2(coordinate.x * TerrainTileData.TileSizeMetres - 50f, coordinate.y * TerrainTileData.TileSizeMetres - 50f);
    }

    static bool BrushOverlapsTile(Vector3 worldPoint, Vector2Int coordinate)
    {
        Vector2 minCorner = TileMinCorner(coordinate);
        float nearestX = Mathf.Clamp(worldPoint.x, minCorner.x, minCorner.x + TerrainTileData.TileSizeMetres);
        float nearestZ = Mathf.Clamp(worldPoint.z, minCorner.y, minCorner.y + TerrainTileData.TileSizeMetres);
        float distance = Vector2.Distance(new Vector2(worldPoint.x, worldPoint.z), new Vector2(nearestX, nearestZ));
        return distance <= BrushState.radiusMetres;
    }

    static PaintSession GetOrCreateSession(Vector2Int coordinate)
    {
        PaintSession session;
        if (sessionsByCoordinate.TryGetValue(coordinate, out session)) return session;

        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile == null) return null;
        if (tile.tileData == null) TerrainTileAssets.EnsureTileData(tile);
        if (tile.tileData == null) return null;
        string maskPath = TerrainTileAssets.MaskPath(coordinate);
        if (!File.Exists(maskPath)) return null;

        session = new PaintSession();
        session.tile = tile;
        session.tileData = tile.tileData;
        session.baseMask = TerrainSplatCodec.ReadPng(maskPath, out session.resolution);
        session.overlay = ReadSourceOrNull(TerrainTileAssets.PaintOverlayPath(coordinate), session.resolution);
        if (session.overlay == null) session.overlay = new Color32[session.resolution * session.resolution];
        session.overlaySnapshot = (Color32[])session.overlay.Clone();
        session.roadMask = ReadSourceOrNull(TerrainTileAssets.RoadMaskPath(coordinate), session.resolution);

        RectInt fullRect = new RectInt(0, 0, session.resolution, session.resolution);
        session.composite = new Color32[session.resolution * session.resolution];
        TerrainCompositor.CompositeTexels(session.baseMask, session.overlay, session.roadMask, session.resolution, fullRect, session.composite);
        session.signedDistanceField = TerrainSplatCodec.EncodeSignedDistanceField(session.composite, session.resolution, TerrainTileData.TileSizeMetres);

        // The imported SDF is BC7 and cannot be edited, so the live path samples its own texture
        session.liveSignedDistanceField = new Texture2D(session.resolution, session.resolution, TextureFormat.RGBA32, false, true);
        session.liveSignedDistanceField.wrapMode = TextureWrapMode.Clamp;
        session.liveSignedDistanceField.filterMode = FilterMode.Bilinear;
        session.liveSignedDistanceField.hideFlags = HideFlags.HideAndDontSave;
        session.liveSignedDistanceField.SetPixels32(session.signedDistanceField);
        session.liveSignedDistanceField.Apply(false);
        MeshRenderer tileRenderer = session.tile.GetComponent<MeshRenderer>();
        if (tileRenderer != null && tileRenderer.sharedMaterial != null)
            tileRenderer.sharedMaterial.SetTexture(SplatMapPropertyName, session.liveSignedDistanceField);

        Undo.RecordObject(session.tileData, "Paint Terrain");
        session.revisionBefore = session.tileData.revision;
        sessionsByCoordinate[coordinate] = session;
        return session;
    }

    static Color32[] ReadSourceOrNull(string assetPath, int expectedResolution)
    {
        if (!File.Exists(assetPath)) return null;
        int sourceResolution;
        Color32[] pixels = TerrainSplatCodec.ReadPng(assetPath, out sourceResolution);
        return sourceResolution == expectedResolution ? pixels : null;
    }

    static float HashNoise(int texelX, int texelY, Vector2Int coordinate)
    {
        uint hash = (uint)texelX * 73856093u ^ (uint)texelY * 19349663u ^ (uint)coordinate.x * 83492791u ^ (uint)coordinate.y * 2654435761u;
        hash ^= hash >> 13;
        hash *= 1274126177u;
        hash ^= hash >> 16;
        return (hash & 0xFFFFu) / 65535f;
    }

    static void StampDisc(PaintSession session, Vector3 worldPoint)
    {
        Vector2 minCorner = TileMinCorner(session.tileData.coordinate);
        float texelSizeMetres = TerrainTileData.TileSizeMetres / session.resolution;
        float radius = BrushState.radiusMetres;
        float innerRadius = radius * BrushState.hardness;
        Color32 paintColour = TerrainSplatCodec.ColourOfLayer(BrushState.paintLayer);
        paintColour.a = 255;

        int searchMinX = Mathf.Max(Mathf.FloorToInt((worldPoint.x - radius - minCorner.x) / texelSizeMetres), 0);
        int searchMinY = Mathf.Max(Mathf.FloorToInt((worldPoint.z - radius - minCorner.y) / texelSizeMetres), 0);
        int searchMaxX = Mathf.Min(Mathf.CeilToInt((worldPoint.x + radius - minCorner.x) / texelSizeMetres), session.resolution - 1);
        int searchMaxY = Mathf.Min(Mathf.CeilToInt((worldPoint.z + radius - minCorner.y) / texelSizeMetres), session.resolution - 1);

        int changedMinX = int.MaxValue;
        int changedMinY = int.MaxValue;
        int changedMaxX = int.MinValue;
        int changedMaxY = int.MinValue;
        for (int texelY = searchMinY; texelY <= searchMaxY; texelY++)
        {
            for (int texelX = searchMinX; texelX <= searchMaxX; texelX++)
            {
                float centreX = minCorner.x + (texelX + 0.5f) * texelSizeMetres;
                float centreZ = minCorner.y + (texelY + 0.5f) * texelSizeMetres;
                float distance = Vector2.Distance(new Vector2(centreX, centreZ), new Vector2(worldPoint.x, worldPoint.z));
                if (distance > radius) continue;
                if (distance > innerRadius)
                {
                    float falloff = 1f - (distance - innerRadius) / Mathf.Max(radius - innerRadius, 0.0001f);
                    if (HashNoise(texelX, texelY, session.tileData.coordinate) >= falloff) continue;
                }

                int texelIndex = texelY * session.resolution + texelX;
                if (SameColour(session.overlay[texelIndex], paintColour)) continue;
                session.overlay[texelIndex] = paintColour;
                changedMinX = Mathf.Min(changedMinX, texelX);
                changedMinY = Mathf.Min(changedMinY, texelY);
                changedMaxX = Mathf.Max(changedMaxX, texelX);
                changedMaxY = Mathf.Max(changedMaxY, texelY);
            }
        }
        if (changedMaxX < changedMinX) return;

        RectInt dirtyRect = new RectInt(changedMinX, changedMinY, changedMaxX - changedMinX + 1, changedMaxY - changedMinY + 1);
        session.strokeRect = session.hasStrokeRect ? UnionOf(session.strokeRect, dirtyRect) : dirtyRect;
        session.hasStrokeRect = true;

        TerrainCompositor.CompositeTexels(session.baseMask, session.overlay, session.roadMask, session.resolution, dirtyRect, session.composite);
        TerrainSplatCodec.EncodeSignedDistanceFieldRegion(session.composite, session.resolution, TerrainTileData.TileSizeMetres, dirtyRect, session.signedDistanceField);
        session.liveSignedDistanceField.SetPixels32(session.signedDistanceField);
        session.liveSignedDistanceField.Apply(false);
    }

    static bool SameColour(Color32 first, Color32 second)
    {
        return first.r == second.r && first.g == second.g && first.b == second.b && first.a == second.a;
    }

    static RectInt UnionOf(RectInt first, RectInt second)
    {
        int minimumX = Mathf.Min(first.xMin, second.xMin);
        int minimumY = Mathf.Min(first.yMin, second.yMin);
        int maximumX = Mathf.Max(first.xMax, second.xMax);
        int maximumY = Mathf.Max(first.yMax, second.yMax);
        return new RectInt(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
    }

    static Color32[] ExtractRect(Color32[] source, int resolution, RectInt rect)
    {
        Color32[] extracted = new Color32[rect.width * rect.height];
        for (int rowIndex = 0; rowIndex < rect.height; rowIndex++)
            System.Array.Copy(source, (rect.yMin + rowIndex) * resolution + rect.xMin, extracted, rowIndex * rect.width, rect.width);
        return extracted;
    }

    static void InsertRect(Color32[] destination, int resolution, RectInt rect, Color32[] pixels)
    {
        for (int rowIndex = 0; rowIndex < rect.height; rowIndex++)
            System.Array.Copy(pixels, rowIndex * rect.width, destination, (rect.yMin + rowIndex) * resolution + rect.xMin, rect.width);
    }

    static void OnBrushStrokeEnded()
    {
        foreach (KeyValuePair<Vector2Int, PaintSession> pair in sessionsByCoordinate)
        {
            PaintSession session = pair.Value;
            if (session.hasStrokeRect) FinishTouchedSession(pair.Key, session);
            else UnityEngine.Object.DestroyImmediate(session.liveSignedDistanceField);
            RestoreRealSignedDistanceField(session);
        }
        sessionsByCoordinate.Clear();
    }

    static void FinishTouchedSession(Vector2Int coordinate, PaintSession session)
    {
        session.tileData.BumpRevision();
        PaintUndoEntry entry = new PaintUndoEntry();
        entry.coordinate = coordinate;
        entry.revisionBefore = session.revisionBefore;
        entry.revisionAfter = session.tileData.revision;
        entry.rect = session.strokeRect;
        entry.beforePixels = ExtractRect(session.overlaySnapshot, session.resolution, session.strokeRect);
        entry.afterPixels = ExtractRect(session.overlay, session.resolution, session.strokeRect);
        undoEntries.Add(entry);
        if (undoEntries.Count > MaximumUndoEntries) undoEntries.RemoveAt(0);

        WriteOverlayAndComposite(session.tile, session.tileData, session.overlay, session.resolution);
        UnityEngine.Object.DestroyImmediate(session.liveSignedDistanceField);
    }

    // Composite rewrites the SDF PNG from disk and puts the real SDF asset back on the material
    static void WriteOverlayAndComposite(TerrainTile tile, TerrainTileData tileData, Color32[] overlay, int resolution)
    {
        string overlayPath = TerrainTileAssets.PaintOverlayPath(tile.tileCoordinate);
        TerrainSplatCodec.WritePng(overlayPath, overlay, resolution, false);
        tileData.paintOverlay = AssetDatabase.LoadAssetAtPath<Texture2D>(overlayPath);
        TerrainCompositor.Composite(tile);
        EditorUtility.SetDirty(tile);
        EditorUtility.SetDirty(tileData);
        if (tile.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);
    }

    static void RestoreRealSignedDistanceField(PaintSession session)
    {
        MeshRenderer tileRenderer = session.tile != null ? session.tile.GetComponent<MeshRenderer>() : null;
        if (tileRenderer == null || tileRenderer.sharedMaterial == null || session.tile.splatSignedDistanceField == null) return;
        tileRenderer.sharedMaterial.SetTexture(SplatMapPropertyName, session.tile.splatSignedDistanceField);
    }

    static void DropAllSessions()
    {
        foreach (PaintSession session in sessionsByCoordinate.Values)
        {
            RestoreRealSignedDistanceField(session);
            if (session.liveSignedDistanceField != null) UnityEngine.Object.DestroyImmediate(session.liveSignedDistanceField);
        }
        sessionsByCoordinate.Clear();
    }

    static void OnUndoRedoPerformed()
    {
        if (sessionsByCoordinate.Count > 0) return;
        foreach (PaintUndoEntry entry in undoEntries)
        {
            TerrainTile tile = TerrainTileCreator.FindTileAt(entry.coordinate);
            if (tile == null || tile.tileData == null) continue;
            int revision = tile.tileData.revision;
            if (revision == entry.revisionBefore) ApplyPixelsToTile(tile, entry, entry.beforePixels);
            else if (revision == entry.revisionAfter) ApplyPixelsToTile(tile, entry, entry.afterPixels);
        }
    }

    static void ApplyPixelsToTile(TerrainTile tile, PaintUndoEntry entry, Color32[] targetPixels)
    {
        string maskPath = TerrainTileAssets.MaskPath(entry.coordinate);
        if (!File.Exists(maskPath)) return;
        int resolution;
        TerrainSplatCodec.ReadPng(maskPath, out resolution);
        if (entry.rect.xMax > resolution || entry.rect.yMax > resolution) return;

        Color32[] overlay = ReadSourceOrNull(TerrainTileAssets.PaintOverlayPath(entry.coordinate), resolution);
        if (overlay == null) overlay = new Color32[resolution * resolution];
        Color32[] current = ExtractRect(overlay, resolution, entry.rect);
        bool alreadyMatches = true;
        for (int pixelIndex = 0; pixelIndex < current.Length && alreadyMatches; pixelIndex++)
            alreadyMatches = SameColour(current[pixelIndex], targetPixels[pixelIndex]);
        if (alreadyMatches) return;

        InsertRect(overlay, resolution, entry.rect, targetPixels);
        WriteOverlayAndComposite(tile, tile.tileData, overlay, resolution);
    }
}
#endif
