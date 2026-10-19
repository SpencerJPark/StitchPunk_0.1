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

    private ComponentLookup<Item>           itemLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Dead>           deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<BrainLibrary>();
        itemLookup      = state.GetComponentLookup<Item>(true);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        deadLookup      = state.GetComponentLookup<Dead>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        itemLookup.Update(ref state);
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);

        state.Dependency = new ThrowAwarenessJob
        {
            itemLookup      = itemLookup,
            transformLookup = transformLookup,
            deadLookup      = deadLookup,
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
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>  itemLibrary;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;

    public void Execute(
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
        if (distance < blob.aiThrowMinRange || distance > blob.aiThrowMaxRange) return;

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
}
