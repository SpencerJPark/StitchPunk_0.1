using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(AnimationAssignmentSystemGroup))]
public partial struct UnitAnimationAssignmentSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<UnitLibraryBlob> library = SystemAPI.GetSingleton<UnitDataLibrary>().library;

        new UnitAnimationAssignmentJob
        {
            library = library
        }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct UnitAnimationAssignmentJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> library;

    public void Execute(
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
        // Idle/None: clear the Action layer. Other actions: assign looping animation.
        if (IsIdleAction(unitAction.current))
        {
            AnimationUtils.ClearLayer(ref layers, AnimationLayerType.Action);
        }
        else if (!IsAttackAction(unitAction.current))
        {
            AnimationType actionAnimation = GetAnimationForAction(unitAction.current, ref unitBlob, movement.isMoving);
            if (!AnimationUtils.IsCurrentLayerActive(ref layers, AnimationLayerType.Action, actionAnimation))
                AnimationUtils.SetLayer(ref layers, AnimationLayerType.Action, actionAnimation, 1f, true);
        }
        // Attack actions: Action layer is managed by MeleeAttackExecutionSystem + AttackHitFrameSystem
    }

    private static bool IsIdleAction(ActionType action)
    {
        return action == ActionType.Idle || action == ActionType.None;
    }

    private static bool IsAttackAction(ActionType action)
    {
        return action == ActionType.Melee
            || action == ActionType.Projectile
            || action == ActionType.Swing
            || action == ActionType.Throw
            || action == ActionType.Shoot
            || action == ActionType.Spawn;
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
