#if UNITY_EDITOR
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TerrainLoadTimer
{
    const string ENABLED_PREF_KEY = "TerrainEditor.LogLoadTimes";
    const string MENU_PATH = "Stitch Punk/Terrain/Log Load Times";

    static Stopwatch sceneOpenStopwatch;
    static string sceneOpenName;
    static Stopwatch domainReloadStopwatch;

    public static bool Enabled
    {
        get { return EditorPrefs.GetBool(ENABLED_PREF_KEY, false); }
        set { EditorPrefs.SetBool(ENABLED_PREF_KEY, value); }
    }

    static TerrainLoadTimer()
    {
        // Static constructors run right after a domain reload, so this stopwatch measures reload -> first update.
        domainReloadStopwatch = Stopwatch.StartNew();
        EditorApplication.update += LogFirstUpdateAfterEvents;
        EditorSceneManager.sceneOpened += (Scene openedScene, OpenSceneMode openMode) =>
        {
            if (openMode != OpenSceneMode.Single) return;
            sceneOpenName = openedScene.name;
            sceneOpenStopwatch = Stopwatch.StartNew();
        };
    }

    static void LogFirstUpdateAfterEvents()
    {
        if (domainReloadStopwatch != null)
        {
            Record("domain reload", domainReloadStopwatch.Elapsed.TotalMilliseconds);
            domainReloadStopwatch = null;
        }
        if (sceneOpenStopwatch != null)
        {
            Record("scene open " + sceneOpenName, sceneOpenStopwatch.Elapsed.TotalMilliseconds);
            sceneOpenStopwatch = null;
        }
    }

    public static Stopwatch Start()
    {
        return Stopwatch.StartNew();
    }

    public static void Record(string stage, double milliseconds)
    {
        if (!Enabled) return;
        UnityEngine.Debug.Log("[TerrainLoad] " + stage + " " + milliseconds.ToString("F0") + " ms");
    }

    [MenuItem(MENU_PATH)]
    static void ToggleEnabled()
    {
        Enabled = !Enabled;
    }

    [MenuItem(MENU_PATH, true)]
    static bool ToggleEnabledValidate()
    {
        Menu.SetChecked(MENU_PATH, Enabled);
        return true;
    }
}
#endif
