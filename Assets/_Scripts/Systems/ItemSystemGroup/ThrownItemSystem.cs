using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Applies horizontal (X/Z) throw velocity each frame.
/// Gravity (Y) is handled entirely by UnitGravitySystem.
/// Stops and re-enables PlayerInteractable when UnitGravitySystem reports isGrounded.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(ItemSystemGroup))]
public partial struct ThrownItemSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (transform, thrownItem, gravity, thrownEnabled, interactableEnabled) in
            SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<ThrownItem>,
                RefRO<Gravity>,
                EnabledRefRW<ThrownItem>,
                EnabledRefRW<PlayerInteractable>>()
            .WithPresent<PlayerInteractable>())
        {
            if (gravity.ValueRO.isGrounded)
            {
                thrownItem.ValueRW.velocity  = float3.zero;
                thrownEnabled.ValueRW        = false;
                interactableEnabled.ValueRW  = true;
                continue;
            }

            // Only move horizontally — UnitGravitySystem owns Y
            float3 horizontalVelocity = new float3(thrownItem.ValueRO.velocity.x, 0f, thrownItem.ValueRO.velocity.z);
            transform.ValueRW.Position += horizontalVelocity * dt;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
