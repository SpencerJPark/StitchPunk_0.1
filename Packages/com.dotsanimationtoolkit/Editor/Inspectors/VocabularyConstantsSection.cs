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
    /// The generated-constants status line both vocabulary inspectors show: no button, no dialog,
    /// ever. On first use it picks <see cref="DefaultDestinationDirectory"/> automatically and
    /// writes there; every later edit keeps the file in sync the same way.
    /// </summary>
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

        /// <param name="registryContext">The same registry as a <see cref="UnityEngine.Object"/>, so a console message can be clicked back to its inspector.</param>
        /// <param name="persistRegistry">Called after the remembered path changes — the project
        /// vocabularies have no autosave, so a path left unpersisted is lost on domain reload.</param>
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
        /// Rewrites the generated file from the current rows, picking a destination automatically
        /// the first time one is needed. The owning inspector calls this after every edit that
        /// could change what the file should say.
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

            // Runs on every field blur, not one deliberate button press, so a same-content rewrite
            // is skipped — it would still trigger a compile-scale AssetDatabase.Refresh for nothing.
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
