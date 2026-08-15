// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// A live instance of the rigged prefab, posed from authored <see cref="BoneTrack"/>s so bone
    /// rows scrub in the clip editor (amendment A42, phase B4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is what closes the loop the toolkit exists for:</strong> one timeline, scrubbed
    /// once, showing every technique at the same instant. Without it, bone tracks are authorable and
    /// bakeable but only visible <em>after</em> a bake — which puts a minutes-long round trip in the
    /// middle of what should be a keyframing loop, and is exactly the friction that makes people
    /// animate in two applications instead of one.
    /// </para>
    /// <para>
    /// <strong>The sampling is <see cref="BoneTrackPoser"/>'s, deliberately.</strong> The preview,
    /// the bake and the runtime must agree about what a curve means. Sharing the sampler makes them
    /// agree by construction; a second implementation here would drift, and the symptom would be a
    /// preview that teaches timing the bake does not reproduce.
    /// </para>
    /// <para>
    /// Optional throughout. A cutout clip set assigns no skinned source and this never instantiates
    /// anything, so the flipbook workflow pays nothing for a feature it does not use.
    /// </para>
    /// </remarks>
    public sealed class PreviewSkeletonMirror
    {
        private readonly Dictionary<string, Transform> bonesByName = new Dictionary<string, Transform>();
        private readonly List<string> unresolvedBoneNames = new List<string>();

        /// <summary>
        /// Every transform of the instance in depth-first order, and the reverse lookup.
        /// </summary>
        /// <remarks>
        /// This index is the identity the whole selection story runs on. The hierarchy tree, the
        /// viewport picker and the selection marker all name a transform by its position in this
        /// list rather than by its name, because names repeat — a rig with two bones called
        /// <c>Hand</c> would otherwise have a tree row that selects the wrong one, and a marker that
        /// draws on the wrong joint. Bone <em>tracks</em> still bind by name; that is a separate
        /// contract with the bake, and not something selection gets to change.
        /// </remarks>
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

        /// <summary>
        /// Instantiates <paramref name="skinnedSourcePrefab"/> into the preview and indexes its bones.
        /// </summary>
        /// <remarks>
        /// The name→<see cref="Transform"/> map is built once here rather than per tick. A full
        /// <c>GetComponentsInChildren</c> walk per bone at 30 Hz is the obvious way to make the
        /// editor crawl on a rig with any real bone count.
        /// </remarks>
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

        /// <summary>
        /// Walks the instance depth-first, numbering every transform.
        /// </summary>
        /// <remarks>
        /// Child order is <see cref="Transform.GetChild"/> order, which is the order the hierarchy
        /// tree displays. The tree does not repeat this walk to derive its own ids — it asks for the
        /// index of each transform as it builds — so there is no parallel ordering to keep in step.
        /// </remarks>
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

        /// <summary>
        /// The index of the first transform with this name, or -1.
        /// </summary>
        /// <remarks>
        /// "First" is the honest answer to an ambiguous question: a bone track names a bone, and if
        /// two transforms share that name the bake resolves it the same way. Selecting the same one
        /// the bake would use beats selecting a different one.
        /// </remarks>
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
