using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class TerraceRamp : MonoBehaviour
{
    // Tile (0,0) is centred on the origin, so tile edges sit at -50 + n * TileSizeMetres (see TerrainTileData.WorldMinCorner).
    private const float TileOriginOffsetMetres = 50f;

    [Min(0.5f)]
    public float widthMetres = 6f;

    [Min(0.5f)]
    public float runMetres = 9f;

    [Min(0)]
    public int lowerLevel;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private float lastWidthMetres;
    private float lastRunMetres;

    public Vector2 CentreXZ => new Vector2(transform.position.x, transform.position.z);

    public float YawDegrees => transform.eulerAngles.y;

    public void GetFootprintCornersXZ(Vector2[] fourCorners)
    {
        FillFootprintCorners(fourCorners, CentreXZ, YawDegrees, widthMetres, runMetres);
    }

    private static void FillFootprintCorners(Vector2[] fourCorners, Vector2 centre, float yawDegrees, float width, float run)
    {
        float yawRadians = yawDegrees * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Sin(yawRadians), Mathf.Cos(yawRadians));
        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 halfRun = forward * (run * 0.5f);
        Vector2 halfWidth = right * (width * 0.5f);
        fourCorners[0] = centre - halfRun - halfWidth;
        fourCorners[1] = centre - halfRun + halfWidth;
        fourCorners[2] = centre + halfRun + halfWidth;
        fourCorners[3] = centre + halfRun - halfWidth;
    }

    private static void CollectOverlappedTiles(Vector2 centre, float yawDegrees, float width, float run, HashSet<Vector2Int> tiles)
    {
        Vector2[] corners = new Vector2[4];
        FillFootprintCorners(corners, centre, yawDegrees, width, run);
        Vector2 minimum = corners[0];
        Vector2 maximum = corners[0];
        for (int cornerIndex = 1; cornerIndex < corners.Length; cornerIndex++)
        {
            minimum = Vector2.Min(minimum, corners[cornerIndex]);
            maximum = Vector2.Max(maximum, corners[cornerIndex]);
        }

        int minimumTileX = TileCoordinateOf(minimum.x);
        int maximumTileX = TileCoordinateOf(maximum.x);
        int minimumTileZ = TileCoordinateOf(minimum.y);
        int maximumTileZ = TileCoordinateOf(maximum.y);
        for (int tileX = minimumTileX; tileX <= maximumTileX; tileX++)
        {
            for (int tileZ = minimumTileZ; tileZ <= maximumTileZ; tileZ++)
            {
                tiles.Add(new Vector2Int(tileX, tileZ));
            }
        }
    }

    private static int TileCoordinateOf(float worldMetres)
    {
        return Mathf.FloorToInt((worldMetres + TileOriginOffsetMetres) / TerrainTileData.TileSizeMetres);
    }

    private static void RaiseForTiles(HashSet<Vector2Int> tiles)
    {
        foreach (Vector2Int tile in tiles)
        {
            TerrainTerraceEvents.RaiseTerraceChanged(tile);
        }
    }

    private void RaiseForCurrentFootprint()
    {
        HashSet<Vector2Int> tiles = new HashSet<Vector2Int>();
        CollectOverlappedTiles(CentreXZ, YawDegrees, widthMetres, runMetres, tiles);
        RaiseForTiles(tiles);
    }

    private void RememberPose()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastWidthMetres = widthMetres;
        lastRunMetres = runMetres;
    }

    private void OnEnable()
    {
        RememberPose();
    }

    private void OnDisable()
    {
        RaiseForCurrentFootprint();
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                RaiseForCurrentFootprint();
            }
        };
#endif
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (transform.position == lastPosition && transform.rotation == lastRotation)
        {
            return;
        }

        HashSet<Vector2Int> tiles = new HashSet<Vector2Int>();
        Vector2 oldCentre = new Vector2(lastPosition.x, lastPosition.z);
        float oldYawDegrees = lastRotation.eulerAngles.y;
        CollectOverlappedTiles(oldCentre, oldYawDegrees, lastWidthMetres, lastRunMetres, tiles);
        CollectOverlappedTiles(CentreXZ, YawDegrees, widthMetres, runMetres, tiles);
        RememberPose();
        RaiseForTiles(tiles);
    }

    private void OnDrawGizmosSelected()
    {
        Vector2[] corners = new Vector2[4];
        GetFootprintCornersXZ(corners);
        float height = transform.position.y;
        Gizmos.color = Color.yellow;
        for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
        {
            Vector2 current = corners[cornerIndex];
            Vector2 next = corners[(cornerIndex + 1) % corners.Length];
            Gizmos.DrawLine(new Vector3(current.x, height, current.y), new Vector3(next.x, height, next.y));
        }
    }
}
