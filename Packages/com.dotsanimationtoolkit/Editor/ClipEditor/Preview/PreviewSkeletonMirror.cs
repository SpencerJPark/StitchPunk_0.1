// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A live instance of the rigged prefab, posed from authored <see cref="BoneTrack"/>s so bone
    /// rows scrub in the clip editor. Sampling goes through <see cref="BoneTrackPoser"/>, the same
    /// sampler the bake and runtime use. Optional throughout: a cutout clip set with no skinned
    /// source never instantiates anything.
    /// </summary>
    public sealed class PreviewSkeletonMirror
    {
        private readonly Dictionary<string, Transform> bonesByName = new Dictionary<string, Transform>();
        private readonly List<string> unresolvedBoneNames = new List<string>();

        // Every transform of the instance in depth-first order, and the reverse lookup. This index
        // is the identity the whole selection story runs on, since names repeat (two bones both
        // called "Hand"). Bone tracks still bind by name — a separate contract with the bake.
        private readonly List<Transform> transformsByIndex = new List<Transform>();
        private readonly Dictionary<Transform, int> indexByTransform = new Dictionary<Transform, int>();

        private GameObject instanceRoot;

        /// <summary>The instantiated rig, or null when no skinned source is assigned.</summary>
        public GameObject InstanceRoot
        {
            get { return instanceRoot; }
        }

        /// <summary>Every transform of the instance, depth-first. Index is the selection identity.</summary>
        public IReadOnlyList<Transform> TransformsByIndex
        {
            get { return transformsByIndex; }
        }

        /// <summary>Bone names on the clip that matched nothing in the instantiated hierarchy.</summary>
        public IReadOnlyList<string> UnresolvedBoneNames
        {
            get { return unresolvedBoneNames; }
        }

        // Instantiates skinnedSourcePrefab into the preview and indexes its bones. The name→
        // Transform map is built once here rather than per tick, to avoid a full hierarchy walk at 30 Hz.
        public void Rebuild(GameObject skinnedSourcePrefab)
        {
            Dispose();

            if (skinnedSourcePrefab == null)
            {
                return;
            }

            instanceRoot = Object.Instantiate(skinnedSourcePrefab);

            // Named after the prefab rather than after this class, because the hierarchy pane shows
            // this instance directly and "ClipPreviewSkeleton" as a root row would be the window
            // naming its own plumbing at the user.
            instanceRoot.name = skinnedSourcePrefab.name;

            // Planted on the origin explicitly rather than left wherever the prefab's own root
            // transform happens to sit. 0,0,0 is the point the camera orbits, the point the floor
            // grid centres on, and the point every part's rest pose is measured from — a prefab
            // authored ten metres off would put the whole preview somewhere the camera never looks.
            instanceRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Without HideAndDontSave the instance leaks into the user's open scene and, worse,
            // gets saved into it.
            instanceRoot.hideFlags = HideFlags.HideAndDontSave;

            IndexHierarchy(instanceRoot.transform);

            for (int boneIndex = 0; boneIndex < transformsByIndex.Count; boneIndex++)
            {
                Transform node = transformsByIndex[boneIndex];
                if (!bonesByName.ContainsKey(node.name))
                {
                    bonesByName.Add(node.name, node);
                }
            }
        }

        // Walks the instance depth-first, numbering every transform, in Transform.GetChild order —
        // the same order the hierarchy tree displays.
        private void IndexHierarchy(Transform node)
        {
            indexByTransform[node] = transformsByIndex.Count;
            transformsByIndex.Add(node);
            for (int childIndex = 0; childIndex < node.childCount; childIndex++)
            {
                IndexHierarchy(node.GetChild(childIndex));
            }
        }

        /// <summary>The selection index of a transform, or -1 when it is not part of the instance.</summary>
        public int GetIndex(Transform node)
        {
            int index;
            if (node == null || !indexByTransform.TryGetValue(node, out index))
            {
                return -1;
            }
            return index;
        }

        /// <summary>Resolves a selection index back to its transform, or null when out of range.</summary>
        public Transform GetTransformByIndex(int index)
        {
            if (index < 0 || index >= transformsByIndex.Count)
            {
                return null;
            }
            return transformsByIndex[index];
        }

        // The index of the first transform with this name, or -1 — matching how the bake resolves a
        // bone track when two transforms share a name.
        public int FindIndexByName(string boneName)
        {
            Transform bone;
            if (string.IsNullOrEmpty(boneName) || !bonesByName.TryGetValue(boneName, out bone))
            {
                return -1;
            }
            return GetIndex(bone);
        }

        /// <summary>Poses every bone track at <paramref name="normalizedTime"/>.</summary>
        public void ApplyBoneTracks(List<BoneTrack> boneTracks, float normalizedTime)
        {
            unresolvedBoneNames.Clear();
            if (instanceRoot == null || boneTracks == null)
            {
                return;
            }

            for (int trackIndex = 0; trackIndex < boneTracks.Count; trackIndex++)
            {
                BoneTrack boneTrack = boneTracks[trackIndex];
                if (boneTrack == null
                    || string.IsNullOrEmpty(boneTrack.boneName)
                    || boneTrack.keys == null
                    || boneTrack.keys.Count == 0)
                {
                    continue;
                }

                Transform boneTransform;
                if (!bonesByName.TryGetValue(boneTrack.boneName, out boneTransform))
                {
                    if (!unresolvedBoneNames.Contains(boneTrack.boneName))
                    {
                        unresolvedBoneNames.Add(boneTrack.boneName);
                    }
                    continue;
                }

                float3 sampledPosition;
                quaternion sampledRotation;
                float3 sampledScale;
                BoneTrackPoser.Sample(
                    boneTrack.keys, normalizedTime,
                    out sampledPosition, out sampledRotation, out sampledScale);

                boneTransform.localPosition =
                    new Vector3(sampledPosition.x, sampledPosition.y, sampledPosition.z);
                boneTransform.localRotation =
                    new Quaternion(sampledRotation.value.x, sampledRotation.value.y,
                        sampledRotation.value.z, sampledRotation.value.w);
                boneTransform.localScale =
                    new Vector3(sampledScale.x, sampledScale.y, sampledScale.z);
            }
        }

        /// <summary>Resolves a bone by name, for socket markers that follow one.</summary>
        public bool TryGetBone(string boneName, out Transform boneTransform)
        {
            boneTransform = null;
            if (instanceRoot == null || string.IsNullOrEmpty(boneName))
            {
                return false;
            }
            return bonesByName.TryGetValue(boneName, out boneTransform);
        }

        /// <summary>Destroys the instance. Idempotent.</summary>
        public void Dispose()
        {
            if (instanceRoot != null)
            {
                Object.DestroyImmediate(instanceRoot);
                instanceRoot = null;
            }
            bonesByName.Clear();
            unresolvedBoneNames.Clear();
            transformsByIndex.Clear();
            indexByTransform.Clear();
        }
    }
}
