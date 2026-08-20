// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// Turns a skinned source mesh into the ordinary mesh a VAT shader renders
    /// (architecture section 4.7; the mesh half of the section 6.2 shader contract).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Bone influences move into <c>UV1</c>, and that is the whole reason this exists.</strong>
    /// A plain <see cref="MeshRenderer"/> does not bind <c>BLENDINDICES</c>/<c>BLENDWEIGHT</c> —
    /// those semantics are bound by the GPU skinning path for a <see cref="SkinnedMeshRenderer"/>.
    /// Not needing a <see cref="SkinnedMeshRenderer"/> is the entire value of VAT, so the bone data
    /// has to travel as ordinary per-vertex data, and <c>TEXCOORD1</c> is where
    /// <c>ToolkitVatCrowdUnlit.shadergraph</c> reads it from.
    /// </para>
    /// <para>
    /// <strong>This shipped in the package because leaving it out was a real hole.</strong> The
    /// packing previously existed only in a demo script in the host project, so a bake through
    /// <c>VatBakeWindow</c> produced textures and left
    /// <c>VatTextureSetAsset.runtimeMesh</c> null — a field nothing in the package ever wrote. A
    /// consumer had to discover the requirement from a document and hand-write the loop, and
    /// forgetting it does not error: the mesh renders as a motionless clump, because every vertex
    /// reads bone 0 at weight 0.
    /// </para>
    /// <para>
    /// Two influences, not four, matching the crowd shader's budget (§12 R3).
    /// <c>ToolkitVat.hlsl</c>'s <c>VatBoneSkin</c> handles up to four and skips any with
    /// non-positive weight, so a host that genuinely needs four can pass its own pair of
    /// <c>float4</c>s instead — this helper covers the shipped reference path.
    /// </para>
    /// </remarks>
    public static class VatMeshPreparer
    {
        /// <summary>
        /// Builds a render-ready copy of <paramref name="sourceRenderer"/>'s mesh with bone
        /// influences packed into <c>UV1</c> as <c>(index0, index1, weight0, weight1)</c>.
        /// </summary>
        /// <param name="sourceRenderer">The skinned renderer the VAT bake sampled.</param>
        /// <param name="runtimeMesh">The prepared mesh, or null on failure.</param>
        /// <param name="failureMessage">Why preparation failed; empty on success.</param>
        /// <returns>False when there was nothing usable to prepare.</returns>
        /// <remarks>
        /// Returns a failure rather than throwing, matching <c>VatTextureBaker.Bake</c> — a baker
        /// that throws cannot be driven from a batch script over a content library, which is
        /// exactly when a mesh with no bone weights turns up.
        /// </remarks>
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

            List<Vector4> packedBoneData = new List<Vector4>(sourceWeights.Length);
            for (int vertexIndex = 0; vertexIndex < sourceWeights.Length; vertexIndex++)
            {
                BoneWeight boneWeight = sourceWeights[vertexIndex];
                packedBoneData.Add(new Vector4(
                    boneWeight.boneIndex0,
                    boneWeight.boneIndex1,
                    boneWeight.weight0,
                    boneWeight.weight1));
            }
            runtimeMesh.SetUVs(1, packedBoneData);

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
