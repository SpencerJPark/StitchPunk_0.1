using System.Diagnostics;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

// Boot path of Game.unity: holds the simulation behind the title, preloads the newest save's moment layer, then
// releases the hold when the player commits (SaveAndTitle spec section 6.4, SM-D9..D14).
public class BootFlow : MonoBehaviour, ITitleMenuActions
{
    public const string SkipTitleEditorPrefsKey = "StitchPunk.SkipTitleInEditor";

    [SerializeField] private MonoBehaviour titleMenuViewBehaviour;
    [SerializeField] private MonoBehaviour titleBackdropBehaviour;
    [SerializeField] private UnityEvent onOpenOptions;
    [SerializeField] private float titleFadeSeconds = 0.4f;

    private World world;
    private EntityManager entityManager;
    private Entity holdEntity;
    private Entity focusOverrideEntity;
    private ITitleMenuView view;
    private ITitleBackdrop backdrop;
    private TitleMenuController controller;
    private TitleMenuModel model;
    private string preloadedSaveId;
    private Stopwatch continueStopwatch;
    private bool loadFinishedSubscribed;
    private bool titleFinished;
    private bool loadRequestInFlight;

    public static void ReturnToTitle()
    {
        SceneManager.LoadScene(0);
    }

    private void Awake()
    {
        world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            Debug.LogError("[BootFlow] No default world; title flow cannot run.");
            return;
        }

        entityManager = world.EntityManager;
        DestroyOrphanedMomentEntities();
        holdEntity = entityManager.CreateEntity(typeof(SimulationHold));

        view = titleMenuViewBehaviour as ITitleMenuView;
        if (view == null)
        {
            Debug.LogError("[BootFlow] titleMenuViewBehaviour does not implement ITitleMenuView.");
            ReleaseHold();
            return;
        }
        backdrop = titleBackdropBehaviour as ITitleBackdrop;

#if UNITY_EDITOR
        if (UnityEditor.EditorPrefs.GetBool(SkipTitleEditorPrefsKey, false))
        {
            ReleaseHold();
            titleMenuViewBehaviour.gameObject.SetActive(false);
            return;
        }
#endif

