using Unity.Burst;
using Unity.Entities;

// Advances the back doors and the body shake; whoever wants them moved has already asked this frame.
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateAfter(typeof(CaravanExitSequenceSystem))]
public partial struct CaravanDoorSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach ((RefRW<CaravanDoors> caravanDoors, RefRW<CaravanShake> caravanShake)
                 in SystemAPI.Query<RefRW<CaravanDoors>, RefRW<CaravanShake>>())
        {
            CaravanDoorControl.Advance(ref caravanDoors.ValueRW, deltaTime);
            if (caravanShake.ValueRO.remainingSeconds > 0f)
                caravanShake.ValueRW.remainingSeconds -= deltaTime;
        }
    }
}
