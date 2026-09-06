// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Reads and writes authored flipbook tracks at a point in time. Evaluation holds the last key,
    /// matching <c>ClipSampler.SampleSpriteTrack</c> — an index cannot be halfway between two
    /// frames, so a flipbook does not interpolate.
    /// </summary>
    public static class ClipSpriteEditing
    {
        // Every flipbook track on a clip that drives one rig target. A list rather than a single
        // track: several tracks per target is how one texture array holds independent feature sets.
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

        // The index of the key holding the track at a time, or −1 for an empty track. The last key
        // at or before the time, matching ClipSampler; before the first key, the first key holds.
        public static int FindEffectiveKeyIndex(SpriteTrack track, float normalizedTime)
        {
            if (track == null || track.keys == null || track.keys.Count == 0)
            {
                return -1;
            }

            int holdingIndex = 0;
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                if (track.keys[keyIndex].normalizedTime > normalizedTime)
                {
                    break;
                }
                holdingIndex = keyIndex;
            }
            return holdingIndex;
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
