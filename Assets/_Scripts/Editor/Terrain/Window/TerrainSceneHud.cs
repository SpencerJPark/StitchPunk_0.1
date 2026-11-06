#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainSceneHud
{
    private const float BottomMargin = 16f;
    private const float SideMargin = 16f;
    private const float StripHeight = 28f;
    private const float PaddingHorizontal = 12f;
    private const float Gap = 8f;
    private const float KeycapPaddingHorizontal = 6f;
    private const float KeycapHeight = 18f;

    private static string activeToolName;
    private static IReadOnlyList<TerrainKeyHint> activeHints;
    private static string cursorInfo = string.Empty;

    private static bool stylesBuiltForProSkin;
    private static bool stylesBuilt;
    private static GUIStyle toolNameStyle;
    private static GUIStyle keycapStyle;
    private static GUIStyle actionStyle;
    private static GUIStyle cursorStyle;

    static TerrainSceneHud()
    {
        SceneView.duringSceneGui += DrawInSceneView;
    }

    public static void SetActiveTool(string toolName, IReadOnlyList<TerrainKeyHint> hints)
    {
        activeToolName = toolName;
        activeHints = hints != null && hints.Count > 0 ? hints : null;
        SceneView.RepaintAll();
    }

    public static void Clear()
    {
        activeToolName = null;
        activeHints = null;
        cursorInfo = string.Empty;
        SceneView.RepaintAll();
    }

    public static void SetCursorInfo(string text)
    {
        string newText = text ?? string.Empty;
        if (newText == cursorInfo)
        {
            return;
        }
        cursorInfo = newText;
        SceneView.RepaintAll();
    }

    private static void EnsureStyles()
    {
        bool proSkin = EditorGUIUtility.isProSkin;
        if (stylesBuilt && stylesBuiltForProSkin == proSkin)
        {
            return;
        }
        stylesBuilt = true;
        stylesBuiltForProSkin = proSkin;

        Color textColor = proSkin ? new Color(0.90f, 0.90f, 0.90f) : new Color(0.96f, 0.96f, 0.96f);
        Color mutedColor = new Color(0.72f, 0.72f, 0.72f);

        toolNameStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            clipping = TextClipping.Overflow
        };
        toolNameStyle.normal.textColor = textColor;

        keycapStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
            clipping = TextClipping.Overflow
        };
        keycapStyle.normal.textColor = textColor;

        actionStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            clipping = TextClipping.Overflow
        };
        actionStyle.normal.textColor = mutedColor;

        cursorStyle = new GUIStyle(actionStyle);
    }

    private static void DrawCard(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.16f));
        Rect inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        EditorGUI.DrawRect(inner, new Color(0.11f, 0.11f, 0.11f, 0.88f));
    }

    private static void DrawInSceneView(SceneView sceneView)
    {
        if (Event.current.type != EventType.Repaint || activeHints == null || EditorApplication.isPlaying)
        {
            return;
        }

        EnsureStyles();
        float viewWidth = sceneView.position.width;
        float viewHeight = sceneView.position.height;
        float maxStripWidth = viewWidth - SideMargin * 2f;

        Vector2 nameSize = toolNameStyle.CalcSize(new GUIContent(activeToolName ?? string.Empty));
        float contentWidth = PaddingHorizontal * 2f + nameSize.x;
        int fittingHintCount = 0;
        float[] keycapWidths = new float[activeHints.Count];
        float[] actionWidths = new float[activeHints.Count];
        for (int index = 0; index < activeHints.Count; index++)
        {
            keycapWidths[index] = keycapStyle.CalcSize(new GUIContent(activeHints[index].keys ?? string.Empty)).x + KeycapPaddingHorizontal * 2f;
            actionWidths[index] = actionStyle.CalcSize(new GUIContent(activeHints[index].action ?? string.Empty)).x;
            float hintWidth = Gap * 2f + 1f + (index == 0 ? 0f : 0f) + keycapWidths[index] + 4f + actionWidths[index];
            if (index == 0)
            {
                hintWidth = Gap * 2f + 1f + keycapWidths[index] + 4f + actionWidths[index];
            }
            else
            {
                hintWidth = Gap + keycapWidths[index] + 4f + actionWidths[index];
            }
            if (contentWidth + hintWidth > maxStripWidth)
            {
                break;
            }
            contentWidth += hintWidth;
            fittingHintCount = index + 1;
        }

        float stripWidth = Mathf.Min(contentWidth, maxStripWidth);
        Rect stripRect = new Rect((viewWidth - stripWidth) * 0.5f, viewHeight - BottomMargin - StripHeight, stripWidth, StripHeight);

        Handles.BeginGUI();
        DrawCard(stripRect);

        float cursorX = stripRect.x + PaddingHorizontal;
        Rect nameRect = new Rect(cursorX, stripRect.y, nameSize.x, StripHeight);
        GUI.Label(nameRect, activeToolName ?? string.Empty, toolNameStyle);
        cursorX += nameSize.x;

        for (int index = 0; index < fittingHintCount; index++)
        {
            if (index == 0)
            {
                cursorX += Gap;
                EditorGUI.DrawRect(new Rect(cursorX, stripRect.y + 6f, 1f, StripHeight - 12f), new Color(1f, 1f, 1f, 0.16f));
                cursorX += 1f;
            }
            cursorX += Gap;

            Rect keycapRect = new Rect(cursorX, stripRect.y + (StripHeight - KeycapHeight) * 0.5f, keycapWidths[index], KeycapHeight);
            EditorGUI.DrawRect(keycapRect, new Color(1f, 1f, 1f, 0.22f));
            EditorGUI.DrawRect(new Rect(keycapRect.x + 1f, keycapRect.y + 1f, keycapRect.width - 2f, keycapRect.height - 2f), new Color(0.2f, 0.2f, 0.2f, 1f));
            GUI.Label(keycapRect, activeHints[index].keys ?? string.Empty, keycapStyle);
            cursorX += keycapWidths[index] + 4f;

            Rect actionRect = new Rect(cursorX, stripRect.y, actionWidths[index], StripHeight);
            GUI.Label(actionRect, activeHints[index].action ?? string.Empty, actionStyle);
            cursorX += actionWidths[index];
        }

        bool hovered = EditorWindow.mouseOverWindow == sceneView;
        if (hovered && !string.IsNullOrEmpty(cursorInfo))
        {
            Vector2 infoSize = cursorStyle.CalcSize(new GUIContent(cursorInfo));
            float infoWidth = Mathf.Min(infoSize.x + PaddingHorizontal * 2f, maxStripWidth);
            Rect infoRect = new Rect((viewWidth - infoWidth) * 0.5f, stripRect.y - 4f - 22f, infoWidth, 22f);
            DrawCard(infoRect);
            GUI.Label(new Rect(infoRect.x + PaddingHorizontal, infoRect.y, infoRect.width - PaddingHorizontal * 2f, infoRect.height), cursorInfo, cursorStyle);
        }
        Handles.EndGUI();
    }
}
#endif
