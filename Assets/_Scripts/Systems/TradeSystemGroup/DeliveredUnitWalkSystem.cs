using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;

// Sends a handed-over unit to its spot at the client. Waits a beat so the ActionInterruptRequest raised at hand-over has torn the
// unit's old behavior down first (it would otherwise halt this path request).
[BurstCompile]
[UpdateInGroup(typeof(TradeSystemGroup))]
public partial struct DeliveredUnitWalkSystem : ISystem
{
    private const float WalkOrderDelaySeconds = 0.3f;
    private const float StoppingDistance = 0.3f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach ((RefRW<DeliveredToClient> delivered, RefRW<PathRequest> pathRequest, EnabledRefRW<PathRequest> pathRequestEnabled)
            in SystemAPI.Query<RefRW<DeliveredToClient>, RefRW<PathRequest>, EnabledRefRW<PathRequest>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            delivered.ValueRW.secondsSinceHandOver += deltaTime;
            if (delivered.ValueRO.walkOrdered || delivered.ValueRO.secondsSinceHandOver < WalkOrderDelaySeconds)
                continue;

            MovementAPI.BeginPathRequest(ref pathRequest.ValueRW, pathRequestEnabled, delivered.ValueRO.standPoint, StoppingDistance);
            delivered.ValueRW.walkOrdered = true;
        }
    }
}
