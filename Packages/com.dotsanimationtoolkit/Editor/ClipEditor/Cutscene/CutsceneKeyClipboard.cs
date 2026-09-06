// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Copy/paste buffer for cutscene timeline items. Times are held relative to the earliest copied
    /// item, so a paste lands the group at the playhead with its rhythm intact; it survives switching
    /// cutscenes but not a domain reload.
    /// </summary>
    public static class CutsceneKeyClipboard
    {
        // One copied item, held as its boxed serialized value: SerializedProperty.boxedValue deep
        // copies both the struct lanes and the class ones, so nothing here aliases the source asset.
        private struct CopiedItem
        {
            public SelectedLaneKind laneKind;

            /// <summary>−1 for the cutscene-scoped lanes; otherwise where a paste with no target slot puts it back.</summary>
            public int sourceSlotIndex;

            /// <summary>The part's tag, so a paste finds the same role on a different slot. 0 for every other lane.</summary>
            public uint partTrackTagId;

            /// <summary>Seconds after the earliest copied item.</summary>
            public float relativeTime;

            public object value;
        }

        private static readonly List<CopiedItem> copiedItems = new List<CopiedItem>();

        public static bool HasContent
        {
            get { return copiedItems.Count > 0; }
        }

        public static int ItemCount
        {
            get { return copiedItems.Count; }
        }

        public static void Clear()
        {
            copiedItems.Clear();
        }

        /// <summary>Replaces the buffer with the given selection of <paramref name="cutscene"/>.</summary>
        public static int Copy(CutsceneAsset cutscene, IEnumerable<CutsceneItemAddress> addresses)
        {
            Clear();
            if (cutscene == null || addresses == null)
            {
                return 0;
            }

            // Its own SerializedObject, read-only: copying must not disturb whatever pending edits
            // the panel's own instance is holding.
            SerializedObject reader = new SerializedObject(cutscene);
            float earliestTime = float.MaxValue;
            foreach (CutsceneItemAddress address in addresses)
            {
                if (!address.HasItem)
                {
                    continue;
                }
                SerializedProperty listProperty;
                if (!CutsceneLaneEditing.TryGetLaneListProperty(
                        reader, address.laneKind, address.slotIndex, address.partTrackIndex, out listProperty))
                {
                    continue;
                }
                if (address.itemIndex >= listProperty.arraySize)
                {
                    continue;
                }

                SerializedProperty itemProperty = listProperty.GetArrayElementAtIndex(address.itemIndex);
                float itemTime = itemProperty
                    .FindPropertyRelative(CutsceneLaneEditing.TimeFieldNameFor(address.laneKind)).floatValue;
                copiedItems.Add(new CopiedItem
                {
                    laneKind = address.laneKind,
                    sourceSlotIndex = address.slotIndex,
                    partTrackTagId = ResolvePartTrackTagId(cutscene, address),
                    relativeTime = itemTime,
                    value = itemProperty.boxedValue
                });
                earliestTime = itemTime < earliestTime ? itemTime : earliestTime;
            }

            if (copiedItems.Count == 0)
            {
                return 0;
            }
            for (int itemIndex = 0; itemIndex < copiedItems.Count; itemIndex++)
            {
                CopiedItem item = copiedItems[itemIndex];
                item.relativeTime -= earliestTime;
                copiedItems[itemIndex] = item;
            }
            return copiedItems.Count;
        }

        /// <summary>
        /// Writes the buffer back at <paramref name="playheadSeconds"/>. Slot-scoped items land on
        /// <paramref name="targetSlotIndex"/> when one is given and back on their source slot when it
        /// is −1; the cutscene-scoped lanes ignore it entirely. The caller applies and re-reads.
        /// </summary>
        public static int Paste(
            CutsceneAsset cutscene, SerializedObject serializedObject, float playheadSeconds,
            int targetSlotIndex, List<CutsceneItemAddress> pastedAddresses)
        {
            if (pastedAddresses != null)
            {
                pastedAddresses.Clear();
            }
            if (cutscene == null || serializedObject == null || copiedItems.Count == 0)
            {
                return 0;
            }

            // Every lane a paste touched, so each is sorted once at the end rather than per item —
            // and so the indices handed back are the post-sort ones.
            List<CutsceneItemAddress> touchedLanes = new List<CutsceneItemAddress>();
            List<List<int>> touchedIndices = new List<List<int>>();

            int writtenCount = 0;
            for (int itemIndex = 0; itemIndex < copiedItems.Count; itemIndex++)
            {
                CopiedItem item = copiedItems[itemIndex];
                int destinationSlotIndex;
                int destinationPartTrackIndex;
                if (!TryResolveDestination(
                        serializedObject, item, targetSlotIndex,
                        out destinationSlotIndex, out destinationPartTrackIndex))
                {
                    continue;
                }

                SerializedProperty listProperty;
                if (!CutsceneLaneEditing.TryGetLaneListProperty(
                        serializedObject, item.laneKind, destinationSlotIndex, destinationPartTrackIndex,
                        out listProperty))
                {
                    continue;
                }

                int insertedIndex = listProperty.arraySize;
                listProperty.InsertArrayElementAtIndex(insertedIndex);
                SerializedProperty inserted = listProperty.GetArrayElementAtIndex(insertedIndex);
                inserted.boxedValue = item.value;
                inserted.FindPropertyRelative(CutsceneLaneEditing.TimeFieldNameFor(item.laneKind)).floatValue =
                    playheadSeconds + item.relativeTime;
                writtenCount++;

                CutsceneItemAddress laneAddress = new CutsceneItemAddress(
                    item.laneKind == SelectedLaneKind.CameraKey || item.laneKind == SelectedLaneKind.Event
                        || item.laneKind == SelectedLaneKind.Hold
                        ? -1 : destinationSlotIndex,
                    item.laneKind, destinationPartTrackIndex, -1);
                int laneSlot = touchedLanes.IndexOf(laneAddress);
                if (laneSlot < 0)
                {
                    touchedLanes.Add(laneAddress);
                    touchedIndices.Add(new List<int>());
                    laneSlot = touchedLanes.Count - 1;
                }
                touchedIndices[laneSlot].Add(insertedIndex);
            }

            for (int laneIndex = 0; laneIndex < touchedLanes.Count; laneIndex++)
            {
                CutsceneItemAddress laneAddress = touchedLanes[laneIndex];
                SerializedProperty listProperty;
                if (!CutsceneLaneEditing.TryGetLaneListProperty(
                        serializedObject, laneAddress.laneKind, laneAddress.slotIndex,
                        laneAddress.partTrackIndex, out listProperty))
                {
                    continue;
                }
                List<int> indices = touchedIndices[laneIndex];
                CutsceneLaneEditing.SortByTime(
                    listProperty, CutsceneLaneEditing.TimeFieldNameFor(laneAddress.laneKind), indices);
                if (pastedAddresses == null)
                {
                    continue;
                }
                for (int cursor = 0; cursor < indices.Count; cursor++)
                {
                    pastedAddresses.Add(new CutsceneItemAddress(
                        laneAddress.slotIndex, laneAddress.laneKind, laneAddress.partTrackIndex,
                        indices[cursor]));
                }
            }
            return writtenCount;
        }

        // Where one copied item lands: which slot, and for a part-track key which track on it. The
        // track is found by tag rather than by position, so a paste onto a slot whose tracks were
        // authored in another order still reaches the same role.
        private static bool TryResolveDestination(
            SerializedObject serializedObject, CopiedItem item, int targetSlotIndex,
            out int destinationSlotIndex, out int destinationPartTrackIndex)
        {
            destinationSlotIndex = -1;
            destinationPartTrackIndex = -1;

            if (item.laneKind == SelectedLaneKind.CameraKey || item.laneKind == SelectedLaneKind.Event
                || item.laneKind == SelectedLaneKind.Hold)
            {
                return true;
            }

            SerializedProperty slotsProperty = serializedObject.FindProperty("slots");
            destinationSlotIndex = targetSlotIndex >= 0 ? targetSlotIndex : item.sourceSlotIndex;
            if (destinationSlotIndex < 0 || destinationSlotIndex >= slotsProperty.arraySize)
            {
                return false;
            }
            if (item.laneKind != SelectedLaneKind.PartTrackKey)
            {
                return true;
            }

            SerializedProperty partTracksProperty = slotsProperty
                .GetArrayElementAtIndex(destinationSlotIndex).FindPropertyRelative("partTracks");
            for (int trackIndex = 0; trackIndex < partTracksProperty.arraySize; trackIndex++)
            {
                if (partTracksProperty.GetArrayElementAtIndex(trackIndex)
                        .FindPropertyRelative("tagId").uintValue == item.partTrackTagId)
                {
                    destinationPartTrackIndex = trackIndex;
                    return true;
                }
            }

            destinationPartTrackIndex = partTracksProperty.arraySize;
            partTracksProperty.InsertArrayElementAtIndex(destinationPartTrackIndex);
            SerializedProperty createdTrack =
                partTracksProperty.GetArrayElementAtIndex(destinationPartTrackIndex);
            createdTrack.FindPropertyRelative("tagId").uintValue = item.partTrackTagId;
            createdTrack.FindPropertyRelative("channels").enumValueFlag = (int)AnimatedChannels.PositionXY;
            createdTrack.FindPropertyRelative("keys").ClearArray();
            return true;
        }

        private static uint ResolvePartTrackTagId(CutsceneAsset cutscene, CutsceneItemAddress address)
        {
            if (address.laneKind != SelectedLaneKind.PartTrackKey
                || address.slotIndex < 0 || address.slotIndex >= cutscene.slots.Count)
            {
                return 0u;
            }
            CutsceneSlot slot = cutscene.slots[address.slotIndex];
            if (slot == null || address.partTrackIndex < 0 || address.partTrackIndex >= slot.partTracks.Count)
            {
                return 0u;
            }
            return slot.partTracks[address.partTrackIndex].tagId;
        }
    }
}
