using System;
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#endif

// Editor-only: terrain feature components must not raise rebake events while scenes open,
// the domain reloads or play mode flips, because those fire OnEnable/OnValidate on every feature.
// OnValidate also fires on every load with nothing changed, so it is change-gated through ShouldRaiseForValidate.
public static class TerrainRebakeGate
{
#if UNITY_EDITOR
    private const double SettleSeconds = 1.0;
    private const int SettleUpdateTicks = 8;
    private const int PruneDestroyedKeysAboveCount = 256;
    private const string KillSwitchFilePath = "UserSettings/TerrainAutoRebake.off";
    private const string KillSwitchMenuPath = "Stitch Punk/Terrain/Auto Rebake On Edit";

    private static bool sceneOpeningInProgress;
    private static bool domainReloadInProgress;
    private static bool playModeTransitionInProgress;
    private static int manualSuppressionDepth;
    private static double suppressedUntilEditorTime;
    private static int settleUpdatesRemaining;
    private static double lastLoadEventEditorTime;
    private static bool autoRebakeKillSwitchEngaged;

    private static readonly Dictionary<UnityEngine.Object, Hash128> lastSeenSerializedStateByComponent = new Dictionary<UnityEngine.Object, Hash128>();

    public static bool SuppressRebakeEvents
    {
        get
        {
            if (autoRebakeKillSwitchEngaged || manualSuppressionDepth > 0 || sceneOpeningInProgress || domainReloadInProgress || playModeTransitionInProgress)
            {
                return true;
            }

            // One long startup frame is one tick, so a slow first frame cannot outlast the clock alone.
            return settleUpdatesRemaining > 0 || EditorApplication.timeSinceStartup < suppressedUntilEditorTime;
        }
    }

    public static bool AutoRebakeKillSwitchEngaged => autoRebakeKillSwitchEngaged;

    public static double SecondsSinceLastLoadEvent => EditorApplication.timeSinceStartup - lastLoadEventEditorTime;

    public static bool ShouldRaiseForValidate(MonoBehaviour component)
    {
        if (component == null)
        {
            return false;
        }

        // Record before checking suppression, so a value changed under Suppress() is not re-raised later.
        bool serializedStateChanged = RecordSerializedStateAndReportChange(component);
        return serializedStateChanged && !SuppressRebakeEvents;
    }

    public static void RememberSerializedState(MonoBehaviour component)
    {
        if (component == null)
        {
            return;
        }

        RecordSerializedStateAndReportChange(component);
    }

    public static void EndSettleForTests()
    {
        settleUpdatesRemaining = 0;
        suppressedUntilEditorTime = 0.0;
    }

    private static bool RecordSerializedStateAndReportChange(MonoBehaviour component)
    {
        Hash128 currentState = Hash128.Compute(EditorJsonUtility.ToJson(component));
        bool seenBefore = lastSeenSerializedStateByComponent.TryGetValue(component, out Hash128 previousState);
        if (!seenBefore && lastSeenSerializedStateByComponent.Count >= PruneDestroyedKeysAboveCount)
        {
            PruneDestroyedComponents();
        }

        lastSeenSerializedStateByComponent[component] = currentState;
        return seenBefore && previousState != currentState;
    }

    private static void PruneDestroyedComponents()
    {
        List<UnityEngine.Object> destroyedComponents = new List<UnityEngine.Object>();
        foreach (UnityEngine.Object trackedComponent in lastSeenSerializedStateByComponent.Keys)
        {
            if (trackedComponent == null) destroyedComponents.Add(trackedComponent);
        }

        foreach (UnityEngine.Object destroyedComponent in destroyedComponents)
        {
            lastSeenSerializedStateByComponent.Remove(destroyedComponent);
        }
    }

    [InitializeOnLoadMethod]
    private static void RegisterEditorHooks()
    {
        autoRebakeKillSwitchEngaged = File.Exists(KillSwitchFilePath);
        ExtendSuppression();
        MarkLoadEvent();

        EditorSceneManager.sceneOpening -= HandleSceneOpening;
        EditorSceneManager.sceneOpening += HandleSceneOpening;
        EditorSceneManager.sceneOpened -= HandleSceneOpened;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.update -= CountDownSettleTicks;
        EditorApplication.update += CountDownSettleTicks;
    }

    private static void CountDownSettleTicks()
    {
        if (settleUpdatesRemaining > 0) settleUpdatesRemaining--;
    }

    private static void HandleSceneOpening(string path, OpenSceneMode mode)
    {
        sceneOpeningInProgress = true;
    }

    private static void HandleSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        sceneOpeningInProgress = false;
        ExtendSuppression();
        MarkLoadEvent();
    }

    private static void HandleBeforeAssemblyReload()
    {
        domainReloadInProgress = true;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange change)
    {
        switch (change)
        {
            case PlayModeStateChange.ExitingEditMode:
            case PlayModeStateChange.ExitingPlayMode:
                playModeTransitionInProgress = true;
                break;
            case PlayModeStateChange.EnteredEditMode:
            case PlayModeStateChange.EnteredPlayMode:
                playModeTransitionInProgress = false;
                ExtendSuppression();
                MarkLoadEvent();
                break;
        }
    }

    private static void ExtendSuppression()
    {
        suppressedUntilEditorTime = EditorApplication.timeSinceStartup + SettleSeconds;
        settleUpdatesRemaining = SettleUpdateTicks;
    }

    private static void MarkLoadEvent()
    {
        lastLoadEventEditorTime = EditorApplication.timeSinceStartup;
    }

    // The file works with the Editor closed: a hung Editor is rescued by creating UserSettings/TerrainAutoRebake.off.
    [MenuItem(KillSwitchMenuPath)]
    private static void ToggleAutoRebakeOnEdit()
    {
        if (File.Exists(KillSwitchFilePath))
        {
            File.Delete(KillSwitchFilePath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KillSwitchFilePath));
            File.WriteAllText(KillSwitchFilePath, "Terrain auto rebake on edit is off while this file exists.\n");
        }

        autoRebakeKillSwitchEngaged = File.Exists(KillSwitchFilePath);
        Debug.Log("[TerrainRebake] auto rebake on edit " + (autoRebakeKillSwitchEngaged ? "OFF" : "ON"));
    }

    [MenuItem(KillSwitchMenuPath, true)]
    private static bool ValidateToggleAutoRebakeOnEdit()
    {
        Menu.SetChecked(KillSwitchMenuPath, !autoRebakeKillSwitchEngaged);
        return true;
    }

    private sealed class SuppressionScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            manualSuppressionDepth--;
            ExtendSuppression();
        }
    }

    public static IDisposable Suppress()
    {
        manualSuppressionDepth++;
        return new SuppressionScope();
    }
#else
    public static bool SuppressRebakeEvents => false;

    public static bool AutoRebakeKillSwitchEngaged => false;

    public static double SecondsSinceLastLoadEvent => double.MaxValue;

    public static bool ShouldRaiseForValidate(UnityEngine.MonoBehaviour component)
    {
        return false;
    }

    public static void RememberSerializedState(UnityEngine.MonoBehaviour component)
    {
    }

    public static void EndSettleForTests()
    {
    }

    private sealed class NoopScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    public static IDisposable Suppress()
    {
        return new NoopScope();
    }
#endif
}
