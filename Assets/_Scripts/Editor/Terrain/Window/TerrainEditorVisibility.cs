#if UNITY_EDITOR
using System;
using UnityEditor;

public static class TerrainEditorVisibility
{
    private static bool toolsLive;

    public static bool ToolsLive => toolsLive;

    public static event Action Changed;

    public static void SetToolsLive(bool live)
    {
        if (toolsLive == live)
        {
            return;
        }

        toolsLive = live;

        // Hidden window must leave no armed drawing or stale HUD behind.
        if (!live)
        {
            TerrainFeatureTool.CancelDrawing();
            TerrainSceneHud.Clear();
        }

        Changed?.Invoke();
        SceneView.RepaintAll();
    }
}
#endif
