#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

[Overlay(typeof(SceneView), "stitchpunk-terrain-tool", "Terrain", true)]
public sealed class TerrainToolOverlay : Overlay
{
    private const string OverlayClassName = "terrain-overlay";
    private const string TitleClassName = "terrain-overlay-title";
    private const string HintRowClassName = "terrain-overlay-hint-row";
    private const string KeycapClassName = "terrain-keycap";
    private const string ActionClassName = "terrain-overlay-action";
    private const string CursorClassName = "terrain-overlay-cursor";
    private const string ToastClassName = "terrain-overlay-toast";
    private const long ToastExpiryPaddingMilliseconds = 50;

    public override void OnCreated()
    {
        TerrainEditorVisibility.Changed += ApplyVisibility;
        ApplyVisibility();
    }

    public override void OnWillBeDestroyed()
    {
        TerrainEditorVisibility.Changed -= ApplyVisibility;
    }

    public override VisualElement CreatePanelContent()
    {
        VisualElement root = new VisualElement();
        TerrainUi.ApplyStyles(root);
        root.AddToClassList(OverlayClassName);

        System.Action rebuildOnHudChanged = () => Rebuild(root);
        root.RegisterCallback<AttachToPanelEvent>(attachEvent =>
        {
            TerrainSceneHud.Changed -= rebuildOnHudChanged;
            TerrainSceneHud.Changed += rebuildOnHudChanged;
            Rebuild(root);
        });
        root.RegisterCallback<DetachFromPanelEvent>(detachEvent => TerrainSceneHud.Changed -= rebuildOnHudChanged);
        return root;
    }

    private void ApplyVisibility()
    {
        displayed = TerrainEditorVisibility.ToolsLive;
    }

    private static void Rebuild(VisualElement root)
    {
        root.Clear();

        string toolName = TerrainSceneHud.ActiveToolName;
        if (!string.IsNullOrEmpty(toolName))
        {
            Label titleLabel = new Label(toolName);
            titleLabel.AddToClassList(TitleClassName);
            root.Add(titleLabel);
        }

        IReadOnlyList<TerrainKeyHint> hints = TerrainSceneHud.ActiveHints;
        if (hints != null)
        {
            for (int index = 0; index < hints.Count; index++)
            {
                VisualElement row = new VisualElement();
                row.AddToClassList(HintRowClassName);

                Label keycapLabel = new Label(hints[index].keys ?? string.Empty);
                keycapLabel.AddToClassList(KeycapClassName);
                row.Add(keycapLabel);

                string actionText = hints[index].action ?? string.Empty;
                Label actionLabel = new Label(actionText);
                actionLabel.AddToClassList(ActionClassName);
                actionLabel.tooltip = actionText;
                row.Add(actionLabel);

                root.Add(row);
            }
        }

        string cursorText = TerrainSceneHud.CursorInfo;
        if (!string.IsNullOrEmpty(cursorText))
        {
            Label cursorLabel = new Label(cursorText);
            cursorLabel.AddToClassList(CursorClassName);
            cursorLabel.tooltip = cursorText;
            root.Add(cursorLabel);
        }

        string toastText = TerrainSceneHud.ActiveToast;
        if (toastText != null)
        {
            Label toastLabel = new Label(toastText);
            toastLabel.AddToClassList(ToastClassName);
            toastLabel.tooltip = toastText;
            root.Add(toastLabel);

            long millisecondsUntilExpiry = (long)(TerrainSceneHud.ActiveToastSecondsRemaining * 1000.0) + ToastExpiryPaddingMilliseconds;
            root.schedule.Execute(() => Rebuild(root)).ExecuteLater(millisecondsUntilExpiry);
        }
    }
}
#endif
