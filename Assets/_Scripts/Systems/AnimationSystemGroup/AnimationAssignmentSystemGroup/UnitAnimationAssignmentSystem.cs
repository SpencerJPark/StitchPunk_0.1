using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;

// Decides which clip each layer should be playing and issues AnimationCommands only on change —
// commands are requests, not state, so re-issuing Play every frame would restart the clip's
// crossfade/queue machinery for no reason. PlaybackQuery answers "what's actually playing" against
// the toolkit's own PlaybackLayer buffer instead of tracking a shadow copy here.
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
        ref DynamicBuffer<AnimationCommand>   commands,
        EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
        in DynamicBuffer<PlaybackLayer>       playbackLayers,
        in UnitData         unitData,
        in Movement         movement,
        in UnitAction       unitAction,
        in LocomotionStance locomotionStance,
        in UnitFacing       unitFacing)
    {
        int unitIndex = (int)unitData.unitType;
        if (unitIndex < 0 || unitIndex >= library.Value.units.Length)
            return;

        ref UnitDataBlob unitBlob = ref library.Value.units[unitIndex];

        // Per-set snap + east-side pick (DirectionFacing_System.md §5): a set with fewer authored
        // directions than the actor turns through folds onto whatever it actually has — a
        // Two-coverage walk on a Six-turning actor just mirrors left/right, no special case needed.
        // The mirror half of this (mirrorX) is served through PartFacing, not the clip pick.
        FacingResolver.ResolveClipFacing(
            unitFacing.current, unitBlob.animationDirections, out Direction clipFacing, out bool _);

        // Base layer always reflects locomotion/stance
        ClipId baseClip = GetBaseAnimation(ref unitBlob, locomotionStance.stance, movement.isMoving, clipFacing);
        if (baseClip.IsValid
            && !PlaybackQuery.IsPlaying(playbackLayers, (byte)AnimationToolkitLayer.Base, baseClip))
        {
            AnimationCommandUtil.Play(ref commands, commandPendingEnabled,
                (byte)AnimationToolkitLayer.Base, baseClip, loop: LoopMode.Loop);
        }

        // Action layer: a non-looping clip (e.g. attack) owns this layer until it finishes — the
        // toolkit deactivates a LoopMode.Once layer on completion, so IsLayerActive answers false
        // and control returns here.
        if (!IsLayerActive(playbackLayers, (byte)AnimationToolkitLayer.Action))
        {
            if (IsIdleAction(unitAction.current))
            {
                // Nothing to stop — an inactive layer needs no Stop command.
            }
            else
            {
                ClipId actionClip = GetAnimationForAction(unitAction.current, ref unitBlob, movement.isMoving, clipFacing);
                if (actionClip.IsValid
                    && !PlaybackQuery.IsPlaying(playbackLayers, (byte)AnimationToolkitLayer.Action, actionClip))
                {
                    AnimationCommandUtil.Play(ref commands, commandPendingEnabled,
                        (byte)AnimationToolkitLayer.Action, actionClip, loop: LoopMode.Once);
                }
            }
        }
    }

    private static bool IsLayerActive(in DynamicBuffer<PlaybackLayer> layers, byte layerIndex)
    {
        if (layerIndex >= layers.Length)
            return false;
        return (layers[layerIndex].flags & PlaybackFlags.Active) != 0;
    }

    private static bool IsIdleAction(ActionType action)
    {
        return action == ActionType.Idle;
    }

    private static ClipId GetBaseAnimation(
        ref UnitDataBlob unitBlob,
        StanceType stance,
        bool isMoving,
        Direction clipFacing)
    {
        if (stance != StanceType.Normal)
        {
            ref BlobArray<StanceAnimationBlob> stances = ref unitBlob.stanceAnimations;
            for (int i = 0; i < stances.Length; i++)
            {
                if (stances[i].stance == stance)
                    return (isMoving ? stances[i].movingAnimation : stances[i].idleAnimation).ResolveSlot(clipFacing);
            }
        }
        return (isMoving ? unitBlob.movingAnimation : unitBlob.idleAnimation).ResolveSlot(clipFacing);
    }

    private static ClipId GetAnimationForAction(
        ActionType action,
        ref UnitDataBlob unitBlob,
        bool isMoving,
        Direction clipFacing)
    {
        ref BlobArray<ActionAnimationMappingBlob> mappings = ref unitBlob.actionAnimations;
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].action == action)
                return mappings[i].animation.ResolveSlot(clipFacing);
        }
        return (isMoving ? unitBlob.movingAnimation : unitBlob.idleAnimation).ResolveSlot(clipFacing);
    }
}
