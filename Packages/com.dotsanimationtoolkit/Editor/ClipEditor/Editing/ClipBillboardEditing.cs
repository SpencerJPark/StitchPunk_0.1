// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Reads and writes authored billboard tracks at a point in time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart to <see cref="ClipTransformEditing"/> and <see cref="ClipSpriteEditing"/> for
    /// the billboard channels, and it splits the difference between them because a billboard key
    /// carries both kinds of channel. Angle and blend weight are continuous and are eased between
    /// keys; <c>enabled</c> is a discrete instruction that fires at a moment and is held from its
    /// key, exactly as a flipbook index is (rule amendment A43). Sampling all three the same way
    /// would either make an enable flag flicker halfway through a segment or make the angle step.
    /// </para>
    /// <para>
    /// Easing is read through <c>ClipSampler.Ease</c>, so the value this shows at the playhead is
    /// the value playback produces there.
    /// </para>
    /// </remarks>
    public static class ClipBillboardEditing
    {
        /// <summary>The tracks animating one billboard root, with their indices in the clip's list.</summary>
        public static void CollectTracksForRoot(
            ClipAsset clip, uint rootStableId,
            List<BillboardTrack> tracks, List<int> trackIndices)
        {
            if (tracks != null)
            {
                tracks.Clear();
            }
            if (trackIndices != null)
            {
                trackIndices.Clear();
            }
            if (clip == null || clip.billboardTracks == null || rootStableId == 0u)
            {
                return;
            }

            for (int trackIndex = 0; trackIndex < clip.billboardTracks.Count; trackIndex++)
            {
                BillboardTrack track = clip.billboardTracks[trackIndex];
                if (track == null || track.rootStableId != rootStableId)
                {
                    continue;
                }
                if (tracks != null)
                {
                    tracks.Add(track);
                }
                if (trackIndices != null)
                {
                    trackIndices.Add(trackIndex);
                }
            }
        }

        /// <summary>The index of a key sitting at a time, or −1.</summary>
        public static int FindKeyIndexAt(BillboardTrack track, float normalizedTime)
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

        /// <summary>The index of the key holding the discrete channel at a time, or −1 when empty.</summary>
        public static int FindEffectiveKeyIndex(BillboardTrack track, float normalizedTime)
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
        /// The billboard channels at a time: angle and weight eased, enabled held.
        /// </summary>
        /// <returns>False when there is nothing to sample, leaving a rest value in the outputs.</returns>
        public static bool TryEvaluate(
            BillboardTrack track, float normalizedTime,
            out float angleOffsetDegrees, out float blendWeight, out bool enabled)
        {
            angleOffsetDegrees = 0f;
            blendWeight = 1f;
            enabled = true;

            if (track == null || track.keys == null || track.keys.Count == 0)
            {
                return false;
            }

            List<BillboardKey> keys = track.keys;
            if (keys.Count == 1 || normalizedTime <= keys[0].normalizedTime)
            {
                Read(keys[0], out angleOffsetDegrees, out blendWeight, out enabled);
                return true;
            }

            BillboardKey lastKey = keys[keys.Count - 1];
            if (normalizedTime >= lastKey.normalizedTime)
            {
                Read(lastKey, out angleOffsetDegrees, out blendWeight, out enabled);
                return true;
            }

            int previousIndex = 0;
            for (int keyIndex = 0; keyIndex < keys.Count - 1; keyIndex++)
            {
                if (keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    previousIndex = keyIndex;
                }
            }
            int nextIndex = Mathf.Min(previousIndex + 1, keys.Count - 1);

            BillboardKey previousKey = keys[previousIndex];
            BillboardKey nextKey = keys[nextIndex];

            // Held from the left key whatever the easing does, because it is an instruction rather
            // than an approximation of anything between two moments.
            enabled = previousKey.enabled;

            if (previousKey.interpolation == Interpolation.Step)
            {
                angleOffsetDegrees = previousKey.angleOffsetDegrees;
                blendWeight = previousKey.blendWeight;
                return true;
            }

            float keySpan = nextKey.normalizedTime - previousKey.normalizedTime;
            float linearWeight = keySpan > 0f
                ? (normalizedTime - previousKey.normalizedTime) / keySpan
                : 0f;
            float easedWeight = ClipSampler.Ease(
                linearWeight, previousKey.interpolation,
                in previousKey.bezierStartHandle, in previousKey.bezierEndHandle);

            angleOffsetDegrees = Mathf.Lerp(
                previousKey.angleOffsetDegrees, nextKey.angleOffsetDegrees, easedWeight);
            blendWeight = Mathf.Lerp(previousKey.blendWeight, nextKey.blendWeight, easedWeight);
            return true;
        }

        /// <summary>
        /// Writes the billboard channels into the key at a time, creating that key if there is none.
        /// </summary>
        /// <returns>The index of the key written, or −1 when there was no track to write into.</returns>
        /// <remarks>
        /// A new key inherits the easing of the key before it — mode and handles both — for the
        /// reason the transform and bone tracks do: a Bézier with no handles is read as linear, so
        /// inheriting the mode alone would flatten the one segment somebody had shaped by hand.
        /// </remarks>
        public static int SetKeyValues(
            BillboardTrack track, float normalizedTime,
            float angleOffsetDegrees, float blendWeight, bool enabled)
        {
            if (track == null)
            {
                return -1;
            }
            if (track.keys == null)
            {
                track.keys = new List<BillboardKey>();
            }

            blendWeight = Mathf.Clamp01(blendWeight);

            int existingIndex = FindKeyIndexAt(track, normalizedTime);
            if (existingIndex >= 0)
            {
                BillboardKey existingKey = track.keys[existingIndex];
                existingKey.angleOffsetDegrees = angleOffsetDegrees;
                existingKey.blendWeight = blendWeight;
                existingKey.enabled = enabled;
                track.keys[existingIndex] = existingKey;
                return existingIndex;
            }

            BillboardKey insertedKey = new BillboardKey
            {
                normalizedTime = normalizedTime,
                angleOffsetDegrees = angleOffsetDegrees,
                blendWeight = blendWeight,
                enabled = enabled
            };
            insertedKey.interpolation = InheritInterpolationAt(
                track, normalizedTime,
                out insertedKey.bezierStartHandle, out insertedKey.bezierEndHandle);

            track.keys.Add(insertedKey);
            track.keys.Sort(CompareKeyTimes);
            return FindKeyIndexAt(track, normalizedTime);
        }

        private static void Read(
            BillboardKey key, out float angleOffsetDegrees, out float blendWeight, out bool enabled)
        {
            angleOffsetDegrees = key.angleOffsetDegrees;
            blendWeight = key.blendWeight;
            enabled = key.enabled;
        }

        private static Interpolation InheritInterpolationAt(
            BillboardTrack track, float normalizedTime,
            out float2 bezierStartHandle, out float2 bezierEndHandle)
        {
            Interpolation inherited = Interpolation.Linear;
            bezierStartHandle = float2.zero;
            bezierEndHandle = float2.zero;
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                if (track.keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    inherited = track.keys[keyIndex].interpolation;
                    bezierStartHandle = track.keys[keyIndex].bezierStartHandle;
                    bezierEndHandle = track.keys[keyIndex].bezierEndHandle;
                }
            }
            return inherited;
        }

        private static int CompareKeyTimes(BillboardKey first, BillboardKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }
    }
}
