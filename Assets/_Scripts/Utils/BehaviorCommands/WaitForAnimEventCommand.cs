using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

// WaitForAnimEvent — blocking command that completes the frame the unit's AnimEventOutput buffer
// carries key IntParam (cast to uint) on layer LayerIndex. Gated on AnimEventsPending so
// event-less frames never pay for the buffer scan. Two rails stop a hang: a unit with no
// AnimEventOutput buffer is skipped (it can never receive the event), and an unauthored Duration
// falls back to WaitForClipFinishedCommand.DEFAULT_TIMEOUT_SECONDS.
[BurstCompile]
public static class WaitForAnimEventCommand
{
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

        bool eventFired = false;
        if (context.animEventsPendingLookup.HasComponent(unit)
            && context.animEventsPendingLookup.IsComponentEnabled(unit)
            && context.animEventOutputLookup.HasBuffer(unit))
        {
            DynamicBuffer<AnimEventOutput> events = context.animEventOutputLookup[unit];
            uint targetKey = (uint)cmd.IntParam;
            for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
            {
                if (events[eventIndex].eventKey == targetKey && events[eventIndex].layerIndex == cmd.LayerIndex)
                {
                    eventFired = true;
                    break;
                }
            }
        }

        float timeout = cmd.Duration > 0f ? cmd.Duration : WaitForClipFinishedCommand.DEFAULT_TIMEOUT_SECONDS;
        bool timedOut = stateMachine.CommandTimer >= timeout;

        if (timedOut && !eventFired && context.loggingEnabled)
        {
            LogUtil.Log(ref context.ecb, context.entityIndex,
                $"[BehaviorExecution] WaitForAnimEvent timed out waiting for key {cmd.IntParam} on layer {cmd.LayerIndex}",
                LogLevel.Warning, context.timestamp, category: LogCategory.StateMachine);
        }

        if (eventFired || timedOut)
        {
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }
}
