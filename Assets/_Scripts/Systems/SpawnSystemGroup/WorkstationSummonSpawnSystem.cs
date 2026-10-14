using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Bypasses UnitSpawner because that spawns out of camera view at random points (CampLoop_System.md CL-A6).
[UpdateInGroup(typeof(SpawnSystemGroup))]
public partial struct WorkstationSummonSpawnSystem : ISystem
{
    private const double ClaimLifetimeSeconds = 6.0;
    private const int MaxSlotSearch = 200;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity libraryEntity = SystemAPI.GetSingletonEntity<UnitDataLibrary>();
        DynamicBuffer<UnitPrefabEntry> prefabs = SystemAPI.GetBuffer<UnitPrefabEntry>(libraryEntity);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        double now = SystemAPI.Time.ElapsedTime;

        NativeList<float3> minionPositions = new NativeList<float3>(Allocator.Temp);
        foreach (RefRO<LocalTransform> minionTransform in
            SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Minion>())
        {
            minionPositions.Add(minionTransform.ValueRO.Position);
        }

        float occupiedRadius = FormationSlots.DefaultSpacing * 0.5f;

        foreach ((RefRO<Workstation> workstation,
                  RefRO<LocalTransform> stationTransform,
                  DynamicBuffer<PendingMinionSpawn> pendingSpawns,
                  DynamicBuffer<WorkstationRallyClaim> claims) in
            SystemAPI.Query<RefRO<Workstation>, RefRO<LocalTransform>,
                DynamicBuffer<PendingMinionSpawn>, DynamicBuffer<WorkstationRallyClaim>>())
        {
            if (pendingSpawns.IsEmpty)
                continue;

            float3 rallyCenter = stationTransform.ValueRO.Position
                + math.rotate(stationTransform.ValueRO.Rotation, workstation.ValueRO.rallyLocalOffset);

            for (int claimIndex = claims.Length - 1; claimIndex >= 0; claimIndex--)
            {
                if (now - claims[claimIndex].claimedTime > ClaimLifetimeSeconds)
                    claims.RemoveAt(claimIndex);
            }

            for (int index = 0; index < pendingSpawns.Length; index++)
            {
                PendingMinionSpawn pendingSpawn = pendingSpawns[index];
                Entity bodyPrefab = FindBodyPrefab(prefabs, pendingSpawn.unitType);
                if (bodyPrefab == Entity.Null)
                {
                    // Already paid for: make a missing library prefab loud rather than silently eating the cost.
                    UnityEngine.Debug.LogWarning($"[WorkstationSummon] No body prefab for {pendingSpawn.unitType} in the unit library; summon dropped.");
                    continue;
                }

                // Claims exist because a unit still walking to its slot isn't there yet, so position alone would double-book it.
                float3 travelDirection = FormationSlots.TravelDirection(pendingSpawn.position, rallyCenter);
                int slotIndex = FindFreeSlot(rallyCenter, travelDirection, claims, minionPositions, occupiedRadius);
                float3 slotPosition = rallyCenter + FormationSlots.RoundedSlotOffset(slotIndex, FormationSlots.DefaultSpacing);
                claims.Add(new WorkstationRallyClaim { slotIndex = slotIndex, claimedTime = now });

                Entity newBody = ecb.Instantiate(bodyPrefab);
                ecb.SetComponent(newBody, LocalTransform.FromPosition(pendingSpawn.position));
                ecb.AddComponent(newBody, new PoolOwner { unitType = pendingSpawn.unitType });
                ecb.SetComponentEnabled<NewlySpawned>(newBody, true);
                ecb.SetComponentEnabled<ActionRequest>(newBody, true);
                ecb.AddComponent<SpawnAsPlayerMinion>(newBody);
                ecb.SetComponent(newBody, new OnMinionMoveCommand { destination = slotPosition });
                ecb.SetComponentEnabled<OnMinionMoveCommand>(newBody, true);
            }

            pendingSpawns.Clear();
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        minionPositions.Dispose();
    }

    // Summons arrive one by one from the station, so the frontmost free slot (farthest along the
    // walk) goes first and later summons fill in behind without walking through earlier ones.
    private static int FindFreeSlot(
        float3 rallyCenter,
        float3 travelDirection,
        DynamicBuffer<WorkstationRallyClaim> claims,
        NativeList<float3> minionPositions,
        float occupiedRadius)
    {
        int frontmostSlot = -1;
        float frontmostDepth = float.MinValue;
        for (int slotIndex = 0; slotIndex < FormationSlots.RallyFormationCapacity; slotIndex++)
        {
            if (!IsSlotFree(rallyCenter, slotIndex, claims, minionPositions, occupiedRadius))
                continue;

            float depth = FormationSlots.SlotDepth(slotIndex, FormationSlots.DefaultSpacing, travelDirection);
            if (depth <= frontmostDepth)
                continue;
            frontmostDepth = depth;
            frontmostSlot = slotIndex;
        }
        if (frontmostSlot >= 0)
            return frontmostSlot;

        // The formation is full: grow it outward.
        for (int slotIndex = FormationSlots.RallyFormationCapacity; slotIndex < MaxSlotSearch; slotIndex++)
        {
            if (IsSlotFree(rallyCenter, slotIndex, claims, minionPositions, occupiedRadius))
                return slotIndex;
        }
        return MaxSlotSearch;
    }

    private static bool IsSlotFree(
        float3 rallyCenter,
        int slotIndex,
        DynamicBuffer<WorkstationRallyClaim> claims,
        NativeList<float3> minionPositions,
        float occupiedRadius)
    {
        if (IsClaimed(claims, slotIndex))
            return false;
        float3 slotPosition = rallyCenter + FormationSlots.RoundedSlotOffset(slotIndex, FormationSlots.DefaultSpacing);
        return !IsOccupiedByMinion(minionPositions, slotPosition, occupiedRadius);
    }

    private static bool IsClaimed(DynamicBuffer<WorkstationRallyClaim> claims, int slotIndex)
    {
        for (int claimIndex = 0; claimIndex < claims.Length; claimIndex++)
        {
            if (claims[claimIndex].slotIndex == slotIndex)
                return true;
        }
        return false;
    }

    private static bool IsOccupiedByMinion(NativeList<float3> minionPositions, float3 slotPosition, float occupiedRadius)
    {
        for (int minionIndex = 0; minionIndex < minionPositions.Length; minionIndex++)
        {
            float2 offsetXZ = minionPositions[minionIndex].xz - slotPosition.xz;
            if (math.lengthsq(offsetXZ) < occupiedRadius * occupiedRadius)
                return true;
        }
        return false;
    }

    private static Entity FindBodyPrefab(DynamicBuffer<UnitPrefabEntry> prefabs, UnitType unitType)
    {
        for (int index = 0; index < prefabs.Length; index++)
        {
            if (prefabs[index].unitType == unitType)
                return prefabs[index].bodyPrefab;
        }
        return Entity.Null;
    }
}
