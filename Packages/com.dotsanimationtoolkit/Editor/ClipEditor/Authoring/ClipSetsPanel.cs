// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
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
        private readonly List<ClipAsset> catalogClips = new List<ClipAsset>();

        private ToolkitCatalogColumn<ClipSetAsset> catalog;

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
            // in ClipEditorWindow.uxml) — the fixed pane (index 0) starts at 280px and the divider is
            // remembered across a tab hide/show.
            CoverPaneSplitView splitView = new CoverPaneSplitView("ClipSets.Catalog", 0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            splitView.Add(BuildCatalogColumn());
            splitView.Add(BuildEditorColumn());
            Add(splitView);

            ApplyChromeForSelection();
        }

        private VisualElement BuildCatalogColumn()
        {
            CatalogColumnOptions<ClipSetAsset> options = new CatalogColumnOptions<ClipSetAsset>
            {
                elementName = "clip-sets-catalog-column",
                namePrefix = "clip-sets",
                title = "Clip Sets",
                newButtonIconName = "Toolbar Plus",
                newButtonTooltip = null,
                refreshButtonIconName = "Refresh",
                refreshButtonTooltip = "Rescan the project for clip sets and clips",
                emptyProjectMessage = "No clip sets in this project yet. Press New.",
                emptySearchMessage = "No clip sets match your search.",
                secondLine = DescribeClipSet,
                allowRename = true,
                allowDelete = true,
            };
            catalog = new ToolkitCatalogColumn<ClipSetAsset>(options);
            catalog.NewRequested += CreateAndSelectNewClipSet;
            catalog.RefreshRequested += RescanProject;
            catalog.AssetSelected += SelectSet;
            catalog.RenameRequested += CommitClipSetRename;
            catalog.DeleteRequested += RequestDeleteClipSet;
            return catalog;
        }

        private static string DescribeClipSet(ClipSetAsset clipSet)
        {
            int clipCount = clipSet != null && clipSet.clips != null ? clipSet.clips.Count : 0;
            string assetPath = clipSet != null ? AssetDatabase.GetAssetPath(clipSet) : string.Empty;
            string folderPath = string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            return clipCount.ToString() + " clips" + (string.IsNullOrEmpty(folderPath) ? string.Empty : " · " + folderPath);
        }

        private void RequestDeleteClipSet(ClipSetAsset targetSet)
        {
            if (targetSet == null)
            {
                return;
            }

            string referenceSummary = AssetReferenceIndex.SummarizeForDialog(AssetReferenceIndex.ReferencesToClipSet(targetSet));
            string dialogBody = "Delete \"" + targetSet.name + "\"? Any actor profile or cutscene slot referencing it will lose those "
                + "clips. The asset moves to the OS trash, not permanently deleted.";
            if (referenceSummary.Length > 0)
            {
                dialogBody = referenceSummary + "\n\n" + dialogBody;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Clip Set",
                dialogBody,
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

                catalog.RefreshRows();
            }
        }

        // The set on show is where the next New lands when no folder has been remembered yet.
        private void RememberFallbackFolderOf(ClipSetAsset shownClipSet)
        {
            string fallbackFolder = "Assets";
            if (shownClipSet != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(shownClipSet);
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

            catalog.SetItems(catalogClipSets);
            catalog.Select(SelectedSet);

            picker.SetClips(catalogClips);
        }

        public void SelectSet(ClipSetAsset clipSet)
        {
            ShowSet(clipSet);
            selection?.SetClipSet(clipSet);
        }

        private void ShowSet(ClipSetAsset clipSet)
        {
            RememberFallbackFolderOf(clipSet);
            SelectedSet = clipSet;

            catalog.Select(clipSet);

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
            catalogClipSets.Clear();
            catalogClips.Clear();
            SelectedSet = null;
        }
    }
}
