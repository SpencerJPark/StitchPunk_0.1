// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The generated-constants status line both vocabulary inspectors show (amendment E6 Task 2,
    /// amendment A54): no button, no dialog, ever. The first time a row is added, removed, or a name
    /// field loses focus, this picks a destination on its own and writes the file there; every edit
    /// after that keeps it in sync the same way — see <see cref="RegenerateIfConfigured"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The destination used to be asked for.</strong> The original design opened a save
    /// dialog on first use, reasoning that a package cannot know which assembly in the host project
    /// should own generated constants. The owner rejected that in favour of zero interaction —
    /// <em>"I don't wanna have to barely do that... auto deal with all that stuff for me"</em> — so
    /// this now picks a fixed, conventional path itself (<see cref="DefaultDestinationDirectory"/>)
    /// the first time anything needs generating, and never asks again. If that default is ever wrong
    /// for a project, the fix is to change <see cref="IVocabularyRegistry.GeneratedConstantsPath"/>
    /// directly on the registry asset; there is deliberately no UI for it any more.
    /// </para>
    /// <para>
    /// <strong>One class serving both vocabularies rather than a block per inspector.</strong> The
    /// two registries differ here only in four strings, and the same standardisation directive that
    /// produced <see cref="VocabularyPicker"/> and <see cref="VocabularyQuickEditWindow"/> applies:
    /// tags and events must not grow parallel implementations of the same control.
    /// </para>
    /// <para>
    /// <strong>Names that could not survive the trip to C# are reported, not swallowed.</strong> The
    /// whole point of the feature (spec §4.2.3) is that the owner works in names; a tag called
    /// <c>"Eye L"</c> silently becoming <c>Eye_L</c> would break that promise at the one moment it
    /// matters. Every such substitution is logged as a warning naming both forms.
    /// </para>
    /// </remarks>
    public sealed class VocabularyConstantsSection : VisualElement
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] ";

        /// <summary>Where a destination is picked automatically the first time one is needed.</summary>
        private const string DefaultDestinationDirectory = "Assets/Generated/DotsAnimationToolkit";

        private readonly IVocabularyRegistry registry;
        private readonly UnityEngine.Object registryContext;
        private readonly string defaultFileName;
        private readonly string entryNoun;
        private readonly string fallbackEntryNamePrefix;
        private readonly Action persistRegistry;

        /// <summary>Builds the block for one vocabulary.</summary>
        /// <param name="registry">The vocabulary to emit constants for.</param>
        /// <param name="registryContext">
        /// The same registry as a <see cref="UnityEngine.Object"/>, used only so a console message
        /// about it can be clicked back to its inspector.
        /// </param>
        /// <param name="defaultFileName">
        /// The generated file's name without extension, e.g. "TargetTags" — and, because the class
        /// name is derived from the file name, this is what makes the owner's
        /// <c>TargetTags.Jaw</c> the default shape without anyone typing a class name.
        /// </param>
        /// <param name="entryNoun">How one row reads in prose, e.g. "Target tag".</param>
        /// <param name="fallbackEntryNamePrefix">Stem for an unnameable row, e.g. "Tag".</param>
        /// <param name="persistRegistry">
        /// Called after the remembered path changes. The project vocabularies live outside the asset
        /// database and have no autosave, so a path this block stores and does not persist is lost on
        /// the next domain reload.
        /// </param>
        public VocabularyConstantsSection(
            IVocabularyRegistry registry,
            UnityEngine.Object registryContext,
            string defaultFileName,
            string entryNoun,
            string fallbackEntryNamePrefix,
            Action persistRegistry)
        {
            this.registry = registry;
            this.registryContext = registryContext;
            this.defaultFileName = defaultFileName;
            this.entryNoun = entryNoun;
            this.fallbackEntryNamePrefix = fallbackEntryNamePrefix;
            this.persistRegistry = persistRegistry;

            style.marginTop = 8f;
            Rebuild();
        }

        /// <summary>
        /// Re-reads the remembered path and redraws — a plain status line, nothing clickable. Called
        /// by the owning inspector whenever the registry may have changed underneath it.
        /// </summary>
        public void Rebuild()
        {
            Clear();
            if (registry == null)
            {
                return;
            }

            string storedPath = registry.GeneratedConstantsPath;
            if (string.IsNullOrEmpty(storedPath))
            {
                // Nothing has ever needed generating yet - a project that never adds a row here
                // carries no constants file and shows nothing in this section either.
                return;
            }

            Label pathLabel = new Label("Constants: " + storedPath);
            pathLabel.selection.isSelectable = true;
            pathLabel.style.opacity = 0.6f;
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            Add(pathLabel);

            if (!File.Exists(storedPath))
            {
                Label missingLabel = new Label(
                    "That file is not there any more. It reappears the next time a row here changes.");
                missingLabel.style.whiteSpace = WhiteSpace.Normal;
                missingLabel.style.color = new Color(0.92f, 0.72f, 0.32f);
                Add(missingLabel);
            }
        }

        /// <summary>
        /// Rewrites the generated file from the current rows, picking a destination automatically the
        /// first time one is needed. The owning inspector calls this after every edit that could
        /// change what the file should say — a row added, removed, or a name field losing focus —
        /// which is what makes the file self-maintaining with nothing to click (amendment A54).
        /// </summary>
        public void RegenerateIfConfigured()
        {
            if (registry == null)
            {
                return;
            }

            string storedPath = registry.GeneratedConstantsPath;
            if (string.IsNullOrEmpty(storedPath))
            {
                storedPath = DefaultDestinationDirectory + "/" + defaultFileName + "."
                    + ConstantsGenerator.GeneratedFileExtension;
                registry.GeneratedConstantsPath = storedPath;
                persistRegistry?.Invoke();
            }

            List<string> reports = new List<string>();
            string className = ConstantsGenerator.ClassNameFromFilePath(storedPath, defaultFileName);
            string generatedSource = ConstantsGenerator.BuildVocabularyConstantsSource(
                registry, className, entryNoun, fallbackEntryNamePrefix, reports);

            // This runs on every field blur, not one deliberate button press, and a same-content
            // rewrite would still touch the file's timestamp and trigger AssetDatabase.Refresh - a
            // compile-scale cost, since the default destination sits under Assets/ - for a click
            // that changed nothing. Skipped whenever the bytes would be identical.
            if (File.Exists(storedPath) && File.ReadAllText(storedPath) == generatedSource)
            {
                return;
            }

            ConstantsGenerator.WriteGeneratedFile(storedPath, generatedSource);

            Debug.Log(
                LogPrefix + "Wrote " + entryNoun.ToLowerInvariant() + " constants as '" + className
                + "' to '" + storedPath + "'.",
                registryContext);

            for (int reportIndex = 0; reportIndex < reports.Count; reportIndex++)
            {
                Debug.LogWarning(LogPrefix + reports[reportIndex], registryContext);
            }

            // Shows the path now that a destination may have just been picked for the first time,
            // and clears a stale "file is missing" warning now that the write above recreated it.
            Rebuild();
        }
    }
}
