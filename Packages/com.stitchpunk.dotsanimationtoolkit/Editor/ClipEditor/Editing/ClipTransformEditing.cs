// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>What a displayed transform value currently is, which decides how it is drawn.</summary>
    public enum TransformValueState : byte
    {
        /// <summary>No track, or a track with no keys — the value is the rest pose.</summary>
        Unkeyed = 0,

        /// <summary>A key sits at the playhead; editing this value edits that key.</summary>
        OnKey = 1,

        /// <summary>Between keys; the value shown is the sampled result, not a stored one.</summary>
        Interpolated = 2,

        /// <summary>Edited but not written to a key — it will be lost when the playhead moves.</summary>
        Modified = 3
    }

    /// <summary>
    /// Reads and writes authored transform tracks at a point in time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the one place a transform value is written.</strong> The inspector's numeric
    /// fields and the viewport's gizmos both come through here, so a drag and a typed number produce
    /// the same key with the same rounding and the same insertion rules. Two write paths would drift
    /// in exactly the places that are hardest to notice — which key a value lands on, whether a new
    /// one is created, what happens at a time that already has one.
    /// </para>
    /// <para>
    /// It samples the <em>authored</em> keys rather than a built blob, because the editor edits
    /// assets and a blob is a bake of them. The easing comes from <c>ClipSampler.Ease</c>, so what
    /// the fields show while scrubbing is the curve the runtime will play, not a second
    /// approximation of it — the same discipline <c>BoneTrackPoser</c> follows.
    /// </para>
    /// <para>
    /// Rotation is in <strong>degrees</strong> here, as it is on the authored key. The bake converts
    /// once to radians (architecture section 4.5 point 2); an editor that showed radians would be
    /// the only surface in the toolkit that did.
    /// </para>
    /// </remarks>
    public static class ClipTransformEditing
    {
        /// <summary>
        /// How close a key must be to the playhead to count as "at" it.
        /// </summary>
        /// <remarks>
        /// In normalized clip time, so a tolerance of 1e-4 is a tenth of a frame on a 1000-frame
        /// clip. It exists because the playhead lands on floats: a key placed by a click at
        /// 0.3333333 and a playhead at 0.33333331 are the same key to everyone except an equality
        /// test.
        /// </remarks>
        public const float KeyTimeTolerance = 1e-4f;

        /// <summary>The first transform track aimed at a target, or null.</summary>
        public static TransformTrack FindTransformTrack(ClipAsset clip, uint targetId)
        {
            if (clip == null || clip.transformTracks == null)
            {
                return null;
            }
            for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
            {
                TransformTrack track = clip.transformTracks[trackIndex];
                if (track != null && track.targetId == targetId)
                {
                    return track;
                }
            }
            return null;
        }

        /// <summary>The index of the key at a time, or −1 when none is close enough.</summary>
        public static int FindKeyIndexAt(TransformTrack track, float normalizedTime)
        {
            if (track == null || track.keys == null)
            {
                return -1;
            }
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                if (Mathf.Abs(track.keys[keyIndex].normalizedTime - normalizedTime) <= KeyTimeTolerance)
                {
                    return keyIndex;
                }
            }
            return -1;
        }

        /// <summary>
        /// Samples a track at a time, clamping outside its first and last keys.
        /// </summary>
        /// <returns>False when there is nothing to sample, leaving the neutral pose in the outputs.</returns>
        public static bool TryEvaluate(
            TransformTrack track, float normalizedTime,
            out float3 position, out float3 rotationDegrees, out float3 scale)
        {
            position = float3.zero;
            rotationDegrees = float3.zero;
            scale = new float3(1f, 1f, 1f);

            if (track == null || track.keys == null || track.keys.Count == 0)
            {
                return false;
            }

            List<TransformKey> keys = track.keys;
            if (keys.Count == 1 || normalizedTime <= keys[0].normalizedTime)
            {
                TransformKey onlyKey = keys[0];
                position = onlyKey.position;
                rotationDegrees = onlyKey.rotation;
                scale = onlyKey.scale;
                return true;
            }

            TransformKey lastKey = keys[keys.Count - 1];
            if (normalizedTime >= lastKey.normalizedTime)
            {
                position = lastKey.position;
                rotationDegrees = lastKey.rotation;
                scale = lastKey.scale;
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

            TransformKey previousKey = keys[previousIndex];
            TransformKey nextKey = keys[nextIndex];

            if (previousKey.interpolation == Interpolation.Step)
            {
                position = previousKey.position;
                rotationDegrees = previousKey.rotation;
                scale = previousKey.scale;
                return true;
            }

            float keySpan = nextKey.normalizedTime - previousKey.normalizedTime;
            float linearWeight = keySpan > 0f
                ? (normalizedTime - previousKey.normalizedTime) / keySpan
                : 0f;
            float easedWeight = ClipSampler.Ease(
                linearWeight, previousKey.interpolation,
                in previousKey.bezierStartHandle, in previousKey.bezierEndHandle);

            position = math.lerp(previousKey.position, nextKey.position, easedWeight);
            rotationDegrees = math.lerp(previousKey.rotation, nextKey.rotation, easedWeight);
            scale = math.lerp(previousKey.scale, nextKey.scale, easedWeight);
            return true;
        }

        /// <summary>
        /// Writes a transform value into the key at a time, creating that key if there is none.
        /// </summary>
        /// <returns>The index of the key written, or −1 when there was no track to write into.</returns>
        /// <remarks>
        /// A new key inherits the interpolation of the key before it rather than the type default,
        /// so keying halfway through a stepped run does not silently turn that segment linear. The
        /// list is kept sorted here rather than by the caller, because a key inserted out of order
        /// is invisible to validation rule V03 until something else sorts it.
        /// </remarks>
        public static int SetKeyValues(
            TransformTrack track, float normalizedTime,
            float3 position, float3 rotationDegrees, float3 scale)
        {
            if (track == null)
            {
                return -1;
            }
            if (track.keys == null)
            {
                track.keys = new List<TransformKey>();
            }

            int existingIndex = FindKeyIndexAt(track, normalizedTime);
            if (existingIndex >= 0)
            {
                TransformKey existingKey = track.keys[existingIndex];
                existingKey.position = position;
                existingKey.rotation = rotationDegrees;
                existingKey.scale = scale;
                track.keys[existingIndex] = existingKey;
                return existingIndex;
            }

            TransformKey insertedKey = new TransformKey
            {
                normalizedTime = normalizedTime,
                position = position,
                rotation = rotationDegrees,
                scale = scale,
                interpolation = InheritInterpolationAt(track, normalizedTime)
            };
            track.keys.Add(insertedKey);
            track.keys.Sort(CompareKeyTimes);
            return FindKeyIndexAt(track, normalizedTime);
        }

        private static Interpolation InheritInterpolationAt(TransformTrack track, float normalizedTime)
        {
            Interpolation inherited = Interpolation.Linear;
            for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
            {
                if (track.keys[keyIndex].normalizedTime <= normalizedTime)
                {
                    inherited = track.keys[keyIndex].interpolation;
                }
            }
            return inherited;
        }

        private static int CompareKeyTimes(TransformKey first, TransformKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }
    }
}
