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
    /// <summary>Browse, create, and edit clip sets: a catalog list plus a create form and clip picker for the selected set.</summary>
    public sealed class ClipSetsPanel : VisualElement, IDisposable
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] Clip Sets: ";

        public enum EditorMode
        {
            None,
            Create,
            Edit
        }

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

        private VisualElement createFormElement;
        private TextField nameField;
        private Label folderLabel;
        private Label targetPathLabel;

        private ClipPickerListElement picker;

        private Toggle loadToggle;
        private Button createButton;
        private Label editHintLabel;
        private Label resultLabel;

        public event Action Closed;
        public event Action<ClipSetAsset, bool> ClipSetCreated;
        public event Action<ClipSetAsset> OpenInEditorRequested;
        public event Action<ClipSetAsset> SetClipsChanged;

        public EditorMode Mode { get; private set; } = EditorMode.None;

        public ClipSetAsset SelectedSet { get; private set; }

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
            RefreshEditorForMode();
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

            Button newButton = ToolkitIcons.MakeIconTextButton(BeginCreate, "Toolbar Plus", null, "New");
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
            catalogSearchField.RegisterValueChangedCallback(OnCatalogSearchTextChanged);
            catalogColumn.Add(catalogSearchField);

            clipSetsList = new ListView();
            clipSetsList.name = "clip-sets-list";
            clipSetsList.fixedItemHeight = 52f;
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
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-box");
            row.style.marginTop = 6f;
            row.style.marginBottom = 6f;
            row.style.marginLeft = 4f;
            row.style.marginRight = 4f;

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

            return row;
        }

        private void PopulateClipSetRowContextMenu(ContextualMenuPopulateEvent populateEvent, VisualElement row)
        {
            ClipSetAsset targetSet = row.userData as ClipSetAsset;
            if (targetSet == null)
            {
                return;
            }

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
                Mode = EditorMode.None;
                SelectedSet = null;
                RefreshEditorForMode();
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
            element.userData = clipSet;

            Label titleLabel = element.Q<Label>("clip-set-row-title");
            titleLabel.text = clipSet != null ? clipSet.name : string.Empty;

            Label infoLabel = element.Q<Label>("clip-set-row-info");
            int clipCount = clipSet != null && clipSet.clips != null ? clipSet.clips.Count : 0;
            string assetPath = clipSet != null ? AssetDatabase.GetAssetPath(clipSet) : string.Empty;
            string folderPath = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            infoLabel.text = clipCount.ToString() + " clips" + (string.IsNullOrEmpty(folderPath) ? string.Empty : " · " + folderPath);

            element.EnableInClassList("toolkit-box--selected", clipSet == SelectedSet);
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

            createFormElement = BuildCreateForm();
            editorColumn.Add(createFormElement);

            picker = new ClipPickerListElement();
            picker.name = "clip-picker";
            picker.style.flexGrow = 1f;
            picker.ClipCheckedChanged += OnPickerClipCheckedChanged;
            editorColumn.Add(picker);

            loadToggle = new Toggle("Load this set into the editor");
            loadToggle.name = "clip-set-load-toggle";
            loadToggle.value = true;
            editorColumn.Add(loadToggle);

            createButton = new Button(Create) { text = "Create Clip Set" };
            createButton.name = "clip-set-create-button";
            createButton.style.height = 28f;
            editorColumn.Add(createButton);

            editHintLabel = new Label("Ticks apply to the set immediately. Ctrl+Z undoes.");
            editHintLabel.AddToClassList("clip-editor__hint");
            editorColumn.Add(editHintLabel);

            resultLabel = new Label();
            resultLabel.name = "clip-sets-result-label";
            resultLabel.style.whiteSpace = WhiteSpace.Normal;
            resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            editorColumn.Add(resultLabel);

            return editorColumn;
        }

        private VisualElement BuildCreateForm()
        {
            VisualElement form = new VisualElement { name = "clip-set-create-form" };

            nameField = new TextField("Name");
            nameField.name = "clip-set-name-field";
            nameField.isDelayed = false;
            nameField.RegisterValueChangedCallback(OnNameFieldChanged);
            form.Add(nameField);

            VisualElement folderRow = new VisualElement();
            folderRow.style.flexDirection = FlexDirection.Row;

            Label folderCaption = new Label("Save Folder");
            folderRow.Add(folderCaption);

            folderLabel = new Label();
            folderLabel.name = "clip-set-folder-label";
            folderLabel.AddToClassList("toolkit-box__label");
            folderRow.Add(folderLabel);

            Button folderButton = new Button(OnFolderButtonClicked) { text = "…" };
            folderButton.name = "clip-set-folder-button";
            folderRow.Add(folderButton);

            form.Add(folderRow);

            targetPathLabel = new Label();
            targetPathLabel.name = "clip-set-target-path-label";
            targetPathLabel.AddToClassList("clip-editor__hint");
            form.Add(targetPathLabel);

            return form;
        }

        private void OnNameFieldChanged(ChangeEvent<string> changeEvent)
        {
            RefreshTargetPathLabel();
        }

        private void OnFolderButtonClicked()
        {
            string currentFolder = saveLocation.Recall();
            string projectAssetsAbsolutePath = Application.dataPath;
            string startingAbsoluteFolder = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(projectAssetsAbsolutePath), currentFolder));

            string pickedAbsoluteFolder = EditorUtility.OpenFolderPanel("Save Clip Set In", startingAbsoluteFolder, string.Empty);
            if (string.IsNullOrEmpty(pickedAbsoluteFolder))
            {
                return;
            }

            string projectRelativeFolder;
            if (ClipSetSaveLocation.TryMakeProjectRelative(pickedAbsoluteFolder, projectAssetsAbsolutePath, out projectRelativeFolder))
            {
                saveLocation.Remember(projectRelativeFolder);
                folderLabel.text = projectRelativeFolder;
                RefreshTargetPathLabel();
            }
            else
            {
                ReportFailure("the chosen folder must be inside this project's Assets folder.");
            }
        }

        private void RefreshTargetPathLabel()
        {
            string folder = saveLocation.Recall();
            targetPathLabel.text = "Will create " + ClipSetSaveLocation.ResolveTargetAssetPath(folder, nameField.value);
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
            if (Mode != EditorMode.Edit || SelectedSet == null)
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

            if (openClipSet != null && Mode == EditorMode.None)
            {
                SelectSet(openClipSet);
            }
        }

        private void RescanProject()
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
                Mode = EditorMode.None;
                SelectedSet = null;
                RefreshEditorForMode();
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
            Mode = EditorMode.Edit;
            SelectedSet = clipSet;

            clipSetsList.SetSelectionWithoutNotify(
                clipSet != null && filteredClipSets.Contains(clipSet)
                    ? new List<int> { filteredClipSets.IndexOf(clipSet) }
                    : new List<int>());
            clipSetsList.Rebuild();

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

            RefreshEditorForMode();
        }

        public void BeginCreate()
        {
            Mode = EditorMode.Create;
            SelectedSet = null;

            clipSetsList.SetSelectionWithoutNotify(new List<int>());
            clipSetsList.Rebuild();

            nameField.SetValueWithoutNotify(ClipSetSaveLocation.DefaultAssetName);
            picker.SetCheckedClips(Array.Empty<ClipAsset>());

            folderLabel.text = saveLocation.Recall();
            RefreshTargetPathLabel();

            RefreshEditorForMode();
        }

        private void RefreshEditorForMode()
        {
            switch (Mode)
            {
                case EditorMode.Create:
                    editorTitleLabel.text = "New Clip Set";
                    openInEditorButton.style.display = DisplayStyle.None;
                    createFormElement.style.display = DisplayStyle.Flex;
                    loadToggle.style.display = DisplayStyle.Flex;
                    createButton.style.display = DisplayStyle.Flex;
                    editHintLabel.style.display = DisplayStyle.None;
                    break;
                case EditorMode.Edit:
                    editorTitleLabel.text = SelectedSet != null ? SelectedSet.name : string.Empty;
                    openInEditorButton.style.display = DisplayStyle.Flex;
                    createFormElement.style.display = DisplayStyle.None;
                    loadToggle.style.display = DisplayStyle.None;
                    createButton.style.display = DisplayStyle.None;
                    editHintLabel.style.display = DisplayStyle.Flex;
                    break;
                default:
                    editorTitleLabel.text = "Select a clip set or press New.";
                    openInEditorButton.style.display = DisplayStyle.None;
                    createFormElement.style.display = DisplayStyle.None;
                    loadToggle.style.display = DisplayStyle.None;
                    createButton.style.display = DisplayStyle.None;
                    editHintLabel.style.display = DisplayStyle.None;
                    break;
            }
        }

        private void Create()
        {
            string folder = saveLocation.Recall();
            if (!AssetDatabase.IsValidFolder(folder))
            {
                ReportFailure("save folder '" + folder + "' is not valid.");
                return;
            }

            string assetPath = ClipSetSaveLocation.ResolveTargetAssetPath(folder, nameField.value);
            ClipSetAsset newSet = ClipAssetUtility.CreateClipSet(assetPath);
            if (newSet == null)
            {
                ReportFailure("could not create the clip set asset at '" + assetPath + "'.");
                return;
            }

            int addedClipCount = 0;
            foreach (ClipAsset clip in picker.CheckedClips)
            {
                if (ClipAssetUtility.AddExistingClipToSet(newSet, clip))
                {
                    addedClipCount++;
                }
            }

            AssetDatabase.SaveAssets();
            saveLocation.Remember(folder);
            EditorGUIUtility.PingObject(newSet);

            // Data-driven outcome colour, not a layout style: an exception to the inline-styles-are-layout-only rule.
            resultLabel.style.color = new StyleColor(ToolkitPalette.Clean);
            resultLabel.text = "Created \"" + newSet.name + "\" with " + addedClipCount.ToString() + " clip(s) at " + assetPath + ".";

            RescanProject();
            SelectSet(newSet);

            if (ClipSetCreated != null)
            {
                ClipSetCreated(newSet, loadToggle.value);
            }

            if (loadToggle.value && Closed != null)
            {
                Closed();
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
            picker.ClipCheckedChanged -= OnPickerClipCheckedChanged;
            clipSetsList.selectionChanged -= OnClipSetsListSelectionChanged;
            clipSetsList.itemsSource = null;
            catalogClipSets.Clear();
            filteredClipSets.Clear();
            catalogClips.Clear();
            SelectedSet = null;
        }
    }
}
