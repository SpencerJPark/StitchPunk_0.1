#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

// State holder for the Scene-view terrain overlay; TerrainToolOverlay renders it.
public static class TerrainSceneHud
{
    private static string activeToolName;
    private static IReadOnlyList<TerrainKeyHint> activeHints;
    private static string cursorInfo = string.Empty;
    private static string toastMessage;
    private static double toastExpiresAt;

    public static event Action Changed;

    public static string ActiveToolName => activeToolName;
    public static IReadOnlyList<TerrainKeyHint> ActiveHints => activeHints;
    public static string CursorInfo => cursorInfo;

    public static string ActiveToast
    {
        get
        {
            if (toastMessage == null || EditorApplication.timeSinceStartup >= toastExpiresAt)
            {
                return null;
            }
            return toastMessage;
        }
    }

    public static double ActiveToastSecondsRemaining => Math.Max(0.0, toastExpiresAt - EditorApplication.timeSinceStartup);

    public static void SetActiveTool(string toolName, IReadOnlyList<TerrainKeyHint> hints)
    {
        activeToolName = toolName;
        activeHints = hints != null && hints.Count > 0 ? hints : null;
        RaiseChanged();
    }

    public static void Clear()
    {
        activeToolName = null;
        activeHints = null;
        cursorInfo = string.Empty;
        toastMessage = null;
        RaiseChanged();
    }

    public static void SetCursorInfo(string text)
    {
        string newText = text ?? string.Empty;
        if (newText == cursorInfo)
        {
            return;
        }
        cursorInfo = newText;
        RaiseChanged();
    }

    public static void ShowToast(string message, double seconds = 3.0)
    {
        toastMessage = string.IsNullOrEmpty(message) ? null : message;
        toastExpiresAt = EditorApplication.timeSinceStartup + seconds;
        RaiseChanged();
    }

    private static void RaiseChanged()
    {
        Changed?.Invoke();
        SceneView.RepaintAll();
    }
}
#endif
