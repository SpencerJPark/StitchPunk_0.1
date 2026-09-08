// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Turns a skinned source mesh into the ordinary mesh a VAT shader renders, packing bone
    /// indices into <c>UV1</c> and weights into <c>UV2</c>, since a plain <see cref="MeshRenderer"/>
    /// does not bind <c>BLENDINDICES</c>/<c>BLENDWEIGHT</c> the way a <see cref="SkinnedMeshRenderer"/> does.
    /// </summary>
    public static class VatMeshPreparer
    {
        /// <summary>
        /// Builds a render-ready copy of <paramref name="sourceRenderer"/>'s mesh with the four bone
        /// indices in <c>UV1</c> and their four weights in <c>UV2</c>.
        /// </summary>
        /// <param name="runtimeMesh">The prepared mesh, or null on failure.</param>
        /// <param name="failureMessage">Why preparation failed; empty on success.</param>
        /// <returns>False when there was nothing usable to prepare.</returns>
        public static bool TryCreateRuntimeMesh(
            SkinnedMeshRenderer sourceRenderer,
            out Mesh runtimeMesh,
            out string failureMessage)
        {
            runtimeMesh = null;
            failureMessage = string.Empty;

            if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
            {
                failureMessage = "No skinned mesh renderer to prepare a runtime mesh from.";
                return false;
            }

            Mesh sourceMesh = sourceRenderer.sharedMesh;
            BoneWeight[] sourceWeights = sourceMesh.boneWeights;
            if (sourceWeights == null || sourceWeights.Length == 0)
            {
                failureMessage =
                    "Source mesh '" + sourceMesh.name + "' carries no bone weights, so no bone " +
                    "influences can be packed. A bone-flavour VAT needs them; a vertex-flavour " +
                    "bake does not use this mesh path at all.";
                return false;
            }

            // Instantiated rather than mutated: the source mesh is a shared asset, and writing UV1
            // into it would silently change every other renderer using it.
            runtimeMesh = Object.Instantiate(sourceMesh);
            runtimeMesh.name = sourceMesh.name + "_VatRuntime";

            // One channel per kind, four influences each: ToolkitVatCrowdUnlit wires UV1 into the
            // VAT Bone Skin node's Bone Indices and UV2 into its Bone Weights, and VatBoneSkin
            // iterates all four. Packing an (index, index, weight, weight) pair into UV1 alone
            // instead feeds weights in as indices and leaves the weights reading an absent channel,
            // which renders as a mesh that inflates with distance from the root rather than skinning.
            List<Vector4> packedBoneIndices = new List<Vector4>(sourceWeights.Length);
            List<Vector4> packedBoneWeights = new List<Vector4>(sourceWeights.Length);
            for (int vertexIndex = 0; vertexIndex < sourceWeights.Length; vertexIndex++)
            {
                BoneWeight boneWeight = sourceWeights[vertexIndex];
                packedBoneIndices.Add(new Vector4(
                    boneWeight.boneIndex0,
                    boneWeight.boneIndex1,
                    boneWeight.boneIndex2,
                    boneWeight.boneIndex3));
                packedBoneWeights.Add(new Vector4(
                    boneWeight.weight0,
                    boneWeight.weight1,
                    boneWeight.weight2,
                    boneWeight.weight3));
            }
            runtimeMesh.SetUVs(1, packedBoneIndices);
            runtimeMesh.SetUVs(2, packedBoneWeights);

            // The skinning data is cleared deliberately. Leaving it on an asset that is no longer
            // rendered as a skinned mesh invites a future reader to bind it back to a
            // SkinnedMeshRenderer, which would then skin on the CPU *and* displace in the shader —
            // a double deformation that looks like a rig explosion rather than a wiring mistake.
            runtimeMesh.boneWeights = new BoneWeight[0];
            runtimeMesh.bindposes = new Matrix4x4[0];

            return true;
        }
    }
}
