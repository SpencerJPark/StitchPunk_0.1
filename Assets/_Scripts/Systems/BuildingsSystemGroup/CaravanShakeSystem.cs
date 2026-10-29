using Unity.Burst;
using Unity.Entities;

// Counts down the body shake the summon sequence started; VehicleAnimationSystem / VehiclePoseSystem show it.
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateAfter(typeof(CaravanExitSequenceSystem))]
public partial struct CaravanShakeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (RefRW<CaravanShake> caravanShake in SystemAPI.Query<RefRW<CaravanShake>>())
        {
            if (caravanShake.ValueRO.remainingSeconds > 0f)
                caravanShake.ValueRW.remainingSeconds -= deltaTime;
        }
    }
}
