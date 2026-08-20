// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Poses a skinned hierarchy from authored <see cref="BoneTrack"/>s so the VAT baker can capture
    /// it (amendment A42, phase B2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is a second <em>source</em> for the existing bake, not a second output.</strong>
    /// <c>VatTextureBaker</c> already walks a clip frame by frame, poses the hierarchy, and reads
    /// <c>bones[i].localToWorldMatrix</c>. Today the posing step is
    /// <c>AnimationMode.SampleAnimationClip</c>; this class is the alternative posing step. Nothing
    /// downstream — matrix capture, texel layout, the loop-safe duplicate frame, socket sampling —
    /// changes at all, which is what makes authored bones a small feature rather than a rewrite.
    /// </para>
    /// <para>
    /// <strong>The easing is <see cref="ClipSampler"/>'s, not a local reimplementation.</strong> An
    /// authored curve must bake to exactly what the clip editor previewed and what the runtime
    /// would produce for the same keys. A second easing function here would drift from the first
    /// and the difference would show up as animation that looks subtly wrong only after baking —
    /// the most expensive kind of bug to trace, because the authoring tool and the result disagree.
    /// </para>
    /// <para>
    /// <strong>Rotation slerps.</strong> Component-wise lerp on a quaternion is not a rotation
    /// interpolation; it shortens the arc and de-normalises, which reads as a joint that speeds up
    /// through the middle of its swing and subtly shrinks the mesh around it.
    /// </para>
    /// </remarks>
    public sealed class BoneTrackPoser
    {
        private readonly Dictionary<string, Transform> bonesByName = new Dictionary<string, Transform>();
        private readonly List<Transform> posedBones = new List<Transform>();
        private readonly List<Vector3> originalLocalPositions = new List<Vector3>();
        private readonly List<Quaternion> originalLocalRotations = new List<Quaternion>();
        private readonly List<Vector3> originalLocalScales = new List<Vector3>();

        /// <summary>Bone names that matched nothing in the hierarchy, in encounter order.</summary>
        public List<string> UnresolvedBoneNames { get; } = new List<string>();

        /// <summary>
        /// Indexes <paramref name="rootTransform"/>'s hierarchy by bone name.
        /// </summary>
        /// <remarks>
        /// Built once per bake rather than per sample: the hierarchy does not change between frames,
        /// and a full tree walk per bone per frame is how a bake of a real rig becomes unusable.
        /// </remarks>
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

        /// <summary>
        /// Writes every track's sampled local TRS onto its bone at <paramref name="normalizedTime"/>.
        /// </summary>
        /// <remarks>
        /// Applied <em>after</em> any imported clip has posed the hierarchy, so authored keys
        /// override imported motion on the bones they name. That order is the useful one: the case
        /// this feature exists for is an imported walk cycle with a hand-authored arm on top, and it
        /// makes an authored track the more specific statement of intent. The reverse would let an
        /// imported clip silently erase deliberate hand-authoring.
        /// </remarks>
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

            // A zero-length segment would divide by zero. Two keys at the same time are a validation
            // error (V03), but a baker must not produce NaN geometry while the author is still
            // fixing it — the clip editor is live against invalid clips by design.
            float linearTime = segmentSpan > 1e-6f
                ? (normalizedTime - fromKey.normalizedTime) / segmentSpan
                : 0f;

            // The easing belongs to the FROM key, matching ClipSampler: a key's interpolation
            // describes how the curve leaves it.
            float easedTime = ClipSampler.Ease(
                linearTime, fromKey.interpolation,
                in fromKey.bezierStartHandle, in fromKey.bezierEndHandle);

            position = math.lerp(fromKey.localPosition, toKey.localPosition, easedTime);
            rotation = math.slerp(fromKey.localRotation, toKey.localRotation, easedTime);
            scale = math.lerp(fromKey.localScale, toKey.localScale, easedTime);
        }

        /// <summary>
        /// Restores every bone this poser wrote to the transform it had before the bake.
        /// </summary>
        /// <remarks>
        /// <strong>Not optional.</strong> <c>AnimationMode</c> restores what it posed; direct writes
        /// to a Transform do not. Without this, baking leaves the user's rig permanently stuck in
        /// the last sampled pose of the last clip — a destructive edit to their scene as a side
        /// effect of a read-only-looking operation. Call it from a <c>finally</c>.
        /// </remarks>
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
