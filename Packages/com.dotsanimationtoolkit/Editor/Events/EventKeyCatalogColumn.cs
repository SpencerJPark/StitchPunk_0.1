// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's Keys column: the event registry as a searchable catalog with New, Refresh and a row context menu.</summary>
    public sealed class EventKeyCatalogColumn : VisualElement
    {
        private readonly List<AnimEventKeyEntry> filteredEntries = new List<AnimEventKeyEntry>();
        private readonly ToolbarSearchField searchField;
        private readonly ListView keysListView;
        private readonly Label emptyLabel;
        private readonly Label budgetLabel;

        private string searchText = string.Empty;

        public event Action<AnimEventKeyEntry> EntrySelected;

        public AnimEventKeyRegistry Registry { get; private set; }

        public AnimEventKeyEntry SelectedEntry { get; private set; }

        public EventKeyCatalogColumn()
        {
            name = "events-keys-column";
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;
            style.minWidth = 200f;

            VisualElement headerActions = new VisualElement();
            headerActions.AddToClassList("toolkit-pane-actions");

            Button newButton = ToolkitIcons.MakeIconTextButton(RaiseNewRequested, "Toolbar Plus", "Create a new event key.", "New");
            newButton.name = "events-keys-new-button";
            headerActions.Add(newButton);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(Rescan, "Refresh", "Rescan asset references.", "Refresh");
            refreshButton.name = "events-keys-refresh-button";
            headerActions.Add(refreshButton);

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            Label titleLabel = new Label("Keys");
            titleLabel.AddToClassList("toolkit-pane-title");
            header.Add(titleLabel);
            header.Add(headerActions);
            Add(header);

            searchField = new ToolbarSearchField();
            searchField.name = "events-keys-search";
            searchField.style.width = new Length(100f, LengthUnit.Percent);
            searchField.style.minWidth = 0f;
            searchField.style.marginTop = 4f;
            searchField.style.marginLeft = 0f;
            searchField.style.marginRight = 0f;
            searchField.RegisterValueChangedCallback(OnSearchTextChanged);
            Add(searchField);

            keysListView = new ListView();
            keysListView.name = "events-keys-list";
            keysListView.fixedItemHeight = 64f;
            keysListView.selectionType = SelectionType.Single;
            keysListView.style.flexGrow = 1f;
            keysListView.style.marginTop = 4f;
            keysListView.makeItem = MakeRow;
            keysListView.bindItem = BindRow;
            keysListView.itemsSource = filteredEntries;
            keysListView.selectionChanged += OnListSelectionChanged;
            Add(keysListView);

            emptyLabel = new Label();
            emptyLabel.AddToClassList("toolkit-hint");
            Add(emptyLabel);

            budgetLabel = new Label();
            budgetLabel.name = "events-keys-budget";
            budgetLabel.AddToClassList("toolkit-hint");
            Add(budgetLabel);

            RefreshEmptyState();
            UpdateBudgetLabel();
        }

        public void Bind(AnimEventKeyRegistry registry)
        {
            Registry = registry;
            RefreshRows();
        }

        public void RefreshRows()
        {
            uint previouslySelectedKey = SelectedEntry != null ? SelectedEntry.eventKey : 0u;

            filteredEntries.Clear();
            if (Registry != null && Registry.entries != null)
            {
                for (int entryIndex = 0; entryIndex < Registry.entries.Count; entryIndex++)
                {
                    AnimEventKeyEntry entry = Registry.entries[entryIndex];
                    if (entry != null && MatchesSearch(entry))
                    {
                        filteredEntries.Add(entry);
                    }
                }
            }

            keysListView.Rebuild();
            RefreshEmptyState();
            UpdateBudgetLabel();

            if (SelectedEntry == null)
            {
                return;
            }

            AnimEventKeyEntry survivingEntry = FindEntry(previouslySelectedKey);
            if (survivingEntry != null)
            {
                SelectedEntry = survivingEntry;
                keysListView.SetSelectionWithoutNotify(
                    filteredEntries.Contains(survivingEntry)
                        ? new List<int> { filteredEntries.IndexOf(survivingEntry) }
                        : new List<int>());
            }
            else
            {
                SelectedEntry = null;
                keysListView.SetSelectionWithoutNotify(new List<int>());
                EntrySelected?.Invoke(null);
            }
        }

        public void SelectKey(uint eventKey)
        {
            AnimEventKeyEntry entry = FindEntry(eventKey);
            SelectedEntry = entry;
            keysListView.SetSelectionWithoutNotify(
                entry != null && filteredEntries.Contains(entry)
                    ? new List<int> { filteredEntries.IndexOf(entry) }
                    : new List<int>());
            keysListView.Rebuild();
            EntrySelected?.Invoke(entry);
        }

        private AnimEventKeyEntry FindEntry(uint eventKey)
        {
            if (eventKey == 0u || Registry == null || Registry.entries == null)
            {
                return null;
            }

            for (int entryIndex = 0; entryIndex < Registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = Registry.entries[entryIndex];
                if (entry != null && entry.eventKey == eventKey)
                {
                    return entry;
                }
            }
            return null;
        }

        private bool MatchesSearch(AnimEventKeyEntry entry)
        {
            if (string.IsNullOrEmpty(searchText))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(entry.name) && entry.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return entry.eventKey.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RaiseNewRequested()
        {
            if (Registry == null)
            {
                return;
            }

            uint newKey = Registry.CreateVocabularyEntry("NewEvent");
            VocabularyRegistryProvider.Persist(Registry);
            GenerateConstants();
            RefreshRows();
            SelectKey(newKey);
        }

        private void Rescan()
        {
            AssetReferenceIndex.MarkDirty();
            RefreshRows();
        }

        private VisualElement MakeRow()
        {
            VisualElement itemSlot = ToolkitChrome.MakeListRowSlot("events-keys-row-box", out VisualElement row);

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-box__header");

            Label titleLabel = new Label();
            titleLabel.name = "events-keys-row-title";
            titleLabel.AddToClassList("toolkit-box__title");
            headerRow.Add(titleLabel);
            row.Add(headerRow);

            Label infoLabel = new Label();
            infoLabel.name = "events-keys-row-info";
            infoLabel.AddToClassList("toolkit-box__label");
            infoLabel.AddToClassList("toolkit-hint");
            row.Add(infoLabel);

            row.AddManipulator(new ContextualMenuManipulator(
                populateEvent => PopulateRowContextMenu(populateEvent, row)));

            return itemSlot;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (index < 0 || index >= filteredEntries.Count)
            {
                return;
            }

            AnimEventKeyEntry entry = filteredEntries[index];

            VisualElement row = element.Q<VisualElement>("events-keys-row-box");
            row.userData = entry;

            Label titleLabel = row.Q<Label>("events-keys-row-title");
            titleLabel.text = entry != null && !string.IsNullOrEmpty(entry.name) ? entry.name : "(unnamed)";

            Label infoLabel = row.Q<Label>("events-keys-row-info");
            infoLabel.text = entry != null ? DescribeKeyLine(entry) : string.Empty;

            row.EnableInClassList("toolkit-box--selected", entry == SelectedEntry);
        }

        private static string DescribeKeyLine(AnimEventKeyEntry entry)
        {
            string maskability = AnimEventMaskKeys.IsMaskable(entry.eventKey) ? "maskable" : "pulse-only";
            return maskability + " · key " + entry.eventKey.ToString();
        }

        private void OnListSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (object selectedItem in selectedItems)
            {
                AnimEventKeyEntry entry = selectedItem as AnimEventKeyEntry;
                SelectedEntry = entry;
                EntrySelected?.Invoke(entry);
                return;
            }
        }

        private void OnSearchTextChanged(ChangeEvent<string> changeEvent)
        {
            searchText = changeEvent.newValue ?? string.Empty;
            RefreshRows();
        }

        private void RefreshEmptyState()
        {
            bool isEmpty = filteredEntries.Count == 0;
            keysListView.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                bool registryHasNoEntries = Registry == null || Registry.entries == null || Registry.entries.Count == 0;
                emptyLabel.text = registryHasNoEntries ? "No event keys yet." : "No event keys match your search.";
            }
        }

        private void UpdateBudgetLabel()
        {
            int maskableUsedCount = 0;
            int pulseOnlyUsedCount = 0;
            if (Registry != null && Registry.entries != null)
            {
                for (int entryIndex = 0; entryIndex < Registry.entries.Count; entryIndex++)
                {
                    AnimEventKeyEntry entry = Registry.entries[entryIndex];
                    if (entry == null)
                    {
                        continue;
                    }
                    if (AnimEventMaskKeys.IsMaskable(entry.eventKey))
                    {
                        maskableUsedCount++;
                    }
                    else
                    {
                        pulseOnlyUsedCount++;
                    }
                }
            }

            budgetLabel.text = maskableUsedCount + " of " + AnimEventMaskKeys.MaskKeyCount
                + " maskable keys used · " + pulseOnlyUsedCount + " pulse-only";
            budgetLabel.EnableInClassList("toolkit-text--warning", maskableUsedCount >= AnimEventMaskKeys.MaskKeyCount);
        }

        private void PopulateRowContextMenu(ContextualMenuPopulateEvent populateEvent, VisualElement row)
        {
            AnimEventKeyEntry entry = row.userData as AnimEventKeyEntry;
            if (entry == null)
            {
                return;
            }

            populateEvent.menu.AppendAction(
                "Rename",
                renameAction => InlineRenameEditing.Begin(
                    row.Q<Label>("events-keys-row-title"),
                    entry.name,
                    committedName => RenameEntry(entry, committedName)),
                DropdownMenuAction.AlwaysEnabled);

            populateEvent.menu.AppendAction(
                "Delete",
                deleteAction => DeleteEntry(entry),
                DropdownMenuAction.AlwaysEnabled);

            populateEvent.menu.AppendAction(
                "Generate Constants",
                generateAction => GenerateConstants(),
                DropdownMenuAction.AlwaysEnabled);

            if (entry.eventKey != 0u)
            {
                populateEvent.menu.AppendAction(
                    "Merge into…",
                    mergeAction => RefactorPromptEditing.ShowMergeIntoMenu(row, entry.eventKey, RefreshRows),
                    DropdownMenuAction.AlwaysEnabled);
            }
        }

        private void RenameEntry(AnimEventKeyEntry entry, string committedName)
        {
            if (Registry == null)
            {
                return;
            }

            entry.name = committedName;
            VocabularyRegistryProvider.Persist(Registry);
            GenerateConstants();
            RefreshRows();
        }

        private void DeleteEntry(AnimEventKeyEntry entry)
        {
            if (Registry == null || Registry.entries == null)
            {
                return;
            }

            List<AssetReference> references = AssetReferenceIndex.ReferencesToEventKey(entry.eventKey);
            string entryLabel = !string.IsNullOrEmpty(entry.name) ? "'" + entry.name + "'" : "(unnamed)";
            string referenceSummary = references.Count > 0 ? AssetReferenceIndex.SummarizeForDialog(references) : "Nothing uses it.";

            if (!EditorUtility.DisplayDialog("Delete Event", "Delete event " + entryLabel + "?\n\n" + referenceSummary, "Delete", "Cancel"))
            {
                return;
            }

            if (references.Count > 0)
            {
                bool confirmedStillInUse = EditorUtility.DisplayDialog(
                    "Delete Event Still In Use",
                    references.Count + " reference(s) will show as an unresolved key. Delete anyway?",
                    "Delete Anyway",
                    "Cancel");
                if (!confirmedStillInUse)
                {
                    return;
                }
            }

            Registry.entries.Remove(entry);
            VocabularyRegistryProvider.Persist(Registry);
            GenerateConstants();
            RefreshRows();
        }

        private void GenerateConstants()
        {
            if (Registry == null)
            {
                return;
            }

            VocabularyConstantsSection constantsSection = new VocabularyConstantsSection(
                Registry,
                Registry,
                "AnimEvents",
                "Event",
                "Event",
                () => VocabularyRegistryProvider.Persist(Registry),
                // Same row callbacks as the registry inspector, or regenerating from here drops the payload lines and value-name constants.
                entryIndex => IsEntryIndexValid(entryIndex) ? AnimEventKeyRegistryEditor.DescribePayloadForConstants(Registry.entries[entryIndex]) : null,
                entryIndex => IsEntryIndexValid(entryIndex) ? Registry.entries[entryIndex].intParamValueNames : null);
            constantsSection.RegenerateIfConfigured();
        }

        private bool IsEntryIndexValid(int entryIndex)
        {
            return Registry != null && Registry.entries != null && entryIndex >= 0
                && entryIndex < Registry.entries.Count && Registry.entries[entryIndex] != null;
        }
    }
}
