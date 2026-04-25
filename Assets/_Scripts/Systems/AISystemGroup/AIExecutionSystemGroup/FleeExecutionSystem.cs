using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Executes the Flee action. Reads the highest-threat attacker from ThreatEntry buffer,
/// computes a flee destination away from them, paths there, then returns to NeedsAction.
///
/// States:
///   Pathing  (FleeAction enabled, ArrivedAtTarget disabled) — path away from threat
///   Arrived  (FleeAction enabled, ArrivedAtTarget enabled)  — short recover pause
///   Complete — disable FleeAction, re-enable NeedsAction
///
/// Timeout: if no threat data is available or arrive takes too long, gives up and re-evaluates.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct FleeExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new FleeJob
        {
            transformLookup = transformLookup,
            deltaTime       = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain), typeof(FleeAction))]
[WithDisabled(typeof(NeedsAction))]
public partial struct FleeJob : IJobEntity
{
    private const float FLEE_DISTANCE    = 15f;
    private const float ARRIVE_RANGE     = 1.5f;
    private const float STOPPING_DIST    = 1.0f;
    private const float RECOVER_DURATION = 1.0f;
    private const float FLEE_TIMEOUT     = 8.0f;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    public float deltaTime;

    public void Execute(
        in LocalTransform transform,
        in DynamicBuffer<ThreatEntry> threats,
        ref PathRequest pathRequest,
        ref PathfindingAgent agent,
        ref Movement movement,
        ref ActionTimer actionTimer,
        EnabledRefRW<FleeAction> fleeActionEnabled,
        EnabledRefRW<NeedsAction> needsActionEnabled,
        EnabledRefRW<ArrivedAtTarget> arrivedEnabled,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dstarEnabled,
        EnabledRefRW<FlowFieldFollower> flowEnabled)
    {
        // Recover pause after arriving
        if (arrivedEnabled.ValueRO)
        {
            actionTimer.time -= deltaTime;
            if (actionTimer.time <= 0f)
            {
                arrivedEnabled.ValueRW = false;
                fleeActionEnabled.ValueRW = false;
                needsActionEnabled.ValueRW = true;
            }
            return;
        }

        // Timeout guard — give up if we've been fleeing too long
        actionTimer.time -= deltaTime;
        if (actionTimer.time <= 0f)
        {
            AIUtils.HaltPathing(ref agent, pathRequestEnabled, dstarEnabled, flowEnabled);
            fleeActionEnabled.ValueRW = false;
            needsActionEnabled.ValueRW = true;
            return;
        }

        // Check arrival
        if (AIUtils.IsTargetInRange(transform, ref arrivedEnabled, pathRequest.targetPosition, ARRIVE_RANGE))
        {
            AIUtils.HaltPathing(ref agent, pathRequestEnabled, dstarEnabled, flowEnabled);
            actionTimer.time = RECOVER_DURATION;
            return;
        }

        // First frame: issue the flee path
        if (!pathRequestEnabled.ValueRO)
        {
            actionTimer.time = FLEE_TIMEOUT;

            // Find highest-threat attacker position
            Entity topAttacker = Entity.Null;
            float  topThreat   = 0f;
            for (int i = 0; i < threats.Length; i++)
            {
                if (threats[i].threatScore > topThreat)
                {
                    topThreat   = threats[i].threatScore;
                    topAttacker = threats[i].attackerEntity;
                }
            }

            float3 fleeTarget;
            if (topAttacker != Entity.Null &&
                transformLookup.TryGetComponent(topAttacker, out LocalTransform threatTransform))
            {
                float3 awayDir = math.normalizesafe(transform.Position - threatTransform.Position);
                fleeTarget = transform.Position + awayDir * FLEE_DISTANCE;
            }
            else
            {
                // No threat data — flee is a no-op, return to selection immediately
                fleeActionEnabled.ValueRW = false;
                needsActionEnabled.ValueRW = true;
                return;
            }

            AIUtils.BeginPathRequest(
                ref pathRequest, pathRequestEnabled,
                ref agent, ref movement,
                fleeTarget, STOPPING_DIST);
        }
    }
}
