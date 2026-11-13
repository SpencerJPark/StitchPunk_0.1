using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

// Not [BurstCompile]: reflection, JsonUtility and System.IO are managed.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(AutoSaveTimerSystem))]
public partial struct PersistentSaveSystem : ISystem
{
    // Built in OnCreate, not per save: query building scans every archetype. Alive-only via WithDisabled<Dead>.
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

        // Consume immediately so nothing re-triggers this frame.
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

    // Dead minions are skipped; they despawn on reload.
    private void WriteMinionRecords(EntityManager entityManager, List<EntityRecord> entities)
    {
        NativeArray<Entity> minions = aliveMinionQuery.ToEntityArray(Allocator.Temp);

        foreach (Entity minion in minions)
        {
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
