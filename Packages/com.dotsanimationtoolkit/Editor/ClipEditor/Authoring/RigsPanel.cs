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
    /// <summary>The Rigs tab: a catalog of project rigs beside an editor for the selected rig's name, folder, source prefab, and targets.</summary>
    public sealed class RigsPanel : VisualElement, IDisposable
    {
        /// <summary>One renderer-bearing node found while scanning the source prefab.</summary>
        private sealed class CandidateRow
        {
            public string DisplayName;
            public string SourceNodePath;
            public uint TargetStableId;
            public uint TagId;
            public TargetKind Kind;
            public bool IsMissingNode;
            public Toggle ToggleControl;
            public Button TagButton;
            public Button KindButton;
            public VisualElement Box;
        }

        private const string SelectedBoxUssClassName = "toolkit-box--selected";

        /// <summary>Mirrors the floor <see cref="RigCatalogColumn"/> sets on itself; the inner split needs to know it.</summary>
        private const float CatalogMinimumWidth = 200f;

        private const float TargetsMinimumWidth = 360f;

        private RigCatalogColumn catalog;
        private Label targetsTitleLabel;
        private Button useInEditorButton;
        private Label noSelectionHintLabel;
        private VisualElement editorContent;
        private TextField rigNameField;
        private Label rigFolderLabel;
        private ObjectField sourcePrefabField;
        private Label candidateSummaryLabel;
        private VisualElement candidateContainer;
        private Label resultLabel;
        private RigSourcePreviewElement preview;
        private CandidateRow focusedRow;

        private readonly List<CandidateRow> candidateRows = new List<CandidateRow>();
        private readonly List<ClipAsset> catalogClips = new List<ClipAsset>();
        private readonly RigSaveLocation saveLocation = new RigSaveLocation();

        private ActiveAssetSelection selection;

        public RigAsset SelectedRig { get; private set; }

        /// <summary>Raised when the targets column header's "Use in Clip Editor" button is clicked.</summary>
        public event Action<RigAsset> UseInEditorRequested;

        /// <summary>Raised after an edit-mode add, remove, or retag has been written to the rig asset.</summary>
        public event Action<RigAsset> RigTargetsChanged;

        public RigsPanel()
        {
            // Written inline rather than through a stylesheet, matching VatBakePanel: this element
            // carries no stylesheet of its own, and a host's sheet has no reason to know the names
            // of rows built here.
            style.flexGrow = 1f;

            // Draggable dividers that remember where they were dragged across a tab hide/show.
            CoverPaneSplitView outerSplitView = new CoverPaneSplitView("Rigs.Targets", 0, 640f, TwoPaneSplitViewOrientation.Horizontal);
            outerSplitView.style.flexGrow = 1f;

            CoverPaneSplitView innerSplitView = new CoverPaneSplitView("Rigs.Catalog", 0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            innerSplitView.style.flexGrow = 1f;
            // The outer split's fixed pane IS this inner split, and hiding the tab drops the pair's
            // stored dimension back to "uninitialised" — without a floor of its own the outer split
            // then re-lays this out at zero and the tab comes back as nothing but the preview. The
            // columns' own minWidths cannot help: they sit inside this element, not on it. 560 is
            // their sum, so the floor costs nothing a drag could otherwise reach.
            innerSplitView.style.minWidth = CatalogMinimumWidth + TargetsMinimumWidth;

            catalog = new RigCatalogColumn();
            catalog.NewRequested += CreateAndSelectNewRig;
            catalog.RefreshRequested += RescanProject;
            catalog.RigSelected += SelectRig;
            catalog.RigRenameRequested += RenameRigAndRefresh;
            catalog.RigDeleteRequested += RequestDeleteRig;
            innerSplitView.Add(catalog);
            innerSplitView.Add(BuildTargetsColumn());

            outerSplitView.Add(innerSplitView);
            outerSplitView.Add(BuildPreviewPane());
            Add(outerSplitView);

            ApplyChromeForSelection();
            RescanProject();

            // An edit-mode tick writes straight to the asset, so Ctrl+Z changes the rig without
            // this panel touching it. Without this the row keeps showing the tick the undo removed.
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        /// <summary>Releases the preview's render utility and its copy of the prefab.</summary>
        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
            }

            preview?.Dispose();
        }

        /// <summary>Adopts the window's shared rig/clip-set selection, following it until re-bound or disposed.</summary>
        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
            }

            selection = sharedSelection;
            selection.RigChanged += OnSharedRigChanged;
            OnSharedRigChanged(selection.Rig);
        }

        private void OnUndoRedoPerformed()
        {
            if (SelectedRig == null)
            {
                return;
            }

            // Rebuilt wholesale rather than reconciled row by row: an undo can restore a target,
            // remove one, or change a tag, and re-reading the rig covers all three.
            BuildRowsForEditMode(SelectedRig);
            RaiseRigTargetsChanged();
        }

        public void SelectRig(RigAsset rig)
        {
            ShowRig(rig);
            selection?.SetRig(rig);
        }

        private void ShowRig(RigAsset rig)
        {
            SelectedRig = rig;
            catalog.SetSelectedRig(rig);
            // Without notify: a plain assignment would fire the field's own change callback and
            // immediately write this rig's prefab (or name) back onto itself.
            sourcePrefabField.SetValueWithoutNotify(rig != null ? rig.sourcePrefab : null);
            rigNameField.SetValueWithoutNotify(rig != null ? rig.name : string.Empty);
            ApplyChromeForSelection();
            BuildRowsForEditMode(rig);
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            if (rig == SelectedRig)
            {
                return;
            }

            ShowRig(rig);
        }

        /// <summary>Creates an empty rig in the remembered folder and selects it, so the catalog gains an entry the user edits in place.</summary>
        public void CreateAndSelectNewRig()
        {
            string folder = saveLocation.Recall();
            string assetPath = RigSaveLocation.ResolveTargetAssetPath(folder, RigSaveLocation.DefaultAssetName);
            RigAsset newRig = RigAssetUtility.CreateRig(assetPath, null, null);
            if (newRig == null)
            {
                ReportFailure("Could not create a new rig asset at \"" + assetPath + "\".");
                return;
            }

            RescanProject();
            SelectRig(newRig);
            EditorGUIUtility.PingObject(newRig);
        }

        private void ApplyChromeForSelection()
        {
            bool hasSelection = SelectedRig != null;
            targetsTitleLabel.text = hasSelection ? SelectedRig.name : "Rig";
            useInEditorButton.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            noSelectionHintLabel.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
            editorContent.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void RescanProject()
        {
            List<RigAsset> rigs = new List<RigAsset>();
            string[] rigAssetGuids = AssetDatabase.FindAssets("t:" + nameof(RigAsset));
            for (int guidIndex = 0; guidIndex < rigAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(rigAssetGuids[guidIndex]);
                RigAsset rig = AssetDatabase.LoadAssetAtPath<RigAsset>(assetPath);
                if (rig != null)
                {
                    rigs.Add(rig);
                }
            }

            catalogClips.Clear();
            string[] clipAssetGuids = AssetDatabase.FindAssets("t:" + nameof(ClipAsset));
            for (int guidIndex = 0; guidIndex < clipAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(clipAssetGuids[guidIndex]);
                ClipAsset clip = AssetDatabase.LoadAssetAtPath<ClipAsset>(assetPath);
                if (clip != null)
                {
                    catalogClips.Add(clip);
                }
            }

            rigs.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));
            catalogClips.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));

            // A fresh project has no saved-folder pref yet; landing beside whatever rigs already
            // exist beats defaulting to the Assets root.
            saveLocation.FallbackFolder = rigs.Count > 0
                ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(rigs[0])).Replace('\\', '/')
                : "Assets";
            rigFolderLabel.text = saveLocation.Recall();

            catalog.SetRigs(rigs);
        }

        private VisualElement BuildTargetsColumn()
        {
            VisualElement targetsColumn = new VisualElement { name = "rig-targets-column" };
            targetsColumn.style.flexGrow = 1f;
            targetsColumn.style.minWidth = TargetsMinimumWidth;
            targetsColumn.style.paddingTop = 8f;
            targetsColumn.style.paddingLeft = 10f;
            targetsColumn.style.paddingRight = 10f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            targetsTitleLabel = new Label("Rig") { name = "rig-targets-title" };
            targetsTitleLabel.AddToClassList("toolkit-pane-title");
            header.Add(targetsTitleLabel);

            useInEditorButton = ToolkitIcons.MakeIconTextButton(
                OnUseInEditorClicked, "editicon.sml", null, "Use in Clip Editor");
            useInEditorButton.name = "rig-use-in-editor-button";
            header.Add(useInEditorButton);

            targetsColumn.Add(header);

            noSelectionHintLabel = new Label("Select a rig, or press New to make one.")
            {
                name = "rig-no-selection-hint"
            };
            noSelectionHintLabel.style.whiteSpace = WhiteSpace.Normal;
            noSelectionHintLabel.style.marginTop = 8f;
            targetsColumn.Add(noSelectionHintLabel);

            editorContent = new VisualElement { name = "rig-editor-content" };

            rigNameField = new TextField("Name") { name = "rig-name-field" };
            // Commit on blur/Enter, not on every keystroke — renaming an asset per character
            // would create a file operation per letter.
            rigNameField.RegisterCallback<FocusOutEvent>(focusOutEvent => CommitRigNameChange());
            rigNameField.RegisterCallback<KeyDownEvent>(keyDownEvent =>
            {
                if (keyDownEvent.keyCode == KeyCode.Return)
                {
                    CommitRigNameChange();
                }
            });
            editorContent.Add(rigNameField);

            VisualElement folderRow = new VisualElement { name = "rig-folder-row" };
            folderRow.style.flexDirection = FlexDirection.Row;
            folderRow.style.alignItems = Align.Center;
            folderRow.style.marginBottom = 4f;

            rigFolderLabel = new Label(saveLocation.Recall()) { name = "rig-folder-label" };
            rigFolderLabel.style.flexGrow = 1f;
            rigFolderLabel.style.overflow = Overflow.Hidden;
            rigFolderLabel.style.textOverflow = TextOverflow.Ellipsis;
            rigFolderLabel.style.whiteSpace = WhiteSpace.NoWrap;
            folderRow.Add(rigFolderLabel);

            Button rigFolderButton = new Button(OnRigFolderButtonClicked)
            {
                text = "…",
                name = "rig-folder-button",
                tooltip = "Where the next New rig is created. Does not move the selected rig."
            };
            rigFolderButton.style.marginLeft = 4f;
            folderRow.Add(rigFolderButton);

            editorContent.Add(folderRow);

            sourcePrefabField = new ObjectField("Source Prefab")
            {
                name = "rig-source-prefab-field",
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = "The prefab the rig previews from and the VAT bake will sample. "
                    + "Its hierarchy is scanned below for nodes to offer as rig targets."
            };
            sourcePrefabField.RegisterValueChangedCallback(changeEvent =>
            {
                if (SelectedRig == null)
                {
                    return;
                }

                // Targets are left exactly as they are; nodes that no longer exist just become
                // missing rows through the ordinary BuildForRig path.
                GameObject newSourcePrefab = changeEvent.newValue as GameObject;
                if (RigAssetUtility.SetRigSourcePrefab(SelectedRig, newSourcePrefab))
                {
                    BuildRowsForEditMode(SelectedRig);
                    RaiseRigTargetsChanged();
                }
            });
            editorContent.Add(sourcePrefabField);

            editorContent.Add(BuildHeading("Targets"));

            candidateSummaryLabel = new Label(
                "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.");
            candidateSummaryLabel.style.whiteSpace = WhiteSpace.Normal;
            editorContent.Add(candidateSummaryLabel);

            ScrollView candidateScroll = new ScrollView();
            candidateScroll.style.flexGrow = 1f;
            candidateScroll.style.marginTop = 4f;
            candidateContainer = candidateScroll.contentContainer;
            editorContent.Add(candidateScroll);

            targetsColumn.Add(editorContent);

            resultLabel = new Label(string.Empty);
            resultLabel.style.whiteSpace = WhiteSpace.Normal;
            resultLabel.style.marginTop = 8f;
            resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            targetsColumn.Add(resultLabel);

            return targetsColumn;
        }

        private VisualElement BuildPreviewPane()
        {
            VisualElement previewPane = new VisualElement { name = "new-rig-preview-pane" };
            previewPane.style.flexGrow = 1f;
            previewPane.style.minWidth = 320f;

            VisualElement previewHeader = new VisualElement();
            previewHeader.AddToClassList("toolkit-pane-header");
            Label previewTitle = new Label("Preview");
            previewTitle.AddToClassList("toolkit-pane-title");
            previewHeader.Add(previewTitle);
            previewPane.Add(previewHeader);

            preview = new RigSourcePreviewElement();
            preview.style.flexGrow = 1f;
            previewPane.Add(preview);

            return previewPane;
        }

        // Opens the searchable tag picker for one candidate row — the same VocabularyPicker every
        // other tag surface in this package uses, so reusing an existing tag works the same way here.
        private void OpenRowTagPicker(CandidateRow row, Button anchor)
        {
            TargetTagRegistry tagRegistry = VocabularyRegistryProvider.TargetTags;
            VocabularyPicker.Open(
                this,
                anchor,
                tagRegistry,
                tagRegistry,
                VocabularyPickerConfig.ForTargetTags(tagRegistry),
                chosenTagId =>
                {
                    row.TagId = chosenTagId;
                    RefreshTagButtonText(row);

                    // A row that is not currently a target cannot be tagged into the asset; its
                    // tag button is already disabled while unticked, so this only guards.
                    if (SelectedRig != null && row.TargetStableId != 0u)
                    {
                        RigAssetUtility.SetTargetTag(SelectedRig, row.TargetStableId, chosenTagId);
                        RaiseRigTargetsChanged();
                    }
                },
                () =>
                {
                    // The registry changed underneath every open row (a tag renamed or newly
                    // created), not just this one's.
                    for (int rowIndex = 0; rowIndex < candidateRows.Count; rowIndex++)
                    {
                        RefreshTagButtonText(candidateRows[rowIndex]);
                    }
                });
        }

        private void RefreshTagButtonText(CandidateRow row)
        {
            if (row.TagButton == null)
            {
                return;
            }
            if (row.TagId == 0u)
            {
                row.TagButton.text = "Tag: (none)";
                return;
            }
            TargetTagRegistry tagRegistry = VocabularyRegistryProvider.TargetTags;
            string tagName = tagRegistry != null ? tagRegistry.FindName(row.TagId) : null;
            row.TagButton.text = tagName != null
                ? "Tag: " + tagName
                : "Tag: (unresolved 0x" + row.TagId.ToString("X8") + ")";
        }

        private void OpenRowKindPicker(CandidateRow row, Button anchor)
        {
            GenericDropdownMenu menu = new GenericDropdownMenu();
            menu.AddItem("Quad", row.Kind == TargetKind.Quad, () => SetRowKind(row, TargetKind.Quad));
            menu.AddItem("VAT Mesh", row.Kind == TargetKind.VatMesh, () => SetRowKind(row, TargetKind.VatMesh));
            menu.AddItem(
                "Flipbook", row.Kind == TargetKind.FlipbookPlane, () => SetRowKind(row, TargetKind.FlipbookPlane));
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        private void SetRowKind(CandidateRow row, TargetKind kind)
        {
            row.Kind = kind;
            RefreshKindButtonText(row);

            // A row that is not currently a target cannot have its kind written into the asset;
            // its kind button is already disabled while unticked, so this only guards.
            if (SelectedRig != null && row.TargetStableId != 0u)
            {
                RigAssetUtility.SetTargetKind(SelectedRig, row.TargetStableId, kind);
            }
        }

        private void RefreshKindButtonText(CandidateRow row)
        {
            if (row.KindButton == null)
            {
                return;
            }
            switch (row.Kind)
            {
                case TargetKind.VatMesh:
                    row.KindButton.text = "Kind: VAT Mesh";
                    return;
                case TargetKind.FlipbookPlane:
                    row.KindButton.text = "Kind: Flipbook";
                    return;
                default:
                    row.KindButton.text = "Kind: Quad";
                    return;
            }
        }

        private static Label BuildHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
        }

        // Lists what BuildForRig reports for the selected rig, ticking the ones already a rig
        // target. Untick/re-tag guarding against overwriting the rig is later work.
        private void BuildRowsForEditMode(RigAsset rig)
        {
            candidateContainer.Clear();
            candidateRows.Clear();
            focusedRow = null;

            preview.ShowPrefab(rig != null ? rig.sourcePrefab : null);

            if (rig == null)
            {
                candidateSummaryLabel.text = "No rig selected.";
                return;
            }

            if (rig.sourcePrefab == null)
            {
                candidateSummaryLabel.text =
                    "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.";
                return;
            }

            List<RigTargetRow> rows = RigTargetRowBuilder.BuildForRig(rig);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                RigTargetRow row = rows[rowIndex];
                BuildCandidateRow(row, row.IsTarget);
            }

            candidateSummaryLabel.text = candidateRows.Count.ToString() + " node(s) in \"" + rig.name + "\".";
        }

        private void BuildCandidateRow(RigTargetRow sourceRow, bool ticked)
        {
            string rowTitleText = sourceRow.SourceNodePath;
            if (sourceRow.IsMissingNode)
            {
                rowTitleText = string.IsNullOrEmpty(sourceRow.SourceNodePath)
                    ? "⚠ " + sourceRow.DisplayName + " (no node)"
                    : "⚠ " + sourceRow.SourceNodePath + " (missing from prefab)";
            }

            Toggle rowToggle = new Toggle(rowTitleText) { value = ticked };
            rowToggle.tooltip = sourceRow.SourceNodePath;
            rowToggle.style.flexGrow = 1f;
            rowToggle.style.flexShrink = 1f;
            rowToggle.style.overflow = Overflow.Hidden;
            // A deep node path is longer than the column is wide. Left to grow it pushes the tag
            // button out of the row and puts a horizontal scrollbar under the whole list.
            rowToggle.labelElement.style.minWidth = 0f;
            rowToggle.labelElement.style.flexShrink = 1f;
            rowToggle.labelElement.style.overflow = Overflow.Hidden;
            rowToggle.labelElement.style.textOverflow = TextOverflow.Ellipsis;
            rowToggle.labelElement.style.whiteSpace = WhiteSpace.NoWrap;

            Button tagButton = new Button { text = "Tag: (none)" };
            tagButton.style.flexShrink = 0f;
            tagButton.style.minWidth = 90f;
            tagButton.style.marginLeft = 4f;
            // An unticked node is not becoming a target, so its tag would go nowhere. Greying
            // the button is what separates the animated parts from the ones just listed.
            tagButton.SetEnabled(ticked);

            Button kindButton = new Button { text = "Kind: Quad" };
            kindButton.style.flexShrink = 0f;
            kindButton.style.minWidth = 100f;
            kindButton.style.marginLeft = 4f;
            // An unticked node is not becoming a target, so its kind would go nowhere. Greying
            // the button is what separates the animated parts from the ones just listed.
            kindButton.SetEnabled(ticked);

            VisualElement candidateBox = new VisualElement();
            candidateBox.AddToClassList("toolkit-box");

            VisualElement rowContainer = new VisualElement();
            rowContainer.AddToClassList("toolkit-box__header");
            rowContainer.Add(rowToggle);
            rowContainer.Add(kindButton);
            rowContainer.Add(tagButton);
            candidateBox.Add(rowContainer);
            candidateContainer.Add(candidateBox);

            CandidateRow row = new CandidateRow
            {
                DisplayName = sourceRow.DisplayName,
                SourceNodePath = sourceRow.SourceNodePath,
                TargetStableId = sourceRow.TargetStableId,
                TagId = sourceRow.TagId,
                Kind = sourceRow.Kind,
                IsMissingNode = sourceRow.IsMissingNode,
                ToggleControl = rowToggle,
                TagButton = tagButton,
                KindButton = kindButton,
                Box = candidateBox
            };
            RefreshTagButtonText(row);
            RefreshKindButtonText(row);
            tagButton.clicked += () => OpenRowTagPicker(row, tagButton);
            kindButton.clicked += () => OpenRowKindPicker(row, kindButton);
            rowToggle.RegisterValueChangedCallback(
                changeEvent => OnRowToggleChanged(row, rowToggle, tagButton, kindButton, changeEvent.newValue));
            // TrickleDown, so clicking the toggle or the tag button still shows which node the
            // row means rather than being swallowed by the control that was hit.
            candidateBox.RegisterCallback<PointerDownEvent>(
                pointerEvent => FocusRow(row), TrickleDown.TrickleDown);
            candidateRows.Add(row);
            // The preview has no copy of a missing node, so it is never told about one.
            if (!row.IsMissingNode)
            {
                preview.SetNodeIncluded(sourceRow.SourceNodePath, ticked);
            }
        }

        // Edit mode writes the rig asset the moment a row is ticked or unticked; create mode keeps
        // the tick in memory until Create Rig runs.
        private void OnRowToggleChanged(
            CandidateRow row, Toggle rowToggle, Button tagButton, Button kindButton, bool isChecked)
        {
            tagButton.SetEnabled(isChecked);
            kindButton.SetEnabled(isChecked);
            if (!row.IsMissingNode)
            {
                preview.SetNodeIncluded(row.SourceNodePath, isChecked);
            }

            if (SelectedRig == null)
            {
                return;
            }

            if (isChecked)
            {
                RigTargetDefinition newTarget =
                    RigAssetUtility.AddTargetToRig(SelectedRig, row.SourceNodePath, row.DisplayName);
                if (newTarget != null)
                {
                    row.TargetStableId = newTarget.Id.Value;
                }
                RaiseRigTargetsChanged();
                return;
            }

            List<ClipAsset> boundClips =
                RigTargetReferenceResolver.FindClipsBoundToTarget(catalogClips, row.TargetStableId, row.TagId);
            if (boundClips.Count > 0)
            {
                bool confirmedRemoval = EditorUtility.DisplayDialog(
                    "Remove Rig Target",
                    "\"" + row.DisplayName + "\" is animated by " + RigTargetReferenceResolver.DescribeClips(boundClips)
                        + ". Those tracks will be skipped when a clip plays on this rig. Remove it anyway?",
                    "Remove",
                    "Cancel");
                if (!confirmedRemoval)
                {
                    // SetValueWithoutNotify, not value = true: a plain set re-enters this same
                    // callback and asks the owner the same question forever.
                    rowToggle.SetValueWithoutNotify(true);
                    tagButton.SetEnabled(true);
                    kindButton.SetEnabled(true);
                    if (!row.IsMissingNode)
                    {
                        preview.SetNodeIncluded(row.SourceNodePath, true);
                    }
                    return;
                }
            }

            RigAssetUtility.RemoveTargetFromRig(SelectedRig, row.TargetStableId);
            row.TargetStableId = 0u;
            RaiseRigTargetsChanged();
        }

        private void OnUseInEditorClicked()
        {
            if (UseInEditorRequested != null)
            {
                UseInEditorRequested(SelectedRig);
            }
        }

        // Refreshes the catalog row's own info line (target count) in place rather than rebuilding
        // this panel's target row list, which would be re-entering the toggle callback's own row.
        private void RaiseRigTargetsChanged()
        {
            if (RigTargetsChanged != null)
            {
                RigTargetsChanged(SelectedRig);
            }
            catalog.SetSelectedRig(SelectedRig);
        }

        private void FocusRow(CandidateRow row)
        {
            if (focusedRow != null && focusedRow.Box != null)
            {
                focusedRow.Box.RemoveFromClassList(SelectedBoxUssClassName);
            }
            focusedRow = row;
            if (row == null)
            {
                preview.ClearFocus();
                return;
            }
            if (row.Box != null)
            {
                row.Box.AddToClassList(SelectedBoxUssClassName);
            }
            // The preview has no copy of a missing node's transform to focus on.
            if (!row.IsMissingNode)
            {
                preview.FocusNode(row.SourceNodePath);
            }
        }

        private void CommitRigNameChange()
        {
            if (SelectedRig == null)
            {
                return;
            }

            RenameRigAndRefresh(SelectedRig, rigNameField.value);
        }

        /// <summary>Shared by the Name field's commit and the catalog row's Rename context-menu action.</summary>
        private void RenameRigAndRefresh(RigAsset rig, string requestedName)
        {
            if (rig == null || requestedName == rig.name)
            {
                return;
            }

            if (RigAssetUtility.RenameRig(rig, requestedName))
            {
                RescanProject();
                SelectRig(rig);
                RaiseRigTargetsChanged();
            }
            else
            {
                ReportFailure("Could not rename to \"" + requestedName + "\".");
                RescanProject();
                SelectRig(rig);
            }
        }

        private void RequestDeleteRig(RigAsset rig)
        {
            if (rig == null)
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Rig",
                "Delete \"" + rig.name + "\"? Any actor profile or clip bound to its targets will lose "
                    + "them. The asset moves to the OS trash, not permanently deleted.",
                "Delete",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            bool wasSelected = SelectedRig == rig;
            if (!RigAssetUtility.DeleteRig(rig))
            {
                ReportFailure("Could not delete \"" + rig.name + "\".");
                return;
            }

            // The panel and its preview both hold a reference to this rig (and the preview a copy
            // of its prefab) -- clearing the selection before rescanning keeps the editor column
            // from pointing at an asset that no longer exists.
            if (wasSelected)
            {
                ShowRig(null);
                selection?.SetRig(null);
            }

            RescanProject();
        }

        private void OnRigFolderButtonClicked()
        {
            string currentFolder = saveLocation.Recall();
            string projectAssetsAbsolutePath = Application.dataPath;
            string startingAbsoluteFolder = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(projectAssetsAbsolutePath), currentFolder));

            string pickedAbsoluteFolder =
                EditorUtility.OpenFolderPanel("New Rig Folder", startingAbsoluteFolder, string.Empty);
            if (string.IsNullOrEmpty(pickedAbsoluteFolder))
            {
                return;
            }

            string projectRelativeFolder;
            if (RigSaveLocation.TryMakeProjectRelative(
                pickedAbsoluteFolder, projectAssetsAbsolutePath, out projectRelativeFolder))
            {
                saveLocation.Remember(projectRelativeFolder);
                rigFolderLabel.text = projectRelativeFolder;
            }
            else
            {
                ReportFailure("The chosen folder must be inside this project's Assets folder.");
            }
        }

        private void ReportFailure(string message)
        {
            resultLabel.style.color = new StyleColor(new Color(0.95f, 0.55f, 0.55f));
            resultLabel.text = message;
            Debug.LogWarning("[DOTS Animation Toolkit] Rigs: " + message);
        }
    }
}
