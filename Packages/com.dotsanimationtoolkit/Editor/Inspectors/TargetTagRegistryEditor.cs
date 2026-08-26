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
    /// The custom inspector for <see cref="TargetTagRegistry"/> (Phase E target-tags spec §4.1,
    /// §4.2.2): add, rename and remove tag rows, with rule T5's findings surfaced the same way
    /// <see cref="RigAssetEditor"/> surfaces a rig's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rows are hand-built rather than left to the default array drawer.</strong>
    /// <see cref="AnimEventKeyRegistryEditor"/> gets away with a single
    /// <see cref="PropertyField"/> over its list because deleting an event key has nothing to warn
    /// about. Deleting a tag does — §4.2.2 requires the count of bindings a tag has before it is
    /// removed — and there is no way to intercept the array drawer's own remove button to show
    /// that count first. Building the rows here, one <see cref="Button"/> per row, is what makes
    /// the confirmation possible.
    /// </para>
    /// <para>
    /// UI Toolkit only, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>: this type
    /// overrides <see cref="UnityEditor.Editor.CreateInspectorGUI"/> and never the immediate-mode
    /// entry point.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(TargetTagRegistry))]
    public sealed class TargetTagRegistryEditor : UnityEditor.Editor
    {
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color ErrorColor = new Color(0.85f, 0.35f, 0.32f);
        private static readonly Color CleanColor = new Color(0.45f, 0.78f, 0.48f);

        private SerializedProperty entriesProperty;
        private VisualElement rowsContainer;
        private VisualElement findingsContainer;

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

            RefreshRows();
            RefreshFindings();

            // Picks up Undo/Redo and edits made from anywhere else, the same way the anim event key
            // registry and the clip-set roster do - without re-walking the list on every repaint.
            // The explicit PersistChange() is the reason this callback exists at all for this
            // asset: TargetTagRegistry lives outside the AssetDatabase (Task 1), so a rename typed
            // into the bound name field above has nothing else that would ever write it to disk.
            root.TrackSerializedObjectValue(serializedObject, changedObject =>
            {
                TargetTagRegistry changedRegistry = target as TargetTagRegistry;
                if (changedRegistry != null)
                {
                    VocabularyRegistryProvider.Persist(changedRegistry);
                }
                RefreshRows();
                RefreshFindings();
            });

            return root;
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
                "Stable tag id. What a rig target's tagId (E2) and a track's tag binding (E3) " +
                "actually store - renaming the row above never touches this value.";
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

            // CreateVocabularyEntry both mints the id and persists the change (Task 1) - the same
            // single code path TargetTagPicker's "Create tag..." row uses, so "another tag, please"
            // never produces a duplicate id or an unsaved row regardless of which surface asked.
            registry.CreateVocabularyEntry("NewTag");

            serializedObject.Update();
            RefreshRows();
            RefreshFindings();
        }

        /// <summary>
        /// Removes one tag row, behind a confirmation naming how many bindings it will break
        /// (Phase E target-tags spec §4.2.2) — a rename is safe by construction, but a delete
        /// produces T3 errors on every clip that used the tag, and that cost should be visible
        /// before it happens rather than discovered afterwards in the console.
        /// </summary>
        /// <param name="entryIndex">Index into <see cref="TargetTagRegistry.entries"/> to remove.</param>
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
            // Rig-target bindings (E2) plus track bindings (E3): a delete breaks both kinds, and a
            // person deciding whether to confirm needs the real total, not half of it.
            int bindingCount = TargetTagBindingUtility.CountRigTargetBindings(entry)
                + TargetTagBindingUtility.CountTrackBindings(entry);

            string question = bindingCount > 0
                ? "Delete tag " + entryLabel + "?\n\n" + bindingCount + " binding(s) use it and " +
                    "will fail validation rule T3 the moment it is gone."
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
                        : entryCount + " tag(s), all valid (rule T5).",
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
