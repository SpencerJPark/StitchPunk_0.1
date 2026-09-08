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
        private readonly List<ClipAsset> catalogClips = new List<ClipAsset>();

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
            style.flexDirection = FlexDirection.Row;

            Add(BuildCatalogColumn());
            Add(BuildEditorColumn());

            RefreshCatalogEmptyState();
            RefreshEditorForMode();
        }

        private VisualElement BuildCatalogColumn()
        {
            VisualElement catalogColumn = new VisualElement { name = "clip-sets-catalog-column" };
            catalogColumn.style.width = 280f;
            catalogColumn.style.flexShrink = 0f;
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

            clipSetsList = new ListView();
            clipSetsList.name = "clip-sets-list";
            clipSetsList.fixedItemHeight = 44f;
            clipSetsList.selectionType = SelectionType.Single;
            clipSetsList.style.flexGrow = 1f;
            clipSetsList.makeItem = MakeClipSetRow;
            clipSetsList.bindItem = BindClipSetRow;
            clipSetsList.itemsSource = catalogClipSets;
            clipSetsList.selectionChanged += OnClipSetsListSelectionChanged;
            catalogColumn.Add(clipSetsList);

            catalogEmptyLabel = new Label("No clip sets in this project yet. Press New.");
            catalogEmptyLabel.AddToClassList("clip-editor__hint");
            catalogColumn.Add(catalogEmptyLabel);

            return catalogColumn;
        }

        private static VisualElement MakeClipSetRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-box");
            row.style.marginTop = 2f;
            row.style.marginBottom = 2f;
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

            return row;
        }

        private void BindClipSetRow(VisualElement element, int index)
        {
            if (index < 0 || index >= catalogClipSets.Count)
            {
                return;
            }

            ClipSetAsset clipSet = catalogClipSets[index];
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

            clipSetsList.itemsSource = catalogClipSets;
            clipSetsList.Rebuild();
            RefreshCatalogEmptyState();

            if (SelectedSet != null && !catalogClipSets.Contains(SelectedSet))
            {
                Mode = EditorMode.None;
                SelectedSet = null;
                RefreshEditorForMode();
            }

            picker.SetClips(catalogClips);
        }

        private void RefreshCatalogEmptyState()
        {
            bool isEmpty = catalogClipSets.Count == 0;
            clipSetsList.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            catalogEmptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SelectSet(ClipSetAsset clipSet)
        {
            Mode = EditorMode.Edit;
            SelectedSet = clipSet;

            clipSetsList.SetSelectionWithoutNotify(clipSet != null ? new List<int> { catalogClipSets.IndexOf(clipSet) } : new List<int>());
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
            catalogClips.Clear();
            SelectedSet = null;
        }
    }
}
