// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="AnimationNameRegistry"/>: add, rename and remove animation
    /// name rows, the same hand-built shape <see cref="TargetTagRegistryEditor"/> uses for tags.
    /// </summary>
    [CustomEditor(typeof(AnimationNameRegistry))]
    public sealed class AnimationNameRegistryEditor : UnityEditor.Editor
    {
        private SerializedProperty entriesProperty;
        private VisualElement rowsContainer;
        private VocabularyConstantsSection constantsSection;

        // Row count as of the last RefreshRows, so OnSerializedObjectChanged can tell a resize
        // (rows added/removed elsewhere) from an ordinary rename keystroke.
        private int builtEntryCount = -1;

        public override VisualElement CreateInspectorGUI()
        {
            entriesProperty = serializedObject.FindProperty("entries");

            VisualElement root = new VisualElement();

            Label helpLabel = new Label(
                "An animation name is what an actor profile's layers play by (\"Walk\", \"Idle\", " +
                "...). Rename freely below - a layer binds a name's id, never its name, so nothing breaks.");
            helpLabel.style.whiteSpace = WhiteSpace.Normal;
            helpLabel.style.opacity = 0.75f;
            helpLabel.style.marginBottom = 6f;
            root.Add(helpLabel);

            rowsContainer = new VisualElement();
            root.Add(rowsContainer);

            Button addButton = new Button(AddEntry) { text = "Add Animation" };
            addButton.style.marginTop = 6f;
            root.Add(addButton);

            constantsSection = new VocabularyConstantsSection(
                target as AnimationNameRegistry,
                target,
                "AnimNames",
                "Animation",
                "Animation",
                () =>
                {
                    AnimationNameRegistry persistedRegistry = target as AnimationNameRegistry;
                    if (persistedRegistry != null)
                    {
                        VocabularyRegistryProvider.Persist(persistedRegistry);
                    }
                });
            root.Add(constantsSection);

            RefreshRows();

            // Picks up Undo/Redo and edits made elsewhere without re-walking the list every repaint.
            // AnimationNameRegistry lives outside the AssetDatabase, so a rename typed into the
            // bound name field above has nothing else that would ever write it to disk.
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
            AnimationNameRegistry changedRegistry = target as AnimationNameRegistry;
            if (changedRegistry != null)
            {
                VocabularyRegistryProvider.Persist(changedRegistry);
            }

            bool rowCountChanged = entriesProperty != null && entriesProperty.arraySize != builtEntryCount;
            if (rowCountChanged)
            {
                RefreshRows();
            }

            // A resize regenerates immediately, since nothing guarantees OnDisable ever runs to
            // flush it; an ordinary rename does nothing here and waits for OnDisable instead.
            if (rowCountChanged)
            {
                constantsSection?.RegenerateIfConfigured();
            }
        }

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
                    "No animation names yet. Add one, then reference it from an actor profile layer.");
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
            SerializedProperty idProperty = entryProperty.FindPropertyRelative("animationKey");

            PropertyField nameField = new PropertyField(nameProperty, string.Empty);
            nameField.style.flexGrow = 1f;
            rowContainer.Add(nameField);

            Label idLabel = new Label("id 0x" + idProperty.uintValue.ToString("X8"));
            idLabel.selection.isSelectable = true;
            idLabel.style.opacity = 0.7f;
            idLabel.style.marginLeft = 6f;
            idLabel.style.marginRight = 6f;
            idLabel.tooltip =
                "Stable animation id. What an actor profile layer's animationKey actually stores - " +
                "renaming the row above never touches this value.";
            rowContainer.Add(idLabel);

            Button removeButton = new Button(() => RemoveEntry(entryIndex)) { text = "Remove" };
            rowContainer.Add(removeButton);

            return rowContainer;
        }

        /// <summary>
        /// Appends an animation name with a freshly minted, collision-free id, so "another
        /// animation, please" can never produce a duplicate by accident even though ids are random.
        /// </summary>
        private void AddEntry()
        {
            AnimationNameRegistry registry = (AnimationNameRegistry)target;

            // CreateVocabularyEntry only mints the id in memory and cannot persist itself, and this
            // mutates the registry directly rather than through SerializedProperty, so the explicit
            // PersistVocabulary call is required — TrackSerializedObjectValue never fires for it.
            registry.CreateVocabularyEntry("NewAnimation");
            VocabularyRegistryProvider.PersistVocabulary(registry);

            serializedObject.Update();
            RefreshRows();
            constantsSection?.RegenerateIfConfigured();
        }

        /// <summary>Removes one animation name row.</summary>
        private void RemoveEntry(int entryIndex)
        {
            AnimationNameRegistry registry = (AnimationNameRegistry)target;
            if (registry.entries == null || entryIndex < 0 || entryIndex >= registry.entries.Count)
            {
                return;
            }

            AnimationNameEntry entry = registry.entries[entryIndex];
            string entryLabel = entry != null && !string.IsNullOrEmpty(entry.name)
                ? "'" + entry.name + "'"
                : "entry " + entryIndex;

            if (!EditorUtility.DisplayDialog(
                "Delete Animation Name",
                "Delete animation " + entryLabel + "? Any layer entry using it will show as an " +
                "unresolved key the moment it is gone.",
                "Delete",
                "Cancel"))
            {
                return;
            }

            registry.entries.RemoveAt(entryIndex);
            VocabularyRegistryProvider.Persist(registry);
            serializedObject.Update();
            RefreshRows();
            constantsSection?.RegenerateIfConfigured();
        }
    }
}
