using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Watches for an enabled SaveRequest. When found, snapshots every IPersist-marked component
/// on the Player and GameData singletons (generically, via <see cref="SaveSerialization"/>) into
/// a <see cref="SaveFile"/> and writes it to disk as JSON.
///
/// Replaces the old hand-written SaveSystem — there is no player-specific copy code here.
/// [BurstCompile] intentionally omitted — reflection / JsonUtility / System.IO are managed.
/// </summary>
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(AutoSaveTimerSystem))]
public partial struct PersistentSaveSystem : ISystem
{
    // Built once here rather than inside WriteMinionRecords: building a query is a lookup against
    // every archetype in the world, which is what Unity's "create queries in OnCreate" warning is
    // about. WithDisabled<Dead> is part of the query, so the alive-only filter still lives with it.
    private EntityQuery aliveMinionQuery;
    private bool warnedForActiveCutscene;

    public void OnCreate(ref SystemState state)
    {
        aliveMinionQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Minion, UnitData, LocalTransform>()
            .WithDisabled<Dead>()
            .Build(ref state);

        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<GameDataTag>();
        state.RequireForUpdate<Player>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity gameDataEntity = SystemAPI.GetSingletonEntity<GameDataTag>();

        bool cutsceneActive = SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity)
            && SystemAPI.IsComponentEnabled<ActiveCutscene>(narrativeEntity);

        if (!cutsceneActive)
        {
            warnedForActiveCutscene = false;
        }
        else if (state.EntityManager.IsComponentEnabled<SaveRequest>(gameDataEntity))
        {
            state.EntityManager.SetComponentEnabled<SaveRequest>(gameDataEntity, false);
            if (!warnedForActiveCutscene)
            {
                Debug.LogWarning("[PersistentSaveSystem] Save request skipped — a cutscene is active.");
                warnedForActiveCutscene = true;
            }
            return;
        }

        if (!state.EntityManager.IsComponentEnabled<SaveRequest>(gameDataEntity))
            return;

        // Consume immediately — subsequent systems won't re-trigger this frame.
        state.EntityManager.SetComponentEnabled<SaveRequest>(gameDataEntity, false);

        SaveRequest saveRequest = state.EntityManager.GetComponentData<SaveRequest>(gameDataEntity);
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        EntityManager entityManager = state.EntityManager;
        List<EntityRecord> entities = new List<EntityRecord>
        {
            SaveSerialization.WriteEntity(entityManager, playerEntity, SaveRoles.Player),
            SaveSerialization.WriteEntity(entityManager, gameDataEntity, SaveRoles.GameData),
        };

        WriteMinionRecords(entityManager, entities);

        PlayTimeTracker playTimeTracker = state.EntityManager.GetComponentData<PlayTimeTracker>(gameDataEntity);

        SaveFile saveFile = new SaveFile
        {
            version = 1,
            header  = new SaveHeader
            {
                timestampUnix    = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                totalPlaySeconds = playTimeTracker.totalSeconds,
                sceneLabel       = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            },
            entities = entities.ToArray(),
        };

        string json = JsonUtility.ToJson(saveFile, true);
        string path = SavePaths.GetSlotPath(saveRequest.slot);
        System.IO.File.WriteAllText(path, json);

        Debug.Log($"[PersistentSaveSystem] Saved to slot {saveRequest.slot}: {path}");
    }

    // Writes one record per alive owned minion (Minion enabled, Dead disabled). Dead minions are
    // skipped — they simply despawn on reload.
    private void WriteMinionRecords(EntityManager entityManager, List<EntityRecord> entities)
    {
        NativeArray<Entity> minions = aliveMinionQuery.ToEntityArray(Allocator.Temp);

        foreach (Entity minion in minions)
        {
            // Assign a stable identity the first time a minion is saved.
            if (!entityManager.HasComponent<PersistId>(minion))
                entityManager.AddComponentData(minion, new PersistId { value = NewPersistId() });

            PersistId persistId = entityManager.GetComponentData<PersistId>(minion);
            UnitData unitData   = entityManager.GetComponentData<UnitData>(minion);

            EntityRecord record = SaveSerialization.WriteEntity(entityManager, minion, SaveRoles.Minion);
            record.persistId = persistId.value.ToString();
            record.unitType  = (int)unitData.unitType;
            entities.Add(record);
        }

        minions.Dispose();
    }

    private static Unity.Entities.Hash128 NewPersistId()
    {
        byte[] guid = Guid.NewGuid().ToByteArray();
        return new Unity.Entities.Hash128(
            BitConverter.ToUInt32(guid, 0),
            BitConverter.ToUInt32(guid, 4),
            BitConverter.ToUInt32(guid, 8),
            BitConverter.ToUInt32(guid, 12));
    }

    public void OnDestroy(ref SystemState state) { }
}
