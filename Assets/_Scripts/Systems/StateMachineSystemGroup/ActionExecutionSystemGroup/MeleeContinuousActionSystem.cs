// using Unity.Burst;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;
// using Unity.Collections;
//
// [BurstCompile]
// [UpdateInGroup(typeof(ActionExecutionSystemGroup))]
// public partial struct MeleeContinuousActionSystem : ISystem
// {
//     private ComponentLookup<Dead>          deadLookup;
//     private ComponentLookup<LocalTransform> transformLookup;
//
//     [BurstCompile]
//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<GameSceneTag>();
//         state.RequireForUpdate<AttackLibrary>();
//         state.RequireForUpdate<UnitDataLibrary>();
//         state.RequireForUpdate<AnimationLibrary>();
//
//         deadLookup = state.GetComponentLookup<Dead>(true);
//         transformLookup = state.GetComponentLookup<LocalTransform>(true);
//     }
//
//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
//         deadLookup.Update(ref state);
//         transformLookup.Update(ref state);
//
//         BlobAssetReference<AttackLibraryBlob> attackLibrary =
//             SystemAPI.GetSingleton<AttackLibrary>().library;
//
//         BlobAssetReference<UnitLibraryBlob> unitLibrary =
//             SystemAPI.GetSingleton<UnitDataLibrary>().library;
//
//         BlobAssetReference<AnimationLibraryBlob> animationLibrary =
//             SystemAPI.GetSingleton<AnimationLibrary>().library;
//
//         state.Dependency = new MeleeContinuousActionJob
//         {
//             deadLookup       = deadLookup,
//             transformLookup  = transformLookup,
//             attackLibrary    = attackLibrary,
//             unitLibrary      = unitLibrary,
//             animationLibrary = animationLibrary,
//         }.ScheduleParallel(state.Dependency);
//     }
// }
//
// [BurstCompile]
// [WithAll(typeof(MeleeContinuousAction))]
// [WithDisabled(typeof(ActionRequest))]
// [WithPresent(typeof(Dead), typeof(ActionTimer), typeof(PathRequest), typeof(AttackRequest), typeof(AnimationRequest))]
// public partial struct MeleeContinuousActionJob : IJobEntity
// {
//     private const float REPATH_DIST_SQ  = 1.0f;
//     private const float HYSTERESIS_MULT = 1.33f;
//
//     [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
//     [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
//     [ReadOnly] public BlobAssetReference<AttackLibraryBlob>    attackLibrary;
//     [ReadOnly] public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
//     [ReadOnly] public BlobAssetReference<AnimationLibraryBlob> animationLibrary;
//
//     public void Execute(
//         in LocalTransform localTransform,
//         in UnitData unitData,
//         ref CurrentAction action,
//         ref ActionTimer actionTimer,
//         ref PathRequest pathRequest,
//         ref AttackRequest attackRequest,
//         ref DynamicBuffer<SetAnimation> setAnimations,
//         in Movement movement,
//         EnabledRefRO<Dead> dead,
//         EnabledRefRW<MeleeContinuousAction> meleeContinuous,
//         EnabledRefRW<ActionRequest> actionRequestEnabled,
//         EnabledRefRW<ActionTimer> actionTimerEnabled,
//         EnabledRefRW<PathRequest> pathRequestEnabled,
//         EnabledRefRW<AttackRequest> attackRequestEnabled,
//         EnabledRefRW<AnimationRequest> animationRequestEnabled)
//     {
//         Entity hostile = action.targetEntity;
//
//         // Action is firing
//         if (actionTimerEnabled.ValueRO && actionTimer.time > 0)
//         {
//             AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
//             return;
//         }
//
//         // 1. Validation & Early Exits
//         bool targetMissing = !transformLookup.TryGetComponent(hostile, out LocalTransform targetTransform);
//         int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
//         ref UnitDataBlob unit = ref unitLibrary.Value.units[unitIndex];
//         bool targetInvalid = !targetMissing && (AIUtils.IsTargetDead(hostile, deadLookup) || AIUtils.IsTargetOutOfRange(localTransform, targetTransform, unit.awarenessRange));
//         bool isDead = dead.ValueRO;
//
//         if (targetMissing || targetInvalid || unitIndex < 0 || isDead)
//         {
//             Terminate(ref attackRequest, ref pathRequest, ref actionTimer, meleeContinuous, actionRequestEnabled, pathRequestEnabled, attackRequestEnabled, actionTimerEnabled);
//             return;
//         }
//
//         // 2. Attack Data Lookup
//         AttackType attackType = AIUtils.GetAttackByAction(ref unit, ActionType.MeleeContinuous);
//         int attackIndex = (int)attackType;
//         if (attackIndex <= 0 || attackIndex >= attackLibrary.Value.attacks.Length)
//         {
//             Terminate(ref attackRequest, ref pathRequest, ref actionTimer, meleeContinuous, actionRequestEnabled, pathRequestEnabled, attackRequestEnabled, actionTimerEnabled);
//             return;
//         }
//
//         ref AttackBlob attackBlob = ref attackLibrary.Value.attacks[attackIndex];
//         float distSq = math.distancesq(localTransform.Position, targetTransform.Position);
//
//         // Use the larger of the two ranges for the "out of range" check
//         float maxRange = attackBlob.range * HYSTERESIS_MULT;
//         if (distSq > (maxRange * maxRange))
//         {
//             bool stoppedMoving = !movement.isMoving;
//             bool targetShifted = math.distancesq(pathRequest.targetPosition, targetTransform.Position) >= REPATH_DIST_SQ;
//             if (stoppedMoving || targetShifted)
//                 AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetTransform.Position, attackBlob.range * 0.9f);
//             return;
//         }
//
//         float rangeSq = attackBlob.range * attackBlob.range;
//         if (distSq > rangeSq)
//         {
//             bool stoppedMoving = !movement.isMoving;
//             bool targetShifted = math.distancesq(pathRequest.targetPosition, targetTransform.Position) >= REPATH_DIST_SQ;
//             if (stoppedMoving || targetShifted)
//                 AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, targetTransform.Position,
//                     stoppedMoving ? 0f : attackBlob.range * 0.9f);
//             return;
//         }
//
//         // 3. Within Attack Range: Halt and Execute Attack
//         AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
//
//         if (actionTimer.time <= 0f && !attackRequestEnabled.ValueRO)
//         {
//             AnimationType animType = AIUtils.GetAnimationByAction(ref unit, ActionType.MeleeContinuous);
//             float animDuration = animationLibrary.Value.clips[(int)animType].duration;
//             if (animDuration <= 0f) return;
//             // Cadence is authored via cooldown, not animation length. Guarantee the hit
//             // (at hitTime) lands before the next swing fires.
//             float interval = math.max(attackBlob.cooldown, attackBlob.hitTime + 0.05f);
//             FireAction(hostile, attackType, animType, interval,
//                 ref actionTimer, ref attackRequest, ref setAnimations,
//                 actionTimerEnabled, attackRequestEnabled, animationRequestEnabled);
//         }
//     }
//
//     // --- Helpers ---
//
//     private void Terminate(
//         ref AttackRequest attackReq,
//         ref PathRequest pathReq,
//         ref ActionTimer actionTimer,
//         EnabledRefRW<MeleeContinuousAction> meleeContinuousEnabled,
//         EnabledRefRW<ActionRequest> actionRequestEnabled,
//         EnabledRefRW<PathRequest> pathRequestEnabled,
//         EnabledRefRW<AttackRequest> attackRequestEnabled,
//         EnabledRefRW<ActionTimer> actionTimerEnabled)
//     {
//         attackRequestEnabled.ValueRW = false;
//         attackReq.hitFired = false;
//         AIUtils.HaltPathing(ref pathReq, pathRequestEnabled);
//         actionRequestEnabled.ValueRW = true;
//         meleeContinuousEnabled.ValueRW = false;
//         actionTimerEnabled.ValueRW = false;
//         actionTimer.time = 0f;
//     }
//
//     private void FireAction(
//         Entity hostile,
//         AttackType type,
//         AnimationType animType,
//         float timerDuration,
//         ref ActionTimer actionTimer,
//         ref AttackRequest attackReq,
//         ref DynamicBuffer<SetAnimation> anims,
//         EnabledRefRW<ActionTimer> actionTimerEnabled,
//         EnabledRefRW<AttackRequest> attackReqEnabled,
//         EnabledRefRW<AnimationRequest> animReqEnabled)
//     {
//         attackReq.targetEntity = hostile;
//         attackReq.attackType = type;
//         attackReq.hitFired = false;
//         attackReq.elapsed = 0f;
//         attackReqEnabled.ValueRW = true;
//
//         anims.Add(new SetAnimation { layer = AnimationLayerType.Action, animation = animType, speed = 1f });
//         animReqEnabled.ValueRW = true;
//
//         actionTimer.time = timerDuration;
//         actionTimerEnabled.ValueRW = true;
//     }
// }
