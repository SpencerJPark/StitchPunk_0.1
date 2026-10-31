using System.Collections.Generic;
using UnityEngine;

// A world-space box holding a flat water surface (lake, sea). Tiles it overlaps get water where the ground is below the level.
[ExecuteAlways]
public class TerrainWaterBody : MonoBehaviour
{
    private const float GizmoSlabThicknessMetres = 0.2f;

    public float waterLevelMetres = 0f;

    [Tooltip("World coordinates, not relative to this transform")]
    public Vector3 boundsMin = new Vector3(-100f, -50f, -100f);

    [Tooltip("World coordinates, not relative to this transform")]
    public Vector3 boundsMax = new Vector3(100f, 50f, 100f);

    public TerrainWaterProfile profile;

    [Tooltip("Highest priority wins where bodies overlap")]
    public int priority;

#if UNITY_EDITOR
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 lastScale;
#endif

    public Bounds WorldBounds()
    {
        Bounds bounds = new Bounds();
        bounds.SetMinMax(boundsMin, boundsMax);
        return bounds;
    }

    public bool ContainsXZ(float worldX, float worldZ)
    {
        return worldX >= boundsMin.x && worldX <= boundsMax.x
            && worldZ >= boundsMin.z && worldZ <= boundsMax.z;
    }

    // Tile (x, z) covers world [x*100-50, x*100+50]; any overlap with the bounds counts.
    public IEnumerable<Vector2Int> TilesInside()
    {
        float tileSize = TerrainTile.TileSizeMetres;
        float halfTile = tileSize * 0.5f;
        int firstTileX = Mathf.FloorToInt((boundsMin.x - halfTile) / tileSize) + 1;
        int lastTileX = Mathf.CeilToInt((boundsMax.x + halfTile) / tileSize) - 1;
        int firstTileZ = Mathf.FloorToInt((boundsMin.z - halfTile) / tileSize) + 1;
        int lastTileZ = Mathf.CeilToInt((boundsMax.z + halfTile) / tileSize) - 1;
        for (int tileX = firstTileX; tileX <= lastTileX; tileX++)
        {
            for (int tileZ = firstTileZ; tileZ <= lastTileZ; tileZ++)
            {
                yield return new Vector2Int(tileX, tileZ);
            }
        }
    }

    private void OnDrawGizmos()
    {
        DrawBody(0.25f, 0.5f);
    }

    private void OnDrawGizmosSelected()
    {
        DrawBody(0.4f, 1f);
    }

    private void DrawBody(float fillAlpha, float wireAlpha)
    {
        Vector3 center = (boundsMin + boundsMax) * 0.5f;
        Vector3 size = boundsMax - boundsMin;
        Gizmos.color = new Color(0.15f, 0.45f, 1f, fillAlpha);
        Gizmos.DrawCube(
            new Vector3(center.x, waterLevelMetres, center.z),
            new Vector3(size.x, GizmoSlabThicknessMetres, size.z));
        Gizmos.color = new Color(0.15f, 0.45f, 1f, wireAlpha);
        Gizmos.DrawWireCube(center, size);
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        RememberTransform();
#endif
    }

    private void OnDisable()
    {
        TerrainWaterEvents.RaiseBody(this);
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                TerrainWaterEvents.RaiseBody(this);
            }
        };
#endif
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (transform.position != lastPosition
            || transform.rotation != lastRotation
            || transform.lossyScale != lastScale)
        {
            RememberTransform();
            TerrainWaterEvents.RaiseBody(this);
        }
    }

    private void RememberTransform()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastScale = transform.lossyScale;
    }
#endif
}
