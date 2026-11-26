#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

// Closed tile subscenes have AutoLoadScene off and draw nothing in edit mode. This asks the SubScene's own scene
// entity to load its cached baked content (the same toggle as the SubScene inspector's Load button), so no rebake runs.
[InitializeOnLoad]
public static class TerrainClosedTilePreview
{
    const string PREVIEW_ENABLED_PREF = "TerrainEditor.PreviewClosedTiles";
    const string MENU_PATH = "Stitch Punk/Terrain/Preview Closed Tiles";
    const double POLL_SECONDS = 0.5;

    static readonly Dictionary<Unity.Entities.Hash128, Entity> previewedSceneEntities = new Dictionary<Unity.Entities.Hash128, Entity>();
    static readonly HashSet<Unity.Entities.Hash128> wantedSceneGuids = new HashSet<Unity.Entities.Hash128>();
    static readonly List<Unity.Entities.Hash128> staleSceneGuids = new List<Unity.Entities.Hash128>();
    const double IN_FLIGHT_TIMEOUT_SECONDS = 10.0;

    static readonly HashSet<Unity.Entities.Hash128> requestedSceneGuids = new HashSet<Unity.Entities.Hash128>();
    static bool hasInFlightTile;
    static Unity.Entities.Hash128 inFlightSceneGuid;
    static string inFlightTileName;
    static string nextTileName;
    static double inFlightStartTime;
    static double nextPollTime;
    static ulong trackedWorldSequenceNumber;

    static TerrainClosedTilePreview()
    {
        EditorApplication.update += PollWhileEditing;
        UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (UnityEngine.SceneManagement.Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode) => ForgetRequests();
        EditorApplication.playModeStateChanged += (PlayModeStateChange change) =>
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.EnteredPlayMode) ForgetRequests();
        };
    }

    static void ForgetRequests()
    {
        previewedSceneEntities.Clear();
        requestedSceneGuids.Clear();
        hasInFlightTile = false;
    }

    static bool IsPreviewEnabled => EditorPrefs.GetBool(PREVIEW_ENABLED_PREF, true);

    [MenuItem(MENU_PATH)]
    static void TogglePreview()
    {
        bool nowEnabled = !IsPreviewEnabled;
        EditorPrefs.SetBool(PREVIEW_ENABLED_PREF, nowEnabled);
        if (!nowEnabled) UnloadAllPreviews();
    }

    [MenuItem(MENU_PATH, true)]
    static bool TogglePreviewValidate()
    {
        Menu.SetChecked(MENU_PATH, IsPreviewEnabled);
        return true;
    }

    static void PollWhileEditing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.timeSinceStartup < nextPollTime) return;
        nextPollTime = EditorApplication.timeSinceStartup + POLL_SECONDS;
        if (!IsPreviewEnabled) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ForgetRequests();
            return;
        }
        if (world.SequenceNumber != trackedWorldSequenceNumber)
        {
            ForgetRequests();
            trackedWorldSequenceNumber = world.SequenceNumber;
        }
        if (world.GetExistingSystem<SceneSystem>() == SystemHandle.Null) return;

        EntityManager entityManager = world.EntityManager;
        wantedSceneGuids.Clear();
        Vector3 cameraPosition = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera.transform.position : Vector3.zero;
        bool hasNextTile = false;
        Unity.Entities.Hash128 nextTileGuid = default;
        float nextTileDistance = float.MaxValue;
        foreach (SubScene subScene in Object.FindObjectsByType<SubScene>(FindObjectsInactive.Exclude))
        {
            if (!subScene.name.StartsWith("Tile_")) continue;
            Unity.Entities.Hash128 sceneGuid = subScene.SceneGUID;
            if (!sceneGuid.IsValid || subScene.EditingScene.isLoaded) continue;
            wantedSceneGuids.Add(sceneGuid);
            if (requestedSceneGuids.Contains(sceneGuid)) continue;
            float distance = (subScene.transform.position - cameraPosition).sqrMagnitude;
            if (distance >= nextTileDistance) continue;
            nextTileDistance = distance;
            nextTileGuid = sceneGuid;
            hasNextTile = true;
            nextTileName = subScene.name;
        }

        bool anyPreviewStillStreaming = false;
        if (hasInFlightTile)
        {
            bool inFlightLoaded = previewedSceneEntities.TryGetValue(inFlightSceneGuid, out Entity trackedEntity)
                && entityManager.Exists(trackedEntity)
                && SceneSystem.IsSceneLoaded(world.Unmanaged, trackedEntity);
            if (inFlightLoaded || !wantedSceneGuids.Contains(inFlightSceneGuid))
            {
                hasInFlightTile = false;
            }
            else if (EditorApplication.timeSinceStartup - inFlightStartTime > IN_FLIGHT_TIMEOUT_SECONDS)
            {
                Debug.LogWarning("TerrainClosedTilePreview: " + inFlightTileName + " did not load within "
                    + IN_FLIGHT_TIMEOUT_SECONDS + " s; moving on to the next closed tile.");
                hasInFlightTile = false;
            }
            else
            {
                // Same tile re-asked until loaded: right after a reload the scene entity exists but its sections are
                // not resolved yet, and a request made then never reaches them.
                anyPreviewStillStreaming = true;
                Entity sceneEntity = SceneSystem.LoadSceneAsync(world.Unmanaged, inFlightSceneGuid, new SceneSystem.LoadParameters());
                if (sceneEntity != Entity.Null) previewedSceneEntities[inFlightSceneGuid] = sceneEntity;
            }
        }

        if (!hasInFlightTile && hasNextTile)
        {
            hasInFlightTile = true;
            inFlightSceneGuid = nextTileGuid;
            inFlightTileName = nextTileName;
            inFlightStartTime = EditorApplication.timeSinceStartup;
            requestedSceneGuids.Add(nextTileGuid);
            anyPreviewStillStreaming = true;
            Entity sceneEntity = SceneSystem.LoadSceneAsync(world.Unmanaged, nextTileGuid, new SceneSystem.LoadParameters());
            if (sceneEntity != Entity.Null) previewedSceneEntities[nextTileGuid] = sceneEntity;
        }

        // A tile opened for edit (or removed) is simply forgotten: live baking and the SubScene own that entity then,
        // so unloading it here could blank the tile the user is editing.
        staleSceneGuids.Clear();
        foreach (KeyValuePair<Unity.Entities.Hash128, Entity> previewed in previewedSceneEntities)
            if (!wantedSceneGuids.Contains(previewed.Key)) staleSceneGuids.Add(previewed.Key);
        foreach (Unity.Entities.Hash128 staleGuid in staleSceneGuids) previewedSceneEntities.Remove(staleGuid);

        // An idle edit-mode world only ticks when something repaints, so the requested tiles would sit unloaded
        // (an empty Scene view) until the mouse moved.
        if (anyPreviewStillStreaming)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }

    static void UnloadAllPreviews()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated && world.SequenceNumber == trackedWorldSequenceNumber)
        {
            foreach (KeyValuePair<Unity.Entities.Hash128, Entity> previewed in previewedSceneEntities)
            {
                if (!world.EntityManager.Exists(previewed.Value)) continue;
                SceneSystem.UnloadScene(world.Unmanaged, previewed.Value, SceneSystem.UnloadParameters.Default);
            }
        }
        ForgetRequests();
    }
}
#endif
