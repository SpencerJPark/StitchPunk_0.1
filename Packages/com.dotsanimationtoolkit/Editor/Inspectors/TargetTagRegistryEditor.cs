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
    /// <strong>Rows are hand-built rather than left to the default array drawer</strong>, so a
    /// delete can be intercepted and show its cost first — §4.2.2 requires the count of bindings a
    /// tag has before it is removed, and there is no way to get that in front of the array drawer's
    /// own remove button. <see cref="AnimEventKeyRegistryEditor"/> now builds its rows the same way
    /// (amendment A55), for the analogous reason on the event registry: a marker count shown before
    /// its own delete.
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
        private VocabularyConstantsSection constantsSection;

        /// <summary>
        /// Row count as of the last <see cref="RefreshRows"/>, so <see cref="OnSerializedObjectChanged"/>
        /// can tell a resize (rows added/removed elsewhere) from an ordinary keystroke — see that
        /// method's remarks for why the distinction matters.
        /// </summary>
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

            // Task 2's generator. It sits on the registry inspector rather than somewhere in the
            // Clip Editor because that inspector is also what VocabularyQuickEditWindow hosts, so
            // "Edit Target Tags..." from any picker reaches it without a second entry point.
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

            // Picks up Undo/Redo and edits made from anywhere else, the same way the anim event key
            // registry and the clip-set roster do - without re-walking the list on every repaint.
            // The explicit PersistChange() is the reason this callback exists at all for this
            // asset: TargetTagRegistry lives outside the AssetDatabase (Task 1), so a rename typed
            // into the bound name field above has nothing else that would ever write it to disk.
            root.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            return root;
        }

        /// <summary>
        /// Flushes a pending constants regeneration when this inspector goes away — the window
        /// closes, the selection changes, or the Project Settings tab is left.
        /// </summary>
        /// <remarks>
        /// Amendment A54 originally regenerated on every <c>FocusOutEvent</c> anywhere in this
        /// inspector, so simply clicking out of a renamed row's field — the ordinary way to finish a
        /// rename — rewrote the constants file and called <see cref="AssetDatabase.Refresh"/>
        /// immediately. Writing a changed <c>.cs</c> file under <c>Assets/</c> makes Unity recompile
        /// and reload the domain, and doing that synchronously out from under someone renaming a tag
        /// mid-session — while a separate window like the Clip Editor is open and in active use — is
        /// what broke it: a domain reload tears down and rebuilds every open window's managed state,
        /// and a UI Toolkit window does not always survive that cleanly. Regenerating here instead,
        /// on <c>OnDisable</c>, still keeps the promise that a rename eventually recompiles anything
        /// referencing the old name, without forcing that recompile mid-keystroke.
        /// </remarks>
        private void OnDisable()
        {
            constantsSection?.RegenerateIfConfigured();
        }

        /// <summary>
        /// Persists every change, but only tears down and rebuilds the rows when the entry count
        /// itself changed (add/remove/Undo) — not on an ordinary rename keystroke, which is a change
        /// to the very row it would be destroying. Each <see cref="PropertyField"/> is already bound
        /// and refreshes its own displayed value; rebuilding it mid-edit only threw away focus and
        /// the character just typed, so a name was un-typeable a keystroke at a time. Same guard
        /// <see cref="RigAssetEditor"/>'s own target-tag rows use for the identical reason.
        /// </summary>
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

            // Constants regeneration is deferred to OnDisable, not triggered here (see its remarks):
            // regenerating immediately on every add/remove/rename forces a script recompile — a
            // domain reload — synchronously out from under whatever else might be open, which is what
            // broke the Clip Editor mid-session under the amendment A54 design this replaced.
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

            // CreateVocabularyEntry only mints the id in memory; it cannot persist itself
            // (Authoring/ never references UnityEditor). The explicit PersistVocabulary call is
            // required here because this mutates the registry directly rather than through
            // SerializedProperty, so TrackSerializedObjectValue below never fires for it.
            registry.CreateVocabularyEntry("NewTag");
            VocabularyRegistryProvider.PersistVocabulary(registry);

            serializedObject.Update();
            RefreshRows();
            RefreshFindings();
            // Constants regeneration is deferred to OnDisable — see its remarks.
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
            // Constants regeneration is deferred to OnDisable — see its remarks.
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
