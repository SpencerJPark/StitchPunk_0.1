using Unity.Collections;
using Unity.Entities;

// Creates a pool of action entities per action-def for every UtilityBrainV2 unit on first appearance.
// Pool size = ActionDefBlob.maxCandidates (authored on UtilityActionSO). ActionInstancingSystem
// enables/disables pool slots each frame based on AwarenessTarget entries.
// Not BurstCompile — uses EntityManager structural APIs directly (runs once per unit).
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
        ref BrainLibraryBlob blob = ref brainLibrary.blob.Value;

        // Pre-collect to avoid iterating a query while making structural changes.
        NativeList<UnitSpawnEntry> toSpawn = new NativeList<UnitSpawnEntry>(Allocator.Temp);
        foreach ((RefRO<UnitData> unitData, Entity unit) in
            SystemAPI.Query<RefRO<UnitData>>()
                .WithAll<UtilityBrainV2>()
                .WithNone<AwarenessTarget>()
                .WithEntityAccess())
        {
            toSpawn.Add(new UnitSpawnEntry { unit = unit, unitType = unitData.ValueRO.unitType });
        }

        foreach (UnitSpawnEntry entry in toSpawn)
        {
            state.EntityManager.AddBuffer<AwarenessTarget>(entry.unit);
            state.EntityManager.AddBuffer<RecentWaypoint>(entry.unit);
            state.EntityManager.AddBuffer<OwnedAction>(entry.unit);

            int unitTypeIndex = (int)entry.unitType;
            if (unitTypeIndex >= blob.brains.Length) continue;
            ref BrainBlob brain = ref blob.brains[unitTypeIndex];

            // Collect entries before touching the buffer: every CreateEntity / AddComponent call is
            // a structural change that invalidates any DynamicBuffer reference we hold.
            NativeList<OwnedAction> pendingOwned = new NativeList<OwnedAction>(Allocator.Temp);

            for (int i = 0; i < brain.actionDefIndices.Length; i++)
            {
                int actionDefIndex = brain.actionDefIndices[i];
                ref ActionDefBlob actionDef = ref blob.actionDefs[actionDefIndex];

                int poolSize = actionDef.maxCandidates > 0 ? actionDef.maxCandidates : 1;

                for (int slot = 0; slot < poolSize; slot++)
                {
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

                    pendingOwned.Add(new OwnedAction
                    {
                        actionEntity   = actionEntity,
                        actionDefIndex = actionDefIndex,
                    });
                }
            }

            // All structural changes are done — re-fetch the buffer and write.
            DynamicBuffer<OwnedAction> ownedActions = state.EntityManager.GetBuffer<OwnedAction>(entry.unit);
            for (int i = 0; i < pendingOwned.Length; i++)
                ownedActions.Add(pendingOwned[i]);

            pendingOwned.Dispose();
        }

        toSpawn.Dispose();
    }

    private struct UnitSpawnEntry
    {
        public Entity   unit;
        public UnitType unitType;
    }
}
