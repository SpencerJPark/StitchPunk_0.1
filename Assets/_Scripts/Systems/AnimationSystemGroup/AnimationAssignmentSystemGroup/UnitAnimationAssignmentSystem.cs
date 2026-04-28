using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(AnimationAssignmentSystemGroup))]
public partial struct UnitAnimationAssignmentSystem : ISystem
{
    private ComponentLookup<AttackRequest> attackRequestLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
        attackRequestLookup = state.GetComponentLookup<AttackRequest>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<UnitLibraryBlob> library = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        attackRequestLookup.Update(ref state);

        new UnitAnimationAssignmentJob
        {
            library = library,
            attackRequestLookup = attackRequestLookup
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UnitAnimationAssignmentJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> library;
    [ReadOnly] public ComponentLookup<AttackRequest>      attackRequestLookup;

    public void Execute(
        Entity                           entity,
        ref DynamicBuffer<AnimationLayer> layers,
        in UnitData          unitData,
        in Movement          movement,
        in UnitAction        unitAction,
        in LocomotionStance  locomotionStance)
    {
        int unitIndex = (int)unitData.unitType;
        if (unitIndex < 0 || unitIndex >= library.Value.units.Length)
            return;

        ref UnitDataBlob unitBlob = ref library.Value.units[unitIndex];

        // Base layer always reflects locomotion/stance
        AnimationType baseAnimation = GetBaseAnimation(ref unitBlob, locomotionStance.stance, movement.isMoving);
        if (!AnimationUtils.IsCurrentLayer(ref layers, AnimationLayerType.Base, baseAnimation))
            AnimationUtils.SetLayer(ref layers, AnimationLayerType.Base, baseAnimation);

        // Action layer: attack animations are managed by execution systems directly.
        // Skip while an attack is in progress so the execution system's layer is not cleared.
        bool isAttacking = attackRequestLookup.HasComponent(entity) && attackRequestLookup.IsComponentEnabled(entity);
        if (!isAttacking)
        {
            if (IsIdleAction(unitAction.current))
            {
                AnimationUtils.ClearLayer(ref layers, AnimationLayerType.Action);
            }
            else
            {
                AnimationType actionAnimation = GetAnimationForAction(unitAction.current, ref unitBlob, movement.isMoving);
                if (!AnimationUtils.IsCurrentLayerActive(ref layers, AnimationLayerType.Action, actionAnimation))
                    AnimationUtils.SetLayer(ref layers, AnimationLayerType.Action, actionAnimation, 1f, true);
            }
        }
    }

    private static bool IsIdleAction(ActionType action)
    {
        return action == ActionType.Idle || action == ActionType.None;
    }

    private static AnimationType GetBaseAnimation(
        ref UnitDataBlob unitBlob,
        StanceType stance,
        bool isMoving)
    {
        if (stance != StanceType.Normal)
        {
            ref BlobArray<StanceAnimationBlob> stances = ref unitBlob.stanceAnimations;
            for (int i = 0; i < stances.Length; i++)
            {
                if (stances[i].stance == stance)
                    return isMoving ? stances[i].movingAnimation : stances[i].idleAnimation;
            }
            // No entry for this stance — fall through to defaults
        }
        return isMoving ? unitBlob.movingAnimation : unitBlob.idleAnimation;
    }

    private static AnimationType GetAnimationForAction(
        ActionType action,
        ref UnitDataBlob unitBlob,
        bool isMoving)
    {
        ref BlobArray<ActionAnimationMappingBlob> mappings = ref unitBlob.actionAnimations;
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].action == action)
                return mappings[i].animation;
        }
        return isMoving ? unitBlob.movingAnimation : unitBlob.idleAnimation;
    }
}
