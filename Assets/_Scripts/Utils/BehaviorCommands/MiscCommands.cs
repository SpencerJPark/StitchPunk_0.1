using Unity.Burst;
using Unity.Entities;

// ReleaseInteraction / PlaySound — both fire-and-advance. ReleaseInteraction takes its dependencies
// directly (not BehaviorCommandContext): BehaviorInterruptSystem's interruptionCleanup pass reuses
// it and doesn't carry the execution job's full lookup set.
[BurstCompile]
public static class MiscCommands
{
    public static void RunReleaseInteraction(
        double                                timestamp,
        in BehaviorCommand                     cmd,
        Entity                                  targetEntity,
        ref DynamicBuffer<RecentInteraction>   recentInteractions)
    {
        if (targetEntity == Entity.Null) return;

        float cooldownEnd = (float)timestamp + (cmd.FloatParam > 0f ? cmd.FloatParam : 30f);
        if (recentInteractions.Length >= 8) recentInteractions.RemoveAt(0);
        recentInteractions.Add(new RecentInteraction
        {
            entity          = targetEntity,
            cooldownEndTime = cooldownEnd,
        });
    }

    public static void RunPlaySound(
        ref BehaviorCommandContext context,
        Entity                     unit,
        in BehaviorCommand         cmd)
    {
        // Behaviour-level audio cue (e.g. a yell when Flee starts). Fire-and-advance.
        SoundUtil.PlayOn(ref context.ecb, context.entityIndex, (SoundType)cmd.IntParam, unit);
    }
}
