// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The Clip Editor's top-bar views, reachable from inside the Scene view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Prefab editing happens in Unity's Scene view, and no window of ours can hold it.</strong>
    /// A prefab stage is a scene the Scene view opens; there is no API to host one somewhere else,
    /// so "keep the top bar visible while editing the prefab" cannot mean putting the stage inside
    /// the Clip Editor. It has to mean the other direction — putting the bar's exits where the user
    /// already is. That is what an overlay is for.
    /// </para>
    /// <para>
    /// <strong>Why it is needed at all:</strong> Edit Prefab docks the Clip Editor into the Scene
    /// view's tab group on the first trip, on purpose — a floating window sits above everything and
    /// has to be dragged aside. The cost of that is what this pays back: sharing a tab group means
    /// the Scene view coming forward puts the Clip Editor behind it, top bar and all.
    /// </para>
    /// <para>
    /// <strong>Navigation only, never state.</strong> Both buttons are one-way commands — go there,
    /// show that. Mirroring a toggle here would put its state in two places, and the copy in the
    /// Scene view is the one nobody would think to update.
    /// </para>
    /// </remarks>
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
