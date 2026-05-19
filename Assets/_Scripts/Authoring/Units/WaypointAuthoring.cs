using Unity.Entities;
using UnityEngine;

public class WaypointAuthoring : MonoBehaviour
{
    public float radius = 5f;

    // Always-on: subtle fill so all waypoints are visible simultaneously in the Scene view.
    // Unity registers WaypointAuthoring in the Gizmos dropdown automatically —
    // toggle Scene view → Gizmos → WaypointAuthoring to show/hide all at once.
    private void OnDrawGizmos() => DrawRadiusGizmo(0.12f);

    // Brighter fill when this waypoint is selected in the hierarchy.
    private void OnDrawGizmosSelected() => DrawRadiusGizmo(0.4f);

    // Zero runtime cost — the MonoBehaviour is discarded after baking.
    private void DrawRadiusGizmo(float fillAlpha)
    {
        if (radius <= 0f) return;
        Gizmos.color = new Color(0f, 1f, 0.5f, fillAlpha);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(0f, 1f, 0.5f, 1f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    public class Baker : Baker<WaypointAuthoring>
    {
        public override void Bake(WaypointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.WorldSpace);
            AddComponent(entity, new NavigationWaypoint { radius = authoring.radius });
        }
    }
}
