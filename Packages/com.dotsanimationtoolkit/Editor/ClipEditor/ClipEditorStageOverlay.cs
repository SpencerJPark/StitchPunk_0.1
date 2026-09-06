// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The Clip Editor's top-bar shortcuts, shown as a Scene view overlay so they stay reachable
    /// while editing a prefab docks the Clip Editor behind the Scene view's tab group.
    /// </summary>
    [Overlay(typeof(SceneView), "dots-animation-toolkit-clip-editor", "Clip Editor", true)]
    public sealed class ClipEditorStageOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            VisualElement content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;

            Button clipEditingButton = new Button(ClipEditorWindow.FocusClipEditing)
            {
                text = "Clip Editor",
                tooltip = "Bring the Clip Editor forward on its timeline. The prefab stage stays "
                    + "open behind it, so this is a switch rather than an exit — come back to the "
                    + "Scene view and the prefab is still the thing you were editing."
            };
            content.Add(clipEditingButton);

            Button vatBakeButton = new Button(ClipEditorWindow.FocusVatBakeSettings)
            {
                text = "VAT Bake",
                tooltip = "Bring the Clip Editor forward on its VAT bake tab. Also leaves the "
                    + "prefab stage open."
            };
            content.Add(vatBakeButton);

            return content;
        }
    }
}
