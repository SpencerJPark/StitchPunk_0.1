// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Clickable joint markers for every bone of the previewed rig, drawn as one line mesh — an
    /// octahedron at each joint plus a line to its parent, since a bone has no geometry of its own
    /// to click. One mesh for the whole skeleton rather than a GameObject per joint.
    /// </summary>
    public sealed class PreviewBoneHandles
    {
        /// <summary>An octahedron is twelve edges, and each edge is written as its own pair.</summary>
        private const int VerticesPerHandle = 24;

        /// <summary>The handle, plus the two-vertex line linking this joint to its parent.</summary>
        private const int VerticesPerBone = VerticesPerHandle + 2;

        private static readonly Color HandleColor = new Color(0.42f, 0.72f, 0.95f, 1f);
        private static readonly Color LinkColor = new Color(0.30f, 0.46f, 0.62f, 1f);

        private readonly List<Transform> boneTransforms = new List<Transform>();

        /// <summary>Each bone's parent within the bone set, or null when it has none.</summary>
        private readonly List<Transform> boneParents = new List<Transform>();

        private readonly List<Vector3> vertexBuffer = new List<Vector3>();

        private GameObject handlesObject;
        private Mesh handlesMesh;
        private Material lineMaterial;

        /// <summary>Every bone driving the previewed rig, deduplicated. The picker's targets.</summary>
        public IReadOnlyList<Transform> Bones
        {
            get { return boneTransforms; }
        }

        /// <summary>The marker mesh's root, or null when the rig has no skinned renderers.</summary>
        public GameObject HandlesObject
        {
            get { return handlesObject; }
        }

        /// <summary>Whether there is anything to draw or pick.</summary>
        public bool HasBones
        {
            get { return boneTransforms.Count > 0; }
        }

        // Collects the bones of skeletonRoot and builds their marker mesh. Bones come from
        // SkinnedMeshRenderer.bones rather than a hierarchy walk, which would offer handles on
        // every empty, prop and mesh node too.
        public void Rebuild(GameObject skeletonRoot)
        {
            Dispose();

            if (skeletonRoot == null)
            {
                return;
            }

            SkinnedMeshRenderer[] skinnedRenderers =
                skeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            HashSet<Transform> collectedBones = new HashSet<Transform>();
            for (int rendererIndex = 0; rendererIndex < skinnedRenderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer skinnedRenderer = skinnedRenderers[rendererIndex];
                if (skinnedRenderer == null)
                {
                    continue;
                }

                // The root bone is not always in the bones array — it is the transform the skin is
                // anchored to, and a rig that never weights it directly would otherwise lose the one
                // joint most likely to be clicked first.
                if (skinnedRenderer.rootBone != null && collectedBones.Add(skinnedRenderer.rootBone))
                {
                    boneTransforms.Add(skinnedRenderer.rootBone);
                }

                Transform[] bones = skinnedRenderer.bones;
                for (int boneIndex = 0; bones != null && boneIndex < bones.Length; boneIndex++)
                {
                    if (bones[boneIndex] != null && collectedBones.Add(bones[boneIndex]))
                    {
                        boneTransforms.Add(bones[boneIndex]);
                    }
                }
            }

            if (boneTransforms.Count == 0)
            {
                return;
            }

            for (int boneIndex = 0; boneIndex < boneTransforms.Count; boneIndex++)
            {
                Transform parent = boneTransforms[boneIndex].parent;
                boneParents.Add(parent != null && collectedBones.Contains(parent) ? parent : null);
            }

            BuildHandlesObject();
        }

        // Rewrites the marker positions for the current pose. Vertices only — index and colour
        // buffers are built once in Rebuild, since every bone always writes exactly
        // VerticesPerBone entries (a parentless bone writes a zero-length link instead of skipping).
        // The drawn radius here must match the picker's, or click targets drift from the markers.
        public void UpdateGeometry(float handleRadius)
        {
            if (handlesMesh == null || boneTransforms.Count == 0)
            {
                return;
            }

            vertexBuffer.Clear();
            for (int boneIndex = 0; boneIndex < boneTransforms.Count; boneIndex++)
            {
                Transform bone = boneTransforms[boneIndex];
                if (bone == null)
                {
                    for (int padIndex = 0; padIndex < VerticesPerBone; padIndex++)
                    {
                        vertexBuffer.Add(Vector3.zero);
                    }
                    continue;
                }

                AppendHandle(bone.position, handleRadius);

                Transform parent = boneParents[boneIndex];
                vertexBuffer.Add(bone.position);
                vertexBuffer.Add(parent != null ? parent.position : bone.position);
            }

            handlesMesh.SetVertices(vertexBuffer);
            handlesMesh.RecalculateBounds();
        }

        /// <summary>Writes an octahedron's twelve edges, world-axis aligned, around a joint.</summary>
        private void AppendHandle(Vector3 center, float radius)
        {
            Vector3 right = new Vector3(radius, 0f, 0f);
            Vector3 up = new Vector3(0f, radius, 0f);
            Vector3 forward = new Vector3(0f, 0f, radius);

            AppendEdge(center + right, center + up);
            AppendEdge(center + right, center - up);
            AppendEdge(center + right, center + forward);
            AppendEdge(center + right, center - forward);
            AppendEdge(center - right, center + up);
            AppendEdge(center - right, center - up);
            AppendEdge(center - right, center + forward);
            AppendEdge(center - right, center - forward);
            AppendEdge(center + up, center + forward);
            AppendEdge(center + up, center - forward);
            AppendEdge(center - up, center + forward);
            AppendEdge(center - up, center - forward);
        }

        private void AppendEdge(Vector3 start, Vector3 end)
        {
            vertexBuffer.Add(start);
            vertexBuffer.Add(end);
        }

        private void BuildHandlesObject()
        {
            int vertexCount = boneTransforms.Count * VerticesPerBone;

            int[] indices = new int[vertexCount];
            Color[] colors = new Color[vertexCount];
            for (int boneIndex = 0; boneIndex < boneTransforms.Count; boneIndex++)
            {
                int baseVertex = boneIndex * VerticesPerBone;
                for (int localIndex = 0; localIndex < VerticesPerBone; localIndex++)
                {
                    indices[baseVertex + localIndex] = baseVertex + localIndex;
                    colors[baseVertex + localIndex] =
                        localIndex < VerticesPerHandle ? HandleColor : LinkColor;
                }
            }

            handlesMesh = new Mesh();
            handlesMesh.name = "ClipPreviewBoneHandlesMesh";
            handlesMesh.hideFlags = HideFlags.HideAndDontSave;

            // Rewritten every frame as the pose changes, which is exactly what MarkDynamic is for.
            handlesMesh.MarkDynamic();
            handlesMesh.SetVertices(new List<Vector3>(new Vector3[vertexCount]));
            handlesMesh.SetColors(new List<Color>(colors));
            handlesMesh.SetIndices(indices, MeshTopology.Lines, 0);

            lineMaterial = PreviewLineMaterial.Create("ClipPreviewBoneHandles");

            handlesObject = new GameObject("ClipPreviewBoneHandles");
            handlesObject.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter meshFilter = handlesObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = handlesMesh;

            MeshRenderer meshRenderer = handlesObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = lineMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>Destroys the marker object, its mesh and its material. Idempotent.</summary>
        public void Dispose()
        {
            boneTransforms.Clear();
            boneParents.Clear();
            vertexBuffer.Clear();

            if (handlesObject != null)
            {
                Object.DestroyImmediate(handlesObject);
                handlesObject = null;
            }
            if (handlesMesh != null)
            {
                Object.DestroyImmediate(handlesMesh);
                handlesMesh = null;
            }
            if (lineMaterial != null)
            {
                Object.DestroyImmediate(lineMaterial);
                lineMaterial = null;
            }
        }
    }
}
