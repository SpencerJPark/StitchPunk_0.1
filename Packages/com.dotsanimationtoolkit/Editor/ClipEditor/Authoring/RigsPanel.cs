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
    /// <summary>The Rigs tab: a catalog of project rigs beside a target list that creates a new <see cref="RigAsset"/> from a prefab's renderer-bearing nodes, or edits a selected rig's targets in place.</summary>
    public sealed class RigsPanel : VisualElement, IDisposable
    {
        public enum EditorMode
        {
            Create,
            Edit,
        }

        /// <summary>One renderer-bearing node found while scanning the source prefab.</summary>
        private sealed class CandidateRow
        {
            public string DisplayName;
            public string SourceNodePath;
            public uint TargetStableId;
            public uint TagId;
            public bool IsMissingNode;
            public Toggle ToggleControl;
            public Button TagButton;
            public VisualElement Box;
        }

        private const string SelectedBoxUssClassName = "toolkit-box--selected";

        private RigCatalogColumn catalog;
        private VisualElement footerContainer;
        private Label targetsTitleLabel;
        private Button useInEditorButton;
        private ObjectField sourcePrefabField;
        private Label candidateSummaryLabel;
        private VisualElement candidateContainer;
        private Toggle assignToggle;
        private Label resultLabel;
        private RigSourcePreviewElement preview;
        private CandidateRow focusedRow;

        private readonly List<CandidateRow> candidateRows = new List<CandidateRow>();
        private readonly List<ClipAsset> catalogClips = new List<ClipAsset>();

        // Once the user has picked something this session (a catalog click or New), an incoming
        // SetSource from the window's own active-rig field must not yank the selection back.
        private bool hasUserSelectedThisSession;

        public EditorMode Mode { get; private set; }

        public RigAsset SelectedRig { get; private set; }

        /// <summary>Raised after a successful Create, so the host can untick its Rigs tab toggle.</summary>
        public event Action Closed;

        // This panel never touches the window's rig itself; it only reports what it built and
        // whether the caller asked to have it loaded — the window decides what loading means.
        /// <summary>
        /// Raised after a rig is created and saved. The second argument is whether the panel's own
        /// "load this rig into the editor" toggle was checked at the time.
        /// </summary>
        public event Action<RigAsset, bool> RigCreated;

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

            // Draggable dividers, same control ClipSetsPanel's own dock uses.
            TwoPaneSplitView outerSplitView = new TwoPaneSplitView(0, 640f, TwoPaneSplitViewOrientation.Horizontal);
            outerSplitView.style.flexGrow = 1f;

            TwoPaneSplitView innerSplitView = new TwoPaneSplitView(0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            innerSplitView.style.flexGrow = 1f;

            catalog = new RigCatalogColumn();
            catalog.NewRequested += BeginCreate;
            catalog.RefreshRequested += RescanProject;
            catalog.RigSelected += SelectRig;
            innerSplitView.Add(catalog);
            innerSplitView.Add(BuildTargetsColumn());

            outerSplitView.Add(innerSplitView);
            outerSplitView.Add(BuildPreviewPane());
            Add(outerSplitView);

            ApplyModeChrome();
            RescanProject();

            // An edit-mode tick writes straight to the asset, so Ctrl+Z changes the rig without
            // this panel touching it. Without this the row keeps showing the tick the undo removed.
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        /// <summary>Releases the preview's render utility and its copy of the prefab.</summary>
        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            preview?.Dispose();
        }

        private void OnUndoRedoPerformed()
        {
            if (Mode != EditorMode.Edit || SelectedRig == null)
            {
                return;
            }

            // Rebuilt wholesale rather than reconciled row by row: an undo can restore a target,
            // remove one, or change a tag, and re-reading the rig covers all three.
            BuildRowsForEditMode(SelectedRig);
            RaiseRigTargetsChanged();
        }

        // Called every time the host shows this tab, so the catalog always reflects the current
        // project and a freshly-activated rig is pre-selected until the user picks something else.
        public void SetSource(RigAsset activeRig)
        {
            RescanProject();
            if (!hasUserSelectedThisSession && activeRig != null)
            {
                SelectRig(activeRig);
            }
        }

        public void SelectRig(RigAsset rig)
        {
            Mode = EditorMode.Edit;
            SelectedRig = rig;
            hasUserSelectedThisSession = true;
            catalog.SetSelectedRig(rig);
            // Without notify: a plain assignment would fire the field's own change callback and
            // immediately write this rig's prefab back onto itself.
            sourcePrefabField.SetValueWithoutNotify(rig != null ? rig.sourcePrefab : null);
            ApplyModeChrome();
            BuildRowsForEditMode(rig);
        }

        public void BeginCreate()
        {
            Mode = EditorMode.Create;
            SelectedRig = null;
            hasUserSelectedThisSession = true;
            catalog.ClearSelection();
            ApplyModeChrome();
            RescanHierarchy();
        }

        private void ApplyModeChrome()
        {
            targetsTitleLabel.text = Mode == EditorMode.Create
                ? "New Rig"
                : (SelectedRig != null ? SelectedRig.name : "Rig");
            footerContainer.style.display = Mode == EditorMode.Create ? DisplayStyle.Flex : DisplayStyle.None;
            useInEditorButton.style.display = Mode == EditorMode.Edit ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RescanProject()
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

            catalog.SetRigs(rigs);
        }

        private VisualElement BuildTargetsColumn()
        {
            VisualElement targetsColumn = new VisualElement { name = "rig-targets-column" };
            targetsColumn.style.flexGrow = 1f;
            targetsColumn.style.minWidth = 360f;
            targetsColumn.style.paddingTop = 8f;
            targetsColumn.style.paddingLeft = 10f;
            targetsColumn.style.paddingRight = 10f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            targetsTitleLabel = new Label("New Rig") { name = "rig-targets-title" };
            targetsTitleLabel.AddToClassList("toolkit-pane-title");
            header.Add(targetsTitleLabel);

            useInEditorButton = ToolkitIcons.MakeIconTextButton(
                OnUseInEditorClicked, "editicon.sml", null, "Use in Clip Editor");
            useInEditorButton.name = "rig-use-in-editor-button";
            header.Add(useInEditorButton);

            targetsColumn.Add(header);

            sourcePrefabField = new ObjectField("Source Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = "The prefab the new rig will preview from and the VAT bake will sample. "
                    + "Its hierarchy is scanned below for nodes to offer as rig targets."
            };
            sourcePrefabField.RegisterValueChangedCallback(changeEvent =>
            {
                if (Mode == EditorMode.Create)
                {
                    RescanHierarchy();
                }
                else if (Mode == EditorMode.Edit && SelectedRig != null)
                {
                    // Targets are left exactly as they are; nodes that no longer exist just become
                    // missing rows through the ordinary BuildForRig path.
                    GameObject newSourcePrefab = changeEvent.newValue as GameObject;
                    if (RigAssetUtility.SetRigSourcePrefab(SelectedRig, newSourcePrefab))
                    {
                        BuildRowsForEditMode(SelectedRig);
                        RaiseRigTargetsChanged();
                    }
                }
            });
            targetsColumn.Add(sourcePrefabField);

            targetsColumn.Add(BuildHeading("Targets"));

            candidateSummaryLabel = new Label(
                "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.");
            candidateSummaryLabel.style.whiteSpace = WhiteSpace.Normal;
            targetsColumn.Add(candidateSummaryLabel);

            ScrollView candidateScroll = new ScrollView();
            candidateScroll.style.flexGrow = 1f;
            candidateScroll.style.marginTop = 4f;
            candidateContainer = candidateScroll.contentContainer;
            targetsColumn.Add(candidateScroll);

            footerContainer = new VisualElement { name = "new-rig-footer" };
            footerContainer.Add(BuildHeading("Create"));

            // Not gated on a clip set: loading a rig into the window needs no set, exactly as
            // picking one in the toolbar does not.
            assignToggle = new Toggle("Load this rig into the editor");
            assignToggle.value = true;
            assignToggle.tooltip =
                "Puts the new rig in the toolbar's Rig field. Window state only — it pairs the rig "
                + "with nothing, and changes no asset.";
            footerContainer.Add(assignToggle);

            // No Cancel beside it: the toolbar’s Rigs toggle is what opens and closes this tab,
            // the way VAT Bake's does, and a second dismissal that leaves the toggle lit would be a
            // button that closes a page the toolbar still says is open.
            Button createButton = new Button(Create) { text = "Create Rig" };
            createButton.style.height = 28f;
            createButton.style.marginTop = 6f;
            footerContainer.Add(createButton);

            targetsColumn.Add(footerContainer);

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
                    if (Mode == EditorMode.Edit && SelectedRig != null && row.TargetStableId != 0u)
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

        private static Label BuildHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
        }

        // Walks the assigned prefab's hierarchy for renderer-bearing nodes and offers each as a
        // candidate target. Renderer rather than a specific subtype, so a cutout part's
        // MeshRenderer and a VAT source's SkinnedMeshRenderer are found the same way.
        private void RescanHierarchy()
        {
            candidateContainer.Clear();
            candidateRows.Clear();
            focusedRow = null;

            GameObject prefab = sourcePrefabField.value as GameObject;
            // Before the rows are built, so every SetNodeIncluded below lands on a copy that exists.
            preview.ShowPrefab(prefab);

            if (prefab == null)
            {
                candidateSummaryLabel.text =
                    "Assign a source prefab to scan its hierarchy for renderer-bearing nodes.";
                return;
            }

            List<RigTargetRow> rows = RigTargetRowBuilder.BuildForNewRig(prefab);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                BuildCandidateRow(rows[rowIndex], rows[rowIndex].PreTicked);
            }

            candidateSummaryLabel.text = candidateRows.Count.ToString()
                + " renderer-bearing node(s) found in \"" + prefab.name + "\". Click a row to find it "
                + "in the preview.";
        }

        // Edit mode's row list — a stub that lists what BuildForRig reports, ticking the ones
        // already a rig target. Untick/re-tag guarding against overwriting the rig is later work.
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

            VisualElement candidateBox = new VisualElement();
            candidateBox.AddToClassList("toolkit-box");

            VisualElement rowContainer = new VisualElement();
            rowContainer.AddToClassList("toolkit-box__header");
            rowContainer.Add(rowToggle);
            rowContainer.Add(tagButton);
            candidateBox.Add(rowContainer);
            candidateContainer.Add(candidateBox);

            CandidateRow row = new CandidateRow
            {
                DisplayName = sourceRow.DisplayName,
                SourceNodePath = sourceRow.SourceNodePath,
                TargetStableId = sourceRow.TargetStableId,
                TagId = sourceRow.TagId,
                IsMissingNode = sourceRow.IsMissingNode,
                ToggleControl = rowToggle,
                TagButton = tagButton,
                Box = candidateBox
            };
            RefreshTagButtonText(row);
            tagButton.clicked += () => OpenRowTagPicker(row, tagButton);
            rowToggle.RegisterValueChangedCallback(
                changeEvent => OnRowToggleChanged(row, rowToggle, tagButton, changeEvent.newValue));
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
        private void OnRowToggleChanged(CandidateRow row, Toggle rowToggle, Button tagButton, bool isChecked)
        {
            tagButton.SetEnabled(isChecked);
            if (!row.IsMissingNode)
            {
                preview.SetNodeIncluded(row.SourceNodePath, isChecked);
            }

            if (Mode != EditorMode.Edit || SelectedRig == null)
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

        private void Create()
        {
            resultLabel.text = string.Empty;

            GameObject prefab = sourcePrefabField.value as GameObject;
            if (prefab == null)
            {
                ReportFailure("Assign a source prefab first.");
                return;
            }
            if (candidateRows.Count == 0)
            {
                ReportFailure("No renderer-bearing nodes were found in \"" + prefab.name + "\"'s hierarchy.");
                return;
            }

            List<RigTargetDefinition> selectedTargets = new List<RigTargetDefinition>();
            for (int rowIndex = 0; rowIndex < candidateRows.Count; rowIndex++)
            {
                CandidateRow row = candidateRows[rowIndex];
                if (row.ToggleControl == null || !row.ToggleControl.value)
                {
                    continue;
                }
                selectedTargets.Add(new RigTargetDefinition
                {
                    displayName = row.DisplayName,
                    sourceNodePath = row.SourceNodePath,
                    tagId = row.TagId
                });
            }

            if (selectedTargets.Count == 0)
            {
                ReportFailure("Tick at least one node to become a rig target.");
                return;
            }

            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Create Rig", prefab.name + "Rig", "asset", "Choose where to save the new rig.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            RigAsset newRig = RigAssetUtility.CreateRig(assetPath, prefab, selectedTargets);
            if (newRig == null)
            {
                ReportFailure("Could not create the rig asset at \"" + assetPath + "\".");
                return;
            }

            resultLabel.style.color = new StyleColor(new Color(0.6f, 0.9f, 0.6f));
            resultLabel.text = "Created \"" + newRig.name + "\" with " + selectedTargets.Count.ToString()
                + " target(s).";
            EditorGUIUtility.PingObject(newRig);

            // So the new rig shows up in the catalog without waiting for a manual Refresh.
            RescanProject();

            if (RigCreated != null)
            {
                RigCreated(newRig, assignToggle.value);
            }
            if (Closed != null)
            {
                Closed();
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
