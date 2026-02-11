// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;
// using UnityEngine;
// using UnityEngine.InputSystem;
//
// [UpdateInGroup(typeof(SimulationSystemGroup))]
// [UpdateAfter(typeof(AISystemGroup))]
// public partial struct AIDebugSystem : ISystem
// {
//     private float logTimer;
//     private const float LOG_INTERVAL = 1f;
//
//     private ComponentLookup<LocalTransform> transformLookup;
//     private ComponentLookup<UnitMover> moverLookup;
//
//     public void OnCreate(ref SystemState state)
//     {
//         logTimer = 0f;
//         transformLookup = state.GetComponentLookup<LocalTransform>(true);
//         moverLookup = state.GetComponentLookup<UnitMover>(true);
//     }
//
//     public void OnUpdate(ref SystemState state)
//     {
//         bool debugKeyPressed = Keyboard.current != null && Keyboard.current.f1Key.isPressed;
//
//         if (!debugKeyPressed)
//             return;
//
//         state.CompleteDependency();
//
//         transformLookup.Update(ref state);
//         moverLookup.Update(ref state);
//
//         logTimer += SystemAPI.Time.DeltaTime;
//
//         if (logTimer < LOG_INTERVAL)
//             return;
//
//         logTimer = 0f;
//
//         foreach (var (selectedAction, currentInteraction, brainLink, awareness, needs, entity) in
//             SystemAPI.Query<
//                 RefRO<SelectedAction>,
//                 RefRO<CurrentInteraction>,
//                 RefRO<BrainLink>,
//                 RefRO<Awareness>,
//                 RefRO<Needs>>()
//                 .WithEntityAccess())
//         {
//             Entity body = brainLink.ValueRO.body;
//
//             string bodyStatus = "NO BODY";
//             if (body != Entity.Null)
//             {
//                 if (transformLookup.TryGetComponent(body, out LocalTransform transform) &&
//                     moverLookup.TryGetComponent(body, out UnitMover mover))
//                 {
//                     bodyStatus = $"Pos:({transform.Position.x:F1},{transform.Position.z:F1}) Target:({mover.targetPosition.x:F1},{mover.targetPosition.z:F1}) Moving:{mover.isMoving}";
//                 }
//             }
//
//             string interactionStatus = "None";
//             Entity interactionTarget = currentInteraction.ValueRO.target;
//             if (interactionTarget != Entity.Null)
//             {
//                 interactionStatus = $"Target:{interactionTarget.Index} InRange:{currentInteraction.ValueRO.isInRange} Time:{currentInteraction.ValueRO.timeRemaining:F1}";
//
//                 if (transformLookup.TryGetComponent(interactionTarget, out LocalTransform targetTransform))
//                 {
//                     interactionStatus += $" TargetPos:({targetTransform.Position.x:F1},{targetTransform.Position.z:F1})";
//                 }
//             }
//
//             string awarenessStatus = "";
//             Awareness aw = awareness.ValueRO;
//             if (aw.hasFood) awarenessStatus += $" Food:{aw.nearestFood.Index}({aw.nearestFoodDistance:F1}m)";
//             if (aw.hasBed) awarenessStatus += $" Bed:{aw.nearestBed.Index}";
//             if (aw.hasSeat) awarenessStatus += $" Seat:{aw.nearestSeat.Index}";
//             if (aw.hasBar) awarenessStatus += $" Bar:{aw.nearestBar.Index}";
//             if (aw.hasBathroom) awarenessStatus += $" Bath:{aw.nearestBathroom.Index}";
//             if (aw.hasWork) awarenessStatus += $" Work:{aw.nearestWork.Index}";
//
//             if (string.IsNullOrEmpty(awarenessStatus))
//                 awarenessStatus = " NONE";
//
//             Needs n = needs.ValueRO;
//             string needsStatus = $"Hung:{n.hunger:F2} Eng:{n.energy:F2} Comf:{n.comfort:F2} Blad:{n.bladder:F2} Ent:{n.entertainment:F2}";
//
//             Debug.Log($"[AI] Brain:{entity.Index} | Action:{selectedAction.ValueRO.current} | {bodyStatus} | Interaction:{interactionStatus} | Aware:{awarenessStatus} | {needsStatus}");
//         }
//     }
// }