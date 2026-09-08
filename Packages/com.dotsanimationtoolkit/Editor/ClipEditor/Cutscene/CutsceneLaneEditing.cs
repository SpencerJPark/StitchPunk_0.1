// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEditor;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Serialized-list operations every cutscene lane shares: finding its list, naming its time field, and sorting it without losing track of a selection.</summary>
    public static class CutsceneLaneEditing
    {
        /// <summary>The field a lane's items carry their timeline position in. A clip block starts rather than happens.</summary>
        public static string TimeFieldNameFor(SelectedLaneKind laneKind)
        {
            return laneKind == SelectedLaneKind.ClipBlock ? "start" : "time";
        }

        /// <summary>The serialized list one lane's items live in, or false when the lane no longer resolves.</summary>
        public static bool TryGetLaneListProperty(
            SerializedObject serializedObject, SelectedLaneKind laneKind, int slotIndex, int partTrackIndex,
            out SerializedProperty listProperty)
        {
            listProperty = null;
            if (serializedObject == null)
            {
                return false;
            }

            switch (laneKind)
            {
                case SelectedLaneKind.CameraKey:
                    listProperty = serializedObject.FindProperty("cameraLane").FindPropertyRelative("keys");
                    return true;
                case SelectedLaneKind.Event:
                    listProperty = serializedObject.FindProperty("events");
                    return true;
                case SelectedLaneKind.Hold:
                    listProperty = serializedObject.FindProperty("holdMarkers");
                    return true;
            }

            SerializedProperty slotsProperty = serializedObject.FindProperty("slots");
            if (slotsProperty == null || slotIndex < 0 || slotIndex >= slotsProperty.arraySize)
            {
                return false;
            }
            SerializedProperty slotProperty = slotsProperty.GetArrayElementAtIndex(slotIndex);

            switch (laneKind)
            {
                case SelectedLaneKind.ClipBlock:
                    listProperty = slotProperty.FindPropertyRelative("clipBlocks");
                    return true;
                case SelectedLaneKind.RootTransformKey:
                    listProperty = slotProperty.FindPropertyRelative("transformKeys");
                    return true;
                case SelectedLaneKind.FacingKey:
                    listProperty = slotProperty.FindPropertyRelative("facingKeys");
                    return true;
                case SelectedLaneKind.AttachMarker:
                    listProperty = slotProperty.FindPropertyRelative("attachMarkers");
                    return true;
                case SelectedLaneKind.MarkKey:
                    listProperty = slotProperty.FindPropertyRelative("markKeys");
                    return true;
                case SelectedLaneKind.LayerStopKey:
                    listProperty = slotProperty.FindPropertyRelative("layerStops");
                    return true;
                case SelectedLaneKind.PartTrackKey:
                {
                    SerializedProperty partTracksProperty = slotProperty.FindPropertyRelative("partTracks");
                    if (partTrackIndex < 0 || partTrackIndex >= partTracksProperty.arraySize)
                    {
                        return false;
                    }
                    listProperty = partTracksProperty
                        .GetArrayElementAtIndex(partTrackIndex).FindPropertyRelative("keys");
                    return true;
                }
            }
            return false;
        }

        // An insertion sort carrying a selected flag beside each element, so the caller learns where
        // its items landed. Matching them back up by time afterwards breaks the moment two items
        // share one, which a paste at the playhead makes ordinary.
        /// <summary>Sorts a lane by time, rewriting <paramref name="trackedIndices"/> to where those items ended up.</summary>
        public static void SortByTime(
            SerializedProperty listProperty, string timeFieldName, List<int> trackedIndices)
        {
            int count = listProperty.arraySize;
            bool[] isTracked = new bool[count];
            if (trackedIndices != null)
            {
                for (int cursor = 0; cursor < trackedIndices.Count; cursor++)
                {
                    if (trackedIndices[cursor] >= 0 && trackedIndices[cursor] < count)
                    {
                        isTracked[trackedIndices[cursor]] = true;
                    }
                }
            }

            for (int upper = 1; upper < count; upper++)
            {
                int cursor = upper;
                while (cursor > 0 &&
                    listProperty.GetArrayElementAtIndex(cursor - 1).FindPropertyRelative(timeFieldName).floatValue >
                    listProperty.GetArrayElementAtIndex(cursor).FindPropertyRelative(timeFieldName).floatValue)
                {
                    listProperty.MoveArrayElement(cursor, cursor - 1);
                    bool swapped = isTracked[cursor - 1];
                    isTracked[cursor - 1] = isTracked[cursor];
                    isTracked[cursor] = swapped;
                    cursor--;
                }
            }

            if (trackedIndices == null)
            {
                return;
            }
            trackedIndices.Clear();
            for (int itemIndex = 0; itemIndex < count; itemIndex++)
            {
                if (isTracked[itemIndex])
                {
                    trackedIndices.Add(itemIndex);
                }
            }
        }
    }
}
