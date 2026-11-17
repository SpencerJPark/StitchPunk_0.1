using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

// Enable LoadRequest only after the game scene has spawned the player entity.
// Minions are respawned here and patched a frame later by MinionRestoreApplySystem. No Burst: managed I/O.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(PersistentSaveSystem))]
public partial struct PersistentLoadSystem : ISystem
{
    // Built once here rather than inside RestoreMinions: building a query is a lookup against every
    // archetype in the world, which is what Unity's "create queries in OnCreate" warning is about.
    private EntityQuery unitDataLibraryQuery;
    private EntityQuery existingMinionQuery;

    public void OnCreate(ref SystemState state)
    {
        unitDataLibraryQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<UnitDataLibrary>()
            .Build(ref state);

        existingMinionQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Minion>()
            .Build(ref state);

        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<GameDataTag>();
        state.RequireForUpdate<Player>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity gameDataEntity = SystemAPI.GetSingletonEntity<GameDataTag>();

        if (!state.EntityManager.IsComponentEnabled<LoadRequest>(gameDataEntity))
            return;

        // Leave the request enabled while a save is being written: it retries next frame instead of being dropped.
        if (SaveNotifications.IsSaveInProgress)
            return;

        state.EntityManager.SetComponentEnabled<LoadRequest>(gameDataEntity, false);

        LoadRequest loadRequest = state.EntityManager.GetComponentData<LoadRequest>(gameDataEntity);
        string requestedSaveId = loadRequest.saveId.ToString();

        SaveHeaderV2 header;
        bool headerFound = string.IsNullOrEmpty(requestedSaveId)
            ? SaveSlotStore.TryGetNewest(out header)
            : SaveSlotStore.TryReadHeader(requestedSaveId, out header);
        if (!headerFound || header == null)
        {
            Debug.LogWarning($"[PersistentLoadSystem] No save found for '{requestedSaveId}'.");
            SaveNotifications.RaiseLoadFailed(requestedSaveId);
            return;
        }

        if (!SaveSlotStore.TryReadFile(header.saveId, SaveSlotStore.ProgressFileName, out byte[] progressBytes))
        {
            Debug.LogWarning($"[PersistentLoadSystem] Missing {SaveSlotStore.ProgressFileName} for {header.saveId}.");
            SaveNotifications.RaiseLoadFailed(header.saveId);
            return;
        }

        SaveFile saveFile = JsonUtility.FromJson<SaveFile>(System.Text.Encoding.UTF8.GetString(progressBytes));
        if (saveFile?.entities == null)
        {
            Debug.LogWarning("[PersistentLoadSystem] Save file is empty or malformed — aborting load.");
            SaveNotifications.RaiseLoadFailed(header.saveId);
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

        World stagingWorld = null;
        if (header.hasMomentLayer && !MomentPreload.TryTake(header.saveId, out stagingWorld))
        {
            if (MomentFiles.TryReadMomentBytes(header.saveId, out byte[] rawSnapshotBytes))
                stagingWorld = MomentSnapshot.DeserializeIntoStagingWorld(rawSnapshotBytes);
        }

        if (stagingWorld != null)
        {
            // MomentSnapshotCommitSystem runs right after this system and replaces every MomentPersist entity.
            PendingMomentCommit.Set(stagingWorld, header.savedElapsedTime);
        }
        else
        {
            RestoreMinions(entityManager, minionRecords);
            if (header.hasMomentLayer)
                Debug.Log($"[PersistentLoadSystem] Moment layer unavailable for {header.saveId}: units reset to the progress layer");
        }

        MomentPreload.Discard();
        SaveNotifications.RaiseLoadFinished(header.saveId);
        Debug.Log($"[PersistentLoadSystem] Loaded {header.saveId}");
    }

    // Component patching is deferred to MinionRestoreApplySystem so SpawnStateInitSystem cannot clobber the restored enabled bits.
    private void RestoreMinions(EntityManager entityManager, List<EntityRecord> minionRecords)
    {
        if (unitDataLibraryQuery.IsEmptyIgnoreFilter)
        {
            if (minionRecords.Count > 0)
                Debug.LogWarning("[PersistentLoadSystem] No UnitDataLibrary singleton — cannot restore minions.");
            return;
        }

        Entity libraryEntity = unitDataLibraryQuery.GetSingletonEntity();
        DynamicBuffer<UnitPrefabEntry> prefabs = entityManager.GetBuffer<UnitPrefabEntry>(libraryEntity);
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        // Remove the scene's current owned minions so the saved roster fully replaces them.
        NativeArray<Entity> existing = existingMinionQuery.ToEntityArray(Allocator.Temp);
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
