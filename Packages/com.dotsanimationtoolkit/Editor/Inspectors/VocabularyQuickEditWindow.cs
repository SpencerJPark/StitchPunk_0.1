// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The small "Edit…" window a <see cref="VocabularyPicker"/> opens for either project vocabulary
    /// (Phase E target-tags spec §4.2.2, generalised in amendment E6 Task 3): the in-flow add, one
    /// row, back to work — so discovering mid-authoring that a role or an event has no row yet does
    /// not mean hunting through <c>ProjectSettings/</c> for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One window shape for both vocabularies, not two.</strong> Originally built as
    /// <c>TargetTagQuickEditWindow</c> for the tag registry alone (E1.5), this class was generalised
    /// for Task 3 ("event names must use the same window shape, not a parallel one") by taking a
    /// plain <see cref="ScriptableObject"/> and a title rather than a <c>TargetTagRegistry</c>
    /// specifically. It hosts a real <see cref="UnityEditor.Editor"/> instance for whatever object it
    /// is given and asks it for its own <see cref="UnityEditor.Editor.CreateInspectorGUI"/> tree — for
    /// a <c>TargetTagRegistry</c> that resolves to <c>TargetTagRegistryEditor</c>, for an
    /// <c>AnimEventKeyRegistry</c> to <c>AnimEventKeyRegistryEditor</c>, exactly the inspector a
    /// Project-view selection would have built. A rename typed here and a rename typed in that
    /// inspector go through one code path.
    /// </para>
    /// <para>
    /// <strong>Closing calls back rather than the picker polling for a change.</strong> A still-open
    /// picker does not need this window to call back while an edit is in progress — it hears about
    /// every add, remove and rename directly from <see cref="VocabularyRegistryProvider.RegistryChanged"/>,
    /// which every mutation in the hosted registry editor already goes through (amendment A54).
    /// <see cref="onClosed"/> is for whatever opened the picker in the first place — usually a
    /// different inspector than the picker's own — which is handed this callback and re-reads the
    /// registry once this window reports it is done.
    /// </para>
    /// </remarks>
    public sealed class VocabularyQuickEditWindow : EditorWindow
    {
        private ScriptableObject registryObject;
        private string missingMessage;
        private UnityEditor.Editor registryEditor;
        private Action onClosed;

        /// <summary>Opens (or refocuses) the quick-edit window for <paramref name="registryObject"/>.</summary>
        /// <param name="title">Window title, e.g. "Edit Target Tags" or "Edit Events".</param>
        /// <param name="registryObject">
        /// The registry to edit. When null, the window still opens and says so rather than opening
        /// nothing — a caller with no registry available yet is exactly who needs the loudest nudge.
        /// </param>
        /// <param name="missingMessage">Shown in place of the inspector when <paramref name="registryObject"/> is null.</param>
        /// <param name="onClosed">
        /// Invoked once, when this window closes, so whatever opened the picker that led here can
        /// re-read the registry's current rows. May be null.
        /// </param>
        public static void Open(
            string title, ScriptableObject registryObject, string missingMessage, Action onClosed)
        {
            VocabularyQuickEditWindow window =
                GetWindow<VocabularyQuickEditWindow>(utility: true, title: title);
            window.minSize = new Vector2(320f, 200f);
            window.SetRegistry(registryObject, missingMessage, onClosed);
            window.Show();
        }

        private void SetRegistry(ScriptableObject newRegistryObject, string newMissingMessage, Action newOnClosed)
        {
            registryObject = newRegistryObject;
            missingMessage = newMissingMessage;
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

            if (registryObject == null)
            {
                Label missingLabel = new Label(
                    string.IsNullOrEmpty(missingMessage) ? "No registry is available." : missingMessage);
                missingLabel.style.whiteSpace = WhiteSpace.Normal;
                rootVisualElement.Add(missingLabel);
                return;
            }

            DestroyRegistryEditor();
            registryEditor = UnityEditor.Editor.CreateEditor(registryObject);
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
