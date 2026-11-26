using System;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Editor-only: terrain feature components must not raise rebake events while scenes open,
// the domain reloads or play mode flips, because those fire OnEnable/OnValidate on every feature.
public static class TerrainRebakeGate
{
#if UNITY_EDITOR
    private const double SettleSeconds = 1.0;

    private static bool sceneOpeningInProgress;
    private static bool domainReloadInProgress;
    private static bool playModeTransitionInProgress;
    private static int manualSuppressionDepth;
    private static double suppressedUntilEditorTime;

    public static bool SuppressRebakeEvents
    {
        get
        {
            if (manualSuppressionDepth > 0 || sceneOpeningInProgress || domainReloadInProgress || playModeTransitionInProgress)
            {
                return true;
            }

            return EditorApplication.timeSinceStartup < suppressedUntilEditorTime;
        }
    }

    [InitializeOnLoadMethod]
    private static void RegisterEditorHooks()
    {
        suppressedUntilEditorTime = EditorApplication.timeSinceStartup + SettleSeconds;

        EditorSceneManager.sceneOpening -= HandleSceneOpening;
        EditorSceneManager.sceneOpening += HandleSceneOpening;
        EditorSceneManager.sceneOpened -= HandleSceneOpened;
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandleSceneOpening(string path, OpenSceneMode mode)
    {
        sceneOpeningInProgress = true;
    }

    private static void HandleSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        sceneOpeningInProgress = false;
        ExtendSuppression();
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
                break;
        }
    }

    private static void ExtendSuppression()
    {
        suppressedUntilEditorTime = EditorApplication.timeSinceStartup + SettleSeconds;
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
