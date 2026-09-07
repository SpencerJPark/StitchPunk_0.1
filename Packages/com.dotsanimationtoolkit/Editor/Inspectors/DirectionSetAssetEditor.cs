// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Custom inspector for <see cref="DirectionSetAsset"/>: the clip queue over its <see cref="DirectionSlots"/>, plus the target-directions field.</summary>
    [CustomEditor(typeof(DirectionSetAsset))]
    public sealed class DirectionSetAssetEditor : UnityEditor.Editor
    {
        private readonly HashSet<Direction> extraVisibleSlots = new HashSet<Direction>();
        private readonly List<Direction> visibleSlots = new List<Direction>();

        private SerializedProperty targetDirectionsProperty;
        private DirectionSetClipQueueView queueView;

        public override VisualElement CreateInspectorGUI()
        {
            targetDirectionsProperty = serializedObject.FindProperty("slots.targetDirections");

            VisualElement inspectorRoot = new VisualElement();
            inspectorRoot.style.paddingTop = 4f;

            if (targetDirectionsProperty != null)
            {
                PropertyField targetDirectionsField =
                    new PropertyField(targetDirectionsProperty, "Target Directions");
                targetDirectionsField.RegisterValueChangeCallback(changeEvent => RebuildQueue());
                inspectorRoot.Add(targetDirectionsField);
            }

            queueView = new DirectionSetClipQueueView();
            queueView.SlotAssigned += OnSlotAssigned;
            queueView.SlotMoved += OnSlotMoved;
            queueView.SlotCleared += OnSlotCleared;
            queueView.OpenClipRequested += OnOpenClipRequested;
            inspectorRoot.Add(queueView);

            // Catches Undo/Redo and any other external edit — the queue reads the target directly
            // rather than through a SerializedProperty, so nothing else would refresh it.
            inspectorRoot.TrackSerializedObjectValue(serializedObject, changedObject => RebuildQueue());
            inspectorRoot.Bind(serializedObject);
            RebuildQueue();
            return inspectorRoot;
        }

        private DirectionSlots TargetSlots
        {
            get
            {
                DirectionSetAsset directionSetAsset = target as DirectionSetAsset;
                return directionSetAsset != null ? directionSetAsset.slots : null;
            }
        }

        private void OnSlotAssigned(Direction slot, ClipAsset clip)
        {
            DirectionSlots slots = TargetSlots;
            if (slots == null || slots.GetSlot(slot) == clip)
            {
                return;
            }
            Undo.RecordObject(target, "Assign Direction Slot");
            slots.SetSlot(slot, clip);
            EditorUtility.SetDirty(target);
            RebuildQueue();
        }

        private void OnSlotMoved(Direction fromSlot, Direction toSlot)
        {
            DirectionSlots slots = TargetSlots;
            if (slots == null)
            {
                return;
            }

            ClipAsset movedClip = slots.GetSlot(fromSlot);
            Undo.RecordObject(target, "Move Direction Slot");
            slots.SetSlot(fromSlot, null);
            slots.SetSlot(toSlot, movedClip);
            EditorUtility.SetDirty(target);

            extraVisibleSlots.Add(fromSlot);
            extraVisibleSlots.Add(toSlot);
            RebuildQueue();
        }

        private void OnSlotCleared(Direction slot)
        {
            DirectionSlots slots = TargetSlots;
            if (slots == null)
            {
                return;
            }
            Undo.RecordObject(target, "Clear Direction Slot");
            slots.SetSlot(slot, null);
            EditorUtility.SetDirty(target);
            extraVisibleSlots.Remove(slot);
            RebuildQueue();
        }

        private static void OnOpenClipRequested(ClipAsset clip)
        {
            if (clip == null)
            {
                return;
            }
            ClipEditorWindow.FocusClipEditing();
            EditorGUIUtility.PingObject(clip);
            Selection.activeObject = clip;
        }

        private void RebuildQueue()
        {
            DirectionSlots slots = TargetSlots;
            visibleSlots.Clear();

            if (slots != null)
            {
                Direction[] requiredSlots = DirectionSlots.GetRequiredSlots(slots.targetDirections);
                for (int slotIndex = 0; slotIndex < DirectionSetClipQueueView.SlotOrder.Length; slotIndex++)
                {
                    Direction slot = DirectionSetClipQueueView.SlotOrder[slotIndex];
                    bool isRequired = System.Array.IndexOf(requiredSlots, slot) >= 0;
                    if (isRequired || slots.GetSlot(slot) != null || extraVisibleSlots.Contains(slot))
                    {
                        visibleSlots.Add(slot);
                    }
                }
            }

            if (queueView != null)
            {
                queueView.Rebuild(slots, visibleSlots, null);
            }
        }
    }
}
