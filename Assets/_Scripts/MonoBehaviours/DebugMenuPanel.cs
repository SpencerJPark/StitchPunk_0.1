#if UNITY_EDITOR || DEVELOPMENT_BUILD
using DotsMovementToolkit;
using UnityEngine;

public class DebugMenuPanel : MonoBehaviour
{
    private const float PanelWidth = 260f;
    private const float PanelMargin = 10f;
    private const int PanelWindowId = 7731;

    private static DebugMenuPanel installedInstance;

    private Rect windowRect;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (installedInstance != null)
            return;

        GameObject host = new GameObject("DebugMenuPanel");
        DontDestroyOnLoad(host);
        installedInstance = host.AddComponent<DebugMenuPanel>();
    }

    private void Update()
    {
        DebugMenuVisibility.PollToggleAction();
    }

    private void OnGUI()
    {
        if (!DebugMenuVisibility.IsPanelOpen)
            return;

        // Height 0 lets the layout shrink the window to its content.
        windowRect = new Rect(Screen.width - PanelWidth - PanelMargin, PanelMargin, PanelWidth, 0f);
        string title = "Debug  (" + DebugMenuVisibility.ToggleBindingDisplayText + " closes)";
        GUILayout.Window(PanelWindowId, windowRect, DrawPanelContents, title);
    }

    private static void DrawPanelContents(int windowId)
    {
        System.Collections.Generic.IReadOnlyList<string> toggleNames = DebugMenuVisibility.RegisteredToggleNames;
        if (toggleNames.Count == 0)
        {
            Color previousColor = GUI.color;
            GUI.color = Color.grey;
            GUILayout.Label("No debug tools registered");
            GUI.color = previousColor;
        }

        for (int index = 0; index < toggleNames.Count; index++)
        {
            string toggleName = toggleNames[index];
            bool currentValue = DebugMenuVisibility.IsToggleOn(toggleName);
            bool newValue = GUILayout.Toggle(currentValue, toggleName);
            if (newValue != currentValue)
                DebugMenuVisibility.SetToggle(toggleName, newValue);
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Nav grid:", GUILayout.Width(70f));
        DrawNavGridModeButton("Off", NavGridDebugDisplayMode.Off);
        DrawNavGridModeButton("Obstacles", NavGridDebugDisplayMode.ObstaclesOnly);
        DrawNavGridModeButton("Full", NavGridDebugDisplayMode.FullGrid);
        GUILayout.EndHorizontal();
    }

    private static void DrawNavGridModeButton(string label, NavGridDebugDisplayMode mode)
    {
        bool isCurrent = DebugMenuVisibility.NavGridMode == mode;
        if (GUILayout.Button(isCurrent ? "> " + label : label))
            DebugMenuVisibility.NavGridMode = mode;
    }
}
#endif
