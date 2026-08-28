// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The custom inspector for <see cref="AnimEventKeyRegistry"/> (architecture section 7.1): the
    /// bound property fields, an "Add Event" button that never mints a colliding key, and the three
    /// problems a list of raw numbers hides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>These findings are not clip validation and deliberately do not live in
    /// <c>ClipValidation</c>.</strong> The registry is authoring furniture that is never baked, so a
    /// broken entry cannot reach a blob and has nothing to say to the clip validator. What a broken
    /// entry <em>can</em> do is point two names at one key, which makes them indistinguishable to
    /// every system downstream — so the check belongs where the mistake is made.
    /// </para>
    /// <para>
    /// UI Toolkit only, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>: this type
    /// overrides <see cref="UnityEditor.Editor.CreateInspectorGUI"/> and never the immediate-mode
    /// entry point.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(AnimEventKeyRegistry))]
    public sealed class AnimEventKeyRegistryEditor : UnityEditor.Editor
    {
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color CleanColor = new Color(0.45f, 0.78f, 0.48f);

        private VisualElement findingsContainer;
        private VocabularyConstantsSection constantsSection;
        private SerializedProperty entriesProperty;

        /// <summary>
        /// Row count as of the last change seen, mirroring the identical guard on
        /// <see cref="TargetTagRegistryEditor"/> — see that type's own field for why the distinction
        /// between a resize and an in-place edit matters here too.
        /// </summary>
        private int builtEntryCount = -1;

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            entriesProperty = serializedObject.FindProperty("entries");
            builtEntryCount = entriesProperty.arraySize;

            root.Add(new PropertyField(serializedObject.FindProperty("referenceFrameRate")));
            root.Add(new PropertyField(entriesProperty));

            Button addButton = new Button(AddEntry) { text = "Add Event" };
            addButton.style.marginTop = 6f;
            root.Add(addButton);

            findingsContainer = new VisualElement();
            findingsContainer.style.marginTop = 8f;
            root.Add(findingsContainer);

            // Task 2's generator, the same block the tag registry shows - one control, two
            // vocabularies, per the standing "no parallel implementations" directive. Persisting is
            // a no-op for an explicitly assigned override asset, which saves the ordinary
            // AssetDatabase way; SetDirty is what covers that case here.
            constantsSection = new VocabularyConstantsSection(
                target as AnimEventKeyRegistry,
                target,
                "AnimEvents",
                "Event",
                "Event",
                () =>
                {
                    AnimEventKeyRegistry persistedRegistry = target as AnimEventKeyRegistry;
                    if (persistedRegistry == null)
                    {
                        return;
                    }
                    VocabularyRegistryProvider.Persist(persistedRegistry);
                    if (AssetDatabase.Contains(persistedRegistry))
                    {
                        EditorUtility.SetDirty(persistedRegistry);
                        AssetDatabase.SaveAssetIfDirty(persistedRegistry);
                    }
                });
            root.Add(constantsSection);

            RefreshFindings();

            // Picks up Undo/Redo and edits made from anywhere else, the same way the clip-set
            // roster does — without re-walking the list on every repaint. The explicit
            // PersistChange() call is what makes a rename typed into the bound name field above
            // survive a domain reload when this is the project singleton (Task 1) — see its remarks;
            // it is a no-op for an explicitly assigned override asset, which persists the ordinary
            // AssetDatabase way instead.
            root.TrackSerializedObjectValue(serializedObject, changedObject =>
            {
                AnimEventKeyRegistry changedRegistry = target as AnimEventKeyRegistry;
                if (changedRegistry != null)
                {
                    VocabularyRegistryProvider.Persist(changedRegistry);
                }
                RefreshFindings();

                // Same guard as TargetTagRegistryEditor: a resize (Undo/Redo, the list drawer's own
                // remove button, or an edit on a different open copy of this inspector) has no
                // FocusOutEvent to trigger off, so it regenerates immediately. An ordinary rename
                // keystroke does nothing here at all - the section's own display does not depend on
                // row text, so rebuilding it per keystroke was pure jitter for a section that had
                // nothing to redraw; FocusOutEvent below is what actually regenerates the file.
                int currentEntryCount = entriesProperty != null ? entriesProperty.arraySize : 0;
                bool rowCountChanged = currentEntryCount != builtEntryCount;
                builtEntryCount = currentEntryCount;
                if (rowCountChanged)
                {
                    constantsSection?.RegenerateIfConfigured();
                }
            });

            // Amendment A54: constants regenerate on their own; see TargetTagRegistryEditor's
            // identical registration for why FocusOutEvent (not every keystroke) is the trigger.
            root.RegisterCallback<FocusOutEvent>(focusOutEvent => constantsSection?.RegenerateIfConfigured());

            return root;
        }

        /// <summary>
        /// Appends an entry holding the lowest key nothing else claims, so the common case of
        /// "another event, please" cannot produce a duplicate or an unmaskable key by accident.
        /// </summary>
        private void AddEntry()
        {
            AnimEventKeyRegistry registry = (AnimEventKeyRegistry)target;

            // CreateVocabularyEntry only mints the key in memory (falling back to the lowest free
            // pulse-only key once every maskable slot is taken - see its remarks); it cannot persist
            // itself. PersistVocabulary writes the project singleton's JSON and is a no-op for an
            // explicitly assigned override asset (Task 1's back-compat clause), which instead needs
            // the ordinary dirty-and-save pair; AssetDatabase.Contains is what tells the two cases
            // apart without ever calling SaveAssetIfDirty on the singleton, which is not a
            // persistent AssetDatabase object.
            registry.CreateVocabularyEntry("NewEvent");
            VocabularyRegistryProvider.PersistVocabulary(registry);
            if (AssetDatabase.Contains(registry))
            {
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssetIfDirty(registry);
            }

            serializedObject.Update();
            builtEntryCount = entriesProperty != null ? entriesProperty.arraySize : builtEntryCount;
            RefreshFindings();
            constantsSection?.RegenerateIfConfigured();
        }

        private void RefreshFindings()
        {
            if (findingsContainer == null)
            {
                return;
            }
            findingsContainer.Clear();

            AnimEventKeyRegistry registry = (AnimEventKeyRegistry)target;
            if (registry.entries == null || registry.entries.Count == 0)
            {
                findingsContainer.Add(MakeNote(
                    "No events yet. Add one, then assign this asset to a Clip Set to pick events "
                    + "by name in the Clip Editor.",
                    CleanColor));
                return;
            }

            int maskableCount = 0;
            HashSet<uint> seenKeys = new HashSet<uint>();
            List<string> problems = new List<string>();

            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = registry.entries[entryIndex];
                if (entry == null)
                {
                    problems.Add("Entry " + entryIndex + " is empty.");
                    continue;
                }

                string label = string.IsNullOrEmpty(entry.name)
                    ? "Entry " + entryIndex
                    : "'" + entry.name + "'";

                if (string.IsNullOrEmpty(entry.name))
                {
                    problems.Add(label + " has no name, so it shows as a bare number.");
                }

                if (entry.eventKey < (uint)ReservedEventKeys.FirstUserKey)
                {
                    problems.Add(
                        label + " uses key " + entry.eventKey + ", which is reserved by the "
                        + "package. Any clip using it fails validation rule V09.");
                }
                else if (!AnimEventMaskKeys.IsMaskable(entry.eventKey))
                {
                    problems.Add(
                        label + " uses key " + entry.eventKey + ", outside the maskable range "
                        + AnimEventMaskKeys.FirstMaskKey + "–" + AnimEventMaskKeys.LastMaskKey
                        + ". It is pulse-only and cannot hold a window.");
                }
                else
                {
                    maskableCount++;
                }

                if (!seenKeys.Add(entry.eventKey))
                {
                    problems.Add(
                        label + " repeats key " + entry.eventKey + ". Two names for one key are "
                        + "indistinguishable to every system that reads it.");
                }
            }

            findingsContainer.Add(MakeNote(
                "Maskable slots used: " + maskableCount + " / " + AnimEventMaskKeys.MaskKeyCount,
                problems.Count == 0 ? CleanColor : WarningColor));

            for (int problemIndex = 0; problemIndex < problems.Count; problemIndex++)
            {
                findingsContainer.Add(MakeNote(problems[problemIndex], WarningColor));
            }
        }

        private static Label MakeNote(string text, Color textColor)
        {
            Label note = new Label(text);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.color = textColor;
            note.style.marginTop = 2f;
            return note;
        }
    }
}
