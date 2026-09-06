// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed partial class CutsceneEditorPanel
    {
        private const string SessionAutoKeyEnabledKey = "DotsAnimationToolkit.CutsceneEditor.AutoKeyEnabled";

        private Toggle autoKeyToggle;
        private bool isAutoKeyEnabled;

        // Reused every editor frame: the detection step runs whether or not anything moved.
        private readonly List<CutscenePreviewController.DriftedPreviewTransform> driftedTransforms =
            new List<CutscenePreviewController.DriftedPreviewTransform>();

        // Transforms a drag has already moved, waiting for the pointer to come up. Keyed by
        // transform instance id so one gesture cannot queue the same object twice.
        private readonly Dictionary<EntityId, CutscenePreviewController.DriftedPreviewTransform> pendingAutoKeyEdits =
            new Dictionary<EntityId, CutscenePreviewController.DriftedPreviewTransform>();

        private VisualElement BuildAutoKeyToggle()
        {
            isAutoKeyEnabled = SessionState.GetBool(SessionAutoKeyEnabledKey, false);
            autoKeyToggle = new Toggle { text = "Auto Key", value = isAutoKeyEnabled };
            autoKeyToggle.style.marginLeft = 8f;
            autoKeyToggle.tooltip =
                "Keys whatever you move with Unity's own gizmo at the playhead, the moment you let "
                + "the drag go. Needs the preview active, and does nothing while the transport plays.";
            autoKeyToggle.RegisterValueChangedCallback(changeEvent =>
            {
                isAutoKeyEnabled = changeEvent.newValue;
                SessionState.SetBool(SessionAutoKeyEnabledKey, isAutoKeyEnabled);
                pendingAutoKeyEdits.Clear();
            });
            return autoKeyToggle;
        }

        private void AutoKeyTick()
        {
            RunAutoKeyDetectionStep(GUIUtility.hotControl != 0);
        }

        // One step of gizmo-edit detection, with the pointer state passed in rather than read, so a
        // test can drive a press and its release without a real drag.
        private void RunAutoKeyDetectionStep(bool isPointerHeld)
        {
            // Inert while the transport plays: it writes a pose every tick, and hotControl is
            // non-zero for unrelated editor UI, so a playing frame cannot be told from a drag.
            if (!isAutoKeyEnabled || isPlaying || cutscene == null || serializedObject == null
                || !previewController.IsActive)
            {
                pendingAutoKeyEdits.Clear();
                return;
            }

            if (isPointerHeld)
            {
                RememberTransformsMovedDuringThisDrag();
                return;
            }
            if (pendingAutoKeyEdits.Count == 0)
            {
                return;
            }
            KeyPendingAutoKeyEdits();
        }

        // A difference from what the preview last applied — never from the sampled value — is the
        // only thing that can be a human's hand: a scrub re-poses and re-records, so it reads clean.
        private void RememberTransformsMovedDuringThisDrag()
        {
            previewController.CollectTransformsMovedSinceLastAppliedPose(driftedTransforms);
            for (int driftIndex = 0; driftIndex < driftedTransforms.Count; driftIndex++)
            {
                CutscenePreviewController.DriftedPreviewTransform drifted = driftedTransforms[driftIndex];
                pendingAutoKeyEdits[drifted.transform.GetEntityId()] = drifted;
            }
        }

        private void KeyPendingAutoKeyEdits()
        {
            bool keyedAnything = false;
            foreach (KeyValuePair<EntityId, CutscenePreviewController.DriftedPreviewTransform> pendingEdit
                in pendingAutoKeyEdits)
            {
                keyedAnything |= TryAutoKeyDriftedTransform(pendingEdit.Value);
                // Accepted whether or not it keyed: a drag this panel has no lane for must not
                // re-fire on the next unrelated gesture.
                previewController.AcceptCurrentPoseAsApplied(pendingEdit.Value.transform);
            }
            pendingAutoKeyEdits.Clear();

            if (!keyedAnything)
            {
                return;
            }
            serializedObject.Update();
            RequestTimelineRebuild();
            RequestInspectorRebuild();
        }

        private bool TryAutoKeyDriftedTransform(CutscenePreviewController.DriftedPreviewTransform drifted)
        {
            int slotIndex = FindSlotIndexBySlotId(drifted.slotId);
            if (slotIndex < 0)
            {
                return false;
            }
            CutsceneSlot slot = cutscene.slots[slotIndex];
            SerializedProperty slotProperty =
                serializedObject.FindProperty("slots").GetArrayElementAtIndex(slotIndex);

            if (drifted.targetStableId == 0u)
            {
                return previewController.TryKeyRoot(
                    serializedObject, slotProperty.FindPropertyRelative("transformKeys"), slot, playheadSeconds);
            }

            int partTrackIndex = FindPartTrackIndexForBoundTransform(slot, drifted.transform);
            if (partTrackIndex < 0)
            {
                return false;
            }
            SerializedProperty keysProperty = slotProperty.FindPropertyRelative("partTracks")
                .GetArrayElementAtIndex(partTrackIndex).FindPropertyRelative("keys");
            return previewController.TryKeyPartTrack(
                serializedObject, keysProperty, slot, slot.partTracks[partTrackIndex], playheadSeconds);
        }

        private int FindSlotIndexBySlotId(uint slotId)
        {
            if (cutscene == null || cutscene.slots == null)
            {
                return -1;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot != null && slot.SlotId == slotId)
                {
                    return slotIndex;
                }
            }
            return -1;
        }

        // Matched by the transform each track's tag resolves to, so a part with no track of its own
        // is left alone rather than keyed into whichever track happens to be selected.
        private int FindPartTrackIndexForBoundTransform(CutsceneSlot slot, Transform movedTransform)
        {
            if (slot.rig == null || slot.partTracks == null)
            {
                return -1;
            }
            for (int trackIndex = 0; trackIndex < slot.partTracks.Count; trackIndex++)
            {
                CutsceneKeyedTrack track = slot.partTracks[trackIndex];
                if (track != null
                    && previewController.GetBoundPartTransform(slot.SlotId, slot.rig, track.tagId) == movedTransform)
                {
                    return trackIndex;
                }
            }
            return -1;
        }
    }
}
