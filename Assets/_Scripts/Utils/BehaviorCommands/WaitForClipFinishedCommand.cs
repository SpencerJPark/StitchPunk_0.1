using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

// WaitForClipFinished — blocking command that completes on ReservedEventKeys.ClipFinished for
// layer LayerIndex; also completes on ClipResolveFailed for that layer, so a missing clip cannot
// hang the behavior forever. Duration (0 = none) is the same safety rail WaitForAnimEvent uses.
[BurstCompile]
public static class WaitForClipFinishedCommand
{
    public static void Run(
        ref BehaviorCommandContext context,
        Entity                     unit,
        ref StateMachine           stateMachine,
        in BehaviorCommand         cmd)
    {
        stateMachine.CommandTimer += context.deltaTime;

        bool clipFinished = false;
        if (context.animEventsPendingLookup.HasComponent(unit)
            && context.animEventsPendingLookup.IsComponentEnabled(unit)
            && context.animEventOutputLookup.HasBuffer(unit))
        {
            DynamicBuffer<AnimEventOutput> events = context.animEventOutputLookup[unit];
            for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
            {
                AnimEventOutput animEvent = events[eventIndex];
                if (animEvent.layerIndex != cmd.LayerIndex)
                    continue;

                if (animEvent.eventKey == (uint)ReservedEventKeys.ClipFinished
                    || animEvent.eventKey == (uint)ReservedEventKeys.ClipResolveFailed)
                {
                    clipFinished = true;
                    break;
                }
            }
        }

        bool timedOut = cmd.Duration > 0f && stateMachine.CommandTimer >= cmd.Duration;

        if (timedOut && !clipFinished && context.loggingEnabled)
        {
            LogUtil.Log(ref context.ecb, context.entityIndex,
                $"[BehaviorExecution] WaitForClipFinished timed out waiting on layer {cmd.LayerIndex}",
                LogLevel.Warning, context.timestamp, category: LogCategory.StateMachine);
        }

        if (clipFinished || timedOut)
        {
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }
}
