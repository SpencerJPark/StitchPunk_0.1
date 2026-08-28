// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The canonical list page for both project vocabularies (Phase E Task 3) — the Tags &amp;
    /// Layers analogue for target tags and event names: add, rename in place, remove. Hosts the same
    /// <see cref="TargetTagRegistryEditor"/> / <see cref="AnimEventKeyRegistryEditor"/> inspector a
    /// <see cref="VocabularyPicker"/>'s "Edit …" row already opens in
    /// <see cref="VocabularyQuickEditWindow"/>, rather than building a third list widget.
    /// </summary>
    /// <remarks>
    /// <strong>A rename here is not undoable</strong>, the same as Unity's own Tags &amp; Layers page:
    /// both registries live in <c>ProjectSettings/</c> rather than the asset database, and Undo only
    /// tracks <see cref="SerializedObject"/> edits against a real asset.
    /// </remarks>
    internal static class VocabularySettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateTargetTagsProvider()
        {
            return CreateProvider(
                "Project/DOTS Animation Toolkit/Target Tags",
                "Target Tags",
                () => VocabularyRegistryProvider.TargetTags,
                new string[] { "tag", "target", "vocabulary" });
        }

        [SettingsProvider]
        public static SettingsProvider CreateEventNamesProvider()
        {
            return CreateProvider(
                "Project/DOTS Animation Toolkit/Event Names",
                "Event Names",
                () => VocabularyRegistryProvider.AnimEventKeys,
                new string[] { "event", "animation", "vocabulary" });
        }

        /// <summary>One shape shared by both pages — the same principle E6 Task 3 established for the picker.</summary>
        private static SettingsProvider CreateProvider(
            string settingsPath, string label, Func<ScriptableObject> resolveRegistry, string[] keywords)
        {
            UnityEditor.Editor registryEditor = null;

            SettingsProvider provider = new SettingsProvider(settingsPath, SettingsScope.Project)
            {
                label = label,
                activateHandler = (searchContext, rootElement) =>
                {
                    ScriptableObject registryObject = resolveRegistry();
                    registryEditor = UnityEditor.Editor.CreateEditor(registryObject);
                    VisualElement inspectorGui = registryEditor.CreateInspectorGUI();
                    if (inspectorGui != null)
                    {
                        rootElement.Add(inspectorGui);
                    }
                },
                deactivateHandler = () =>
                {
                    if (registryEditor != null)
                    {
                        UnityEngine.Object.DestroyImmediate(registryEditor);
                        registryEditor = null;
                    }
                },
                keywords = new HashSet<string>(keywords)
            };
            return provider;
        }
    }
}
