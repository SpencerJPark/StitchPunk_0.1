// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Copy/paste buffer for timeline keys (architecture section 7.1, parity item "copy/paste").
    /// </summary>
    /// <remarks>
    /// <para>
    /// Times are stored <em>relative to the earliest copied key</em>, not absolutely. Paste then
    /// lands the group at the playhead with its internal rhythm intact — pasting absolute times
    /// would make paste a no-op whenever the source and destination happen to be the same clip,
    /// which is the common case.
    /// </para>
    /// <para>
    /// Track indices travel with the keys so a multi-track copy reassembles across tracks. A paste
    /// into a clip with fewer tracks drops the entries that have nowhere to go rather than
    /// inventing tracks — silently growing the destination rig would be the worse failure.
    /// </para>
    /// </remarks>
    public static class ClipKeyClipboard
    {
        private static readonly List<int> transformTrackIndices = new List<int>();
        private static readonly List<TransformKey> transformKeys = new List<TransformKey>();
        private static readonly List<int> spriteTrackIndices = new List<int>();
        private static readonly List<SpriteKey> spriteKeys = new List<SpriteKey>();
        private static readonly List<EventMarker> eventMarkers = new List<EventMarker>();

        /// <summary>Whether anything is on the clipboard.</summary>
        public static bool HasContent
        {
            get
            {
                return transformKeys.Count > 0 || spriteKeys.Count > 0 || eventMarkers.Count > 0;
            }
        }

        public static void Clear()
        {
            transformTrackIndices.Clear();
            transformKeys.Clear();
            spriteTrackIndices.Clear();
            spriteKeys.Clear();
            eventMarkers.Clear();
        }

        /// <summary>Replaces the buffer with the given selection of <paramref name="clip"/>.</summary>
        public static void Copy(ClipAsset clip, IEnumerable<KeyAddress> addresses)
        {
            Clear();
            if (clip == null)
            {
                return;
            }

            float earliestTime = float.MaxValue;
            foreach (KeyAddress address in addresses)
            {
                switch (address.trackKind)
                {
                    case TimelineTrackKind.Transform:
                    {
                        TransformKey key = clip.transformTracks[address.trackIndex].keys[address.keyIndex];
                        transformTrackIndices.Add(address.trackIndex);
                        transformKeys.Add(key);
                        earliestTime = key.normalizedTime < earliestTime ? key.normalizedTime : earliestTime;
                        break;
                    }
                    case TimelineTrackKind.Sprite:
                    {
                        SpriteKey key = clip.spriteTracks[address.trackIndex].keys[address.keyIndex];
                        spriteTrackIndices.Add(address.trackIndex);
                        spriteKeys.Add(key);
                        earliestTime = key.normalizedTime < earliestTime ? key.normalizedTime : earliestTime;
                        break;
                    }
                    default:
                    {
                        EventMarker marker = clip.events[address.keyIndex];
                        eventMarkers.Add(marker);
                        earliestTime = marker.normalizedTime < earliestTime ? marker.normalizedTime : earliestTime;
                        break;
                    }
                }
            }

            if (!HasContent)
            {
                return;
            }

            // Rebase to the earliest key so the buffer holds offsets, not positions.
            for (int keyIndex = 0; keyIndex < transformKeys.Count; keyIndex++)
            {
                TransformKey key = transformKeys[keyIndex];
                key.normalizedTime -= earliestTime;
                transformKeys[keyIndex] = key;
            }
            for (int keyIndex = 0; keyIndex < spriteKeys.Count; keyIndex++)
            {
                SpriteKey key = spriteKeys[keyIndex];
                key.normalizedTime -= earliestTime;
                spriteKeys[keyIndex] = key;
            }
            for (int markerIndex = 0; markerIndex < eventMarkers.Count; markerIndex++)
            {
                EventMarker marker = eventMarkers[markerIndex];
                marker.normalizedTime -= earliestTime;
                eventMarkers[markerIndex] = marker;
            }
        }

        /// <summary>
        /// Pastes the buffer into <paramref name="clip"/> anchored at <paramref name="atTime"/>.
        /// </summary>
        /// <remarks>
        /// The caller owns the undo group — this only mutates. Pasted keys are appended and the
        /// caller re-sorts, matching how a drag defers sorting to the end of the gesture.
        /// </remarks>
        /// <returns>How many keys were actually placed.</returns>
        public static int Paste(ClipAsset clip, float atTime)
        {
            if (clip == null || !HasContent)
            {
                return 0;
            }

            int pastedCount = 0;

            for (int keyIndex = 0; keyIndex < transformKeys.Count; keyIndex++)
            {
                int trackIndex = transformTrackIndices[keyIndex];
                if (clip.transformTracks == null || trackIndex >= clip.transformTracks.Count)
                {
                    continue;
                }
                TransformKey key = transformKeys[keyIndex];
                key.normalizedTime = atTime + key.normalizedTime;
                clip.transformTracks[trackIndex].keys.Add(key);
                pastedCount++;
            }

            for (int keyIndex = 0; keyIndex < spriteKeys.Count; keyIndex++)
            {
                int trackIndex = spriteTrackIndices[keyIndex];
                if (clip.spriteTracks == null || trackIndex >= clip.spriteTracks.Count)
                {
                    continue;
                }
                SpriteKey key = spriteKeys[keyIndex];
                key.normalizedTime = atTime + key.normalizedTime;
                clip.spriteTracks[trackIndex].keys.Add(key);
                pastedCount++;
            }

            for (int markerIndex = 0; markerIndex < eventMarkers.Count; markerIndex++)
            {
                EventMarker marker = eventMarkers[markerIndex];
                marker.normalizedTime = atTime + marker.normalizedTime;
                clip.events.Add(marker);
                pastedCount++;
            }

            return pastedCount;
        }
    }
}
