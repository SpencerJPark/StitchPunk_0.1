// using UnityEngine;
//
// /// <summary>
// /// Add this component alongside WaypointAuthoring to see debug visualization in the Scene view.
// /// Shows:
// ///   - Broadcast radius (blue wire sphere): how far the waypoint can be detected
// ///   - Interaction range (green wire sphere): how close NPC must be to start action
// ///   - Approach point (yellow line): line from waypoint to approach point
// ///   - Wander radius (cyan wire sphere): for WanderArea actions
// ///   - Label with waypoint name and action list
// ///
// /// Use WaypointDebugToggle MonoBehaviour or set WaypointDebugAuthoring.ShowDebug to toggle.
// /// </summary>
// [RequireComponent(typeof(InteractionProviderAuthoring))]
// public class InteractionDebugAuthoring : MonoBehaviour
// {
//     public static bool ShowDebug = true;
//
//     [Header("Debug Colors")]
//     public Color broadcastColor = new Color(0.3f, 0.5f, 1f, 0.25f);
//     public Color interactionColor = new Color(0.3f, 1f, 0.3f, 0.5f);
//     public Color approachColor = Color.yellow;
//
//     [Header("Display Options")]
//     public bool alwaysShowBroadcast = false;
//     public bool showLabel = true;
//
//     private void OnDrawGizmos()
//     {
//         if (!ShowDebug)
//             return;
//
//         InteractionProviderAuthoring interactionProviderAuthoring = GetComponent<InteractionProviderAuthoring>();
//         if (interactionProviderAuthoring == null)
//             return;
//
//         Vector3 position = transform.position;
//
//         if (alwaysShowBroadcast)
//         {
//             Gizmos.color = broadcastColor;
//             Gizmos.DrawWireSphere(position, interactionProviderAuthoring.broadcastRadius);
//         }
//
//         // Always show interaction range as a small disc
//         Gizmos.color = interactionColor;
//         Gizmos.DrawWireSphere(position, interactionProviderAuthoring.interactionRange);
//
//         // Approach point line
//         if (interactionProviderAuthoring.approachPoint != null)
//         {
//             Gizmos.color = approachColor;
//             Gizmos.DrawLine(position, interactionProviderAuthoring.approachPoint.position);
//             Gizmos.DrawWireSphere(interactionProviderAuthoring.approachPoint.position, 0.3f);
//         }
//     }
//
//     private void OnDrawGizmosSelected()
//     {
//         if (!ShowDebug)
//             return;
//
//         InteractionProviderAuthoring interactionProviderAuthoring = GetComponent<InteractionProviderAuthoring>();
//         if (interactionProviderAuthoring == null)
//             return;
//
//         Vector3 position = transform.position;
//
//         // Broadcast radius - large blue wire sphere
//         Gizmos.color = broadcastColor;
//         Gizmos.DrawWireSphere(position, interactionProviderAuthoring.broadcastRadius);
//
//         // Semi-transparent fill for broadcast
//         Color fillColor = broadcastColor;
//         fillColor.a = 0.05f;
//         Gizmos.color = fillColor;
//         Gizmos.DrawSphere(position, interactionProviderAuthoring.broadcastRadius);
//
//         // Interaction range - green sphere
//         Gizmos.color = interactionColor;
//         Gizmos.DrawWireSphere(position, interactionProviderAuthoring.interactionRange);
//
//         Color interactionFill = interactionColor;
//         interactionFill.a = 0.1f;
//         Gizmos.color = interactionFill;
//         Gizmos.DrawSphere(position, interactionProviderAuthoring.interactionRange);
//
//         // Approach point
//         if (interactionProviderAuthoring.approachPoint != null)
//         {
//             Gizmos.color = approachColor;
//             Gizmos.DrawLine(position, interactionProviderAuthoring.approachPoint.position);
//             Gizmos.DrawWireSphere(interactionProviderAuthoring.approachPoint.position, 0.3f);
//         }
//
// #if UNITY_EDITOR
//         // Label
//         if (showLabel)
//         {
//             string label = gameObject.name;
//
//             if (interactionProviderAuthoring.actions != null && interactionProviderAuthoring.actions.Count > 0)
//             {
//                 label += "\n";
//                 foreach (InteractionProviderAuthoring.InteractionActionDef action in interactionProviderAuthoring.actions)
//                 {
//                     label += $"  {action.actionType} ({action.behavior}, {action.duration}s)\n";
//                 }
//             }
//
//             label += $"Occupants: {interactionProviderAuthoring.maxOccupants}";
//
//             GUIStyle style = new GUIStyle();
//             style.normal.textColor = Color.white;
//             style.fontSize = 10;
//             style.alignment = TextAnchor.MiddleCenter;
//
//             UnityEditor.Handles.Label(position + Vector3.up * 2f, label, style);
//         }
// #endif
//     }
// }