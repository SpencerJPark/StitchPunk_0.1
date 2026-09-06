// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The small "Edit…" window a <see cref="VocabularyPicker"/> opens for either project
    /// vocabulary: in-flow add, one row, back to work. Hosts whatever registry it is given through
    /// a real <see cref="UnityEditor.Editor"/>, so a rename here goes through the same code path as
    /// the registry's own inspector.
    /// </summary>
    public sealed class VocabularyQuickEditWindow : EditorWindow
    {
        private ScriptableObject registryObject;
        private string missingMessage;
        private UnityEditor.Editor registryEditor;
        private Action onClosed;

        /// <summary>Opens (or refocuses) the quick-edit window for <paramref name="registryObject"/>.</summary>
        /// <param name="registryObject">When null, the window still opens and shows <paramref name="missingMessage"/>.</param>
        /// <param name="onClosed">Invoked once, when this window closes. May be null.</param>
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
