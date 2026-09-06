// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Owns the project-wide instances of the two authoring vocabularies — target tags and event
    /// names — and the only code that writes either to disk (amendment E6 Task 1, owner directive
    /// 2026-08-23: <em>"I don't want to manually create and wire it — it should just exist"</em>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This lives in the Editor assembly, and that placement is the whole point.</strong>
    /// The first attempt put <c>Instance</c> and <c>PersistChange</c> on the registry types
    /// themselves, behind <c>#if UNITY_EDITOR</c>. That compiles, but it put the token
    /// <c>UnityEditor</c> inside <c>Authoring/</c> — an assembly with no platform restriction, which
    /// therefore ships to players — and <c>Conformance_C</c> caught it. The rule is not a formality:
    /// <c>ClipValidation</c> takes a registry parameter and is documented as having no
    /// editor-assembly dependency precisely so it keeps compiling in a player build.
    /// </para>
    /// <para>
    /// A preprocessor guard would have satisfied the compiler while leaving the dependency in the
    /// source. Moving the machinery is what actually keeps the data types shippable: a
    /// <see cref="TargetTagRegistry"/> is now a plain <see cref="ScriptableObject"/> holding rows,
    /// and everything that knows about <c>ProjectSettings/</c>, JSON and file writes is here.
    /// </para>
    /// <para>
    /// <strong>Not a <c>ScriptableSingleton&lt;T&gt;</c>, though that is the shape being
    /// reproduced.</strong> Inheriting it would force the registry types to derive from a
    /// <c>UnityEditor</c> base class, which is the very dependency this file exists to avoid — and
    /// they cannot, since <c>ClipValidation</c> takes one as a parameter and must keep compiling in
    /// a player build. The lazy-create-and-hydrate contract is hand-rolled here instead.
    /// </para>
    /// <para>
    /// <strong>Nothing is written until something changes.</strong> Reading a registry the first
    /// time creates an empty instance in memory and hydrates it from the settings file if one
    /// exists. The file appears on the first <see cref="Persist(TargetTagRegistry)"/>, which is what
    /// makes the zero-setup promise true rather than merely convenient — a project that never adds a
    /// tag carries no tag file.
    /// </para>
    /// </remarks>
    public static class VocabularyRegistryProvider
    {
        private const string TargetTagFilePath =
            "ProjectSettings/DotsAnimationToolkitTargetTagRegistry.asset";

        private const string AnimEventKeyFilePath =
            "ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset";

        private static TargetTagRegistry projectTargetTags;
        private static AnimEventKeyRegistry projectAnimEventKeys;

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

        /// <summary>
        /// The one project-wide event-name vocabulary. Never null, never assigned by hand — Phase F
        /// decision D4 removed the per-set override that used to shadow it.
        /// </summary>
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

        /// <summary>
        /// Raised after either project vocabulary is written by <see cref="Persist(TargetTagRegistry)"/>
        /// or <see cref="Persist(AnimEventKeyRegistry)"/> — every add, remove, and (live, per
        /// keystroke) rename. A still-open <see cref="VocabularyPicker"/> subscribes to this so its
        /// row list stays current while a separate <see cref="VocabularyQuickEditWindow"/> is being
        /// edited, including an add or remove the picker would otherwise have no way to hear about:
        /// those never touch the field a <c>FocusOutEvent</c> could bubble from (amendment A54).
        /// Not raised for an explicitly assigned override asset, which is not the project instance —
        /// see either overload's remarks for why.
        /// </summary>
        public static event Action RegistryChanged;

        /// <summary>Writes the project target-tag vocabulary to disk.</summary>
        /// <remarks>
        /// A no-op for any registry that is not the project instance: an explicitly assigned asset is
        /// an ordinary <c>AssetDatabase</c> asset and is saved the ordinary way, never through here.
        /// Every editor surface that mutates a row must call this immediately — unlike an asset,
        /// the project instance has no autosave, so an edit that skips this is lost on domain reload.
        /// </remarks>
        public static void Persist(TargetTagRegistry registry)
        {
            if (registry == null || registry != projectTargetTags)
            {
                return;
            }
            WriteJson(registry, TargetTagFilePath);
            RegistryChanged?.Invoke();
        }

        /// <summary>Writes the project event-name vocabulary to disk. See the tag overload's remarks.</summary>
        public static void Persist(AnimEventKeyRegistry registry)
        {
            if (registry == null || registry != projectAnimEventKeys)
            {
                return;
            }
            WriteJson(registry, AnimEventKeyFilePath);
            RegistryChanged?.Invoke();
        }

        /// <summary>
        /// Dispatches to the typed <see cref="Persist(TargetTagRegistry)"/>/<see cref="Persist(AnimEventKeyRegistry)"/>
        /// overload for whichever vocabulary <paramref name="registry"/> is.
        /// </summary>
        /// <remarks>
        /// A distinct name rather than a third <c>Persist(ScriptableObject)</c> overload: callers that
        /// hold a <see cref="TargetTagRegistry"/> or <see cref="AnimEventKeyRegistry"/> reference would
        /// silently bind to this one instead of the typed overload under normal overload resolution,
        /// which defeats the point of having two.
        /// </remarks>
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
        }

        /// <summary>
        /// Hands the event vocabulary to <c>Authoring/</c>, which bakes a holding event's hold under
        /// the event's own name (amendment A65 §3.1) but may not name <c>UnityEditor</c> to find it.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void PublishEventVocabularyToAuthoring()
        {
            // A lazy accessor, not the registry: this runs on every domain reload, and reading the
            // property here would load (and on a fresh project create) the settings file whether or
            // not anything ever bakes a cutscene.
            CutsceneDerivedHolds.EventNameRegistrySource = () => AnimEventKeys;
        }

        private static TRegistry LoadOrCreate<TRegistry>(string filePath)
            where TRegistry : ScriptableObject
        {
            TRegistry created = ScriptableObject.CreateInstance<TRegistry>();

            // DontSave, not HideAndDontSave: this instance belongs to ProjectSettings rather than to
            // the asset database, so it must stay out of the hierarchy and never autosave into
            // whatever scene happens to be open (HideInHierarchy | DontSaveInEditor |
            // DontSaveInBuild is exactly that). HideAndDontSave adds NotEditable on top, which is a
            // different thing entirely: it tells the Inspector/SerializedObject binding the object's
            // fields cannot be written at all, so every PropertyField bound against this instance
            // renders but silently refuses every click and keystroke. That flag choice was the actual
            // cause of "there's no way to type a tag name" - not a UI wiring bug, an editability bit.
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
