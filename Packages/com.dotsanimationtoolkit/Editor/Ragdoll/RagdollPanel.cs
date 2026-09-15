// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Ragdoll tab: a bodies column, the viewport with its Drop transport, and the
    /// selected body's inspector, all over the shared Rig.</summary>
    public sealed class RagdollPanel : VisualElement, IDisposable
    {
        private readonly ObjectField rigField;
        private readonly Label summaryLabel;
        private readonly RagdollBodiesColumn bodiesColumn;
        private readonly RagdollViewportElement viewport;
        private readonly RagdollInspectorColumn inspectorColumn;

        private ActiveAssetSelection selection;
        private RigAsset localRig;
        private ClipSetAsset localClipSet;
        private uint selectedBodyId;
        private bool isDisposed;

        public RigAsset SelectedRig
        {
            get { return localRig; }
        }

        public uint SelectedBodyId
        {
            get { return selectedBodyId; }
        }

        public RagdollPanel()
        {
            name = "ragdoll-panel";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement headerRow = ToolkitChrome.MakeAssetBar("ragdoll-header-row");
            Add(headerRow);

            headerRow.Add(ToolkitChrome.MakeAssetBarLabel("Rig"));

            rigField = new ObjectField
            {
                name = "ragdoll-rig-field",
                objectType = typeof(RigAsset),
                allowSceneObjects = false
            };
            rigField.AddToClassList("toolkit-asset-bar__field");
            rigField.RegisterValueChangedCallback(changeEvent =>
            {
                RigAsset newRig = changeEvent.newValue as RigAsset;
                if (selection != null)
                {
                    selection.SetRig(newRig);
                }
                else
                {
                    ApplyRig(newRig);
                }
            });
            headerRow.Add(rigField);

            summaryLabel = new Label { name = "ragdoll-summary-label" };
            summaryLabel.style.marginLeft = 6f;
            summaryLabel.AddToClassList("toolkit-text--dim");
            headerRow.Add(summaryLabel);

            bodiesColumn = new RagdollBodiesColumn();
            bodiesColumn.BodySelected += OnBodySelected;
            bodiesColumn.RigBodiesChanged += OnRigBodiesChanged;

            viewport = new RagdollViewportElement();
            viewport.style.flexGrow = 1f;
            viewport.BodyPicked += OnBodyPicked;
            viewport.BodyBoxEdited += OnBodyBoxEdited;

            inspectorColumn = new RagdollInspectorColumn();
            inspectorColumn.BodyEdited += OnBodyEdited;

            CoverPaneSplitView inspectorSplitView =
                new CoverPaneSplitView("Ragdoll.Inspector", 1, 320f, TwoPaneSplitViewOrientation.Horizontal);
            inspectorSplitView.style.flexGrow = 1f;
            inspectorSplitView.Add(viewport);
            inspectorSplitView.Add(inspectorColumn);

            CoverPaneSplitView bodiesSplitView =
                new CoverPaneSplitView("Ragdoll.Bodies", 0, 240f, TwoPaneSplitViewOrientation.Horizontal);
            bodiesSplitView.style.flexGrow = 1f;
            bodiesSplitView.Add(bodiesColumn);
            bodiesSplitView.Add(inspectorSplitView);
            Add(bodiesSplitView);

            Undo.undoRedoPerformed += Refresh;
        }

        // Window path: follows the shared Rig; the ClipSet is tracked for the viewport's rest-pose source.
        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }
            selection = sharedSelection;
            if (selection != null)
            {
                selection.RigChanged += OnSharedRigChanged;
                selection.ClipSetChanged += OnSharedClipSetChanged;
            }
            localRig = selection != null ? selection.Rig : null;
            localClipSet = selection != null ? selection.ClipSet : null;
            Refresh();
        }

        // Detached path: drives with a rig handed in directly, no shared selection to react to.
        public void Bind(RigAsset rig)
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection = null;
            }
            ApplyRig(rig);
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            isDisposed = true;

            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection = null;
            }
            Undo.undoRedoPerformed -= Refresh;
            viewport.Dispose();
        }

        private void ApplyRig(RigAsset rig)
        {
            localRig = rig;
            Refresh();
        }

        private void Refresh()
        {
            selectedBodyId = 0u;
            bodiesColumn.SetRig(localRig);
            viewport.Show(localRig);
            inspectorColumn.SetRig(localRig);
            bodiesColumn.SetSelectedBodyId(selectedBodyId);
            viewport.SetSelectedBodyId(selectedBodyId);
            inspectorColumn.SetSelectedBodyId(selectedBodyId);
            rigField.SetValueWithoutNotify(localRig);
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            RagdollBodySummary summary = RagdollBodySummaryResolver.Resolve(localRig);
            summaryLabel.text = summary.text;
        }

        private void OnBodySelected(uint bodyId)
        {
            selectedBodyId = bodyId;
            viewport.SetSelectedBodyId(bodyId);
            inspectorColumn.SetSelectedBodyId(bodyId);
        }

        private void OnBodyPicked(uint bodyId)
        {
            selectedBodyId = bodyId;
            bodiesColumn.SetSelectedBodyId(bodyId);
            inspectorColumn.SetSelectedBodyId(bodyId);
        }

        private void OnBodyBoxEdited()
        {
            inspectorColumn.Refresh();
        }

        private void OnBodyEdited()
        {
            bodiesColumn.Refresh();
            RefreshSummary();
        }

        private void OnRigBodiesChanged()
        {
            viewport.Show(localRig);
            inspectorColumn.Refresh();
            RefreshSummary();
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            localRig = rig;
            Refresh();
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            localClipSet = clipSet;
        }
    }
}
