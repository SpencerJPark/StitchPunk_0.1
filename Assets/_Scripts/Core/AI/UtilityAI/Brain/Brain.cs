// using UnityEngine;
// using System.Collections.Generic;
// using UtilityAI;
// using PathFinding;
//
// namespace UtilityAI
// {
//     public class Brain : IUpdateObserver
//     {
//         // AI Components
//         [SerializeField] private PathfindingComponent pathfinding;
//
//         public List<AIAction> actions;
//         public Context context;
//
//         public Health health;
//
//
//         // IInputProvider implementation
//         public  Vector2 MoveInput => pathfinding.CurrentMoveInput;
//         public Vector2 SteerInput { get; private set; }   // can later be filled with vehicle steering logic
//         public bool ExitVehicleFired => false;
//         public bool InteractFired => false;
//         public bool ActionFired => false;
//
//
//         void OnEnable() => UpdateManager.RegisterObserver(this);
//         void OnDisable() => UpdateManager.UnregisterObserver(this);
//
//         
//         void Awake()
//         {
//             context = new Context(this);
//             
//
//             foreach (var action in actions) {
//                 action.Initialize(context);
//             }
//         }
//
//         public void ObservedUpdate()
//         {
//             UpdateContext();
//
//             // Decision logic will go here later.
//
//
//             pathfinding?.Tick();
//         }
//
//         private void UpdateContext()
//         {
//             context.SetData("health", health.normalizedHealth);
//         }
//
//
//         // Action API's
//         public void SetDestination(Vector3 pos)
//         {
//             if (pathfinding != null)
//                 pathfinding.SetDestination(pos);
//             else
//                 Debug.LogError("Brain is missing PathfindingComponent!");
//         }
//     }
// }