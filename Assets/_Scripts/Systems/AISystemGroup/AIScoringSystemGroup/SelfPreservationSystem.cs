// using Unity.Burst;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;
//
// [BurstCompile]
// [UpdateInGroup(typeof(AIScoringSystemGroup))]
// public partial struct SelfPreservationSystem : ISystem
// {
//     private ComponentLookup<Health> healthLookup;
//     private ComponentLookup<LocalTransform> transformLookup;
//     private ComponentLookup<FleeInteraction> fleeInteractionLookup;
//     private ComponentLookup<InteractionProvider> interactionLookup;
//
//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<SelfPreservation>();
//         state.RequireForUpdate<SpatialHashSingleton>();
//
//         healthLookup = state.GetComponentLookup<Health>(true);
//         transformLookup = state.GetComponentLookup<LocalTransform>(true);
//         fleeInteractionLookup = state.GetComponentLookup<FleeInteraction>(true);
//         interactionLookup = state.GetComponentLookup<InteractionProvider>(true);
//     }
//
//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
//         healthLookup.Update(ref state);
//         transformLookup.Update(ref state);
//         fleeInteractionLookup.Update(ref state);
//         interactionLookup.Update(ref state);
//
//         SpatialHashSingleton spatialHash = SystemAPI.GetSingleton<SpatialHashSingleton>();
//
//         new SelfPreservationScoringJob
//         {
//             healthLookup = healthLookup,
//             transformLookup = transformLookup,
//             fleeInteractionLookup = fleeInteractionLookup,
//             interactionLookup = interactionLookup,
//             waypointCells = spatialHash.waypointCells,
//             cellSize = SpatialHashSystem.CELL_SIZE
//         }.ScheduleParallel();
//     }
//
//     [BurstCompile]
//     public partial struct SelfPreservationScoringJob : IJobEntity
//     {
//         [ReadOnly] public ComponentLookup<Health> healthLookup;
//         [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
//         [ReadOnly] public ComponentLookup<FleeInteraction> fleeInteractionLookup;
//         [ReadOnly] public ComponentLookup<InteractionProvider> interactionLookup;
//         [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
//         public float cellSize;
//
//         public void Execute(
//             ref DynamicBuffer<ActionOption> options,
//             in SelfPreservation selfPreservation,
//             in BrainLink brainLink,
//             EnabledRefRO<NeedsAction> needsAction)
//         {
//             Entity body = brainLink.body;
//
//             if (!healthLookup.TryGetComponent(body, out Health health))
//                 return;
//
//             if (!transformLookup.TryGetComponent(body, out LocalTransform bodyTransform))
//                 return;
//
//             float baseScore = CalculateBaseScore(in health, selfPreservation.healthThreshold);
//
//             if (baseScore < -50f)
//                 return;
//
//             float3 pos = bodyTransform.Position;
//
//             NativeList<Entity> nearby = new NativeList<Entity>(8, Allocator.Temp);
//             AIUtil.QueryNearbyInteractions(
//                 in waypointCells,
//                 in interactionLookup,
//                 in transformLookup,
//                 pos,
//                 1f,
//                 cellSize,
//                 ref nearby);
//
//             FindBestFleeTarget(in nearby, pos, baseScore, out Entity bestTarget, out float bestScore);
//
//             nearby.Dispose();
//
//             AIUtil.AddActionOption(ref options, ref bestTarget, bestScore);
//         }
//
//         private static float CalculateBaseScore(in Health health, float threshold)
//         {
//             float healthPercent = health.healthAmount / health.healthAmountMax;
//
//             if (healthPercent >= threshold)
//             {
//                 float safety = (healthPercent - threshold) / (1f - threshold);
//                 return -100f * safety;
//             }
//
//             float normalizedDanger = 1f - (healthPercent / threshold);
//             return 100f * normalizedDanger * normalizedDanger;
//         }
//
//         private void FindBestFleeTarget(
//             in NativeList<Entity> nearby,
//             float3 pos,
//             float baseScore,
//             out Entity bestTarget,
//             out float bestScore)
//         {
//             bestScore = float.MinValue;
//             bestTarget = Entity.Null;
//
//             for (int i = 0; i < nearby.Length; i++)
//             {
//                 Entity candidate = nearby[i];
//
//                 if (!fleeInteractionLookup.HasComponent(candidate))
//                     continue;
//
//                 float score = ScoreCandidate(candidate, pos, baseScore);
//
//                 if (score > bestScore)
//                 {
//                     bestScore = score;
//                     bestTarget = candidate;
//                 }
//             }
//         }
//
//         private float ScoreCandidate(Entity candidate, float3 pos, float baseScore)
//         {
//             InteractionProvider interactionProvider = interactionLookup[candidate];
//             float3 targetPos = transformLookup[candidate].Position;
//             float distance = math.distance(pos, targetPos);
//
//             float distanceBonus = math.remap(0f, interactionProvider.broadcastRadius, 10f, 0f, distance);
//             return math.clamp(baseScore + distanceBonus, -100f, 100f);
//         }
//     }
// }