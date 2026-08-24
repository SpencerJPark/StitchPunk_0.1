// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The small "Edit tags…" window a <see cref="TargetTagPicker"/> opens (Phase E target-tags spec
    /// §4.2.2): the in-flow add, one tag, back to work — so discovering mid-tagging that a role has
    /// no tag yet does not mean abandoning the rig to hunt for the registry asset in the Project view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Not a second copy of the editing logic.</strong> Spec §4.2.2 is explicit that this is
    /// a second <em>entry point</em> onto the same asset the <see cref="TargetTagRegistry"/> inspector
    /// edits, not a second implementation of add/rename/remove. This window therefore hosts a real
    /// <see cref="UnityEditor.Editor"/> instance for the registry and asks it for its own
    /// <see cref="UnityEditor.Editor.CreateInspectorGUI"/> tree — the exact same
    /// <see cref="TargetTagRegistryEditor"/> the Project-view inspector would build, rows, T5 findings
    /// and all. A rename typed here and a rename typed in the inspector go through one code path.
    /// </para>
    /// <para>
    /// <strong>Closing calls back rather than the picker polling for a change.</strong> The picker
    /// that opened this window has usually already closed itself (picking "Edit tags…" closes the
    /// overlay it came from, like every other row), so there is no live picker instance to push a
    /// refresh into. Whatever opened the picker in the first place — a rig inspector's tag column —
    /// is handed the same callback and re-reads the registry itself once this window reports it is
    /// done, which is simpler than either side trying to diff what changed.
    /// </para>
    /// </remarks>
    public sealed class TargetTagQuickEditWindow : EditorWindow
    {
        private TargetTagRegistry registry;
        private UnityEditor.Editor registryEditor;
        private Action onClosed;

        /// <summary>Opens (or refocuses) the quick-edit window for <paramref name="registry"/>.</summary>
        /// <param name="registry">
        /// The registry to edit. When null, the window still opens and says so rather than opening
        /// nothing — a caller with no registry assigned yet is exactly who needs the loudest nudge to
        /// create one.
        /// </param>
        /// <param name="onClosed">
        /// Invoked once, when this window closes, so whatever opened the picker that led here can
        /// re-read the registry's current tags. May be null.
        /// </param>
        public static void Open(TargetTagRegistry registry, Action onClosed)
        {
            TargetTagQuickEditWindow window = GetWindow<TargetTagQuickEditWindow>(
                utility: true, title: "Edit Target Tags");
            window.minSize = new Vector2(320f, 200f);
            window.SetRegistry(registry, onClosed);
            window.Show();
        }

        private void SetRegistry(TargetTagRegistry newRegistry, Action newOnClosed)
        {
            registry = newRegistry;
            onClosed = newOnClosed;
            RebuildContent();
        }

        private void CreateGUI()
        {
            RebuildContent();
        }

        private void RebuildContent()
        {
            if (rootVisualElement == null)
            {
                return;
            }
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 6f;
            rootVisualElement.style.paddingRight = 6f;
            rootVisualElement.style.paddingTop = 6f;

            if (registry == null)
            {
                Label missingLabel = new Label(
                    "No target tag registry is assigned. Create one via Assets > Create > DOTS " +
                    "Animation Toolkit > Target Tag Registry, then assign it above the rig's target " +
                    "list.");
                missingLabel.style.whiteSpace = WhiteSpace.Normal;
                rootVisualElement.Add(missingLabel);
                return;
            }

            DestroyRegistryEditor();
            registryEditor = UnityEditor.Editor.CreateEditor(registry);
            VisualElement inspectorGui = registryEditor.CreateInspectorGUI();
            if (inspectorGui != null)
            {
                rootVisualElement.Add(inspectorGui);
            }
        }

        private void DestroyRegistryEditor()
        {
            if (registryEditor == null)
            {
                return;
            }
            DestroyImmediate(registryEditor);
            registryEditor = null;
        }

        private void OnDestroy()
        {
            DestroyRegistryEditor();
            onClosed?.Invoke();
        }
    }
}
