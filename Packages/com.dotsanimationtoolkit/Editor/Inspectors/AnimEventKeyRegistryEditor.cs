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
    /// The custom inspector for <see cref="AnimEventKeyRegistry"/> (architecture section 7.1,
    /// amendment A55): hand-built rename/remove rows matching <see cref="TargetTagRegistryEditor"/>'s
    /// shape, an "Add Event" button that never mints a colliding key, and the three problems a list
    /// of raw numbers hides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rows are hand-built rather than left to the default array drawer</strong> (amendment
    /// A55, reversing the single-<see cref="PropertyField"/> shape this used before). Deleting an
    /// event now shows how many markers use it first, the same courtesy
    /// <see cref="TargetTagRegistryEditor"/> already gave tag deletes — and there is no way to
    /// intercept the array drawer's own remove button to show that count first.
    /// </para>
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

        private SerializedProperty entriesProperty;
        private VisualElement rowsContainer;
        private VisualElement findingsContainer;
        private VocabularyConstantsSection constantsSection;

        /// <summary>
        /// Row count as of the last <see cref="RefreshRows"/>, so <see cref="OnSerializedObjectChanged"/>
        /// can tell a resize (rows added/removed elsewhere) from an ordinary keystroke — see
        /// <see cref="TargetTagRegistryEditor"/>'s identical field for why the distinction matters.
        /// </summary>
        private int builtEntryCount = -1;

        public override VisualElement CreateInspectorGUI()
        {
            entriesProperty = serializedObject.FindProperty("entries");

            VisualElement root = new VisualElement();

            root.Add(new PropertyField(serializedObject.FindProperty("referenceFrameRate")));

            Label helpLabel = new Label(
                "An event names a moment in a clip (\"Footstep\", \"ApplyDamage\"). Rename freely - "
                + "a marker stores the key, never the name, so nothing breaks.");
            helpLabel.style.whiteSpace = WhiteSpace.Normal;
            helpLabel.style.opacity = 0.75f;
            helpLabel.style.marginTop = 4f;
            helpLabel.style.marginBottom = 6f;
            root.Add(helpLabel);

            rowsContainer = new VisualElement();
            root.Add(rowsContainer);

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

            RefreshRows();
            RefreshFindings();

            // Picks up Undo/Redo and edits made from anywhere else, the same way the clip-set
            // roster and the tag registry do - without re-walking the list on every repaint. The
            // explicit PersistChange() call is what makes a rename typed into a row's name field
            // survive a domain reload when this is the project singleton (Task 1) - see its
            // remarks; it is a no-op for an explicitly assigned override asset, which persists the
            // ordinary AssetDatabase way instead.
            root.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            // Amendment A54: constants regenerate on their own; see TargetTagRegistryEditor's
            // identical registration for why FocusOutEvent (not every keystroke) is the trigger.
            root.RegisterCallback<FocusOutEvent>(focusOutEvent => constantsSection?.RegenerateIfConfigured());

            return root;
        }

        /// <summary>
        /// Persists every change, but only tears down and rebuilds the rows when the entry count
        /// itself changed (add/remove/Undo) — not on an ordinary rename keystroke, which is a change
        /// to the very row it would be destroying. Same guard <see cref="TargetTagRegistryEditor"/>
        /// uses for the identical reason.
        /// </summary>
        private void OnSerializedObjectChanged(SerializedObject changedSerializedObject)
        {
            AnimEventKeyRegistry changedRegistry = target as AnimEventKeyRegistry;
            if (changedRegistry != null)
            {
                VocabularyRegistryProvider.Persist(changedRegistry);
            }

            bool rowCountChanged = entriesProperty != null && entriesProperty.arraySize != builtEntryCount;
            if (rowCountChanged)
            {
                RefreshRows();
            }
            RefreshFindings();

            // A resize (Undo/Redo, or an edit made on a different open copy of this inspector) has
            // no FocusOutEvent here to trigger off, so it regenerates immediately. An ordinary
            // rename keystroke does nothing here at all - FocusOutEvent above is what actually
            // regenerates the file once the edit is done.
            if (rowCountChanged)
            {
                constantsSection?.RegenerateIfConfigured();
            }
        }

        // -----------------------------------------------------------------------------------
        // Rows.
        // -----------------------------------------------------------------------------------

        private void RefreshRows()
        {
            if (rowsContainer == null || entriesProperty == null)
            {
                return;
            }
            rowsContainer.Clear();

            int entryCount = entriesProperty.arraySize;
            builtEntryCount = entryCount;
            if (entryCount == 0)
            {
                Label emptyLabel = new Label(
                    "No events yet. Add one, then assign this asset to a Clip Set to pick events "
                    + "by name in the Clip Editor.");
                emptyLabel.style.opacity = 0.7f;
                emptyLabel.style.whiteSpace = WhiteSpace.Normal;
                rowsContainer.Add(emptyLabel);
                return;
            }

            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                rowsContainer.Add(BuildRow(entryIndex));
            }

            // The rows were created after the root was bound, so they carry no bindings yet.
            rowsContainer.Bind(serializedObject);
        }

        private VisualElement BuildRow(int entryIndex)
        {
            VisualElement rowGroup = new VisualElement();
            rowGroup.style.marginTop = 2f;

            VisualElement rowContainer = new VisualElement();
            rowContainer.style.flexDirection = FlexDirection.Row;
            rowContainer.style.alignItems = Align.Center;
            rowGroup.Add(rowContainer);

            SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(entryIndex);
            SerializedProperty nameProperty = entryProperty.FindPropertyRelative("name");
            SerializedProperty eventKeyProperty = entryProperty.FindPropertyRelative("eventKey");
            SerializedProperty defaultWindowFramesProperty =
                entryProperty.FindPropertyRelative("defaultWindowFrames");
            SerializedProperty descriptionProperty = entryProperty.FindPropertyRelative("description");

            PropertyField nameField = new PropertyField(nameProperty, string.Empty);
            nameField.style.flexGrow = 1f;
            rowContainer.Add(nameField);

            uint eventKeyValue = eventKeyProperty.uintValue;
            string maskabilityNote = AnimEventMaskKeys.IsMaskable(eventKeyValue)
                ? "maskable"
                : "pulse-only";
            Label keyLabel = new Label("key " + eventKeyValue + " (" + maskabilityNote + ")");
            keyLabel.selection.isSelectable = true;
            keyLabel.style.opacity = 0.7f;
            keyLabel.style.marginLeft = 6f;
            keyLabel.style.marginRight = 6f;
            keyLabel.tooltip =
                "Stable event key. What a marker's EventMarker.eventKey (architecture §3.2, §5.5) "
                + "actually stores - renaming the row above never touches this value. A pulse-only "
                + "key still fires; it just cannot hold a window (rule V19/V20).";
            rowContainer.Add(keyLabel);

            IntegerField windowFramesField = new IntegerField();
            windowFramesField.bindingPath = defaultWindowFramesProperty.propertyPath;
            windowFramesField.style.width = 44f;
            windowFramesField.style.marginRight = 6f;
            windowFramesField.tooltip =
                "Default event window, in frames at the registry's reference frame rate. A new "
                + "marker for this event starts with this window; 0 leaves it pulse-only.";
            rowContainer.Add(windowFramesField);

            Button removeButton = new Button(() => RemoveEntry(entryIndex)) { text = "Remove" };
            rowContainer.Add(removeButton);

            Foldout descriptionFoldout = new Foldout { text = "Description", value = false };
            descriptionFoldout.style.marginLeft = 12f;
            PropertyField descriptionField = new PropertyField(descriptionProperty, string.Empty);
            descriptionFoldout.Add(descriptionField);
            rowGroup.Add(descriptionFoldout);

            return rowGroup;
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
            RefreshRows();
            RefreshFindings();
            constantsSection?.RegenerateIfConfigured();
        }

        /// <summary>
        /// Removes one event row, behind a confirmation naming how many markers use it (amendment
        /// A55 Task 2). Unlike a tag delete, this never fails a bake — a marker's key is arithmetic
        /// it already carries, so the dialog says the key becomes unresolved, not that anything
        /// breaks.
        /// </summary>
        /// <param name="entryIndex">Index into <see cref="AnimEventKeyRegistry.entries"/> to remove.</param>
        private void RemoveEntry(int entryIndex)
        {
            AnimEventKeyRegistry registry = (AnimEventKeyRegistry)target;
            if (registry.entries == null || entryIndex < 0 || entryIndex >= registry.entries.Count)
            {
                return;
            }

            AnimEventKeyEntry entry = registry.entries[entryIndex];
            string entryLabel = entry != null && !string.IsNullOrEmpty(entry.name)
                ? "'" + entry.name + "'"
                : "entry " + entryIndex;
            int markerCount = AnimEventBindingUtility.CountMarkerBindings(entry);
            int clipCount = AnimEventBindingUtility.CountBoundClips(entry);

            string question = markerCount > 0
                ? "Delete event " + entryLabel + "?\n\n" + markerCount + " marker(s) across "
                    + clipCount + " clip(s) use it and will show as an unresolved key the moment "
                    + "it is gone."
                : "Delete event " + entryLabel + "? Nothing currently uses it.";

            if (!EditorUtility.DisplayDialog("Delete Event", question, "Delete", "Cancel"))
            {
                return;
            }

            registry.entries.RemoveAt(entryIndex);
            VocabularyRegistryProvider.Persist(registry);
            serializedObject.Update();
            RefreshRows();
            RefreshFindings();
            constantsSection?.RegenerateIfConfigured();
        }

        // -----------------------------------------------------------------------------------
        // Findings.
        // -----------------------------------------------------------------------------------

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
