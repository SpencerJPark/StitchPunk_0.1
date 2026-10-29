using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Opens a zone's doors while an allowed opener stands in it (Caravan_System.md CV-D20). Rewrites
// Door.zoneWantsOpen every frame; DoorSystem weighs it against any script override.
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateBefore(typeof(DoorSystem))]
public partial struct DoorZoneSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DoorZone>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup writes to the doors below wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        foreach (RefRW<Door> door in SystemAPI.Query<RefRW<Door>>())
            door.ValueRW.zoneWantsOpen = false;

        DoorOpeners openersInUse = DoorOpeners.None;
        foreach (RefRO<DoorZone> doorZone in SystemAPI.Query<RefRO<DoorZone>>())
            openersInUse |= doorZone.ValueRO.openers;

        NativeList<float3> playerPositions = new NativeList<float3>(1, Allocator.Temp);
        NativeList<float3> minionPositions = new NativeList<float3>(16, Allocator.Temp);
        NativeList<float3> otherUnitPositions = new NativeList<float3>(64, Allocator.Temp);

        if ((openersInUse & DoorOpeners.Player) != 0)
        {
            foreach (RefRO<LocalToWorld> playerPose in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<Player>())
                playerPositions.Add(playerPose.ValueRO.Position);
        }
        if ((openersInUse & DoorOpeners.Minions) != 0)
        {
            foreach (RefRO<LocalToWorld> minionPose in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<Minion>())
                minionPositions.Add(minionPose.ValueRO.Position);
        }
        if ((openersInUse & DoorOpeners.OtherUnits) != 0)
        {
            // Minion is enableable: a citizen carries it disabled and still counts as someone else.
            foreach ((RefRO<LocalToWorld> unitPose, Entity unitEntity)
                     in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<Faction>().WithNone<Player, Caravan>().WithEntityAccess())
            {
                bool isPlayersMinion = SystemAPI.HasComponent<Minion>(unitEntity) && SystemAPI.IsComponentEnabled<Minion>(unitEntity);
                if (!isPlayersMinion) otherUnitPositions.Add(unitPose.ValueRO.Position);
            }
        }

        foreach ((RefRO<DoorZone> doorZone, RefRO<LocalToWorld> zonePose, DynamicBuffer<DoorZoneDoor> zoneDoors)
                 in SystemAPI.Query<RefRO<DoorZone>, RefRO<LocalToWorld>, DynamicBuffer<DoorZoneDoor>>())
        {
            DoorZone zone = doorZone.ValueRO;
            if (IsBeingDriven(ref state, zone.lockingVehicle)) continue;

            float4x4 worldToZone = math.inverse(zonePose.ValueRO.Value);
            bool occupied = ((zone.openers & DoorOpeners.Player) != 0 && AnyInside(playerPositions, worldToZone, in zone))
                || ((zone.openers & DoorOpeners.Minions) != 0 && AnyInside(minionPositions, worldToZone, in zone))
                || ((zone.openers & DoorOpeners.OtherUnits) != 0 && AnyInside(otherUnitPositions, worldToZone, in zone));
            if (!occupied) continue;

            for (int doorIndex = 0; doorIndex < zoneDoors.Length; doorIndex++)
            {
                Entity doorEntity = zoneDoors[doorIndex].door;
                if (SystemAPI.HasComponent<Door>(doorEntity))
                    SystemAPI.GetComponentRW<Door>(doorEntity).ValueRW.zoneWantsOpen = true;
            }
        }

        playerPositions.Dispose();
        minionPositions.Dispose();
        otherUnitPositions.Dispose();
    }

    private bool IsBeingDriven(ref SystemState state, Entity vehicle)
    {
        return vehicle != Entity.Null && SystemAPI.HasComponent<CaravanDriving>(vehicle)
            && SystemAPI.IsComponentEnabled<CaravanDriving>(vehicle);
    }

    private static bool AnyInside(NativeList<float3> worldPositions, float4x4 worldToZone, in DoorZone zone)
    {
        for (int positionIndex = 0; positionIndex < worldPositions.Length; positionIndex++)
        {
            float3 zonePoint = math.transform(worldToZone, worldPositions[positionIndex]);
            if (math.all(math.abs(zonePoint - zone.localCenter) <= zone.halfExtents)) return true;
        }
        return false;
    }
}
