// using Unity.Burst;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;
//
// public struct CounterAlert
// {
//     public Entity attackerEntity;
//     public float3 triggerPosition;
// }
//
// [BurstCompile]
// [UpdateInGroup(typeof(MinionActionSelectionSystemGroup))]
// public partial struct MinionSelfDefenceSystem : ISystem
// {
//     private NativeList<CounterAlert>         alertList;
//     private ComponentLookup<PlayerOrder>     playerOrderLookup;
//     private ComponentLookup<Dead>           aliveLookup;
//     private ComponentLookup<LocalTransform>  transformLookup;
//
//     [BurstCompile]
//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<GameSceneTag>();
//         state.RequireForUpdate<AttackLibrary>();
//         alertList         = new NativeList<CounterAlert>(16, Allocator.Persistent);
//         playerOrderLookup = state.GetComponentLookup<PlayerOrder>(true);
//         aliveLookup       = state.GetComponentLookup<Dead>(true);
//         transformLookup   = state.GetComponentLookup<LocalTransform>(true);
//     }
//
//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
//         alertList.Clear();
//         playerOrderLookup.Update(ref state);
//         aliveLookup.Update(ref state);
//         transformLookup.Update(ref state);
//
//         BlobAssetReference<AttackLibraryBlob> attackLibrary =
//             SystemAPI.GetSingleton<AttackLibrary>().library;
//
//         state.Dependency = new MinionCounterJob
//         {
//             alertList         = alertList,
//             playerOrderLookup = playerOrderLookup,
//             aliveLookup       = aliveLookup,
//             transformLookup   = transformLookup,
//             attackLibrary     = attackLibrary,
//         }.Schedule(state.Dependency);
//
//         state.Dependency = new NearbyAlertJob
//         {
//             alertEntries      = alertList,
//             playerOrderLookup = playerOrderLookup,
//             aliveLookup       = aliveLookup,
//             transformLookup   = transformLookup,
//             attackLibrary     = attackLibrary,
//         }.ScheduleParallel(state.Dependency);
//     }
//
//     [BurstCompile]
//     public void OnDestroy(ref SystemState state)
//     {
//         alertList.Dispose();
//     }
// }
//
// [BurstCompile]
// [WithAll(typeof(Minion), typeof(PlayerUnitBrain))]
// [WithDisabled(typeof(Dead))]
// public partial struct MinionCounterJob : IJobEntity
// {
//     public  NativeList<CounterAlert>                    alertList;
//     [ReadOnly] public ComponentLookup<PlayerOrder>      playerOrderLookup;
//     [ReadOnly] public ComponentLookup<Dead>            aliveLookup;
//     [ReadOnly] public ComponentLookup<LocalTransform>   transformLookup;
//     [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
//
//     public void Execute(
//         Entity                              entity,
//         in  LocalTransform                  transform,
//         ref DynamicBuffer<ThreatEntry>      threatBuffer,
//         ref DynamicBuffer<ActionOption>     actionOptions,
//         in  DynamicBuffer<AvailableAttack>  availableAttacks,
//         EnabledRefRW<PlayerUnitBrain>      playerControlledEnabled,
//         EnabledRefRW<ActionRequest>         actionRequestEnabled)
//     {
//         if (threatBuffer.Length == 0)
//             return;
//
//         if (playerOrderLookup.HasComponent(entity) &&
//             playerOrderLookup[entity].commandType == CommandType.Attack)
//             return;
//
//         Entity bestAttacker = Entity.Null;
//         float  highestScore = -1f;
//         for (int i = 0; i < threatBuffer.Length; i++)
//         {
//             ThreatEntry threat = threatBuffer[i];
//             if (threat.attackerEntity == Entity.Null)
//                 continue;
//             if (!aliveLookup.IsComponentEnabled(threat.attackerEntity))
//                 continue;
//             if (threat.threatScore > highestScore)
//             {
//                 highestScore = threat.threatScore;
//                 bestAttacker = threat.attackerEntity;
//             }
//         }
//
//         threatBuffer.Clear();
//
//         if (bestAttacker == Entity.Null)
//             return;
//
//         float aggressorDist = 0f;
//         if (transformLookup.TryGetComponent(bestAttacker, out LocalTransform aggressorTransform))
//             aggressorDist = math.distance(transform.Position, aggressorTransform.Position);
//
//         for (int a = 0; a < availableAttacks.Length; a++)
//         {
//             ActionType actionType  = availableAttacks[a].actionType;
//             AttackType attackType  = availableAttacks[a].attackType;
//             int        attackIndex = (int)attackType;
//             if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
//                 continue;
//             float attackRange = attackLibrary.Value.attacks[attackIndex].range;
//             float rangeScore  = AIUtils.AttackRangeScore(aggressorDist, attackRange);
//
//             actionOptions.Add(new ActionOption
//             {
//                 actionType     = actionType,
//                 needType       = NeedType.SelfDefence,
//                 priority = 3,
//                 utilityScore   = rangeScore,
//                 needsValidation    = false,
//                 targetEntity   = bestAttacker,
//             });
//         }
//
//         playerControlledEnabled.ValueRW = false;
//         actionRequestEnabled.ValueRW    = true;
//
//         alertList.Add(new CounterAlert
//         {
//             attackerEntity  = bestAttacker,
//             triggerPosition = transform.Position,
//         });
//     }
// }
//
// [BurstCompile]
// [WithAll(typeof(Minion), typeof(PlayerUnitBrain))]
// [WithDisabled(typeof(Dead))]
// public partial struct NearbyAlertJob : IJobEntity
// {
//     private const float ALERT_RADIUS_SQ = 64f;
//
//     [ReadOnly] public NativeList<CounterAlert>              alertEntries;
//     [ReadOnly] public ComponentLookup<PlayerOrder>          playerOrderLookup;
//     [ReadOnly] public ComponentLookup<Dead>                aliveLookup;
//     [ReadOnly] public ComponentLookup<LocalTransform>       transformLookup;
//     [ReadOnly] public BlobAssetReference<AttackLibraryBlob> attackLibrary;
//
//     public void Execute(
//         Entity                             entity,
//         in  LocalTransform                 transform,
//         ref DynamicBuffer<ActionOption>    actionOptions,
//         in  DynamicBuffer<AvailableAttack> availableAttacks,
//         EnabledRefRW<PlayerUnitBrain>     playerControlledEnabled,
//         EnabledRefRW<ActionRequest>        actionRequestEnabled)
//     {
//         if (playerOrderLookup.HasComponent(entity) &&
//             playerOrderLookup[entity].commandType == CommandType.Attack)
//             return;
//
//         for (int i = 0; i < alertEntries.Length; i++)
//         {
//             CounterAlert alert = alertEntries[i];
//             if (math.distancesq(transform.Position, alert.triggerPosition) > ALERT_RADIUS_SQ)
//                 continue;
//             if (!aliveLookup.IsComponentEnabled(alert.attackerEntity))
//                 continue;
//             
//             float aggressorDist = 0f;
//             if (transformLookup.TryGetComponent(alert.attackerEntity, out LocalTransform aggressorTransform))
//                 aggressorDist = math.distance(transform.Position, aggressorTransform.Position);
//
//             for (int a = 0; a < availableAttacks.Length; a++)
//             {
//                 ActionType actionType  = availableAttacks[a].actionType;
//                 AttackType attackType  = availableAttacks[a].attackType;
//                 int        attackIndex = (int)attackType;
//                 if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
//                     continue;
//                 float attackRange = attackLibrary.Value.attacks[attackIndex].range;
//                 float rangeScore  = AIUtils.AttackRangeScore(aggressorDist, attackRange);
//
//                 actionOptions.Add(new ActionOption
//                 {
//                     actionType     = actionType,
//                     needType       = NeedType.SelfDefence,
//                     priority = 2,
//                     utilityScore   = rangeScore,
//                     needsValidation    = false,
//                     targetEntity   = alert.attackerEntity,
//                 });
//             }
//
//             playerControlledEnabled.ValueRW = false;
//             actionRequestEnabled.ValueRW    = true;
//             return;
//         }
//     }
// }
