using UnityEngine;

public enum TerrainCrossingKind
{
    Bridge,
    Ford
}

// Marks where a road crosses water so the baker keeps the channel open (bridge) or shallow (ford).
[ExecuteAlways]
public class TerrainCrossing : MonoBehaviour
{
    private const float HalfHeightMetres = 2f;

    public TerrainCrossingKind kind;

    [Tooltip("x = length along local X, y = width along local Z, centred on the transform")]
    public Vector2 footprintMetres = new Vector2(12f, 4f);

    [Tooltip("Optional and informational; the bridge model is placed by hand")]
    public GameObject deckPrefab;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 lastScale;

    public Bounds WorldBounds()
    {
        Vector3 axisX = transform.right;
        Vector3 axisZ = transform.forward;
        float halfLength = footprintMetres.x * 0.5f;
        float halfWidth = footprintMetres.y * 0.5f;
        float extentX = Mathf.Abs(axisX.x) * halfLength + Mathf.Abs(axisZ.x) * halfWidth;
        float extentZ = Mathf.Abs(axisX.z) * halfLength + Mathf.Abs(axisZ.z) * halfWidth;
        return new Bounds(transform.position, new Vector3(extentX * 2f, HalfHeightMetres * 2f, extentZ * 2f));
    }

    public bool ContainsXZ(float worldX, float worldZ)
    {
        Vector3 axisX = transform.right;
        Vector3 axisZ = transform.forward;
        float offsetX = worldX - transform.position.x;
        float offsetZ = worldZ - transform.position.z;
        float alongLength = offsetX * axisX.x + offsetZ * axisX.z;
        float alongWidth = offsetX * axisZ.x + offsetZ * axisZ.z;
        float axisXLengthSquared = axisX.x * axisX.x + axisX.z * axisX.z;
        float axisZLengthSquared = axisZ.x * axisZ.x + axisZ.z * axisZ.z;
        if (axisXLengthSquared < 1e-6f || axisZLengthSquared < 1e-6f)
        {
            return false;
        }

        // Normalise by the projected axis length so a tilted object still measures in metres
        alongLength /= Mathf.Sqrt(axisXLengthSquared);
        alongWidth /= Mathf.Sqrt(axisZLengthSquared);
        return Mathf.Abs(alongLength) <= footprintMetres.x * 0.5f
            && Mathf.Abs(alongWidth) <= footprintMetres.y * 0.5f;
    }

    private void OnEnable()
    {
        RememberTransform();
    }

    private void OnDisable()
    {
        TerrainWaterEvents.RaiseCrossing(this);
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                TerrainWaterEvents.RaiseCrossing(this);
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
            TerrainWaterEvents.RaiseCrossing(this);
        }
    }
#endif

    private void OnDrawGizmos()
    {
        Gizmos.color = kind == TerrainCrossingKind.Bridge
            ? new Color(1f, 0.55f, 0.1f)
            : Color.cyan;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(footprintMetres.x, 0.1f, footprintMetres.y));
        Gizmos.matrix = previousMatrix;
    }

    private void RememberTransform()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastScale = transform.lossyScale;
    }
}
