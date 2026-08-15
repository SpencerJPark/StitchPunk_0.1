// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// Reads and writes authored flipbook tracks at a point in time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flipbook counterpart to <see cref="ClipTransformEditing"/>, and for the same reason: the
    /// inspector shows the value at the playhead and writes edits back to a key, so both need one
    /// implementation of "what is this track showing right now" rather than two that drift.
    /// </para>
    /// <para>
    /// <strong>Evaluation is nearest-key, matching <c>ClipSampler.SampleSpriteTrack</c>.</strong>
    /// An index cannot be halfway between two frames, so a flipbook does not interpolate — it snaps
    /// at the midpoint between keys. An editor that interpolated here would show a frame the runtime
    /// never displays.
    /// </para>
    /// </remarks>
    public static class ClipSpriteEditing
    {
        /// <summary>Every flipbook track on a clip that drives one rig target.</summary>
        /// <remarks>
        /// A list rather than a single track: several tracks per target is how one texture array
        /// holds independent feature sets, and each carries its own base index.
        /// </remarks>
        public static void CollectTracksForTarget(
            ClipAsset clip, uint targetId, List<SpriteTrack> tracks, List<int> trackIndices)
        {
            tracks.Clear();
            trackIndices.Clear();
            if (clip == null || clip.spriteTracks == null)
            {
                return;
            }
            for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
            {
                SpriteTrack track = clip.spriteTracks[trackIndex];
                if (track != null && track.targetId == targetId)
                {
                    tracks.Add(track);
                    trackIndices.Add(trackIndex);
                }
            }
        }

        /// <summary>The index of the key at a time, or −1 when none is close enough.</summary>
        public static int FindKeyIndexAt(SpriteTrack track, float normalizedTime)
        {
            if (track == null || track.keys == null)
            {
                return -1;
            }
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                float keyTime = track.keys[keyIndex].normalizedTime;
                if (Mathf.Abs(keyTime - normalizedTime) <= ClipTransformEditing.KeyTimeTolerance)
                {
                    return keyIndex;
                }
            }
            return -1;
        }

        /// <summary>
        /// The index of the key a time resolves to under nearest-key selection, or −1 for an empty
        /// track.
        /// </summary>
        public static int FindEffectiveKeyIndex(SpriteTrack track, float normalizedTime)
        {
            if (track == null || track.keys == null || track.keys.Count == 0)
            {
                return -1;
            }
            if (track.keys.Count == 1)
            {
                return 0;
            }

            int previousIndex = 0;
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                if (track.keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    previousIndex = keyIndex;
                }
            }
            if (previousIndex >= track.keys.Count - 1)
            {
                return track.keys.Count - 1;
            }

            int nextIndex = previousIndex + 1;
            float previousTime = track.keys[previousIndex].normalizedTime;
            float span = track.keys[nextIndex].normalizedTime - previousTime;
            if (span <= 0f)
            {
                return previousIndex;
            }

            // The midpoint rule the runtime uses: below it the left key wins, at or above it the
            // right one does.
            float weight = (normalizedTime - previousTime) / span;
            return weight < 0.5f ? previousIndex : nextIndex;
        }

        /// <summary>
        /// Writes a stored value and mode into the key at a time, creating that key if there is none.
        /// </summary>
        /// <returns>The index of the key written, or −1 when there was no track to write into.</returns>
        public static int SetKeyValue(
            SpriteTrack track, float normalizedTime, int storedValue, SpriteIndexMode indexMode)
        {
            if (track == null)
            {
                return -1;
            }
            if (track.keys == null)
            {
                track.keys = new List<SpriteKey>();
            }

            int existingIndex = FindKeyIndexAt(track, normalizedTime);
            if (existingIndex >= 0)
            {
                SpriteKey existingKey = track.keys[existingIndex];
                existingKey.sliceIndex = storedValue;
                existingKey.indexMode = indexMode;
                track.keys[existingIndex] = existingKey;
                return existingIndex;
            }

            track.keys.Add(new SpriteKey
            {
                normalizedTime = normalizedTime,
                sliceIndex = storedValue,
                indexMode = indexMode
            });
            track.keys.Sort(CompareKeyTimes);
            return FindKeyIndexAt(track, normalizedTime);
        }

        private static int CompareKeyTimes(SpriteKey first, SpriteKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }
    }
}
