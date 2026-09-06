// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Project Settings page for both vocabularies (target tags, event names): add, rename in
    /// place, remove. Hosts the same <see cref="TargetTagRegistryEditor"/> /
    /// <see cref="AnimEventKeyRegistryEditor"/> inspector rather than a third list widget.
    /// </summary>
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

        // A rename here is not undoable, like Unity's own Tags & Layers page: both registries live
        // in ProjectSettings/, and Undo only tracks SerializedObject edits against a real asset.
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
