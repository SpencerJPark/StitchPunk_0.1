// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Owns the project-wide instances of the authoring vocabularies — target tags, event names,
    /// animation names — and the only code that writes any of them to disk.
    /// </summary>
    public static class VocabularyRegistryProvider
    {
        private const string TargetTagFilePath =
            "ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset";

        private const string AnimEventKeyFilePath =
            "ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset";

        private const string AnimationNameFilePath =
            "ProjectSettings/DotsAnimationToolkitAnimationNameRegistry.asset";

        private static TargetTagRegistry projectTargetTags;
        private static AnimEventKeyRegistry projectAnimEventKeys;
        private static AnimationNameRegistry projectAnimationNames;

        /// <summary>
        /// The one project-wide target-tag vocabulary. Never null, never assigned by hand.
        /// </summary>
        public static TargetTagRegistry TargetTags
        {
            get
            {
                if (projectTargetTags == null)
                {
                    projectTargetTags = LoadOrCreate<TargetTagRegistry>(TargetTagFilePath);
                }
                return projectTargetTags;
            }
        }

        /// <summary>The one project-wide event-name vocabulary. Never null, never assigned by hand.</summary>
        public static AnimEventKeyRegistry AnimEventKeys
        {
            get
            {
                if (projectAnimEventKeys == null)
                {
                    projectAnimEventKeys = LoadOrCreate<AnimEventKeyRegistry>(AnimEventKeyFilePath);
                }
                return projectAnimEventKeys;
            }
        }

        /// <summary>The one project-wide animation-name vocabulary. Never null, never assigned by hand.</summary>
        public static AnimationNameRegistry AnimationNames
        {
            get
            {
                if (projectAnimationNames == null)
                {
                    projectAnimationNames = LoadOrCreate<AnimationNameRegistry>(AnimationNameFilePath);
                }
                return projectAnimationNames;
            }
        }

        /// <summary>
        /// Raised after either project vocabulary is written, so a still-open
        /// <see cref="VocabularyPicker"/> stays current during a separate edit elsewhere. Not
        /// raised for an explicitly assigned override asset, which is not the project instance.
        /// </summary>
        public static event Action RegistryChanged;

        // No-op for a registry that is not the project instance. The project instance has no
        // autosave, so a caller that skips this loses its edit on domain reload.
        public static void Persist(TargetTagRegistry registry)
        {
            if (registry == null || registry != projectTargetTags)
            {
                return;
            }
            WriteJson(registry, TargetTagFilePath);
            RegistryChanged?.Invoke();
        }

        // No-op for a registry that is not the project instance — see the other overload.
        public static void Persist(AnimEventKeyRegistry registry)
        {
            if (registry == null || registry != projectAnimEventKeys)
            {
                return;
            }
            WriteJson(registry, AnimEventKeyFilePath);
            RegistryChanged?.Invoke();
        }

        // No-op for a registry that is not the project instance — see the other overload.
        public static void Persist(AnimationNameRegistry registry)
        {
            if (registry == null || registry != projectAnimationNames)
            {
                return;
            }
            WriteJson(registry, AnimationNameFilePath);
            RegistryChanged?.Invoke();
        }

        // A distinct name rather than a fourth Persist(ScriptableObject) overload — a typed caller
        // would otherwise silently bind to this one under normal overload resolution.
        public static void PersistVocabulary(ScriptableObject registry)
        {
            if (registry is TargetTagRegistry targetTagRegistry)
            {
                Persist(targetTagRegistry);
            }
            else if (registry is AnimEventKeyRegistry animEventKeyRegistry)
            {
                Persist(animEventKeyRegistry);
            }
            else if (registry is AnimationNameRegistry animationNameRegistry)
            {
                Persist(animationNameRegistry);
            }
        }

        // Hands the event vocabulary to Authoring/, which needs it to resolve event names but may
        // not reference UnityEditor to find it itself.
        [InitializeOnLoadMethod]
        private static void PublishEventVocabularyToAuthoring()
        {
            // A lazy accessor, not the registry itself — this runs on every domain reload, and
            // reading the property here would load the settings file whether or not anything needs it.
            CutsceneDerivedHolds.EventNameRegistrySource = () => AnimEventKeys;
        }

        private static TRegistry LoadOrCreate<TRegistry>(string filePath)
            where TRegistry : ScriptableObject
        {
            TRegistry created = ScriptableObject.CreateInstance<TRegistry>();

            // DontSave, not HideAndDontSave — the latter also sets NotEditable, which silently makes
            // every bound PropertyField refuse clicks and keystrokes instead of just hiding the asset.
            created.hideFlags = HideFlags.DontSave;

            if (File.Exists(filePath))
            {
                string storedJson = File.ReadAllText(filePath);
                EditorJsonUtility.FromJsonOverwrite(storedJson, created);
            }
            return created;
        }

        private static void WriteJson(ScriptableObject registry, string filePath)
        {
            string json = EditorJsonUtility.ToJson(registry, true);
            File.WriteAllText(filePath, json);
        }
    }
}
