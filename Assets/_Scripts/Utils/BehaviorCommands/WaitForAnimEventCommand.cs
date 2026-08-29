using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Entities;

// WaitForAnimEvent — blocking command that completes the frame the unit's AnimEventOutput buffer
// carries key IntParam (cast to uint) on layer LayerIndex. Gated on AnimEventsPending so
// event-less frames never pay for the buffer scan. Duration (0 = none) is the safety rail: same
// missing-data-completes philosophy as the qualifier semantics — a clip that never fires the
// event cannot hang the behavior forever.
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

        bool timedOut = cmd.Duration > 0f && stateMachine.CommandTimer >= cmd.Duration;

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
