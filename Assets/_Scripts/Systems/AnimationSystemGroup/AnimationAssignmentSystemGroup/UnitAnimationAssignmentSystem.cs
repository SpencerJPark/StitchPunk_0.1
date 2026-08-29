using DotsMovementToolkit;
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
            library = library,
        }.ScheduleParallel();
    }
}

[BurstCompile]
[WithPresent(typeof(Movement))] // must still assign the Death animation after Movement is disabled
public partial struct UnitAnimationAssignmentJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> library;

    public void Execute(
        ref DynamicBuffer<AnimationLayer> layers,
        in UnitData         unitData,
        in Movement         movement,
        in UnitAction       unitAction,
        in LocomotionStance locomotionStance)
    {
        int unitIndex = (int)unitData.unitType;
        if (unitIndex < 0 || unitIndex >= library.Value.units.Length)
            return;

        ref UnitDataBlob unitBlob = ref library.Value.units[unitIndex];

        // Base layer always reflects locomotion/stance
        AnimationType baseAnimation = GetBaseAnimation(ref unitBlob, locomotionStance.stance, movement.isMoving);
        if (!AnimationUtils.IsCurrentLayer(ref layers, AnimationLayerType.Base, baseAnimation))
            AnimationUtils.SetLayer(ref layers, AnimationLayerType.Base, baseAnimation);

        // Action layer: a non-looping clip (e.g. attack) owns this layer until it finishes.
        // AnimationRequestSystem runs first this frame and sets active=true, looping=false;
        // AnimationTimeSystem clears active when the clip ends, returning control here.
        if (!HasActiveNonLoopingLayer(ref layers, AnimationLayerType.Action))
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

    private static bool HasActiveNonLoopingLayer(ref DynamicBuffer<AnimationLayer> layers, AnimationLayerType layerType)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].layer == layerType)
                return layers[i].active && !layers[i].looping;
        }
        return false;
    }

    private static bool IsIdleAction(ActionType action)
    {
        return action == ActionType.Idle;
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
