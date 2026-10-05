// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab: checks each rig part's material against the inputs its clips drive.</summary>
    public sealed class MaterialsPanel : VisualElement, IDisposable
    {
        private readonly List<PartInputReport> reports = new List<PartInputReport>();
        private readonly ObjectField rigField;
        private readonly ObjectField clipSetField;
        private readonly Label resultLabel;
        private readonly MaterialPartListColumn partList;
        private readonly MaterialInspectorColumn inspector;
        private readonly MaterialCheckPreviewElement preview;
        private ActiveAssetSelection selection;
        private RigAsset rigShownInPreview;
        private bool hasShownRig;
        private bool isRefreshing;

        public RigAsset BoundRig { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }

        public IReadOnlyList<PartInputReport> Reports
        {
            get { return reports; }
        }

        public MaterialsPanel()
        {
            style.flexGrow = 1f;

            VisualElement header = ToolkitChrome.MakeAssetBar("materials-asset-bar");

            header.Add(ToolkitChrome.MakeAssetBarLabel("Rig"));
            rigField = new ObjectField
            {
                objectType = typeof(RigAsset),
                allowSceneObjects = false,
                name = "materials-rig-field"
            };
            rigField.AddToClassList("toolkit-asset-bar__field");
            rigField.RegisterValueChangedCallback(changeEvent =>
            {
                RigAsset newRig = changeEvent.newValue as RigAsset;
                if (selection != null) { selection.SetRig(newRig); } else { SetRig(newRig); }
            });
            header.Add(rigField);

            header.Add(ToolkitChrome.MakeAssetBarLabel("Clip Set"));
            clipSetField = new ObjectField
            {
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false,
                name = "materials-clip-set-field"
            };
            clipSetField.AddToClassList("toolkit-asset-bar__field");
            clipSetField.RegisterValueChangedCallback(changeEvent =>
            {
                ClipSetAsset newClipSet = changeEvent.newValue as ClipSetAsset;
                if (selection != null) { selection.SetClipSet(newClipSet); } else { SetClipSet(newClipSet); }
            });
            header.Add(clipSetField);

            VisualElement statusRow = ToolkitChrome.MakeStatusRow(out resultLabel, out _, true);
            resultLabel.name = "materials-result";

            partList = new MaterialPartListColumn { name = "materials-part-list-column" };
            partList.PartSelected += OnPartSelected;
            partList.RefreshRequested += Refresh;

            inspector = new MaterialInspectorColumn();

            VisualElement previewColumn = new VisualElement { name = "materials-preview-column" };
            previewColumn.AddToClassList("toolkit-column");
            previewColumn.style.flexGrow = 1f;
            VisualElement previewHeader = new VisualElement();
            previewHeader.AddToClassList("toolkit-pane-header");
            Label previewTitle = new Label("Preview");
            previewTitle.AddToClassList("toolkit-pane-title");
            previewHeader.Add(previewTitle);
            previewColumn.Add(previewHeader);
            preview = new MaterialCheckPreviewElement { name = "materials-preview" };
            previewColumn.Add(preview);

            CoverPaneSplitView inspectorSplit =
                new CoverPaneSplitView("Materials.Inspector", 1, 340f, TwoPaneSplitViewOrientation.Horizontal);
            inspectorSplit.style.flexGrow = 1f;
            inspectorSplit.Add(previewColumn);
            inspectorSplit.Add(inspector);

            CoverPaneSplitView split =
                new CoverPaneSplitView("Materials.CatalogThreeColumns", 0, 300f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(partList);
            split.Add(inspectorSplit);

            Add(header);
            Add(split);
            Add(statusRow);
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }

            selection = sharedSelection;
            selection.RigChanged += OnSharedRigChanged;
            selection.ClipSetChanged += OnSharedClipSetChanged;
            BoundClipSet = selection.ClipSet;
            clipSetField.SetValueWithoutNotify(BoundClipSet);
            SetRig(selection.Rig);
        }

        public void SetRig(RigAsset rig)
        {
            BoundRig = rig;
            rigField.SetValueWithoutNotify(rig);
            Refresh();
        }

        public void SetClipSet(ClipSetAsset clipSet)
        {
            BoundClipSet = clipSet;
            clipSetField.SetValueWithoutNotify(clipSet);
            Refresh();
        }

        public void Refresh()
        {
            // A column raising RefreshRequested from inside this call must not recurse back into it.
            if (isRefreshing)
            {
                return;
            }

            isRefreshing = true;
            try
            {
                string selectedNodePath = partList.SelectedReport != null
                    ? partList.SelectedReport.Target.sourceNodePath
                    : null;

                reports.Clear();
                reports.AddRange(PartInputCheck.Evaluate(BoundRig, BoundClipSet));
                partList.SetReports(reports, BoundClipSet != null);

                // Reshowing the rig resets the preview camera, so only do it when the rig actually changed.
                if (!hasShownRig || rigShownInPreview != BoundRig)
                {
                    preview.ShowRig(BoundRig);
                    rigShownInPreview = BoundRig;
                    hasShownRig = true;
                }

                List<string> flaggedNodePaths = new List<string>();
                PartInputReport reportToSelect = null;
                for (int reportIndex = 0; reportIndex < reports.Count; reportIndex++)
                {
                    PartInputReport report = reports[reportIndex];
                    if (report.HasMissingInputs)
                    {
                        flaggedNodePaths.Add(report.Target.sourceNodePath);
                    }

                    if (selectedNodePath != null && report.Target.sourceNodePath == selectedNodePath)
                    {
                        reportToSelect = report;
                    }
                }

                preview.SetFlaggedNodes(flaggedNodePaths);
                partList.SetSelected(reportToSelect);
                BindSelection(reportToSelect);
                ReportStatus(flaggedNodePaths.Count);
            }
            finally
            {
                isRefreshing = false;
            }
        }

        public void Dispose()
        {
            preview.Dispose();
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            SetRig(rig);
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            SetClipSet(clipSet);
        }

        private void OnPartSelected(PartInputReport report)
        {
            BindSelection(report);
        }

        private void BindSelection(PartInputReport report)
        {
            preview.FocusNode(report != null ? report.Target.sourceNodePath : null);
            inspector.Bind(report, BoundRig, BoundClipSet);
        }

        private void ReportStatus(int missingPartCount)
        {
            if (BoundClipSet == null)
            {
                ToolkitChrome.SetStatus(
                    resultLabel, "Pick a clip set to check what each part's animations need.", ToolkitStatusTone.Neutral);
            }
            else if (missingPartCount > 0)
            {
                ToolkitChrome.SetStatus(
                    resultLabel,
                    missingPartCount + " of " + reports.Count + " parts are missing inputs their animations need.",
                    ToolkitStatusTone.Error);
            }
            else
            {
                ToolkitChrome.SetStatus(
                    resultLabel, "Every part has the inputs its animations need.", ToolkitStatusTone.Ok);
            }
        }
    }
}
