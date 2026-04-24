// using Unity.Burst;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Collections;
// using Unity.Transforms;
//
// [BurstCompile]
// [UpdateInGroup(typeof(AIAwarenessSystemGroup))]
// public partial struct EnvironmentalAwarenessSystem : ISystem
// {
//     private ComponentLookup<LocalTransform> transformLookup;
//     private ComponentLookup<Interaction> interactionLookup;
//
//     [BurstCompile]
//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<GameSceneTag>();
//         transformLookup = state.GetComponentLookup<LocalTransform>(true);
//         interactionLookup = state.GetComponentLookup<Interaction>(true);
//     }
//
//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
//         transformLookup.Update(ref state);
//         interactionLookup.Update(ref state);
//
//         SpatialHashRegistry registry = SystemAPI.GetSingleton<SpatialHashRegistry>();
//
//         state.Dependency = new EnvironmentalAwarenessJob
//         {
//             registry = registry,
//             transformLookup = transformLookup,
//             interactionLookup = interactionLookup
//         }.ScheduleParallel(state.Dependency);
//     }
// }
//
// [BurstCompile]
// [WithAll(typeof(ActiveBrain), typeof(NeedsAction))]
// public partial struct EnvironmentalAwarenessJob : IJobEntity
// {
//     [ReadOnly] public SpatialHashRegistry registry;
//     [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
//     [ReadOnly] public ComponentLookup<Interaction> interactionLookup;
//
//     public void Execute(
//         Entity entity,
//         ref DynamicBuffer<ActionOption> options,
//         in DynamicBuffer<Motivation> motivations,
//         in LocalTransform transform,
//         in Awareness awareness)
//     {
//         float3 npcPos = transform.Position;
//         int2 centerCell = SpatialHashSystem.GetCell(npcPos);
//         int cellRange = (int)math.ceil(awareness.range / SpatialHashSystem.CELL_SIZE);
//
//         for (int m = 0; m < motivations.Length; m++)
//         {
//             MotivationType currentNeed = motivations[m].motivationType;
//             if (currentNeed == MotivationType.None) continue;
//
//             for (int x = -cellRange; x <= cellRange; x++)
//             {
//                 for (int z = -cellRange; z <= cellRange; z++)
//                 {
//                     int2 targetCell = centerCell + new int2(x, z);
//                     SpatialInteractionKey searchKey = new SpatialInteractionKey(targetCell, currentNeed);
//
//                     if (registry.interactionCells.TryGetFirstValue(searchKey, out Entity target, out var it))
//                     {
//                         do
//                         {
//                             AddActionIfValid(target, npcPos, awareness.range, currentNeed, ref options);
//                         } while (registry.interactionCells.TryGetNextValue(out target, ref it));
//                     }
//                 }
//             }
//         }
//     }
//
//     private void AddActionIfValid(Entity target, float3 npcPos, float maxRange,
//         MotivationType motivationType, ref DynamicBuffer<ActionOption> options)
//     {
//         if (transformLookup.TryGetComponent(target, out LocalTransform targetTransform))
//         {
//             float dist = math.distance(npcPos, targetTransform.Position);
//
//             if (dist <= maxRange)
//             {
//                 float distScore = 1.0f - math.saturate(dist / maxRange);
//
//                 if (interactionLookup.TryGetComponent(target, out Interaction interactData))
//                 {
//                     options.Add(new ActionOption
//                     {
//                         actionType     = interactData.actionType,
//                         motivationType = motivationType,
//                         targetEntity   = target,
//                         interaction    = true,
//                         utilityScore   = distScore
//                     });
//                 }
//             }
//         }
//     }
// }
