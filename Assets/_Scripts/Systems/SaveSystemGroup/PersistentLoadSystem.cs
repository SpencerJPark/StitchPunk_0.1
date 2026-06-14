using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Watches for an enabled LoadRequest. When found, reads the JSON save file for that slot and
/// restores each saved entity record. Player/GameData singletons are patched in place (generically,
/// via <see cref="SaveSerialization"/>); minions are respawned from the pool prefab by UnitType and
/// patched a frame later by <c>MinionRestoreApplySystem</c> (after spawn-init settles).
///
/// Call order note: enable LoadRequest only AFTER the game scene has spawned the player entity.
/// Manual loads (UI button click) always satisfy this since the player already exists.
///
/// Replaces the old hand-written LoadSystem. [BurstCompile] intentionally omitted — managed I/O.
/// </summary>
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(PersistentSaveSystem))]
public partial struct PersistentLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<GameDataTag>();
        state.RequireForUpdate<Player>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity gameDataEntity = SystemAPI.GetSingletonEntity<GameDataTag>();

        if (!state.EntityManager.IsComponentEnabled<LoadRequest>(gameDataEntity))
            return;

        state.EntityManager.SetComponentEnabled<LoadRequest>(gameDataEntity, false);

        LoadRequest loadRequest = state.EntityManager.GetComponentData<LoadRequest>(gameDataEntity);
        string path = SavePaths.GetSlotPath(loadRequest.slot);

        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[PersistentLoadSystem] No save file found at slot {loadRequest.slot}: {path}");
            return;
        }

        SaveFile saveFile = JsonUtility.FromJson<SaveFile>(System.IO.File.ReadAllText(path));
        if (saveFile?.entities == null)
        {
            Debug.LogWarning("[PersistentLoadSystem] Save file is empty or malformed — aborting load.");
            return;
        }

        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        EntityManager entityManager = state.EntityManager;

        List<EntityRecord> minionRecords = new List<EntityRecord>();

        foreach (EntityRecord record in saveFile.entities)
        {
            switch (record.role)
            {
                case SaveRoles.Player:
                    SaveSerialization.ApplyEntity(entityManager, playerEntity, record);
                    break;
                case SaveRoles.GameData:
                    SaveSerialization.ApplyEntity(entityManager, gameDataEntity, record);
                    break;
                case SaveRoles.Minion:
                    minionRecords.Add(record);
                    break;
                default:
                    Debug.LogWarning($"[PersistentLoadSystem] Unknown entity role '{record.role}' — skipping.");
                    break;
            }
        }

        RestoreMinions(ref state, entityManager, minionRecords);

        Debug.Log($"[PersistentLoadSystem] Loaded slot {loadRequest.slot} from {path}");
    }

    // Despawns current owned minions, then instantiates the saved roster from the pool prefabs.
    // Component patching (Health, Minion-enabled, transform) is deferred to MinionRestoreApplySystem,
    // which runs after spawn-init so the restored enabled bits aren't clobbered by SpawnStateInitSystem.
    private void RestoreMinions(ref SystemState state, EntityManager entityManager, List<EntityRecord> minionRecords)
    {
        EntityQuery libraryQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<UnitDataLibrary>().Build(ref state);
        if (libraryQuery.IsEmptyIgnoreFilter)
        {
            if (minionRecords.Count > 0)
                Debug.LogWarning("[PersistentLoadSystem] No UnitDataLibrary singleton — cannot restore minions.");
            return;
        }

        Entity libraryEntity = libraryQuery.GetSingletonEntity();
        DynamicBuffer<UnitPrefabEntry> prefabs = entityManager.GetBuffer<UnitPrefabEntry>(libraryEntity);
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        // Remove the scene's current owned minions so the saved roster fully replaces them.
        EntityQuery existingMinions = new EntityQueryBuilder(Allocator.Temp).WithAll<Minion>().Build(ref state);
        NativeArray<Entity> existing = existingMinions.ToEntityArray(Allocator.Temp);
        foreach (Entity current in existing)
            ecb.DestroyEntity(current); // LinkedEntityGroup → body-part children go too
        existing.Dispose();

        foreach (EntityRecord record in minionRecords)
        {
            Entity bodyPrefab = GetBodyPrefabForType(prefabs, (UnitType)record.unitType);
            if (bodyPrefab == Entity.Null)
            {
                Debug.LogWarning($"[PersistentLoadSystem] No body prefab for UnitType {record.unitType} — skipping minion.");
                continue;
            }

            Entity body = ecb.Instantiate(bodyPrefab);

            // Place immediately so it doesn't flash at the origin before the deferred apply.
            if (SaveSerialization.TryReadComponent(record, out LocalTransform savedTransform))
                ecb.SetComponent(body, savedTransform);

            ecb.AddComponent(body, new PoolOwner { unitType = (UnitType)record.unitType });
            ecb.AddComponent(body, new PersistId { value = new Unity.Entities.Hash128(record.persistId) });
            ecb.AddComponent(body, new RestorePending { recordId = MinionRestoreQueue.Enqueue(record) });

            // Mirror UnitSpawnerSystem's spawn setup so parts + AI initialize.
            ecb.SetComponentEnabled<NewlySpawned>(body, true);
            ecb.SetComponentEnabled<ActionRequest>(body, true);
        }

        ecb.Playback(entityManager);
        ecb.Dispose();
    }

    private static Entity GetBodyPrefabForType(DynamicBuffer<UnitPrefabEntry> prefabs, UnitType type)
    {
        for (int i = 0; i < prefabs.Length; i++)
            if (prefabs[i].unitType == type) return prefabs[i].bodyPrefab;
        return Entity.Null;
    }

    public void OnDestroy(ref SystemState state) { }
}
