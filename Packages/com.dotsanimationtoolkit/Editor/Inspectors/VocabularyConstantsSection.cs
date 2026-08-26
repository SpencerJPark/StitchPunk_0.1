// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The "Generate … Constants" block both vocabulary inspectors show (amendment E6 Task 2): a
    /// destination chosen once, then a Regenerate button that rewrites it in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The destination is asked for exactly once.</strong> A package cannot know — and must
    /// not guess — which assembly in the host project should own generated constants, so the first
    /// press opens a save dialog. Asking again on every press would be worse than merely tedious:
    /// re-picking a path is how a project ends up with two constants files, one of which nobody
    /// regenerates, so half the code compiles against names that have since been renamed. The chosen
    /// path is remembered on the registry itself (see
    /// <see cref="IVocabularyRegistry.GeneratedConstantsPath"/>) and afterwards the button rewrites
    /// that file with no dialog at all.
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

        private readonly IVocabularyRegistry registry;
        private readonly UnityEngine.Object registryContext;
        private readonly string dialogTitle;
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
        /// <param name="dialogTitle">Save-dialog and button text, e.g. "Generate Target Tag Constants".</param>
        /// <param name="defaultFileName">
        /// Offered in the save dialog without extension, e.g. "TargetTags" — and, because the class
        /// name is derived from the file name, this is what makes the owner's
        /// <c>TargetTags.Jaw</c> the default shape without anyone typing a class name.
        /// </param>
        /// <param name="entryNoun">How one row reads in prose, e.g. "Target tag".</param>
        /// <param name="fallbackEntryNamePrefix">Stem for an unnameable row, e.g. "Tag".</param>
        /// <param name="persistRegistry">
        /// Called after the remembered path changes. The project vocabularies live outside the asset
        /// database and have no autosave, so a path this block stores and does not persist is lost on
        /// the next domain reload — and the button silently reverts to asking for a destination.
        /// </param>
        public VocabularyConstantsSection(
            IVocabularyRegistry registry,
            UnityEngine.Object registryContext,
            string dialogTitle,
            string defaultFileName,
            string entryNoun,
            string fallbackEntryNamePrefix,
            Action persistRegistry)
        {
            this.registry = registry;
            this.registryContext = registryContext;
            this.dialogTitle = dialogTitle;
            this.defaultFileName = defaultFileName;
            this.entryNoun = entryNoun;
            this.fallbackEntryNamePrefix = fallbackEntryNamePrefix;
            this.persistRegistry = persistRegistry;

            style.marginTop = 8f;
            Rebuild();
        }

        /// <summary>
        /// Re-reads the remembered path and redraws. Called by the owning inspector whenever the
        /// registry may have changed underneath it.
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
                Button generateButton = new Button(ChooseDestinationAndGenerate) { text = dialogTitle };
                generateButton.tooltip =
                    "Writes a C# file of constants so game code refers to " + entryNoun.ToLowerInvariant()
                    + "s by name. You will be asked where once; after that this becomes Regenerate.";
                Add(generateButton);
                return;
            }

            Label pathLabel = new Label("Constants: " + storedPath);
            pathLabel.selection.isSelectable = true;
            pathLabel.style.opacity = 0.75f;
            pathLabel.style.whiteSpace = WhiteSpace.Normal;
            Add(pathLabel);

            if (!File.Exists(storedPath))
            {
                Label missingLabel = new Label(
                    "That file is not there any more. Regenerate writes it again, or pick a new "
                    + "location.");
                missingLabel.style.whiteSpace = WhiteSpace.Normal;
                missingLabel.style.color = new Color(0.92f, 0.72f, 0.32f);
                Add(missingLabel);
            }

            VisualElement buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.marginTop = 2f;

            Button regenerateButton = new Button(RegenerateAtStoredPath) { text = "Regenerate" };
            regenerateButton.style.flexGrow = 1f;
            regenerateButton.tooltip =
                "Rewrites the file above from the current rows. Renaming a row renames its constant, "
                + "so code using the old name stops compiling - which is the intended warning.";
            buttonRow.Add(regenerateButton);

            Button relocateButton =
                new Button(ChooseDestinationAndGenerate) { text = "Change location..." };
            buttonRow.Add(relocateButton);

            Add(buttonRow);
        }

        /// <summary>
        /// Asks where the constants should live, remembers the answer, and writes them there.
        /// </summary>
        /// <remarks>
        /// An empty return from the dialog means the user cancelled — the documented contract of
        /// <see cref="EditorUtility.SaveFilePanel"/> — and must leave the remembered path alone, so a
        /// cancelled relocation does not orphan the file that is already generated.
        /// </remarks>
        private void ChooseDestinationAndGenerate()
        {
            string startingDirectory = string.Empty;
            string storedPath = registry.GeneratedConstantsPath;
            if (!string.IsNullOrEmpty(storedPath))
            {
                string storedDirectory = Path.GetDirectoryName(storedPath);
                if (!string.IsNullOrEmpty(storedDirectory) && Directory.Exists(storedDirectory))
                {
                    startingDirectory = storedDirectory;
                }
            }

            string chosenFilePath = EditorUtility.SaveFilePanel(
                dialogTitle,
                startingDirectory,
                string.IsNullOrEmpty(storedPath)
                    ? defaultFileName
                    : Path.GetFileNameWithoutExtension(storedPath),
                ConstantsGenerator.GeneratedFileExtension);
            if (string.IsNullOrEmpty(chosenFilePath))
            {
                return;
            }

            registry.GeneratedConstantsPath = ConstantsGenerator.ToStorablePath(chosenFilePath);
            if (persistRegistry != null)
            {
                persistRegistry();
            }

            RegenerateAtStoredPath();
            Rebuild();
        }

        private void RegenerateAtStoredPath()
        {
            string storedPath = registry.GeneratedConstantsPath;
            if (string.IsNullOrEmpty(storedPath))
            {
                return;
            }

            List<string> reports = new List<string>();
            string className = ConstantsGenerator.ClassNameFromFilePath(storedPath, defaultFileName);
            string generatedSource = ConstantsGenerator.BuildVocabularyConstantsSource(
                registry, className, entryNoun, fallbackEntryNamePrefix, reports);

            ConstantsGenerator.WriteGeneratedFile(storedPath, generatedSource);

            Debug.Log(
                LogPrefix + "Wrote " + entryNoun.ToLowerInvariant() + " constants as '" + className
                + "' to '" + storedPath + "'.",
                registryContext);

            for (int reportIndex = 0; reportIndex < reports.Count; reportIndex++)
            {
                Debug.LogWarning(LogPrefix + reports[reportIndex], registryContext);
            }
        }
    }
}
