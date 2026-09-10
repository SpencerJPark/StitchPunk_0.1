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
    /// <summary>Browse, create, and edit clip sets: a catalog list plus a name/folder editor and clip picker for the selected set.</summary>
    public sealed class ClipSetsPanel : VisualElement, IDisposable
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] Clip Sets: ";

        private readonly ClipSetSaveLocation saveLocation = new ClipSetSaveLocation();
        private readonly List<ClipSetAsset> catalogClipSets = new List<ClipSetAsset>();
        private readonly List<ClipSetAsset> filteredClipSets = new List<ClipSetAsset>();
        private readonly List<ClipAsset> catalogClips = new List<ClipAsset>();

        private string catalogSearchText = string.Empty;
        private ToolbarSearchField catalogSearchField;
        private ListView clipSetsList;
        private Label catalogEmptyLabel;

        private Label editorTitleLabel;
        private Button openInEditorButton;
        private Label noSelectionHintLabel;
        private VisualElement editorContent;

        private TextField nameField;
        private Label folderLabel;

        private ClipPickerListElement picker;

        private Label editHintLabel;
        private Label resultLabel;

        public event Action<ClipSetAsset> OpenInEditorRequested;
        public event Action<ClipSetAsset> SetClipsChanged;

        public ClipSetAsset SelectedSet { get; private set; }

        private ActiveAssetSelection selection;

        public ClipSetsPanel()
        {
            style.flexGrow = 1f;

            // The same draggable-divider control the Clip Editor's own dock uses (dock-columns
            // in ClipEditorWindow.uxml) — the fixed pane (index 0) starts at 280px and the user
            // drags the handle TwoPaneSplitView inserts between the two children.
            TwoPaneSplitView splitView = new TwoPaneSplitView(0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            splitView.Add(BuildCatalogColumn());
            splitView.Add(BuildEditorColumn());
            Add(splitView);

            RefreshCatalogEmptyState();
            ApplyChromeForSelection();
        }

        private VisualElement BuildCatalogColumn()
        {
            // Width is TwoPaneSplitView's to manage (drag-resized); flexGrow so this element
            // actually fills whatever dimension the split view's fixed pane currently holds
            // (TwoPaneSplitView sizes its own pane wrapper, not this child directly), and
            // minWidth as a floor so the drag cannot squeeze it to an unusable sliver.
            VisualElement catalogColumn = new VisualElement { name = "clip-sets-catalog-column" };
            catalogColumn.style.flexGrow = 1f;
            catalogColumn.style.minWidth = 200f;
            catalogColumn.style.paddingTop = 8f;
            catalogColumn.style.paddingLeft = 10f;
            catalogColumn.style.paddingRight = 10f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            Label title = new Label("Clip Sets");
            title.AddToClassList("toolkit-pane-title");
            header.Add(title);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");

            Button newButton = ToolkitIcons.MakeIconTextButton(CreateAndSelectNewClipSet, "Toolbar Plus", null, "New");
            newButton.name = "clip-sets-new-button";
            actions.Add(newButton);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(
                RescanProject, "Refresh", "Rescan the project for clip sets and clips", "Refresh");
            refreshButton.name = "clip-sets-refresh-button";
            actions.Add(refreshButton);

            header.Add(actions);
            catalogColumn.Add(header);

            catalogSearchField = new ToolbarSearchField();
            catalogSearchField.name = "clip-sets-search";
            // alignSelf: Stretch alone was not enough -- the field's own internal content
            // (text input + icon + cancel button) imposes a min-content width Yoga still honours
            // over stretch, so it kept overflowing a narrow column regardless of min-width: 0.
            // An explicit percentage width is clamped to the parent's box unconditionally.
            catalogSearchField.style.width = new Length(100f, LengthUnit.Percent);
            catalogSearchField.style.minWidth = 0f;
            catalogSearchField.style.marginTop = 4f;
            // ToolbarSearchField's own default USS ships a 4px-left/2px-right margin (verified
            // live) -- on top of an already-100%-wide box that pushes its right edge past the
            // rows below, which is the "overshoot" this was reported as. Zero it so the field is
            // flush with the list.
            catalogSearchField.style.marginLeft = 0f;
            catalogSearchField.style.marginRight = 0f;
            catalogSearchField.RegisterValueChangedCallback(OnCatalogSearchTextChanged);
            catalogColumn.Add(catalogSearchField);

            clipSetsList = new ListView();
            clipSetsList.name = "clip-sets-list";
            // DynamicHeight virtualization renders zero rows in this Unity version (verified live:
            // itemsSource.Count == 2 but the ListView's own childCount == 0) -- stick with
            // FixedHeight. ListView positions each slot at a fixed index * fixedItemHeight
            // regardless of the row's actual content height, so any slack left over here adds
            // straight onto the visual gap on top of the row's own margin -- sized tight to the
            // row's measured content (56px) + its 4px top/bottom margin, not generously, so the
            // margin is the only thing producing the gap.
            clipSetsList.fixedItemHeight = 64f;
            clipSetsList.selectionType = SelectionType.Single;
            clipSetsList.style.flexGrow = 1f;
            clipSetsList.style.marginTop = 4f;
            clipSetsList.makeItem = MakeClipSetRow;
            clipSetsList.bindItem = BindClipSetRow;
            clipSetsList.itemsSource = filteredClipSets;
            clipSetsList.selectionChanged += OnClipSetsListSelectionChanged;
            catalogColumn.Add(clipSetsList);

            catalogEmptyLabel = new Label("No clip sets in this project yet. Press New.");
            catalogEmptyLabel.AddToClassList("clip-editor__hint");
            catalogColumn.Add(catalogEmptyLabel);

            return catalogColumn;
        }

        private VisualElement MakeClipSetRow()
        {
            // ListView (FixedHeight virtualization) tags whatever makeItem returns with its own
            // internal item classes and forcibly zeroes ITS margin to keep the fixed-slot math
            // exact (verified live: an 8px inline margin set directly on that root read back as 0).
            // A margin on this outer slot is a no-op, so the boxed row that actually wants the gap
            // has to live one level deeper, as a plain child Unity's pooling never touches.
            VisualElement itemSlot = new VisualElement();
            // Unity also paints its own hover/selected background straight onto this slot (verified
            // live: unity-collection-view__item--selected resolves a solid grey fill across the
            // WHOLE slot, gap margin included) -- an inline override beats that USS state styling
            // unconditionally, so the slot itself never shades and only the boxed row below reacts.
            itemSlot.style.backgroundColor = new StyleColor(Color.clear);

            VisualElement row = new VisualElement();
            row.name = "clip-set-row-box";
            row.AddToClassList("toolkit-box");
            // Enough to read as separated instead of touching, without the gap dominating a
            // 56px-tall row -- fixedItemHeight is sized to match (content height + this margin).
            row.style.marginTop = 4f;
            row.style.marginBottom = 4f;
            // No horizontal margin: the row is left flush with the ListView's own bounds, which
            // stretch to the same catalog-column width the search field's 100% width fills --
            // an inset here would leave the row short of the search field's right edge.
            row.style.marginLeft = 0f;
            row.style.marginRight = 0f;

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-box__header");

            Label titleLabel = new Label();
            titleLabel.name = "clip-set-row-title";
            titleLabel.AddToClassList("toolkit-box__title");
            headerRow.Add(titleLabel);

            row.Add(headerRow);

            Label infoLabel = new Label();
            infoLabel.name = "clip-set-row-info";
            infoLabel.AddToClassList("toolkit-box__label");
            infoLabel.AddToClassList("clip-editor__hint");
            row.Add(infoLabel);

            // Closes over the row element itself (stable identity, never recreated) rather than
            // any per-bind data — the callback reads row.userData live when the menu opens, so a
            // recycled row always offers to delete whatever it is currently showing.
            row.AddManipulator(new ContextualMenuManipulator(
                populateEvent => PopulateClipSetRowContextMenu(populateEvent, row)));

            itemSlot.Add(row);
            return itemSlot;
        }

        private void PopulateClipSetRowContextMenu(ContextualMenuPopulateEvent populateEvent, VisualElement row)
        {
            ClipSetAsset targetSet = row.userData as ClipSetAsset;
            if (targetSet == null)
            {
                return;
            }

            populateEvent.menu.AppendAction(
                "Rename",
                renameAction => InlineRenameEditing.Begin(
                    row.Q<Label>("clip-set-row-title"),
                    targetSet.name,
                    committedName => CommitClipSetRename(targetSet, committedName)),
                DropdownMenuAction.AlwaysEnabled);
            populateEvent.menu.AppendAction(
                "Delete", deleteAction => RequestDeleteClipSet(targetSet), DropdownMenuAction.AlwaysEnabled);
        }

        private void RequestDeleteClipSet(ClipSetAsset targetSet)
        {
            if (targetSet == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Clip Set",
                "Delete \"" + targetSet.name + "\"? Any actor profile referencing it will lose those "
                    + "clips. The asset moves to the OS trash, not permanently deleted.",
                "Delete",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            bool wasSelected = SelectedSet == targetSet;
            if (!ClipAssetUtility.DeleteClipSet(targetSet))
            {
                ReportFailure("could not delete \"" + targetSet.name + "\".");
                return;
            }

            if (wasSelected)
            {
                ShowSet(null);
                selection?.SetClipSet(null);
            }

            RescanProject();
        }

        private void BindClipSetRow(VisualElement element, int index)
        {
            if (index < 0 || index >= filteredClipSets.Count)
            {
                return;
            }

            ClipSetAsset clipSet = filteredClipSets[index];

            // The boxed row (userData, the selected-state class, the context menu) lives one level
            // below the item slot ListView hands bindItem -- see MakeClipSetRow.
            VisualElement row = element.Q<VisualElement>("clip-set-row-box");
            row.userData = clipSet;

            Label titleLabel = row.Q<Label>("clip-set-row-title");
            titleLabel.text = clipSet != null ? clipSet.name : string.Empty;

            Label infoLabel = row.Q<Label>("clip-set-row-info");
            int clipCount = clipSet != null && clipSet.clips != null ? clipSet.clips.Count : 0;
            string assetPath = clipSet != null ? AssetDatabase.GetAssetPath(clipSet) : string.Empty;
            string folderPath = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            infoLabel.text = clipCount.ToString() + " clips" + (string.IsNullOrEmpty(folderPath) ? string.Empty : " · " + folderPath);

            row.EnableInClassList("toolkit-box--selected", clipSet == SelectedSet);
        }

        private void OnClipSetsListSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (object selectedItem in selectedItems)
            {
                SelectSet(selectedItem as ClipSetAsset);
                return;
            }
        }

        private VisualElement BuildEditorColumn()
        {
            VisualElement editorColumn = new VisualElement { name = "clip-sets-editor-column" };
            editorColumn.style.flexGrow = 1f;
            editorColumn.style.minWidth = 360f;
            editorColumn.style.paddingTop = 8f;
            editorColumn.style.paddingLeft = 10f;
            editorColumn.style.paddingRight = 10f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            editorTitleLabel = new Label();
            editorTitleLabel.name = "clip-set-editor-title";
            editorTitleLabel.AddToClassList("toolkit-pane-title");
            header.Add(editorTitleLabel);

            openInEditorButton = ToolkitIcons.MakeIconTextButton(
                OnOpenInEditorClicked, "editicon.sml", null, "Open in Clip Editor");
            openInEditorButton.name = "clip-set-open-button";
            header.Add(openInEditorButton);

            editorColumn.Add(header);

            noSelectionHintLabel = new Label("Select a clip set, or press New to make one.")
            {
                name = "clip-set-no-selection-hint"
            };
            noSelectionHintLabel.style.whiteSpace = WhiteSpace.Normal;
            noSelectionHintLabel.style.marginTop = 8f;
            editorColumn.Add(noSelectionHintLabel);

            editorContent = new VisualElement { name = "clip-set-editor-content" };

            nameField = new TextField("Name") { name = "clip-set-name-field" };
            // Commit on blur/Enter, not on every keystroke — renaming an asset per character
            // would create a file operation per letter.
            nameField.RegisterCallback<FocusOutEvent>(focusOutEvent => CommitNameFieldChange());
            nameField.RegisterCallback<KeyDownEvent>(keyDownEvent =>
            {
                if (keyDownEvent.keyCode == KeyCode.Return)
                {
                    CommitNameFieldChange();
                }
            });
            editorContent.Add(nameField);

            VisualElement folderRow = new VisualElement { name = "clip-set-folder-row" };
            folderRow.style.flexDirection = FlexDirection.Row;
            folderRow.style.alignItems = Align.Center;
            folderRow.style.marginBottom = 4f;

            folderLabel = new Label(saveLocation.Recall()) { name = "clip-set-folder-label" };
            folderLabel.style.flexGrow = 1f;
            folderLabel.style.overflow = Overflow.Hidden;
            folderLabel.style.textOverflow = TextOverflow.Ellipsis;
            folderLabel.style.whiteSpace = WhiteSpace.NoWrap;
            folderRow.Add(folderLabel);

            Button folderButton = new Button(OnFolderButtonClicked)
            {
                text = "…",
                name = "clip-set-folder-button",
                tooltip = "Where the next New clip set is created. Does not move the selected clip set."
            };
            folderButton.style.marginLeft = 4f;
            folderRow.Add(folderButton);

            editorContent.Add(folderRow);

            picker = new ClipPickerListElement();
            picker.name = "clip-picker";
            picker.style.flexGrow = 1f;
            picker.ClipCheckedChanged += OnPickerClipCheckedChanged;
            picker.ClipRenameRequested += OnPickerClipRenameRequested;
            editorContent.Add(picker);

            editHintLabel = new Label("Ticks apply to the set immediately. Ctrl+Z undoes.");
            editHintLabel.AddToClassList("clip-editor__hint");
            editorContent.Add(editHintLabel);

            editorColumn.Add(editorContent);

            resultLabel = new Label();
            resultLabel.name = "clip-sets-result-label";
            resultLabel.style.whiteSpace = WhiteSpace.Normal;
            resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            editorColumn.Add(resultLabel);

            return editorColumn;
        }

        private void OnFolderButtonClicked()
        {
            string currentFolder = saveLocation.Recall();
            string projectAssetsAbsolutePath = Application.dataPath;
            string startingAbsoluteFolder = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(projectAssetsAbsolutePath), currentFolder));

            string pickedAbsoluteFolder = EditorUtility.OpenFolderPanel("New Clip Set Folder", startingAbsoluteFolder, string.Empty);
            if (string.IsNullOrEmpty(pickedAbsoluteFolder))
            {
                return;
            }

            string projectRelativeFolder;
            if (ClipSetSaveLocation.TryMakeProjectRelative(pickedAbsoluteFolder, projectAssetsAbsolutePath, out projectRelativeFolder))
            {
                saveLocation.Remember(projectRelativeFolder);
                folderLabel.text = projectRelativeFolder;
            }
            else
            {
                ReportFailure("the chosen folder must be inside this project's Assets folder.");
            }
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
            // Re-bindable: unsubscribe the old one first, or a re-dock double-subscribes.
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }

            selection = sharedSelection;
            selection.ClipSetChanged += OnSharedClipSetChanged;
            OnSharedClipSetChanged(selection.ClipSet);
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            if (clipSet == SelectedSet)
            {
                return;
            }

            ShowSet(clipSet);
        }

        private void OnOpenInEditorClicked()
        {
            if (OpenInEditorRequested != null)
            {
                OpenInEditorRequested(SelectedSet);
            }
        }

        private void OnPickerClipCheckedChanged(ClipAsset clip, bool isChecked)
        {
            if (SelectedSet == null)
            {
                return;
            }

            bool changed = isChecked
                ? ClipAssetUtility.AddExistingClipToSet(SelectedSet, clip)
                : ClipAssetUtility.RemoveClipFromSet(SelectedSet, clip);

            if (changed)
            {
                if (SetClipsChanged != null)
                {
                    SetClipsChanged(SelectedSet);
                }

                clipSetsList.Rebuild();
            }
        }

        public void SetSource(ClipSetAsset openClipSet)
        {
            RescanProject();

            string fallbackFolder = "Assets";
            if (openClipSet != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(openClipSet);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    string folderPath = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        fallbackFolder = folderPath;
                    }
                }
            }
            saveLocation.FallbackFolder = fallbackFolder;
        }

        public void RescanProject()
        {
            List<ClipSetAsset> clipSets = new List<ClipSetAsset>();
            string[] clipSetAssetGuids = AssetDatabase.FindAssets("t:" + nameof(ClipSetAsset));
            for (int guidIndex = 0; guidIndex < clipSetAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(clipSetAssetGuids[guidIndex]);
                ClipSetAsset clipSet = AssetDatabase.LoadAssetAtPath<ClipSetAsset>(assetPath);
                if (clipSet != null)
                {
                    clipSets.Add(clipSet);
                }
            }

            List<ClipAsset> clips = new List<ClipAsset>();
            string[] clipAssetGuids = AssetDatabase.FindAssets("t:" + nameof(ClipAsset));
            for (int guidIndex = 0; guidIndex < clipAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(clipAssetGuids[guidIndex]);
                ClipAsset clip = AssetDatabase.LoadAssetAtPath<ClipAsset>(assetPath);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            clipSets.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));
            clips.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));

            LoadCatalog(clipSets, clips);
            folderLabel.text = saveLocation.Recall();
        }

        public void LoadCatalog(IReadOnlyList<ClipSetAsset> clipSets, IReadOnlyList<ClipAsset> clips)
        {
            catalogClipSets.Clear();
            if (clipSets != null)
            {
                catalogClipSets.AddRange(clipSets);
            }

            catalogClips.Clear();
            if (clips != null)
            {
                catalogClips.AddRange(clips);
            }

            if (SelectedSet != null && !catalogClipSets.Contains(SelectedSet))
            {
                SelectedSet = null;
                ApplyChromeForSelection();
            }

            ApplyCatalogFilter();

            picker.SetClips(catalogClips);
        }

        private void OnCatalogSearchTextChanged(ChangeEvent<string> changeEvent)
        {
            catalogSearchText = changeEvent.newValue ?? string.Empty;
            ApplyCatalogFilter();
        }

        private void ApplyCatalogFilter()
        {
            filteredClipSets.Clear();
            for (int index = 0; index < catalogClipSets.Count; index++)
            {
                ClipSetAsset clipSet = catalogClipSets[index];
                if (clipSet == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(catalogSearchText)
                    || clipSet.name.IndexOf(catalogSearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredClipSets.Add(clipSet);
                }
            }

            clipSetsList.Rebuild();
            RefreshCatalogEmptyState();

            if (SelectedSet != null)
            {
                clipSetsList.SetSelectionWithoutNotify(
                    filteredClipSets.Contains(SelectedSet)
                        ? new List<int> { filteredClipSets.IndexOf(SelectedSet) }
                        : new List<int>());
            }
        }

        private void RefreshCatalogEmptyState()
        {
            bool isEmpty = filteredClipSets.Count == 0;
            clipSetsList.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            catalogEmptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                catalogEmptyLabel.text = catalogClipSets.Count == 0
                    ? "No clip sets in this project yet. Press New."
                    : "No clip sets match your search.";
            }
        }

        public void SelectSet(ClipSetAsset clipSet)
        {
            ShowSet(clipSet);
            selection?.SetClipSet(clipSet);
        }

        private void ShowSet(ClipSetAsset clipSet)
        {
            SelectedSet = clipSet;

            clipSetsList.SetSelectionWithoutNotify(
                clipSet != null && filteredClipSets.Contains(clipSet)
                    ? new List<int> { filteredClipSets.IndexOf(clipSet) }
                    : new List<int>());
            clipSetsList.Rebuild();

            // Without notify: a plain assignment would fire the field's own change callback and
            // immediately write this set's name back onto itself.
            nameField.SetValueWithoutNotify(clipSet != null ? clipSet.name : string.Empty);

            List<ClipAsset> checkedClips = new List<ClipAsset>();
            if (clipSet != null && clipSet.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip != null && !checkedClips.Contains(clip))
                    {
                        checkedClips.Add(clip);
                    }
                }
            }
            picker.SetCheckedClips(checkedClips);

            ApplyChromeForSelection();
        }

        /// Creates an empty clip set in the remembered folder and selects it, so the catalog gains an entry the user edits in place.
        public void CreateAndSelectNewClipSet()
        {
            string folder = saveLocation.Recall();
            string assetPath = ClipSetSaveLocation.ResolveTargetAssetPath(folder, ClipSetSaveLocation.DefaultAssetName);
            ClipSetAsset newClipSet = ClipAssetUtility.CreateClipSet(assetPath);
            if (newClipSet == null)
            {
                ReportFailure("could not create the clip set asset at '" + assetPath + "'.");
                return;
            }

            RescanProject();
            SelectSet(newClipSet);
            EditorGUIUtility.PingObject(newClipSet);
        }

        private void ApplyChromeForSelection()
        {
            bool hasSelection = SelectedSet != null;
            editorTitleLabel.text = hasSelection ? SelectedSet.name : "Clip Set";
            openInEditorButton.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            noSelectionHintLabel.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
            editorContent.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void CommitNameFieldChange()
        {
            if (SelectedSet == null)
            {
                return;
            }

            CommitClipSetRename(SelectedSet, nameField.value);
        }

        // Duplicates ClipAssetUtility.RenameClip's guard/rename body — RenameClip takes a ClipAsset,
        // not a ClipSetAsset; a RenameClipSet utility method would be the better home for this.
        private void CommitClipSetRename(ClipSetAsset targetSet, string requestedName)
        {
            if (targetSet == null || string.IsNullOrWhiteSpace(requestedName) || requestedName == targetSet.name)
            {
                if (targetSet == SelectedSet)
                {
                    nameField.SetValueWithoutNotify(targetSet != null ? targetSet.name : string.Empty);
                }
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(targetSet);
            if (string.IsNullOrEmpty(assetPath))
            {
                ReportFailure("could not rename \"" + targetSet.name + "\": it has no asset path.");
                return;
            }

            string renameError = AssetDatabase.RenameAsset(assetPath, requestedName);
            if (!string.IsNullOrEmpty(renameError))
            {
                ReportFailure("could not rename \"" + targetSet.name + "\": " + renameError);
                if (targetSet == SelectedSet)
                {
                    nameField.SetValueWithoutNotify(targetSet.name);
                }
                return;
            }

            ClipSetAsset renamedSet = targetSet;
            RescanProject();
            SelectSet(renamedSet);
        }

        // The picker reports the rename rather than performing it, the same way it reports ticks.
        private void OnPickerClipRenameRequested(ClipAsset clip, string requestedName)
        {
            if (!ClipAssetUtility.RenameClip(clip, requestedName))
            {
                ReportFailure("could not rename \"" + (clip != null ? clip.name : "clip") + "\".");
            }

            // Rescan either way: on success the picker row needs the new name, and on failure it
            // needs the old one back, since the inline field left the label hidden mid-edit.
            RescanProject();
            if (SelectedSet != null)
            {
                SelectSet(SelectedSet);
            }
        }

        private void ReportFailure(string message)
        {
            resultLabel.style.color = new StyleColor(ToolkitPalette.Error);
            resultLabel.text = message;
            Debug.LogWarning(LogPrefix + message);
        }

        public void Dispose()
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }

            picker.ClipCheckedChanged -= OnPickerClipCheckedChanged;
            picker.ClipRenameRequested -= OnPickerClipRenameRequested;
            clipSetsList.selectionChanged -= OnClipSetsListSelectionChanged;
            clipSetsList.itemsSource = null;
            catalogClipSets.Clear();
            filteredClipSets.Clear();
            catalogClips.Clear();
            SelectedSet = null;
        }
    }
}
