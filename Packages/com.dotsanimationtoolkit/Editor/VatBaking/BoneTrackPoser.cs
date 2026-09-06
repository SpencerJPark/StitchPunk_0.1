// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Poses a skinned hierarchy from authored <see cref="BoneTrack"/>s so the VAT baker can
    /// capture it, as an alternative posing step to <c>AnimationMode.SampleAnimationClip</c>.
    /// </summary>
    public sealed class BoneTrackPoser
    {
        private readonly Dictionary<string, Transform> bonesByName = new Dictionary<string, Transform>();
        private readonly List<Transform> posedBones = new List<Transform>();
        private readonly List<Vector3> originalLocalPositions = new List<Vector3>();
        private readonly List<Quaternion> originalLocalRotations = new List<Quaternion>();
        private readonly List<Vector3> originalLocalScales = new List<Vector3>();

        /// <summary>Bone names that matched nothing in the hierarchy, in encounter order.</summary>
        public List<string> UnresolvedBoneNames { get; } = new List<string>();

        // Call once per bake, not per sample: the hierarchy does not change between frames, and a
        // full tree walk per bone per frame is how a bake of a real rig becomes unusable.
        public void Bind(Transform rootTransform)
        {
            bonesByName.Clear();
            UnresolvedBoneNames.Clear();
            ClearRestoreState();

            Transform[] hierarchy = rootTransform.GetComponentsInChildren<Transform>(true);
            for (int boneIndex = 0; boneIndex < hierarchy.Length; boneIndex++)
            {
                // First wins. Duplicate names in an imported rig are legal but ambiguous; taking the
                // first is deterministic, and the alternative — failing the bake — would reject rigs
                // that animate perfectly well because of a name collision on a bone nobody touches.
                if (!bonesByName.ContainsKey(hierarchy[boneIndex].name))
                {
                    bonesByName.Add(hierarchy[boneIndex].name, hierarchy[boneIndex]);
                }
            }
        }

        // Call after any imported clip has posed the hierarchy — authored keys are meant to
        // override imported motion on the bones they name, not the other way round.
        public void ApplyTracks(List<BoneTrack> boneTracks, float normalizedTime)
        {
            if (boneTracks == null)
            {
                return;
            }

            for (int trackIndex = 0; trackIndex < boneTracks.Count; trackIndex++)
            {
                BoneTrack boneTrack = boneTracks[trackIndex];
                if (boneTrack == null || string.IsNullOrEmpty(boneTrack.boneName))
                {
                    continue;
                }
                if (boneTrack.keys == null || boneTrack.keys.Count == 0)
                {
                    continue;
                }

                Transform boneTransform;
                if (!bonesByName.TryGetValue(boneTrack.boneName, out boneTransform))
                {
                    // Reported, never substituted. A bone silently left at rest looks like an
                    // animation that "just doesn't play", which is far harder to trace back to a
                    // renamed bone than a named warning is.
                    if (!UnresolvedBoneNames.Contains(boneTrack.boneName))
                    {
                        UnresolvedBoneNames.Add(boneTrack.boneName);
                    }
                    continue;
                }

                RememberOriginal(boneTransform);

                float3 sampledPosition;
                quaternion sampledRotation;
                float3 sampledScale;
                Sample(boneTrack.keys, normalizedTime, out sampledPosition, out sampledRotation, out sampledScale);

                boneTransform.localPosition =
                    new Vector3(sampledPosition.x, sampledPosition.y, sampledPosition.z);
                boneTransform.localRotation =
                    new Quaternion(sampledRotation.value.x, sampledRotation.value.y,
                        sampledRotation.value.z, sampledRotation.value.w);
                boneTransform.localScale = new Vector3(sampledScale.x, sampledScale.y, sampledScale.z);
            }
        }

        /// <summary>
        /// Samples a bone track, matching <see cref="ClipSampler"/>'s segment search and easing.
        /// </summary>
        public static void Sample(
            List<BoneKey> keys,
            float normalizedTime,
            out float3 position,
            out quaternion rotation,
            out float3 scale)
        {
            BoneKey firstKey = keys[0];
            if (keys.Count == 1 || normalizedTime <= firstKey.normalizedTime)
            {
                position = firstKey.localPosition;
                rotation = firstKey.localRotation;
                scale = firstKey.localScale;
                return;
            }

            BoneKey lastKey = keys[keys.Count - 1];
            if (normalizedTime >= lastKey.normalizedTime)
            {
                position = lastKey.localPosition;
                rotation = lastKey.localRotation;
                scale = lastKey.localScale;
                return;
            }

            int segmentIndex = 0;
            for (int keyIndex = 0; keyIndex < keys.Count - 1; keyIndex++)
            {
                if (normalizedTime >= keys[keyIndex].normalizedTime
                    && normalizedTime <= keys[keyIndex + 1].normalizedTime)
                {
                    segmentIndex = keyIndex;
                    break;
                }
            }

            BoneKey fromKey = keys[segmentIndex];
            BoneKey toKey = keys[segmentIndex + 1];
            float segmentSpan = toKey.normalizedTime - fromKey.normalizedTime;

            // A zero-length segment would divide by zero. Two keys at the same time are invalid,
            // but a baker must not produce NaN geometry while the author is still fixing it.
            float linearTime = segmentSpan > 1e-6f
                ? (normalizedTime - fromKey.normalizedTime) / segmentSpan
                : 0f;

            // The easing belongs to the FROM key, matching ClipSampler: a key's interpolation
            // describes how the curve leaves it.
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation,
                in fromKey.bezierStartHandle, in fromKey.bezierEndHandle);

            position = math.lerp(fromKey.localPosition, toKey.localPosition, easedTime);
            // slerp, not lerp — a component-wise lerp on a quaternion shortens the arc and
            // de-normalises, which reads as the joint speeding up mid-swing.
            rotation = math.slerp(fromKey.localRotation, toKey.localRotation, easedTime);
            scale = math.lerp(fromKey.localScale, toKey.localScale, easedTime);
        }

        // Call from a finally block. Unlike AnimationMode, direct Transform writes are not
        // auto-restored — skipping this leaves the user's rig stuck in the last sampled pose.
        public void RestoreOriginalPose()
        {
            for (int boneIndex = 0; boneIndex < posedBones.Count; boneIndex++)
            {
                Transform boneTransform = posedBones[boneIndex];
                if (boneTransform == null)
                {
                    continue;
                }
                boneTransform.localPosition = originalLocalPositions[boneIndex];
                boneTransform.localRotation = originalLocalRotations[boneIndex];
                boneTransform.localScale = originalLocalScales[boneIndex];
            }
            ClearRestoreState();
        }

        private void RememberOriginal(Transform boneTransform)
        {
            // Recorded on first touch only. Recording every frame would capture the pose this bake
            // already applied, and "restore" would restore the animation rather than the rest pose.
            if (posedBones.Contains(boneTransform))
            {
                return;
            }
            posedBones.Add(boneTransform);
            originalLocalPositions.Add(boneTransform.localPosition);
            originalLocalRotations.Add(boneTransform.localRotation);
            originalLocalScales.Add(boneTransform.localScale);
        }

        private void ClearRestoreState()
        {
            posedBones.Clear();
            originalLocalPositions.Clear();
            originalLocalRotations.Clear();
            originalLocalScales.Clear();
        }
    }
}
