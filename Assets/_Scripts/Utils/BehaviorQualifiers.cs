using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Evaluates LoopQualifier flags for LoopUntil (and later: early-exit on blocking commands).
// OR semantics — returns true (exit) when ANY ticked flag is satisfied.
// Missing-data convention: a qualifier whose referenced data is gone evaluates TRUE,
// so a loop can never spin on a despawned target.
[BurstCompile]
public static class BehaviorQualifiers
{
    public const float  DEFAULT_LOOP_TIMEOUT = 60f;
    public const ushort MAX_LOOP_ITERATIONS  = 4096;

    public static bool Evaluate(
        Entity                            unit,
        in BehaviorCommand                command,
        in StateMachine                   stateMachine,
        in LocalTransform                 transform,
        in ComponentLookup<LocalTransform> transformLookup,
        in ComponentLookup<Dead>           deadLookup,
        in BufferLookup<Motivation>        motivationLookup,
        in ComponentLookup<StateMachine>   stateMachineLookup)
    {
        LoopQualifier qualifier = command.Qualifier;
        if (qualifier == LoopQualifier.None) return false;

        Entity target = stateMachine.targetEntity;

        if ((qualifier & LoopQualifier.TargetDead) != 0)
        {
            if (target == Entity.Null) return true;
            if (!transformLookup.HasComponent(target)) return true; // entity destroyed
            if (deadLookup.HasComponent(target) && deadLookup.IsComponentEnabled(target)) return true;
        }

        if ((qualifier & LoopQualifier.TargetLost) != 0)
        {
            if (target == Entity.Null || !transformLookup.HasComponent(target)) return true;
        }

        if ((qualifier & LoopQualifier.TargetOutOfRange) != 0)
        {
            if (target == Entity.Null
                || !transformLookup.TryGetComponent(target, out LocalTransform targetTransform))
                return true;
            float rangeSq = command.FloatParam * command.FloatParam;
            if (math.distancesq(transform.Position, targetTransform.Position) > rangeSq) return true;
        }

        if ((qualifier & LoopQualifier.TimerExpired) != 0)
        {
            float timeout = command.Duration > 0f ? command.Duration : DEFAULT_LOOP_TIMEOUT;
            if (stateMachine.LoopTimer >= timeout) return true;
        }

        if ((qualifier & LoopQualifier.MotivationSatisfied) != 0)
        {
            if (!motivationLookup.TryGetBuffer(unit, out DynamicBuffer<Motivation> motivations))
                return true;
            bool foundNeed = false;
            for (int i = 0; i < motivations.Length; i++)
            {
                if (motivations[i].needType != (NeedType)command.QualifierIntParam) continue;
                foundNeed = true;
                if (motivations[i].value >= command.QualifierFloatParam) return true;
                break;
            }
            if (!foundNeed) return true; // unit doesn't track this need — exit rather than spin
        }

        if ((qualifier & LoopQualifier.TargetNotEngagedWithSelf) != 0)
        {
            if (target == Entity.Null) return true;
            if (!stateMachineLookup.TryGetComponent(target, out StateMachine targetStateMachine))
                return true;
            if (targetStateMachine.targetEntity != unit) return true;
        }

        return false;
    }
}
