using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Holds the back doors open while the player walks out of the caravan after leaving the workstation. Sitting inside
// keeps them shut (owner call 2026-11-08); the back zone opens them on the way in, and a summon sequence owns them.
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateBefore(typeof(CaravanExitSequenceSystem))]
public partial struct CaravanPlayerExitDoorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Caravan>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup writes to the doors wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        NativeList<Entity> caravansWithPlayerWalkingOut = new NativeList<Entity>(2, Allocator.Temp);
        foreach (RefRO<CaravanExitWalker> exitWalker in SystemAPI.Query<RefRO<CaravanExitWalker>>().WithAll<Player>())
            caravansWithPlayerWalkingOut.Add(exitWalker.ValueRO.caravan);

        foreach ((DynamicBuffer<CaravanSummonExitDoor> exitDoors, Entity caravanEntity)
                 in SystemAPI.Query<DynamicBuffer<CaravanSummonExitDoor>>()
                     .WithAll<Caravan>()
                     .WithDisabled<CaravanExitSequence>()
                     .WithEntityAccess())
        {
            DoorOverride wantedOverride = caravansWithPlayerWalkingOut.Contains(caravanEntity) ? DoorOverride.HoldOpen : DoorOverride.None;
            for (int doorIndex = 0; doorIndex < exitDoors.Length; doorIndex++)
            {
                Entity doorEntity = exitDoors[doorIndex].door;
                if (!SystemAPI.HasComponent<Door>(doorEntity)) continue;
                RefRW<Door> door = SystemAPI.GetComponentRW<Door>(doorEntity);
                if (door.ValueRO.scriptOverride != wantedOverride)
                    door.ValueRW.scriptOverride = wantedOverride;
            }
        }

        caravansWithPlayerWalkingOut.Dispose();
    }
}
