using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// Shared movement system — walks a unit toward a target entity and signals arrival.
///
/// Enable MoveToTargetRequest on a unit to begin chasing. This system:
///   1. Requests a path toward the target on the first frame (or when the target moves > 1 unit).
///   2. Each frame checks distance. When distance &lt; arrivalRange it disables MoveToTargetRequest
///      and enables ArrivedAtTarget.
///
/// Execution systems (e.g. CombatAttackExecutionSystem, InteractionExecutionSystem) watch
/// ArrivedAtTarget to know when to begin their action.
///
/// If the target is dead or missing, MoveToTargetRequest is disabled.
/// The calling execution system detects that state and cleans up.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct MoveToTargetSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Alive>          aliveLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        aliveLookup     = state.GetComponentLookup<Alive>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        aliveLookup.Update(ref state);

        state.Dependency = new MoveToTargetJob
        {
            transformLookup = transformLookup,
            aliveLookup     = aliveLookup,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithDisabled(typeof(PlayerControlled))]
[WithAll(typeof(MoveToTargetRequest))]
[WithPresent(typeof(PathRequest), typeof(PathfindingAgent), typeof(Movement))]
public partial struct MoveToTargetJob : IJobEntity
{
    private const float REPATH_DIST_SQ = 1.0f;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [ReadOnly] public ComponentLookup<Alive>          aliveLookup;

    public void Execute(
        in LocalTransform                 myTransform,
        ref MoveToTargetRequest           request,
        ref PathfindingAgent              pathAgent,
        ref PathRequest                   pathRequest,
        ref Movement                      movement,
        EnabledRefRW<MoveToTargetRequest> moveRequestEnabled,
        EnabledRefRW<ArrivedAtTarget>     arrivedEnabled,
        EnabledRefRW<PathRequest>         pathRequestEnabled)
    {
        Entity targetEntity = request.targetEntity;

        if (targetEntity == Entity.Null)
        {
            moveRequestEnabled.ValueRW = false;
            return;
        }

        // Target dead or gone — disable request; execution system handles cleanup
        if (aliveLookup.HasComponent(targetEntity) && !aliveLookup.IsComponentEnabled(targetEntity))
        {
            moveRequestEnabled.ValueRW = false;
            return;
        }

        if (!transformLookup.TryGetComponent(targetEntity, out LocalTransform targetTransform))
        {
            moveRequestEnabled.ValueRW = false;
            return;
        }

        float3 targetPos = targetTransform.Position;
        float  distSq    = math.distancesq(myTransform.Position, targetPos);

        // Arrived
        if (distSq <= request.arrivalRange * request.arrivalRange)
        {
            moveRequestEnabled.ValueRW = false;
            arrivedEnabled.ValueRW     = true;
            return;
        }

        // Re-request path if target has moved significantly or this is a fresh request
        float movedSq = math.distancesq(targetPos, request.lastKnownTargetPos);
        if (movedSq >= REPATH_DIST_SQ)
        {
            pathAgent.targetPosition = targetPos;
            pathAgent.isActive       = true;
            pathAgent.needsRepath    = true;

            pathRequest.targetPosition = targetPos;
            pathRequest.requestedMode  = pathAgent.preferredMode;
            pathRequestEnabled.ValueRW = true;

            movement.targetPosition = targetPos;

            request.lastKnownTargetPos = targetPos;
        }
    }
}
