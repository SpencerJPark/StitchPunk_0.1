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

        private const string SelectedRowUssClassName = "toolkit-list-row--selected";

        /// <summary>Mirrors the floor <see cref="RigCatalogColumn"/> sets on itself; the inner split needs to know it.</summary>
        private const float CatalogMinimumWidth = 200f;

        private const float TargetsMinimumWidth = 360f;

        private RigCatalogColumn catalog;
        private Label targetsTitleLabel;
        private Button useInEditorButton;
        private VisualElement noSelectionHintLabel;
        private VisualElement editorContent;
        private TextField rigNameField;
        private PathPickerRowElement rigFolderRow;
        private ObjectField sourcePrefabField;
        private Label candidateSummaryLabel;
        private Label targetsCountBadge;
        private VisualElement candidateContainer;
        private Label resultLabel;
        private RigSourcePreviewElement preview;
        private CandidateRow focusedRow;
        private VisualElement targetCard;
        private Label targetNameLabel;
        private Label targetNodeLabel;
        private Button targetKindButton;
        private Button targetTagButton;
        private Toggle targetFacesDirectionToggle;
        private Label targetUntickedHint;

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
            rigFolderRow.Path = saveLocation.Recall();

            catalog.SetRigs(rigs);
        }

        private VisualElement BuildTargetsColumn()
        {
            VisualElement targetsColumn = new VisualElement { name = "rig-targets-column" };
            targetsColumn.AddToClassList("toolkit-column");
            targetsColumn.style.minWidth = TargetsMinimumWidth;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            targetsTitleLabel = new Label("Rig") { name = "rig-targets-title" };
            targetsTitleLabel.AddToClassList("toolkit-pane-title");
            header.Add(targetsTitleLabel);

            useInEditorButton = ToolkitChrome.MakePrimaryAction(
                OnUseInEditorClicked, "editicon.sml", "Make this the Clip Editor's rig", "Use in Clip Editor");
            useInEditorButton.name = "rig-use-in-editor-button";
            header.Add(useInEditorButton);

            targetsColumn.Add(header);

            // RG6: same element name so line 201's toggle and any test locator still find it, but a
            // designed empty state (R13) instead of a bare sentence; reuses the catalog's own New
            // handler rather than duplicating it.
            noSelectionHintLabel = ToolkitChrome.MakeEmptyState(
                "rig-no-selection-hint",
                "No rig selected",
                "Pick a rig on the left to see and edit its targets.",
                "New rig",
                CreateAndSelectNewRig);
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

            rigFolderRow = new PathPickerRowElement(
                "Folder", "Where the next New rig is created. Does not move the selected rig.")
            {
                name = "rig-folder-row",
                Path = saveLocation.Recall()
            };
            rigFolderRow.BrowseRequested += OnRigFolderButtonClicked;
            editorContent.Add(rigFolderRow);

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

            VisualElement targetsHeader = ToolkitChrome.MakePaneHeader(
                "Targets", out _, out VisualElement targetsHeaderActions);
            targetsCountBadge = ToolkitChrome.MakeBadge(string.Empty, ToolkitStatusTone.Neutral);
            targetsHeaderActions.Add(targetsCountBadge);
            editorContent.Add(targetsHeader);

            // RG3: the count now lives only in the badge above; this label says which rig (or, with
            // no rig/prefab yet, the same guidance it always gave) so nothing is said twice (R03).
            candidateSummaryLabel = ToolkitChrome.MakeHint(
                "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.");
            editorContent.Add(candidateSummaryLabel);

            ScrollView candidateScroll = new ScrollView();
            candidateScroll.style.flexGrow = 1f;
            candidateScroll.style.marginTop = 4f;
            // RG5: the middle column stays window-coloured, but its list body must not — the base
            // class's -10px pull-back matches this column's own 10px inset exactly (ClipEditorWindow.uss
            // .toolkit-column), so no --flush modifier is needed here.
            candidateScroll.AddToClassList("toolkit-list-surface");
            candidateContainer = candidateScroll.contentContainer;
            editorContent.Add(candidateScroll);

            // Fixed-content card beside the flexGrow scroll region above; flexShrink 0 keeps it
            // from being squashed into an overlap when the column runs short (recorded trap).
            targetCard = ToolkitChrome.MakeCard("rig-target-card", "Target", out VisualElement targetCardBody, out _);
            targetCard.style.flexShrink = 0f;

            targetNameLabel = new Label();
            targetCardBody.Add(ToolkitChrome.MakePropertyRow("Name", targetNameLabel, "The rig target's display name."));

            targetNodeLabel = new Label();
            targetNodeLabel.style.overflow = Overflow.Hidden;
            targetNodeLabel.style.textOverflow = TextOverflow.Ellipsis;
            targetNodeLabel.style.whiteSpace = WhiteSpace.NoWrap;
            targetCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Node", targetNodeLabel, "The source prefab node this target reads from; full path in the tooltip."));

            targetKindButton = new Button { text = "Kind: Quad", name = "rig-target-kind-button" };
            ToolkitChrome.StyleButton(targetKindButton, ToolkitButtonVariant.Secondary);
            targetKindButton.clicked += () =>
            {
                if (focusedRow != null)
                {
                    OpenRowKindPicker(focusedRow, targetKindButton);
                }
            };
            targetCardBody.Add(ToolkitChrome.MakePropertyRow("Kind", targetKindButton, "How this target is drawn at runtime."));

            targetTagButton = new Button { text = "Tag: (none)", name = "rig-target-tag-button" };
            ToolkitChrome.StyleButton(targetTagButton, ToolkitButtonVariant.Secondary);
            targetTagButton.clicked += () =>
            {
                if (focusedRow != null)
                {
                    OpenRowTagPicker(focusedRow, targetTagButton);
                }
            };
            targetTagButton.AddManipulator(new ContextualMenuManipulator(
                menuEvent =>
                {
                    if (focusedRow != null)
                    {
                        PopulateTagButtonContextMenu(menuEvent, focusedRow, targetTagButton);
                    }
                }));
            targetCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Tag", targetTagButton, "The vocabulary tag clips bind to on this target."));

            targetFacesDirectionToggle = new Toggle { name = "rig-target-faces-direction-toggle" };
            targetFacesDirectionToggle.RegisterValueChangedCallback(
                changeEvent => SetFocusedRowFacesDirection(changeEvent.newValue));
            targetCardBody.Add(ToolkitChrome.MakePropertyRow(
                "Faces direction", targetFacesDirectionToggle,
                "Bakes a PartFacing component so this target's art changes with the direction the actor faces."));

            targetUntickedHint = ToolkitChrome.MakeHint("Tick the node in the list above to make it a target.");
            targetCardBody.Add(targetUntickedHint);

            targetCard.style.display = DisplayStyle.None;
            editorContent.Add(targetCard);

            targetsColumn.Add(editorContent);

            targetsColumn.Add(ToolkitChrome.MakeStatusRow(out resultLabel, out _, true));

            return targetsColumn;
        }

        private VisualElement BuildPreviewPane()
        {
            VisualElement previewPane = new VisualElement { name = "new-rig-preview-pane" };
            previewPane.AddToClassList("toolkit-column");
            previewPane.AddToClassList("toolkit-column--flush");
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
                    RefreshTargetCard();
                },
                () =>
                {
                    // The registry changed underneath every open row (a tag renamed or newly
                    // created), not just this one's.
                    for (int rowIndex = 0; rowIndex < candidateRows.Count; rowIndex++)
                    {
                        RefreshTagButtonText(candidateRows[rowIndex]);
                    }
                    RefreshTargetCard();
                });
        }

        private void RefreshTagButtonText(CandidateRow row)
        {
            if (row.TagButton == null)
            {
                return;
            }
            // The row chip is compact, so it drops the "Tag: " prefix the Target card's button keeps.
            row.TagButton.text = DescribeTagChip(row.TagId);
        }

        private static string DescribeTagChip(uint tagId)
        {
            string describedTag = DescribeTag(tagId);
            const string tagPrefix = "Tag: ";
            return describedTag.StartsWith(tagPrefix, StringComparison.Ordinal)
                ? describedTag.Substring(tagPrefix.Length)
                : describedTag;
        }

        private static string DescribeTag(uint tagId)
        {
            if (tagId == 0u)
            {
                return "Tag: (none)";
            }
            TargetTagRegistry tagRegistry = VocabularyRegistryProvider.TargetTags;
            string tagName = tagRegistry != null ? tagRegistry.FindName(tagId) : null;
            return tagName != null
                ? "Tag: " + tagName
                : "Tag: (unresolved 0x" + tagId.ToString("X8") + ")";
        }

        // Moves clip and cutscene tracks off this row's tag; the rig target itself keeps the tag.
        private void PopulateTagButtonContextMenu(ContextualMenuPopulateEvent menuEvent, CandidateRow row, Button anchor)
        {
            menuEvent.menu.AppendAction(
                "Move clip tracks to another tag…",
                menuAction => RefactorPromptEditing.PickTagThenReplaceTrackTag(this, anchor, row.TagId, RescanProject),
                row.TagId != 0u ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
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
            RefreshTargetCard();
        }

        private void RefreshKindButtonText(CandidateRow row)
        {
            if (row.KindButton == null)
            {
                return;
            }
            // The row chip is compact, so it drops the "Kind: " prefix the Target card's button keeps.
            row.KindButton.text = DescribeKindChip(row.Kind);
        }

        private static string DescribeKindChip(TargetKind kind)
        {
            string describedKind = DescribeKind(kind);
            const string kindPrefix = "Kind: ";
            return describedKind.StartsWith(kindPrefix, StringComparison.Ordinal)
                ? describedKind.Substring(kindPrefix.Length)
                : describedKind;
        }

        private static string DescribeKind(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.VatMesh:
                    return "Kind: VAT Mesh";
                case TargetKind.FlipbookPlane:
                    return "Kind: Flipbook";
                default:
                    return "Kind: Quad";
            }
        }

        // The Target card (SG-D6) always mirrors the focused row; every write path that changes a
        // row's ticked state, tag or kind calls this rather than touching a button directly.
        private void RefreshTargetCard()
        {
            if (focusedRow == null)
            {
                targetCard.style.display = DisplayStyle.None;
                return;
            }

            targetCard.style.display = DisplayStyle.Flex;
            targetNameLabel.text = focusedRow.DisplayName;
            targetNodeLabel.text = focusedRow.IsMissingNode
                ? (string.IsNullOrEmpty(focusedRow.SourceNodePath)
                    ? "⚠ " + focusedRow.DisplayName + " (no node)"
                    : "⚠ " + focusedRow.SourceNodePath + " (missing from prefab)")
                : focusedRow.SourceNodePath;
            targetNodeLabel.tooltip = focusedRow.SourceNodePath;
            targetKindButton.text = DescribeKind(focusedRow.Kind);
            targetTagButton.text = DescribeTag(focusedRow.TagId);

            bool isTicked = focusedRow.ToggleControl != null && focusedRow.ToggleControl.value;
            targetKindButton.SetEnabled(isTicked);
            targetTagButton.SetEnabled(isTicked);
            targetUntickedHint.style.display = isTicked ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // Lists what BuildForRig reports for the selected rig, ticking the ones already a rig
        // target. Untick/re-tag guarding against overwriting the rig is later work.
        private void BuildRowsForEditMode(RigAsset rig)
        {
            candidateContainer.Clear();
            candidateRows.Clear();
            focusedRow = null;
            RefreshTargetCard();

            preview.ShowPrefab(rig != null ? rig.sourcePrefab : null);

            if (rig == null)
            {
                candidateSummaryLabel.text = "No rig selected.";
                targetsCountBadge.text = string.Empty;
                return;
            }

            if (rig.sourcePrefab == null)
            {
                candidateSummaryLabel.text =
                    "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.";
                targetsCountBadge.text = string.Empty;
                return;
            }

            List<RigTargetRow> rows = RigTargetRowBuilder.BuildForRig(rig);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                RigTargetRow row = rows[rowIndex];
                BuildCandidateRow(row, row.IsTarget);
            }

            targetsCountBadge.text = candidateRows.Count.ToString();
            candidateSummaryLabel.text = "in \"" + rig.name + "\".";
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

            // SG-D6's order is checkbox first, then the node name. A Toggle built WITH label text
            // draws its label before its checkmark, which put the tick at a different x on every
            // row -- a ragged column that is harder to scan than the chips it replaced. The name is
            // a sibling Label instead, so every tick lands on one x.
            Toggle rowToggle = new Toggle { value = ticked };
            rowToggle.tooltip = sourceRow.SourceNodePath;
            rowToggle.style.flexShrink = 0f;

            Label rowPathLabel = new Label(rowTitleText);
            rowPathLabel.AddToClassList("toolkit-list-row__title");

            // A105-D3 (SG-D6): Kind/Tag chips that open the same pickers as the Target card, shown
            // only while the row is ticked so an untargeted node stays a plain checkbox + name.
            Button rowKindChip = new Button();
            rowKindChip.AddToClassList("toolkit-badge");
            ToolkitChrome.StyleButton(rowKindChip, ToolkitButtonVariant.Ghost);
            rowKindChip.tooltip = "Change how this target is drawn at runtime.";
            rowKindChip.style.display = ticked ? DisplayStyle.Flex : DisplayStyle.None;

            Button rowTagChip = new Button();
            rowTagChip.AddToClassList("toolkit-badge");
            ToolkitChrome.StyleButton(rowTagChip, ToolkitButtonVariant.Ghost);
            rowTagChip.tooltip = "Change the vocabulary tag clips bind to on this target.";
            rowTagChip.style.display = ticked ? DisplayStyle.Flex : DisplayStyle.None;

            VisualElement candidateRow = new VisualElement();
            candidateRow.AddToClassList("toolkit-list-row");
            // The visible label ellipsizes a deep node path; the tooltip carries the full path.
            candidateRow.tooltip = sourceRow.SourceNodePath;
            candidateRow.Add(rowToggle);
            candidateRow.Add(rowPathLabel);
            candidateRow.Add(rowKindChip);
            candidateRow.Add(rowTagChip);
            candidateContainer.Add(candidateRow);

            CandidateRow row = new CandidateRow
            {
                DisplayName = sourceRow.DisplayName,
                SourceNodePath = sourceRow.SourceNodePath,
                TargetStableId = sourceRow.TargetStableId,
                TagId = sourceRow.TagId,
                Kind = sourceRow.Kind,
                IsMissingNode = sourceRow.IsMissingNode,
                ToggleControl = rowToggle,
                TagButton = rowTagChip,
                KindButton = rowKindChip,
                Box = candidateRow
            };
            rowKindChip.clicked += () => OpenRowKindPicker(row, rowKindChip);
            rowTagChip.clicked += () => OpenRowTagPicker(row, rowTagChip);
            RefreshKindButtonText(row);
            RefreshTagButtonText(row);
            rowToggle.RegisterValueChangedCallback(
                changeEvent => OnRowToggleChanged(row, rowToggle, changeEvent.newValue));
            // TrickleDown, so clicking the toggle still shows which node the row means rather than
            // being swallowed by the control that was hit.
            candidateRow.RegisterCallback<PointerDownEvent>(
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
        private void OnRowToggleChanged(CandidateRow row, Toggle rowToggle, bool isChecked)
        {
            RefreshTargetCard();
            // A105-D3: the Kind/Tag chips are only meaningful once the node is a target.
            row.KindButton.style.display = isChecked ? DisplayStyle.Flex : DisplayStyle.None;
            row.TagButton.style.display = isChecked ? DisplayStyle.Flex : DisplayStyle.None;
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
                    row.KindButton.style.display = DisplayStyle.Flex;
                    row.TagButton.style.display = DisplayStyle.Flex;
                    RefreshTargetCard();
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

        /// <summary>Focuses the target row at this index exactly as clicking it does, so the Target card mirrors that row; an index outside the list clears the focus.</summary>
        // A row is focused by a pointer event on the row itself, which a detached panel has no
        // dispatcher for -- so a fixture or a drive has no other way to ask for the card's contents.
        public void FocusTargetRow(int rowIndex)
        {
            FocusRow(rowIndex >= 0 && rowIndex < candidateRows.Count ? candidateRows[rowIndex] : null);
        }

        private void FocusRow(CandidateRow row)
        {
            if (focusedRow != null && focusedRow.Box != null)
            {
                focusedRow.Box.RemoveFromClassList(SelectedRowUssClassName);
            }
            focusedRow = row;
            if (row == null)
            {
                preview.ClearFocus();
                RefreshTargetCard();
                return;
            }
            if (row.Box != null)
            {
                row.Box.AddToClassList(SelectedRowUssClassName);
            }
            // The preview has no copy of a missing node's transform to focus on.
            if (!row.IsMissingNode)
            {
                preview.FocusNode(row.SourceNodePath);
            }
            RefreshTargetCard();
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

            string referenceSummary = AssetReferenceIndex.SummarizeForDialog(AssetReferenceIndex.ReferencesToRig(rig));
            string dialogBody = "Delete \"" + rig.name + "\"? Any actor profile or cutscene slot bound to it will lose "
                + "it. The asset moves to the OS trash, not permanently deleted.";
            if (referenceSummary.Length > 0)
            {
                dialogBody = referenceSummary + "\n\n" + dialogBody;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Rig",
                dialogBody,
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
                rigFolderRow.Path = projectRelativeFolder;
            }
            else
            {
                ReportFailure("The chosen folder must be inside this project's Assets folder.");
            }
        }

        private void ReportFailure(string message)
        {
            ToolkitChrome.SetStatus(resultLabel, message, ToolkitStatusTone.Error);
            Debug.LogWarning("[DOTS Animation Toolkit] Rigs: " + message);
        }
    }
}
