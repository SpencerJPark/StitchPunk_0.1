#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using Unity.Entities;
using UnityEngine;

public class UnitInspectorPanel : MonoBehaviour
{
    private const float PanelWidth = 320f;
    private const float PanelMargin = 10f;
    private const float PanelTop = 250f;
    private const int MaxOptionRows = 12;

    private static UnitInspectorPanel installedInstance;

    private UnitInspectorReader reader;
    private UnitInspectorSnapshot snapshot;
    private Rect panelRect;
    private Vector2 scrollPosition;
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private readonly StringBuilder lineBuilder = new StringBuilder(128);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (installedInstance != null)
            return;

        GameObject host = new GameObject("UnitInspectorPanel");
        DontDestroyOnLoad(host);
        installedInstance = host.AddComponent<UnitInspectorPanel>();
    }

    private void Awake()
    {
        DebugMenuVisibility.RegisterToggle(UnitInspectorPicker.ToggleName);
        reader = new UnitInspectorReader();
        snapshot = new UnitInspectorSnapshot();
        UnitInspectorOverlay.Snapshot = snapshot;
        panelRect = BuildPanelRect();
    }

    private static Rect BuildPanelRect()
    {
        return new Rect(Screen.width - PanelWidth - PanelMargin, PanelTop, PanelWidth, Screen.height - PanelTop - PanelMargin);
    }

    private void Update()
    {
        if (!DebugMenuVisibility.IsToggleOn(UnitInspectorPicker.ToggleName))
        {
            if (UnitInspectorPicker.WatchedUnit != Entity.Null)
                UnitInspectorPicker.ClearWatchedUnit();
            snapshot.Clear();
            return;
        }

        panelRect = BuildPanelRect();
        UnitInspectorPicker.Tick(panelRect);

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            snapshot.Clear();
            return;
        }

        reader.Fill(world.EntityManager, UnitInspectorPicker.WatchedUnit, snapshot, Time.realtimeSinceStartup);
    }

    private void OnGUI()
    {
        if (!DebugMenuVisibility.IsToggleOn(UnitInspectorPicker.ToggleName))
            return;

        EnsureStyles();
        GUI.Box(panelRect, GUIContent.none);
        GUILayout.BeginArea(panelRect);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        if (UnitInspectorPicker.WatchedUnit == Entity.Null)
            GUILayout.Label("Click a unit to inspect it.", labelStyle);
        else if (!snapshot.hasUnit)
            GUILayout.Label("Watched unit is gone.", labelStyle);
        else
            DrawSnapshot();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void EnsureStyles()
    {
        if (labelStyle != null)
            return;

        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, wordWrap = true };
        headerStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold };
    }

    private void DrawLine(string text)
    {
        GUILayout.Label(text, labelStyle);
    }

    private void DrawSnapshot()
    {
        GUILayout.Label("Unit", headerStyle);
        DrawLine(snapshot.unitLabel + "  (" + snapshot.unitType + ", " + snapshot.faction + ")");
        DrawLine("HP " + snapshot.health + "/" + snapshot.healthMax + (snapshot.isDead ? "  <color=red>DEAD</color>" : ""));
        if (GUILayout.Button("Clear"))
            UnitInspectorPicker.ClearWatchedUnit();

        GUILayout.Label("Plan", headerStyle);
        DrawLine("Action " + snapshot.action + "  Behaviour " + snapshot.behavior);
        DrawLine("Phase " + snapshot.phase + "  Command " + snapshot.commandIndex);
        DrawLine("Target " + snapshot.targetLabel);
        DrawLine("Nav " + ColourTag(snapshot.navState) + "  Mode " + snapshot.navModeLabel);
        DrawLine("Waypoint " + snapshot.currentWaypointIndex + " / " + snapshot.pathPoints.Count);
        DrawLine("Goal dist " + snapshot.distanceToGoal.ToString("F1") + "  Stop " + snapshot.stoppingDistance.ToString("F1")
            + "  Sight " + snapshot.awarenessRange.ToString("F1"));

        GUILayout.Label(snapshot.optionsAgeSeconds > 0.05f ? "Why  (scored " + snapshot.optionsAgeSeconds.ToString("0.0") + "s ago)" : "Why", headerStyle);
        int optionCount = Mathf.Min(snapshot.options.Count, MaxOptionRows);
        for (int index = 0; index < optionCount; index++)
        {
            UnitInspectorOptionRow option = snapshot.options[index];
            lineBuilder.Clear();
            lineBuilder.Append(option.isWinner ? "> " : "  ");
            lineBuilder.Append(option.action).Append("  u").Append(option.totalUtility.ToString("F2"));
            lineBuilder.Append("  p").Append(option.priority);
            if (option.isPlayerOrdered)
                lineBuilder.Append("  P");
            DrawLine(lineBuilder.ToString());
        }

        GUILayout.Label("Needs", headerStyle);
        for (int index = 0; index < snapshot.motivations.Count; index++)
        {
            UnitInspectorMotivationRow motivation = snapshot.motivations[index];
            DrawLine(motivation.need + " " + motivation.value.ToString("F1") + " (" + motivation.decayRate.ToString("F2") + ")");
        }
        for (int index = 0; index < snapshot.personality.Count; index++)
        {
            UnitInspectorPersonalityRow trait = snapshot.personality[index];
            DrawLine(trait.personality + " " + trait.value.ToString("F2"));
        }

        GUILayout.Label("History", headerStyle);
        for (int index = 0; index < snapshot.history.Count; index++)
        {
            UnitInspectorHistoryRow row = snapshot.history[index];
            lineBuilder.Clear();
            lineBuilder.Append('-').Append(row.secondsAgo.ToString("F1")).Append("s  ");
            lineBuilder.Append(row.fromAction).Append(" -> ").Append(row.toAction);
            lineBuilder.Append("  [").Append(row.cause).Append("] p").Append(row.priority);
            lineBuilder.Append(" u").Append(row.winningUtility.ToString("F2"));
            if (row.wasPlayerOrdered)
                lineBuilder.Append(" P");
            DrawLine(lineBuilder.ToString());
        }
    }

    private static string ColourTag(UnitInspectorNavState navState)
    {
        string colour;
        switch (navState)
        {
            case UnitInspectorNavState.Walking: colour = "green"; break;
            case UnitInspectorNavState.WaitingForPath: colour = "yellow"; break;
            case UnitInspectorNavState.Stuck: colour = "red"; break;
            case UnitInspectorNavState.Abstract: colour = "#5aa0ff"; break;
            default: colour = "grey"; break;
        }
        return "<color=" + colour + ">" + navState + "</color>";
    }
}
#endif
