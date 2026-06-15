// using Unity.Burst;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;
//
// [UpdateInGroup(typeof(PlayerInputSystemGroup))]
// [BurstCompile]
// public partial struct PlayerAttackSystem : ISystem
// {
//     private const float RANGED_CONE_HALF_ANGLE_DEG = 45f;
//
//     [BurstCompile]
//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<AttackLibrary>();
//         state.RequireForUpdate<UnitDataLibrary>();
//         state.RequireForUpdate<AnimationLibrary>();
//         state.RequireForUpdate<Player>();
//     }
//
//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
//         var attackLibrary = SystemAPI.GetSingleton<AttackLibrary>().library;
//         var unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
//         float cosHalfAngle = math.cos(math.radians(RANGED_CONE_HALF_ANGLE_DEG));
//
//         foreach (var (attackInputEnabled, selfEntity) in
//             SystemAPI.Query<EnabledRefRW<OnAttackPlayerInput>>()
//                 .WithAll<Player>()
//                 .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
//                 .WithEntityAccess())
//         {
//             if (SystemAPI.GetComponent<PlayerActionMap>(selfEntity).activeActionMap != ActionMaps.Player) continue;
//             if (!attackInputEnabled.ValueRO) continue;
//
//             attackInputEnabled.ValueRW = false;
//
//             // GATE: Stop if the action timer is still running
//             if (SystemAPI.IsComponentEnabled<ActionTimer>(selfEntity) && SystemAPI.GetComponent<ActionTimer>(selfEntity).time > 0f) continue;
//
//             if (!SystemAPI.HasComponent<UnitData>(selfEntity)) continue;
//             UnitData unitData = SystemAPI.GetComponent<UnitData>(selfEntity);
//
//             int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
//             if (unitIndex < 0 || unitIndex >= unitLibrary.Value.units.Length) continue;
//
//             ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
//             if (unitBlob.attacks.Length == 0) continue;
//
//             PlayerSelectedAttack selectedAttack = SystemAPI.GetComponent<PlayerSelectedAttack>(selfEntity);
//             AttackType attackType = selectedAttack.attackType != AttackType.None
//                 ? selectedAttack.attackType
//                 : unitBlob.attacks[0].attack;
//
//             ActionType actionType = AIUtils.GetActionByAttack(ref unitBlob, attackType);
//             int attackIndex = (int)attackType;
//             if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length) continue;
//             ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];
//
//             LocalTransform playerTransform = SystemAPI.GetComponent<LocalTransform>(selfEntity);
//             float3 playerPos = playerTransform.Position;
//             float3 facingDir = math.normalizesafe(new float3(math.forward(playerTransform.Rotation).x, 0, math.forward(playerTransform.Rotation).z));
//
//             bool isMelee = actionType == ActionType.MeleeContinuous || actionType == ActionType.MeleeSingle;
//             float rangeSq = attackBlob.range * attackBlob.range;
//
//             Entity bestTarget = Entity.Null;
//             float bestDistSq = float.MaxValue;
//             float bestDot = float.MinValue;
//
//             // Target Detection
//             foreach (var (enemyTransform, enemyEntity) in SystemAPI.Query<RefRO<LocalTransform>>()
//                          .WithAll<Health>().WithNone<Player, PlayerImmune, Dead>().WithEntityAccess())
//             {
//                 float3 toEnemy = enemyTransform.ValueRO.Position - playerPos;
//                 toEnemy.y = 0f;
//                 float distSq = math.lengthsq(toEnemy);
//                 if (distSq < 0.0001f || distSq > rangeSq) continue;
//
//                 if (isMelee)
//                 {
//                     if (distSq < bestDistSq) { bestDistSq = distSq; bestTarget = enemyEntity; }
//                 }
//                 else
//                 {
//                     float dot = math.dot(facingDir, toEnemy * math.rsqrt(distSq));
//                     if (dot < cosHalfAngle) continue;
//                     if (dot > bestDot) { bestDot = dot; bestTarget = enemyEntity; }
//                 }
//             }
//
//             if (bestTarget == Entity.Null) continue;
//
//             // Fire the Attack
//             RefRW<AttackRequest> attackRequest = SystemAPI.GetComponentRW<AttackRequest>(selfEntity);
//             attackRequest.ValueRW.targetEntity = bestTarget;
//             attackRequest.ValueRW.attackType = attackType;
//             attackRequest.ValueRW.hitFired = false;
//             attackRequest.ValueRW.elapsed = 0f;
//             SystemAPI.SetComponentEnabled<AttackRequest>(selfEntity, true);
//
//             // Handle Animation & Timer
//             AnimationType animType = GetAttackAnimation(ref unitBlob, actionType);
//
//             // Cadence is authored via cooldown; guarantee the hit (at hitTime) lands first.
//             RefRW<ActionTimer> actionTimer = SystemAPI.GetComponentRW<ActionTimer>(selfEntity);
//             actionTimer.ValueRW.time = math.max(attackBlob.cooldown, attackBlob.hitTime + 0.05f);
//             SystemAPI.SetComponentEnabled<ActionTimer>(selfEntity, true);
//
//             DynamicBuffer<SetAnimation> setAnimations = SystemAPI.GetBuffer<SetAnimation>(selfEntity);
//             setAnimations.Add(new SetAnimation
//             {
//                 layer = AnimationLayerType.Action,
//                 animation = animType,
//                 speed = 1f,
//                 looping = false,
//             });
//             SystemAPI.SetComponentEnabled<AnimationRequest>(selfEntity, true);
//         }
//     }
//
//     private static AnimationType GetAttackAnimation(ref UnitDataBlob unitBlob, ActionType actionType)
//     {
//         ref var mappings = ref unitBlob.actionAnimations;
//         for (int i = 0; i < mappings.Length; i++)
//         {
//             if (mappings[i].action == actionType) return mappings[i].animation;
//         }
//         return AnimationType.None;
//     }
// }