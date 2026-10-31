using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
[UpdateAfter(typeof(EnemyAwarenessSystem))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
[UpdateBefore(typeof(GuardPostAwarenessSystem))]
public partial struct ThrowAwarenessSystem : ISystem
{
    // Above the tier-3 fight so a unit closing to melee stops and throws; GuardPost's leash (5) still wins.
    public const int THROW_PRIORITY = AIPriorityTier.Critical;

    public const int    KEEP_RANGE_PRIORITY               = AIPriorityTier.Critical;
    public const float  KEEP_RANGE_PREFERRED_FRACTION     = 0.3f;
    public const double KEEP_RANGE_COOLDOWN_SECONDS       = 4.0;
    public const float  KEEP_RANGE_FAN_STEP_DEGREES       = 20f;

    private ComponentLookup<Item>           itemLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Dead>           deadLookup;
    private ComponentLookup<GuardPost>      guardPostLookup;
    private ComponentLookup<TacticsCooldown> tacticsCooldownLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<BrainLibrary>();
        itemLookup      = state.GetComponentLookup<Item>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        deadLookup      = state.GetComponentLookup<Dead>(true);
        guardPostLookup       = state.GetComponentLookup<GuardPost>(true);
        tacticsCooldownLookup = state.GetComponentLookup<TacticsCooldown>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        itemLookup.Update(ref state);
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        guardPostLookup.Update(ref state);
        tacticsCooldownLookup.Update(ref state);

        state.Dependency = new ThrowAwarenessJob
        {
            itemLookup      = itemLookup,
            transformLookup = transformLookup,
            deadLookup      = deadLookup,
            guardPostLookup       = guardPostLookup,
            tacticsCooldownLookup = tacticsCooldownLookup,
            elapsedTime           = SystemAPI.Time.ElapsedTime,
            itemLibrary     = SystemAPI.GetSingleton<ItemLibrary>().library,
            aiConfig        = SystemAPI.GetSingleton<BrainLibrary>().blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct ThrowAwarenessJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Item>           itemLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Dead>           deadLookup;
    [ReadOnly] public ComponentLookup<GuardPost>      guardPostLookup;
    // Each iteration writes only its own entity's cooldown.
    [NativeDisableParallelForRestriction] public ComponentLookup<TacticsCooldown> tacticsCooldownLookup;
    public double elapsedTime;
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>  itemLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;

    public void Execute(
        Entity                            self,
        in UtilityBrain                   brain,
        in UnitEquip                      unitEquip,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (unitEquip.equipItemEntity == Entity.Null) return;
        if (!itemLookup.TryGetComponent(unitEquip.equipItemEntity, out Item item)) return;

        int itemIndex = (int)item.itemType;
        if (itemIndex < 0 || itemIndex >= itemLibrary.Value.items.Length) return;

        ref ItemBlob blob = ref itemLibrary.Value.items[itemIndex];
        if (blob.aiThrowMaxRange <= 0f) return;

        Entity target = Entity.Null;
        for (int index = 0; index < actions.Length; index++)
        {
            UtilityActions option = actions[index];
            if (option.actionType.IsCombatAction()
                && option.actionType != ActionType.Flee
                && option.actionType != ActionType.Throw
                && option.targetEntity != Entity.Null)
            {
                target = option.targetEntity;
                break;
            }
        }

        if (target == Entity.Null
            && stateMachine.action.IsCombatAction()
            && stateMachine.action != ActionType.Flee
            && stateMachine.action != ActionType.Throw)
        {
            target = stateMachine.targetEntity;
        }

        if (target == Entity.Null) return;
        if (deadLookup.HasComponent(target) && deadLookup.IsComponentEnabled(target)) return;
        if (!transformLookup.TryGetComponent(target, out LocalTransform targetTransform)) return;

        float3 offset   = targetTransform.Position - transform.Position;
        float  distance = math.length(new float2(offset.x, offset.z));
        if (distance > blob.aiThrowMaxRange) return;

        // KeepRange and Throw share Critical but never coexist: below vs at/above min range.
        if (distance < blob.aiThrowMinRange)
        {
            EmitKeepRange(self, brain, transform, stateMachine, targetTransform, ref blob, ref actions);
            return;
        }

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Throw);
        if (defIndex < 0) return;

        actions.Add(new UtilityActions
        {
            actionType      = ActionType.Throw,
            actionDefIndex  = defIndex,
            priority        = ThrowAwarenessSystem.THROW_PRIORITY,
            targetEntity    = target,
        });
    }

    private void EmitKeepRange(
        Entity                            self,
        in UtilityBrain                   brain,
        in LocalTransform                 transform,
        in StateMachine                   stateMachine,
        in LocalTransform                 targetTransform,
        ref ItemBlob                      blob,
        ref DynamicBuffer<UtilityActions> actions)
    {
        if (!tacticsCooldownLookup.TryGetComponent(self, out TacticsCooldown cooldown)) return;
        if (elapsedTime < cooldown.keepRangeReadyAtSeconds) return;
        if (stateMachine.action == ActionType.KeepRange) return;

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.KeepRange);
        if (defIndex < 0) return;

        float3 away     = transform.Position - targetTransform.Position;
        away.y          = 0f;
        float  awayLengthSq = math.lengthsq(away);
        away = awayLengthSq < 0.0001f ? new float3(0f, 0f, 1f) : away / math.sqrt(awayLengthSq);

        int    fanStep     = (int)(((uint)self.Index * 2654435761u) % 5u) - 2;
        float  fanRadians  = math.radians(fanStep * ThrowAwarenessSystem.KEEP_RANGE_FAN_STEP_DEGREES);
        float3 rotatedAway = math.mul(quaternion.RotateY(fanRadians), away);

        float  preferredRange = math.lerp(blob.aiThrowMinRange, blob.aiThrowMaxRange, ThrowAwarenessSystem.KEEP_RANGE_PREFERRED_FRACTION);
        float3 destination    = targetTransform.Position + rotatedAway * preferredRange;
        destination.y         = transform.Position.y;

        // Backing off must never walk a guard into its tier-5 leash return.
        if (guardPostLookup.TryGetComponent(self, out GuardPost guardPost))
        {
            float3 anchorOffset = destination - guardPost.anchorPosition;
            anchorOffset.y      = 0f;
            float  anchorDistance = math.length(anchorOffset);
            float  maxDistance    = guardPost.leashRadius * GuardPostAwarenessSystem.REENGAGE_LEASH_FRACTION;
            if (anchorDistance > maxDistance)
            {
                float3 clamped = guardPost.anchorPosition + anchorOffset / anchorDistance * maxDistance;
                destination    = new float3(clamped.x, destination.y, clamped.z);
            }
        }

        actions.Add(new UtilityActions
        {
            actionType        = ActionType.KeepRange,
            actionDefIndex    = defIndex,
            priority          = ThrowAwarenessSystem.KEEP_RANGE_PRIORITY,
            targetPosition    = destination,
            hasTargetPosition = true,
        });

        cooldown.keepRangeReadyAtSeconds = elapsedTime + ThrowAwarenessSystem.KEEP_RANGE_COOLDOWN_SECONDS;
        tacticsCooldownLookup[self]      = cooldown;
    }
}
