using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

// PlayAnimation / PlayActionAnimation / StopAnimation — all thin PlaybackApi wrappers,
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
            || cmd.AnimationKey == 0)
            return;

        DynamicBuffer<AnimationCommand> playCommands = context.animationCommandLookup[unit];
        PlaybackApi.PlayAnimation(
            ref playCommands,
            context.animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(unit),
            cmd.AnimationKey,
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
        if (unitIndex < 0) return;

        // Played by name — the toolkit re-picks the directional clip against the actor's own
        // ActorFacing (UnitFacingSystem writes it), so this no longer resolves a facing itself.
        uint animationKey = AIUtils.GetAnimationKeyByAction(ref context.unitLibrary.Value.units[unitIndex], stateMachine.action);
        if (animationKey == 0) return;

        DynamicBuffer<AnimationCommand> playCommands = context.animationCommandLookup[unit];
        PlaybackApi.PlayAnimation(
            ref playCommands,
            context.animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(unit),
            animationKey,
            speed: cmd.FloatParam > 0f ? cmd.FloatParam : 1f,
            loop: cmd.Looping ? LoopMode.Loop : LoopMode.Once);
    }

    // No stored key names what to stop (a StopAnimation command carries no data). Chosen fallback
    // (ActorProfileCutover_System.md §5 P4): stop every layer whose PlaybackLayer.animationKey != 0
    // except layer 0 (Base never carries an action clip a behavior would need to interrupt) — a
    // faithful "clear whatever action/override/etc. clip is live" without needing the started key.
    public static void RunStopAnimation(
        ComponentLookup<AnimationCommandPending> animationCommandPendingLookup,
        BufferLookup<AnimationCommand>           animationCommandLookup,
        BufferLookup<PlaybackLayer>              playbackLayerLookup,
        Entity                                    unit)
    {
        if (!animationCommandPendingLookup.HasComponent(unit)
            || !animationCommandLookup.HasBuffer(unit)
            || !playbackLayerLookup.HasBuffer(unit))
            return;

        DynamicBuffer<AnimationCommand> stopCommands = animationCommandLookup[unit];
        DynamicBuffer<PlaybackLayer> layers = playbackLayerLookup[unit];
        for (int layerIndex = 1; layerIndex < layers.Length; layerIndex++)
        {
            uint animationKey = layers[layerIndex].animationKey;
            if (animationKey == 0)
                continue;

            PlaybackApi.StopAnimation(
                ref stopCommands,
                animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(unit),
                animationKey,
                blendDuration: 0f);
        }
    }
}
