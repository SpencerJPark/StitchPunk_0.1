using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// A handed-over unit stays at the client until the player is far away AND cannot see it, then goes through the Despawn funnel
// (TR-D8 / TR-A7). "Cannot see" is the CameraView singleton, the sim-safe view circle the spawner also uses; CameraVisible is
// presentation-only and must not gate gameplay (RULES.md).
[BurstCompile]
[UpdateInGroup(typeof(DespawnSystemGroup))]
public partial struct DeliveredUnitDespawnSystem : ISystem
{
    private const float DespawnDistanceFromPlayer = 60f;
    private const float ViewPaddingMeters = 6f;
    private const float MinimumSecondsAtClient = 3f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<CameraView>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float3 playerPosition = SystemAPI.GetComponent<LocalTransform>(SystemAPI.GetSingletonEntity<Player>()).Position;
        CameraView cameraView = SystemAPI.GetSingleton<CameraView>();
        float outOfViewDistance = cameraView.viewRadius + ViewPaddingMeters;
        EntityCommandBuffer commandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        // A killed unit is left to the corpse handling, so only living ones are despawned here.
        foreach ((RefRO<DeliveredToClient> delivered, RefRO<LocalTransform> transform, EnabledRefRW<Despawn> despawnEnabled, Entity unitEntity)
            in SystemAPI.Query<RefRO<DeliveredToClient>, RefRO<LocalTransform>, EnabledRefRW<Despawn>>()
                .WithDisabled<Dead>()
                .WithPresent<Despawn>()
                .WithEntityAccess())
        {
            if (delivered.ValueRO.secondsSinceHandOver < MinimumSecondsAtClient)
                continue;

            float3 position = transform.ValueRO.Position;
            bool farFromPlayer = math.distance(position.xz, playerPosition.xz) > DespawnDistanceFromPlayer;
            bool outsideView = math.distance(position.xz, cameraView.center.xz) > outOfViewDistance;
            if (!farFromPlayer || !outsideView)
                continue;

            despawnEnabled.ValueRW = true;
            // A pooled unit is reclaimed as an ordinary one later, so it must not carry the delivered marker back out.
            commandBuffer.RemoveComponent<DeliveredToClient>(unitEntity);
        }
    }
}
