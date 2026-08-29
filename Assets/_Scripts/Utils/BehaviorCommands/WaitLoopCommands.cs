using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// WaitTime / LoopUntil — the two blocking commands that share BehaviorQualifiers.Evaluate for
// early-exit / loop-exit checks. Both own their advancement entirely (WaitTime resets on
// Duration/early-exit, LoopUntil jumps back to IntParam or falls through with its own timer/iteration
// reset) — BehaviorExecutionSystem's dispatch just calls in and returns.
[BurstCompile]
public static class WaitLoopCommands
{
    public static void RunWaitTime(
        ref BehaviorCommandContext context,
        Entity                     unit,
        ref StateMachine           stateMachine,
        in BehaviorCommand         cmd,
        in LocalTransform          transform)
    {
        stateMachine.CommandTimer += context.deltaTime;

        // Qualifier-as-early-exit: any ticked flag ends the wait before Duration
        // (e.g. Talk's WaitTime exits when the partner dies or disengages).
        bool earlyExit = cmd.Qualifier != LoopQualifier.None
            && BehaviorQualifiers.Evaluate(unit, in cmd, in stateMachine,
                in transform, in context.transformLookup, in context.deadLookup,
                in context.motivationLookup, in context.stateMachineLookup);

        if (stateMachine.CommandTimer >= cmd.Duration || earlyExit)
        {
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }

    public static void RunLoopUntil(
        ref BehaviorCommandContext context,
        Entity                     unit,
        ref StateMachine           stateMachine,
        in BehaviorCommand         cmd,
        in LocalTransform          transform)
    {
        bool qualified = BehaviorQualifiers.Evaluate(unit, in cmd, in stateMachine,
            in transform, in context.transformLookup, in context.deadLookup,
            in context.motivationLookup, in context.stateMachineLookup);

        // Safety guards — always armed, regardless of ticked qualifiers.
        float timeout    = cmd.Duration > 0f ? cmd.Duration : BehaviorQualifiers.DEFAULT_LOOP_TIMEOUT;
        bool  timedOut   = stateMachine.LoopTimer >= timeout;
        bool  capReached = stateMachine.LoopIterations >= BehaviorQualifiers.MAX_LOOP_ITERATIONS;

        if (qualified || timedOut || capReached)
        {
            if (!qualified && context.loggingEnabled)
            {
                int timedOutFlag = timedOut ? 1 : 0; // hoisted: Burst forbids control flow inside format arguments (BC1352)
                LogUtil.Log(ref context.ecb, context.entityIndex,
                    $"[BehaviorExecution] LoopUntil guard fired in {stateMachine.activeBehavior.Name()} (timedOut: {timedOutFlag}, iterations: {stateMachine.LoopIterations})",
                    LogLevel.Warning, context.timestamp, category: LogCategory.StateMachine);
            }

            stateMachine.CurrentCommandIndex++;
            stateMachine.LoopTimer      = 0f;
            stateMachine.LoopIterations = 0;
        }
        else
        {
            stateMachine.CurrentCommandIndex =
                math.clamp(cmd.IntParam, 0, stateMachine.CurrentCommandIndex);
            stateMachine.LoopIterations++;
        }

        stateMachine.CommandTimer = 0f;
    }
}
