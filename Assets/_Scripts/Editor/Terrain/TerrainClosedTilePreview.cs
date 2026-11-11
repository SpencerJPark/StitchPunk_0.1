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
    static double nextPollTime;
    static ulong trackedWorldSequenceNumber;

    static TerrainClosedTilePreview()
    {
        EditorApplication.update += PollWhileEditing;
        EditorApplication.playModeStateChanged += (PlayModeStateChange change) =>
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.EnteredPlayMode) previewedSceneEntities.Clear();
        };
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
        if (EditorApplication.timeSinceStartup < nextPollTime) return;
        nextPollTime = EditorApplication.timeSinceStartup + POLL_SECONDS;
        if (!IsPreviewEnabled) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            previewedSceneEntities.Clear();
            return;
        }
        if (world.SequenceNumber != trackedWorldSequenceNumber)
        {
            previewedSceneEntities.Clear();
            trackedWorldSequenceNumber = world.SequenceNumber;
        }
        if (world.GetExistingSystem<SceneSystem>() == SystemHandle.Null) return;

        EntityManager entityManager = world.EntityManager;
        wantedSceneGuids.Clear();
        foreach (SubScene subScene in Object.FindObjectsByType<SubScene>(FindObjectsInactive.Exclude))
        {
            if (!subScene.name.StartsWith("Tile_")) continue;
            Unity.Entities.Hash128 sceneGuid = subScene.SceneGUID;
            if (!sceneGuid.IsValid || subScene.EditingScene.isLoaded) continue;
            wantedSceneGuids.Add(sceneGuid);

            bool needsLoadRequest = true;
            if (previewedSceneEntities.TryGetValue(sceneGuid, out Entity trackedEntity)
                && entityManager.Exists(trackedEntity)
                && entityManager.HasComponent<RequestSceneLoaded>(trackedEntity))
                needsLoadRequest = false;
            if (!needsLoadRequest) continue;

            Entity sceneEntity = SceneSystem.LoadSceneAsync(world.Unmanaged, sceneGuid, new SceneSystem.LoadParameters());
            if (sceneEntity != Entity.Null) previewedSceneEntities[sceneGuid] = sceneEntity;
        }

        // A tile opened for edit (or removed) is simply forgotten: live baking and the SubScene own that entity then,
        // so unloading it here could blank the tile the user is editing.
        staleSceneGuids.Clear();
        foreach (KeyValuePair<Unity.Entities.Hash128, Entity> previewed in previewedSceneEntities)
            if (!wantedSceneGuids.Contains(previewed.Key)) staleSceneGuids.Add(previewed.Key);
        foreach (Unity.Entities.Hash128 staleGuid in staleSceneGuids) previewedSceneEntities.Remove(staleGuid);
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
        previewedSceneEntities.Clear();
    }
}
#endif
