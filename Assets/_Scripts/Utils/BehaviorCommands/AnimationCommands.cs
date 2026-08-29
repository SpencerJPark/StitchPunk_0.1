using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

// PlayAnimation / PlayActionAnimation / StopAnimation — all thin AnimationCommandUtil wrappers,
// fire-and-advance. StopAnimation takes its dependencies directly (not BehaviorCommandContext):
// BehaviorInterruptSystem's interruptionCleanup pass reuses it and doesn't carry the execution
// job's full lookup set.
[BurstCompile]
public static class AnimationCommands
{
    public static void RunPlayAnimation(
        ref BehaviorCommandContext context,
        Entity                     unit,
        in BehaviorCommand         cmd)
    {
        if (!context.animationCommandPendingLookup.HasComponent(unit)
            || !context.animationCommandLookup.HasBuffer(unit)
            || !cmd.AnimationClip.IsValid)
            return;

        DynamicBuffer<AnimationCommand> playCommands = context.animationCommandLookup[unit];
        AnimationCommandUtil.Play(
            ref playCommands,
            context.animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(unit),
            (byte)AnimationToolkitLayer.Action,
            cmd.AnimationClip,
            speed: cmd.FloatParam > 0f ? cmd.FloatParam : 1f,
            loop: cmd.Looping ? LoopMode.Loop : LoopMode.Once);
    }

    public static void RunPlayActionAnimation(
        ref BehaviorCommandContext context,
        Entity                     unit,
        in BehaviorCommand         cmd,
        in StateMachine            stateMachine,
        in UtilityBrain            brain)
    {
        if (!context.animationCommandPendingLookup.HasComponent(unit)
            || !context.animationCommandLookup.HasBuffer(unit))
            return;

        int unitIndex = context.unitLibrary.Value.FindByUnitType(brain.unitType);
        Direction clipFacing = Direction.SouthEast;
        if (context.unitFacingLookup.HasComponent(unit))
        {
            FacingResolver.ResolveClipFacing(
                context.unitFacingLookup[unit].current,
                unitIndex >= 0 ? context.unitLibrary.Value.units[unitIndex].animationDirections : AnimationDirections.One,
                out clipFacing, out bool _);
        }
        ClipId actionAnimation = unitIndex >= 0
            ? AIUtils.GetAnimationByAction(ref context.unitLibrary.Value.units[unitIndex], stateMachine.action, clipFacing)
            : default;

        if (!actionAnimation.IsValid) return;

        DynamicBuffer<AnimationCommand> playCommands = context.animationCommandLookup[unit];
        AnimationCommandUtil.Play(
            ref playCommands,
            context.animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(unit),
            (byte)AnimationToolkitLayer.Action,
            actionAnimation,
            speed: cmd.FloatParam > 0f ? cmd.FloatParam : 1f,
            loop: cmd.Looping ? LoopMode.Loop : LoopMode.Once);
    }

    public static void RunStopAnimation(
        ComponentLookup<AnimationCommandPending> animationCommandPendingLookup,
        BufferLookup<AnimationCommand>           animationCommandLookup,
        Entity                                    unit)
    {
        if (!animationCommandPendingLookup.HasComponent(unit) || !animationCommandLookup.HasBuffer(unit))
            return;

        DynamicBuffer<AnimationCommand> stopCommands = animationCommandLookup[unit];
        AnimationCommandUtil.Stop(
            ref stopCommands,
            animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(unit),
            (byte)AnimationToolkitLayer.Action,
            blendDuration: 0f);
    }
}
