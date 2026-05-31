using Unity.Collections;
using Unity.Entities;

// Creates one action entity per action-def for every newly spawned UtilityBrainV2 unit.
// Also adds DynamicBuffer<AwarenessTarget> to the unit for v2 perception systems.
// Runs after UnitSpawnerSystem has created entities so NewlySpawned units are present.
// Not BurstCompile — uses EntityManager structural APIs directly (only runs once per spawn).
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
public partial struct ActionEntitySpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();
        if (!brainLibrary.blob.IsCreated) return;
        ref AIConfigBlob blob = ref brainLibrary.blob.Value;

        // Pre-collect to avoid iterating a query while making structural changes.
        NativeList<UnitSpawnEntry> toSpawn = new NativeList<UnitSpawnEntry>(Allocator.Temp);
        foreach ((RefRO<UnitData> unitData, Entity unit) in
            SystemAPI.Query<RefRO<UnitData>>()
                .WithAll<UtilityBrainV2, NewlySpawned>()
                .WithEntityAccess())
        {
            toSpawn.Add(new UnitSpawnEntry { unit = unit, unitType = unitData.ValueRO.unitType });
        }

        foreach (UnitSpawnEntry entry in toSpawn)
        {
            if (!state.EntityManager.HasBuffer<AwarenessTarget>(entry.unit))
                state.EntityManager.AddBuffer<AwarenessTarget>(entry.unit);
            if (!state.EntityManager.HasBuffer<RecentWaypoint>(entry.unit))
                state.EntityManager.AddBuffer<RecentWaypoint>(entry.unit);

            int unitTypeIndex = (int)entry.unitType;
            if (unitTypeIndex >= blob.brains.Length) continue;
            ref BrainBlob brain = ref blob.brains[unitTypeIndex];

            for (int i = 0; i < brain.actionDefIndices.Length; i++)
            {
                int actionDefIndex = brain.actionDefIndices[i];
                ref ActionDefBlob actionDef = ref blob.actionDefs[actionDefIndex];

                Entity actionEntity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(actionEntity, new UtilityAction
                {
                    ownerEntity    = entry.unit,
                    action         = actionDef.actionType,
                    priority       = actionDef.priority,
                    actionDefIndex = actionDefIndex,
                });
                state.EntityManager.AddComponent<ActionActive>(actionEntity);
                state.EntityManager.SetComponentEnabled<ActionActive>(actionEntity, false);
            }
        }

        toSpawn.Dispose();
    }

    private struct UnitSpawnEntry
    {
        public Entity   unit;
        public UnitType unitType;
    }
}