        model = new TitleMenuModel();
        RefreshSaveList();
        controller = new TitleMenuController(view, this, model);
        controller.Start();
        backdrop?.Enter();
        PreloadNewestSaveAsync().Forget();
    }

    private void DestroyOrphanedMomentEntities()
    {
        // A committed moment from a previous visit belongs to no subscene, so it survives SceneManager.LoadScene.
        using (EntityQuery orphanQuery = entityManager.CreateEntityQuery(
            new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(MomentPersist) },
                None = new ComponentType[] { typeof(SceneTag) },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities,
            }))
        {
            using (NativeArray<Entity> orphans = orphanQuery.ToEntityArray(Allocator.Temp))
            {
                if (orphans.Length > 0)
                    entityManager.DestroyEntity(orphans);
            }
        }
    }

    private void RefreshSaveList()
    {
        model.SavesNewestFirst = SaveSlotStore.ListHeadersNewestFirst();
        model.UnitsResetSaveIds = MomentFiles.FindSavesWithStaleMomentLayer(model.SavesNewestFirst);
        model.NewestThumbnailPng = null;

        if (model.HasAnySave
            && SaveSlotStore.TryReadFile(model.NewestSave.saveId, SaveSlotStore.ThumbnailFileName, out byte[] thumbnailBytes))
        {
            model.NewestThumbnailPng = thumbnailBytes;
        }
    }

    private async UniTaskVoid PreloadNewestSaveAsync()
    {
        CancellationToken destroyToken = destroyCancellationToken;
        Stopwatch preloadStopwatch = Stopwatch.StartNew();

        if (!model.HasAnySave)
        {
            model.ContinuePreloadFinished = true;
            controller.RefreshCurrentScreen();
            Debug.Log($"[BootFlow] preload {preloadStopwatch.ElapsedMilliseconds} ms (no saves)");
            return;
        }

        string saveId = model.NewestSave.saveId;

        using (EntityQuery gameDataQuery = entityManager.CreateEntityQuery(typeof(GameDataTag)))
        using (EntityQuery playerQuery = entityManager.CreateEntityQuery(typeof(Player)))
        {
            float waitedSeconds = 0f;
            while ((gameDataQuery.IsEmptyIgnoreFilter || playerQuery.IsEmptyIgnoreFilter) && waitedSeconds < 10f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, destroyToken);
                waitedSeconds += Time.unscaledDeltaTime;
            }
        }

        byte[] rawMomentBytes = null;
        string progressJson = null;
        await UniTask.RunOnThreadPool(() =>
        {
            if (MomentFiles.TryReadMomentBytes(saveId, out byte[] momentBytes))
                rawMomentBytes = momentBytes;
            if (SaveSlotStore.TryReadFile(saveId, SaveSlotStore.ProgressFileName, out byte[] progressBytes))
                progressJson = Encoding.UTF8.GetString(progressBytes);
        }, true, destroyToken);

        if (this == null || titleFinished || !world.IsCreated)
            return;

        if (!string.IsNullOrEmpty(progressJson))
        {
            SaveFile progressFile = JsonUtility.FromJson<SaveFile>(progressJson);
            if (TryFindSavedPlayerPosition(progressFile, out Unity.Mathematics.float3 playerPosition))
                SetStreamingFocusOverride(playerPosition);
        }

        if (rawMomentBytes != null)
        {
            World stagingWorld = MomentSnapshot.DeserializeIntoStagingWorld(rawMomentBytes);
            if (stagingWorld != null)
            {
                MomentPreload.Offer(saveId, stagingWorld);
                preloadedSaveId = saveId;
            }
        }

        model.ContinuePreloadFinished = true;
        controller.RefreshCurrentScreen();
        Debug.Log($"[BootFlow] preload {preloadStopwatch.ElapsedMilliseconds} ms");
    }

    private static bool TryFindSavedPlayerPosition(SaveFile progressFile, out Unity.Mathematics.float3 position)
    {
        position = default;
        if (progressFile?.entities == null)
            return false;

        foreach (EntityRecord record in progressFile.entities)
        {
            if (record.role != SaveRoles.Player)
                continue;
            if (!SaveSerialization.TryReadComponent(record, out LocalTransform playerTransform))
                continue;

            position = playerTransform.Position;
            return true;
        }
        return false;
    }

    private void SetStreamingFocusOverride(Unity.Mathematics.float3 position)
    {
        if (!entityManager.Exists(focusOverrideEntity))
            focusOverrideEntity = entityManager.CreateEntity(typeof(TerrainStreamingFocusOverride));
        entityManager.SetComponentData(focusOverrideEntity, new TerrainStreamingFocusOverride { position = position });
    }

    public void ContinueNewest()
    {
        // A second press during the preload wait would queue a second waiter and a second load.
        if (loadRequestInFlight)
            return;
        loadRequestInFlight = true;
        continueStopwatch = Stopwatch.StartNew();
        ContinueNewestAfterPreloadAsync().Forget();
    }

    private async UniTaskVoid ContinueNewestAfterPreloadAsync()
    {
        CancellationToken destroyToken = destroyCancellationToken;
        while (!model.ContinuePreloadFinished)
            await UniTask.Yield(PlayerLoopTiming.Update, destroyToken);

        if (this == null)
            return;
        if (!model.HasAnySave)
        {
            FinishTitle();
            return;
        }
        RequestLoad(model.NewestSave.saveId);
    }

    public void LoadSave(string saveId)
    {
        if (loadRequestInFlight)
            return;
        loadRequestInFlight = true;
        continueStopwatch = Stopwatch.StartNew();
        RequestLoad(saveId);
    }

    private void RequestLoad(string saveId)
    {
        using (EntityQuery gameDataQuery = entityManager.CreateEntityQuery(typeof(GameDataTag), typeof(LoadRequest)))
        {
            if (gameDataQuery.IsEmptyIgnoreFilter)
            {
                Debug.LogError("[BootFlow] No GameData entity with LoadRequest; cannot load.");
                FinishTitle();   // never strand the player on the title
                return;
            }

            Entity gameDataEntity = gameDataQuery.GetSingletonEntity();
            entityManager.SetComponentData(gameDataEntity, new LoadRequest { saveId = new FixedString64Bytes(saveId) });
            entityManager.SetComponentEnabled<LoadRequest>(gameDataEntity, true);
        }

        if (!loadFinishedSubscribed)
        {
            SaveNotifications.LoadFinished += OnLoadFinished;
            SaveNotifications.LoadFailed += OnLoadFinished;   // a failed load still drops into the authored world
            loadFinishedSubscribed = true;
        }
    }

    private void OnLoadFinished(string saveId)
    {
        SaveNotifications.LoadFinished -= OnLoadFinished;
        SaveNotifications.LoadFailed -= OnLoadFinished;
        loadFinishedSubscribed = false;
        FinishTitle();
        long elapsedMilliseconds = continueStopwatch != null ? continueStopwatch.ElapsedMilliseconds : 0L;
        Debug.Log($"[BootFlow] continue->control {elapsedMilliseconds} ms");
    }

    public void StartNewGame()
    {
        MomentPreload.Discard();
        preloadedSaveId = null;
        FinishTitle();
    }

    private void FinishTitle()
    {
        if (titleFinished)
            return;
        titleFinished = true;

        if (world != null && world.IsCreated && entityManager.Exists(focusOverrideEntity))
            entityManager.DestroyEntity(focusOverrideEntity);
        ReleaseHold();
        view?.Hide(titleFadeSeconds);
        backdrop?.Exit(titleFadeSeconds);
        controller?.Stop();
    }

    public void DeleteSave(string saveId)
    {
        SaveSlotStore.DeleteSave(saveId);
        if (saveId == preloadedSaveId)
        {
            MomentPreload.Discard();
            preloadedSaveId = null;
        }
        RefreshSaveList();
    }

    public void OpenOptions()
    {
        onOpenOptions?.Invoke();
    }

    public void Quit()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void ReleaseHold()
    {
        if (world == null || !world.IsCreated)
            return;
        if (entityManager.Exists(holdEntity))
            entityManager.DestroyEntity(holdEntity);
        holdEntity = Entity.Null;
    }

    private void OnDestroy()
    {
        if (loadFinishedSubscribed)
        {
            SaveNotifications.LoadFinished -= OnLoadFinished;
            SaveNotifications.LoadFailed -= OnLoadFinished;
            loadFinishedSubscribed = false;
        }
        ReleaseHold();
        controller?.Stop();
    }
}
