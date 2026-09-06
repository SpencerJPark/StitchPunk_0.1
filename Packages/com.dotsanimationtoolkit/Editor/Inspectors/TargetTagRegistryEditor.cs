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
    /// Custom inspector for <see cref="TargetTagRegistry"/>: add, rename and remove tag rows.
    /// Rows are hand-built, not the default array drawer, so a delete can show its binding count
    /// first — the same shape <see cref="AnimEventKeyRegistryEditor"/> uses for events.
    /// </summary>
    [CustomEditor(typeof(TargetTagRegistry))]
    public sealed class TargetTagRegistryEditor : UnityEditor.Editor
    {
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color ErrorColor = new Color(0.85f, 0.35f, 0.32f);
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

            Label helpLabel = new Label(
                "A tag names a role a rig target can carry (\"EyeL\", \"Jaw\", ...) so clips can be " +
                "shared between rigs that tag a target the same way. Rename freely below - a clip " +
                "binds a tag's id, never its name, so nothing breaks.");
            helpLabel.style.whiteSpace = WhiteSpace.Normal;
            helpLabel.style.opacity = 0.75f;
            helpLabel.style.marginBottom = 6f;
            root.Add(helpLabel);

            rowsContainer = new VisualElement();
            root.Add(rowsContainer);

            Button addButton = new Button(AddEntry) { text = "Add Tag" };
            addButton.style.marginTop = 6f;
            root.Add(addButton);

            findingsContainer = new VisualElement();
            findingsContainer.style.marginTop = 8f;
            root.Add(findingsContainer);

            // Sits on the registry inspector, not the Clip Editor, because this inspector is also
            // what VocabularyQuickEditWindow hosts — one entry point for every "Edit Target Tags..." picker.
            constantsSection = new VocabularyConstantsSection(
                target as TargetTagRegistry,
                target,
                "TargetTags",
                "Target tag",
                "Tag",
                () =>
                {
                    TargetTagRegistry persistedRegistry = target as TargetTagRegistry;
                    if (persistedRegistry != null)
                    {
                        VocabularyRegistryProvider.Persist(persistedRegistry);
                    }
                });
            root.Add(constantsSection);

            RefreshRows();
            RefreshFindings();

            // Picks up Undo/Redo and edits made elsewhere without re-walking the list every repaint.
            // TargetTagRegistry lives outside the AssetDatabase, so a rename typed into the bound
            // name field above has nothing else that would ever write it to disk.
            root.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            return root;
        }

        // Flushes a pending rename's regeneration here, not on every keystroke's blur, so an
        // in-progress rename does not force a recompile mid-edit.
        private void OnDisable()
        {
            constantsSection?.RegenerateIfConfigured();
        }

        // Rebuilds rows only on a count change (add/remove/Undo) — rebuilding a bound PropertyField
        // on an ordinary rename keystroke would throw away focus and the character just typed.
        private void OnSerializedObjectChanged(SerializedObject changedSerializedObject)
        {
            TargetTagRegistry changedRegistry = target as TargetTagRegistry;
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
                    "No tags yet. Add one, then tag a rig's targets to make it selectable there.");
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
            VisualElement rowContainer = new VisualElement();
            rowContainer.style.flexDirection = FlexDirection.Row;
            rowContainer.style.alignItems = Align.Center;
            rowContainer.style.marginTop = 2f;

            SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(entryIndex);
            SerializedProperty nameProperty = entryProperty.FindPropertyRelative("name");
            SerializedProperty idProperty = entryProperty.FindPropertyRelative("stableId");

            PropertyField nameField = new PropertyField(nameProperty, string.Empty);
            nameField.style.flexGrow = 1f;
            rowContainer.Add(nameField);

            Label idLabel = new Label("id 0x" + idProperty.uintValue.ToString("X8"));
            idLabel.selection.isSelectable = true;
            idLabel.style.opacity = 0.7f;
            idLabel.style.marginLeft = 6f;
            idLabel.style.marginRight = 6f;
            idLabel.tooltip =
                "Stable tag id. What a rig target's tagId and a track's tag binding actually " +
                "store - renaming the row above never touches this value.";
            rowContainer.Add(idLabel);

            Button removeButton = new Button(() => RemoveEntry(entryIndex)) { text = "Remove" };
            rowContainer.Add(removeButton);

            return rowContainer;
        }

        /// <summary>
        /// Appends a tag with a freshly minted, collision-free id, so "another tag, please" can
        /// never produce a duplicate by accident even though ids are random.
        /// </summary>
        private void AddEntry()
        {
            TargetTagRegistry registry = (TargetTagRegistry)target;

            // CreateVocabularyEntry only mints the id in memory and cannot persist itself, and this
            // mutates the registry directly rather than through SerializedProperty, so the explicit
            // PersistVocabulary call is required — TrackSerializedObjectValue never fires for it.
            registry.CreateVocabularyEntry("NewTag");
            VocabularyRegistryProvider.PersistVocabulary(registry);

            serializedObject.Update();
            RefreshRows();
            RefreshFindings();
            constantsSection?.RegenerateIfConfigured();
        }

        /// <summary>
        /// Removes one tag row, behind a confirmation naming how many bindings it will break —
        /// unlike a rename, a delete fails validation on every clip that used the tag.
        /// </summary>
        private void RemoveEntry(int entryIndex)
        {
            TargetTagRegistry registry = (TargetTagRegistry)target;
            if (registry.entries == null || entryIndex < 0 || entryIndex >= registry.entries.Count)
            {
                return;
            }

            TargetTagEntry entry = registry.entries[entryIndex];
            string entryLabel = entry != null && !string.IsNullOrEmpty(entry.name)
                ? "'" + entry.name + "'"
                : "entry " + entryIndex;
            // Rig-target bindings plus track bindings: a delete breaks both kinds, and the
            // confirmation needs the real total, not half of it.
            int bindingCount = TargetTagBindingUtility.CountRigTargetBindings(entry)
                + TargetTagBindingUtility.CountTrackBindings(entry);

            string question = bindingCount > 0
                ? "Delete tag " + entryLabel + "?\n\n" + bindingCount + " binding(s) use it and " +
                    "will fail validation the moment it is gone."
                : "Delete tag " + entryLabel + "? Nothing currently binds to it.";

            if (!EditorUtility.DisplayDialog("Delete Target Tag", question, "Delete", "Cancel"))
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

            TargetTagRegistry registry = target as TargetTagRegistry;
            if (registry == null)
            {
                return;
            }

            List<ValidationMessage> messages = ClipValidation.ValidateTargetTagRegistry(registry);
            if (messages.Count == 0)
            {
                int entryCount = registry.entries == null ? 0 : registry.entries.Count;
                findingsContainer.Add(MakeNote(
                    entryCount == 0
                        ? "No tags yet."
                        : entryCount + " tag(s), all valid.",
                    CleanColor));
                return;
            }

            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                ValidationMessage message = messages[messageIndex];
                findingsContainer.Add(MakeNote(
                    message.ToString(),
                    message.severity == ValidationSeverity.Error ? ErrorColor : WarningColor));
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
