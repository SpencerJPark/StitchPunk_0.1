using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

// WaitForClipFinished — blocking command that completes on ReservedEventKeys.ClipFinished for
// layer LayerIndex, or ClipResolveFailed for that layer. Two rails stop a hang: a unit with no
// AnimEventOutput buffer is skipped (it can never receive the event), and an unauthored Duration
// falls back to DEFAULT_TIMEOUT_SECONDS so a clip that never reports cannot freeze a unit.
[BurstCompile]
public static class WaitForClipFinishedCommand
{
    // Used when Duration is not authored, so a clip that never reports cannot freeze a unit.
    public const float DEFAULT_TIMEOUT_SECONDS = 5f;

    public static void Run(
        ref BehaviorCommandContext context,
        Entity                     unit,
        ref StateMachine           stateMachine,
        in BehaviorCommand         cmd)
    {
        stateMachine.CommandTimer += context.deltaTime;

        bool unitCanReportAnimation = context.animEventOutputLookup.HasBuffer(unit);
        if (!unitCanReportAnimation)
        {
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
            return;
        }

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

        float timeout = cmd.Duration > 0f ? cmd.Duration : WaitForClipFinishedCommand.DEFAULT_TIMEOUT_SECONDS;
        bool timedOut = stateMachine.CommandTimer >= timeout;

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
