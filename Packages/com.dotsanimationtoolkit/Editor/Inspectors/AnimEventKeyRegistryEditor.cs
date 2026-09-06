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
    /// Custom inspector for <see cref="AnimEventKeyRegistry"/>: hand-built rename/remove rows
    /// matching <see cref="TargetTagRegistryEditor"/>'s shape, an "Add Event" button that never
    /// mints a colliding key, and findings that flag a broken entry before it confuses every reader.
    /// </summary>
    [CustomEditor(typeof(AnimEventKeyRegistry))]
    public sealed class AnimEventKeyRegistryEditor : UnityEditor.Editor
    {
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color CleanColor = new Color(0.45f, 0.78f, 0.48f);

        private SerializedProperty entriesProperty;
        private VisualElement rowsContainer;
        private VisualElement findingsContainer;
        private VocabularyConstantsSection constantsSection;

        // Row count as of the last RefreshRows, so OnSerializedObjectChanged can tell a resize
        // (rows added/removed elsewhere) from an ordinary rename keystroke.
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

            // Same block the tag registry shows. Persisting is a no-op for an explicitly assigned
            // override asset, which instead saves the ordinary AssetDatabase way via SetDirty below.
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

            // Picks up Undo/Redo and edits made elsewhere without re-walking the list every repaint.
            // The explicit Persist call is what makes a rename survive a domain reload when this is
            // the project singleton; it is a no-op for an explicitly assigned override asset.
            root.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            return root;
        }

        // Flushes a pending constants regeneration here, not on every field blur, so an ordinary
        // rename does not synchronously force a recompile while other windows are in active use.
        private void OnDisable()
        {
            constantsSection?.RegenerateIfConfigured();
        }

        // Rebuilds rows only on a count change (add/remove/Undo) — rebuilding a bound PropertyField
        // on an ordinary rename keystroke would throw away focus and the character just typed.
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

            // A resize regenerates immediately, since nothing guarantees OnDisable ever runs to
            // flush it; an ordinary rename does nothing here and waits for OnDisable instead.
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
                "Stable event key. What a marker's EventMarker.eventKey actually stores - renaming "
                + "the row above never touches this value. A pulse-only key still fires; it just "
                + "cannot hold a window.";
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
            descriptionFoldout.tooltip =
                "What this event is for. Shown as the grey wording beside the event's name in the "
                + "picker, so it is read at the moment someone is choosing which event to fire.";
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

            // CreateVocabularyEntry only mints the key in memory; it cannot persist itself.
            // PersistVocabulary writes the project singleton's JSON and is a no-op for an
            // explicitly assigned override asset, which needs the dirty-and-save pair instead.
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
        /// Removes one event row, behind a confirmation naming how many markers use it. Unlike a
        /// tag delete, this never fails a bake — the dialog says the key becomes unresolved.
        /// </summary>
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
                        + "package. Any clip using it fails validation.");
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
