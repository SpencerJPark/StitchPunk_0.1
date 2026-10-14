using UnityEngine;

// Ground ring showing which minions hear an on-foot quick command; drawn only while the radial is open on foot.
[RequireComponent(typeof(LineRenderer))]
public class OnFootCommandRing : MonoBehaviour
{
    [SerializeField] private Material ringMaterial;
    [SerializeField] private Color ringColor = new Color(0.45f, 0.9f, 1f, 0.6f);

    private const int SegmentCount = 64;
    private const float RingWidth = 0.08f;
    private const float GroundLift = 0.05f;

    private LineRenderer lineRenderer;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = SegmentCount;
        lineRenderer.widthMultiplier = RingWidth;
        lineRenderer.startColor = ringColor;
        lineRenderer.endColor = ringColor;
        if (ringMaterial != null) lineRenderer.sharedMaterial = ringMaterial;
        lineRenderer.enabled = false;
    }

    private void LateUpdate()
    {
        bool shouldShow = QuickCommandRadialUI.IsOpenOnFoot && SquadCommandManager.Instance != null;
        if (lineRenderer.enabled != shouldShow) lineRenderer.enabled = shouldShow;
        if (!shouldShow) return;

        Vector3 centre = SquadCommandManager.Instance.CommanderPosition;
        float radius = SquadCommandManager.Instance.OnFootCommandRadius;
        for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
        {
            float angle = segmentIndex * Mathf.PI * 2f / SegmentCount;
            lineRenderer.SetPosition(segmentIndex, new Vector3(
                centre.x + Mathf.Cos(angle) * radius, centre.y + GroundLift, centre.z + Mathf.Sin(angle) * radius));
        }
    }
}
