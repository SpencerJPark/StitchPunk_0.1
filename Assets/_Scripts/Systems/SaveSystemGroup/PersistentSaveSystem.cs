using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

// Not [BurstCompile]: reflection, JsonUtility and System.IO are managed.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(AutoSaveTimerSystem))]
public partial class PersistentSaveSystem : SystemBase
{
    // Built in OnCreate, not per save: query building scans every archetype. Alive-only via WithDisabled<Dead>.
    private EntityQuery aliveMinionQuery;
    private bool warnedForActiveCutscene;

    // Completion is raised on the main thread from OnUpdate; the task only flips the success flag.
    private System.Threading.Tasks.Task pendingWriteTask;
    private string pendingSaveId;
    private SaveKind pendingSaveKind;
    private volatile bool pendingWriteSucceeded;

    protected override void OnCreate()
    {
        aliveMinionQuery = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
            .WithAll<Minion, UnitData, LocalTransform>()
            .WithDisabled<Dead>());

        RequireForUpdate<GameSceneTag>();
        RequireForUpdate<GameDataTag>();
        RequireForUpdate<Player>();
    }

    protected override void OnUpdate()
    {
        if (pendingWriteTask != null && pendingWriteTask.IsCompleted)
        {
            SaveNotifications.RaiseSaveFinished(pendingSaveId, pendingWriteSucceeded);
            Debug.Log($"[PersistentSaveSystem] Saved {pendingSaveKind} {pendingSaveId}");
            pendingWriteTask = null;
        }

        Entity gameDataEntity = SystemAPI.GetSingletonEntity<GameDataTag>();

        bool cutsceneActive = SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity)
            && SystemAPI.IsComponentEnabled<ActiveCutscene>(narrativeEntity);

        if (!cutsceneActive)
        {
            warnedForActiveCutscene = false;
        }
        else if (EntityManager.IsComponentEnabled<SaveRequest>(gameDataEntity))
        {
            EntityManager.SetComponentEnabled<SaveRequest>(gameDataEntity, false);
            if (!warnedForActiveCutscene)
            {
                Debug.LogWarning("[PersistentSaveSystem] Save request skipped — a cutscene is active.");
                SaveNotifications.RaiseSaveBlocked("Can't save during a cutscene");
                warnedForActiveCutscene = true;
            }
            return;
        }

        if (!EntityManager.IsComponentEnabled<SaveRequest>(gameDataEntity))
            return;

        // Consume immediately so nothing re-triggers this frame.
        EntityManager.SetComponentEnabled<SaveRequest>(gameDataEntity, false);

        if (SaveNotifications.IsSaveInProgress)
        {
            SaveNotifications.RaiseSaveBlocked("A save is already being written");
            return;
        }

        SaveRequest saveRequest = EntityManager.GetComponentData<SaveRequest>(gameDataEntity);
        SaveKind kind = (SaveKind)saveRequest.kind;
        string saveId = kind == SaveKind.Manual && saveRequest.overwriteSaveId.Length > 0
            ? saveRequest.overwriteSaveId.ToString()
            : SaveSlotStore.CreateSaveFolder(kind);

        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        EntityManager entityManager = EntityManager;
        List<EntityRecord> entities = new List<EntityRecord>
        {
            SaveSerialization.WriteEntity(entityManager, playerEntity, SaveRoles.Player),
            SaveSerialization.WriteEntity(entityManager, gameDataEntity, SaveRoles.GameData),
        };

        WriteMinionRecords(entityManager, entities);

        PlayTimeTracker playTimeTracker = EntityManager.GetComponentData<PlayTimeTracker>(gameDataEntity);
        string locationLabel = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        SaveFile saveFile = new SaveFile
        {
            version = 1,
            header  = new SaveHeader
            {
                timestampUnix    = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                totalPlaySeconds = playTimeTracker.totalSeconds,
                sceneLabel       = locationLabel,
            },
            entities = entities.ToArray(),
        };

        byte[] progressBytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(saveFile, false));

        byte[] momentBytes = null;
        string layoutHash = "";
        bool hasMomentLayer = false;
        try
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            momentBytes = MomentSnapshot.Capture(EntityManager, out layoutHash, out int referencedObjectCount);
            stopwatch.Stop();
            hasMomentLayer = momentBytes != null;
            int momentByteCount = momentBytes != null ? momentBytes.Length : 0;
            Debug.Log($"[PersistentSaveSystem] Moment capture {stopwatch.ElapsedMilliseconds} ms, {momentByteCount} bytes, {referencedObjectCount} referenced objects");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[PersistentSaveSystem] Moment capture failed, saving without a moment layer: {exception}");
            momentBytes = null;
            hasMomentLayer = false;
        }

        SaveHeaderV2 header = new SaveHeaderV2
        {
            saveId = saveId,
            kind = kind,
            timestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            totalPlaySeconds = playTimeTracker.totalSeconds,
            locationLabel = locationLabel,
            buildVersion = Application.version,
            hasMomentLayer = hasMomentLayer,
            momentLayoutHash = hasMomentLayer ? layoutHash : "",
            savedElapsedTime = SystemAPI.Time.ElapsedTime,
        };

        SaveNotifications.RaiseSaveStarted(saveId, kind);

        pendingSaveId = saveId;
        pendingSaveKind = kind;
        pendingWriteSucceeded = false;
        pendingWriteTask = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (hasMomentLayer)
                {
                    using (System.IO.MemoryStream compressed = new System.IO.MemoryStream())
                    {
                        using (System.IO.Compression.GZipStream gzip = new System.IO.Compression.GZipStream(
                            compressed, System.IO.Compression.CompressionLevel.Fastest, true))
                        {
                            gzip.Write(momentBytes, 0, momentBytes.Length);
                        }
                        SaveSlotStore.WriteFileAtomically(saveId, SaveSlotStore.MomentFileName, compressed.ToArray());
                    }
                }

                SaveSlotStore.WriteFileAtomically(saveId, SaveSlotStore.ProgressFileName, progressBytes);
                // Header last: a save without a header is invisible, so a crash never shows a half save.
                SaveSlotStore.WriteHeader(header);
                pendingWriteSucceeded = true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PersistentSaveSystem] Save write failed for {saveId}: {exception}");
            }
        });
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
}
