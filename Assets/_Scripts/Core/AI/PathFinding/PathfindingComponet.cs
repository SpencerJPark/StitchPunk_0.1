// // PathfindingComponent.cs
// using UnityEngine;
// using UnityEngine.AI;
//
// namespace PathFinding
// {
//     public class PathfindingComponent : MonoBehaviour
//     {
//         [SerializeField] private PathSystem pathSystem;
//         [SerializeField] private int areaMask = NavMesh.AllAreas;
//         [SerializeField] private float cornerReachedRadius = 0.3f;
//         [SerializeField] private float waypointRefreshInterval = 0.25f;
//
//         private Transform owner;
//         private Vector3 currentGoal;
//         private bool hasGoal;
//
//         private Vector3 currentWaypoint;
//         private float nextRefreshTime;
//
//         // Local avoidance accumulation
//         private Vector3 accumulatedAvoidance = Vector3.zero;
//
//         public Vector2 CurrentMoveInput { get; private set; }
//
//         private void Awake() { owner = transform; }
//
//         public void SetDestination(Vector3 worldGoal)
//         {
//             // ✅ Use explicit NavMeshHit, not `out var`
//             NavMeshHit hit;
//             if (NavMesh.SamplePosition(worldGoal, out hit, 1.5f, areaMask))
//                 worldGoal = hit.position;
//
//             currentGoal = worldGoal;
//             hasGoal = true;
//             nextRefreshTime = 0f; // force refresh now
//         }
//
//         public void ClearDestination()
//         {
//             hasGoal = false;
//             CurrentMoveInput = Vector2.zero;
//         }
//
//         public void Tick()
//         {
//             if (!hasGoal || pathSystem == null)
//             {
//                 CurrentMoveInput = Vector2.zero;
//                 return;
//             }
//
//             // Refresh/advance waypoint
//             if (Time.time >= nextRefreshTime || Vector3.Distance(owner.position, currentWaypoint) <= cornerReachedRadius)
//             {
//                 // ✅ Use the new synchronous helper (no callbacks, no Action<> confusion)
//                 if (pathSystem.TryGetFirstWaypoint(owner.position, currentGoal, areaMask, out var wp))
//                 {
//                     currentWaypoint = wp;
//                     nextRefreshTime = Time.time + waypointRefreshInterval;
//                 }
//                 else
//                 {
//                     // No path: stop
//                     CurrentMoveInput = Vector2.zero;
//                     return;
//                 }
//             }
//
//             // Move toward waypoint (+ local avoidance)
//             Vector3 toWp = currentWaypoint - owner.position;
//             toWp.y = 0f;
//
//             if (toWp.sqrMagnitude < 1e-6f)
//             {
//                 CurrentMoveInput = Vector2.zero;
//                 return;
//             }
//
//             toWp.Normalize();
//
//             // ✅ Apply any nudge from LocalAvoidanceManager
//             Vector3 finalDir = toWp + accumulatedAvoidance;
//             finalDir.y = 0f;
//             if (finalDir.sqrMagnitude > 1f) finalDir.Normalize();
//
//             CurrentMoveInput = new Vector2(finalDir.x, finalDir.z);
//
//             // ✅ Clear nudges for next frame
//             accumulatedAvoidance = Vector3.zero;
//         }
//
//         // ===== Called by LocalAvoidanceManager =====
//         public void AddAvoidanceNudge(Vector3 nudge) => accumulatedAvoidance += nudge;
//         public void ClearAvoidanceNudge() => accumulatedAvoidance = Vector3.zero;
//     }
// }