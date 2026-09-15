// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public sealed partial class ClipEditorWindow
    {
        /// <summary>Which ragdoll body the component stack and the viewport handles are pointed at. 0 for none.</summary>
        private uint selectedRagdollBodyId;

        private readonly RagdollBoxDragSession ragdollBoxDragSession = new RagdollBoxDragSession();

        // The pointer routing in ClipEditorWindow reads this to tell a ragdoll box drag from every
        // other viewport gesture; the state behind it now lives on the shared drag session.
        private RagdollBoxHandle activeRagdollBoxHandle
        {
            get { return ragdollBoxDragSession.ActiveHandle; }
        }

        // Points the component stack's active marking and the viewport handles at one ragdoll body.
        // Separate field from selectedSocketId/selectedTargetId: a Ragdoll selection does not move
        // the ordinary hierarchy selection or outline.
        private void FocusRagdollBody(uint bodyId)
        {
            selectedRagdollBodyId = bodyId;
            if (previewController != null)
            {
                previewController.SetSelectedRagdollBodyId(bodyId);
            }
            clipInspectorPane.RebuildInspector();
        }

        /// <summary>Whether the press landed on the selected ragdoll body's grab handle, and if so, starts the drag.</summary>
        private bool TryBeginRagdollBoxDrag(Vector2 localPosition)
        {
            if (selectedRagdollBodyId == 0u || previewController == null)
            {
                return false;
            }

            Vector2 viewportPoint;
            float aspect;
            if (!TryGetViewportPoint(localPosition, out viewportPoint, out aspect))
            {
                return false;
            }

            return ragdollBoxDragSession.TryBegin(previewController, ActiveRig, selectedRagdollBodyId, viewportPoint, aspect);
        }

        private void ContinueRagdollBoxDrag(Vector2 localPosition, bool symmetric)
        {
            Vector2 viewportPoint;
            float aspect;
            if (!TryGetViewportPoint(localPosition, out viewportPoint, out aspect))
            {
                return;
            }

            ragdollBoxDragSession.Continue(previewController, ActiveRig, viewportPoint, aspect, symmetric);
        }

        // Ends a ragdoll box drag, routed through GizmoDragRouting like every other viewport drag —
        // always RagdollBody in practice, since a selected body wins outright.
        private void EndRagdollBoxDrag()
        {
            if (!ragdollBoxDragSession.End(previewController, ActiveRig))
            {
                return;
            }

            RigAsset rig = ActiveRig;
            GizmoDragDestination destination = GizmoDragRouting.Resolve(
                hierarchyPane.SelectedSocketId != 0u, true, IsRigEditMode, IsAutoKeyEnabled, true);
            if (destination == GizmoDragDestination.RagdollBody && rig != null)
            {
                EditorUtility.SetDirty(rig);
            }
            clipInspectorPane.RebuildInspector();
        }
    }
}
