// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
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
    /// Reads and writes authored transform tracks at a point in time. This is the one place a
    /// transform value is written — the inspector's numeric fields and the viewport's gizmos both
    /// come through here. Rotation is in degrees, as on the authored key; the bake converts once to radians.
    /// </summary>
    public static class ClipTransformEditing
    {
        // How close a key must be to the playhead to count as "at" it, in normalized clip time — a
        // key placed at 0.3333333 and a playhead at 0.33333331 are the same key to everyone but an equality test.
        public const float KeyTimeTolerance = 1e-4f;

        // The first transform track aimed at a target by id, or null. Blind to tag-bound tracks:
        // prefer FindTransformTrack(ClipAsset, RigAsset, uint) wherever a rig is available; this
        // overload remains for tests and callers that deliberately want the target-id-only view.
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

        // The first transform track that animates the rig target targetId, whether the track binds
        // it directly or through a tag that resolves to it. Without this, every editing and keying
        // path would find no track for a tag-bound node and mint a second, target-id-bound track alongside it.
        /// <param name="rig">
        /// The clip's rig, used only to resolve a tag-bound track's <c>tagId</c>. Null falls back to
        /// <see cref="FindTransformTrack(ClipAsset, uint)"/>'s target-id-only behaviour.
        /// </param>
        public static TransformTrack FindTransformTrack(ClipAsset clip, RigAsset rig, uint targetId)
        {
            if (clip == null || clip.transformTracks == null)
            {
                return null;
            }
            for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
            {
                TransformTrack track = clip.transformTracks[trackIndex];
                if (track != null && ClipComponentModel.TrackBindsTarget(track.targetId, track.tagId, targetId, rig))
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

        // Writes a transform value into the key at a time, creating that key if there is none. A
        // new key inherits the interpolation of the key before it rather than the type default. The
        // list is kept sorted here rather than by the caller.
        /// <returns>The index of the key written, or −1 when there was no track to write into.</returns>
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

            float2 inheritedStartHandle;
            float2 inheritedEndHandle;
            Interpolation inheritedInterpolation = InheritInterpolationAt(
                track, normalizedTime, out inheritedStartHandle, out inheritedEndHandle);
            TransformKey insertedKey = new TransformKey
            {
                normalizedTime = normalizedTime,
                position = position,
                rotation = rotationDegrees,
                scale = scale,
                interpolation = inheritedInterpolation,
                bezierStartHandle = inheritedStartHandle,
                bezierEndHandle = inheritedEndHandle
            };
            track.keys.Add(insertedKey);
            track.keys.Sort(CompareKeyTimes);
            return FindKeyIndexAt(track, normalizedTime);
        }

        // The easing of the key a new one lands after — mode and handles both, since a Bézier
        // without handles reads as linear and would flatten a hand-shaped segment.
        private static Interpolation InheritInterpolationAt(
            TransformTrack track, float normalizedTime,
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

        private static int CompareKeyTimes(TransformKey first, TransformKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }
    }
}
