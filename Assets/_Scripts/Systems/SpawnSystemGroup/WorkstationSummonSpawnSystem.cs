using Unity.Entities;
using Unity.Transforms;

// Bypasses UnitSpawner because that spawns out of camera view at random points (CampLoop_System.md CL-A6).
[UpdateInGroup(typeof(SpawnSystemGroup))]
public partial struct WorkstationSummonSpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity libraryEntity = SystemAPI.GetSingletonEntity<UnitDataLibrary>();
        DynamicBuffer<UnitPrefabEntry> prefabs = SystemAPI.GetBuffer<UnitPrefabEntry>(libraryEntity);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (DynamicBuffer<PendingMinionSpawn> pendingSpawns in
            SystemAPI.Query<DynamicBuffer<PendingMinionSpawn>>())
        {
            if (pendingSpawns.IsEmpty)
                continue;

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

                Entity newBody = ecb.Instantiate(bodyPrefab);
                ecb.SetComponent(newBody, LocalTransform.FromPosition(pendingSpawn.position));
                ecb.AddComponent(newBody, new PoolOwner { unitType = pendingSpawn.unitType });
                ecb.SetComponentEnabled<NewlySpawned>(newBody, true);
                ecb.SetComponentEnabled<ActionRequest>(newBody, true);
                ecb.AddComponent<SpawnAsPlayerMinion>(newBody);
            }

            pendingSpawns.Clear();
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
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
