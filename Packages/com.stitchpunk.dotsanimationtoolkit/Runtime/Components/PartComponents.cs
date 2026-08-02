// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Part-entity binding back to its actor (architecture section 5.2). The dense
    /// <see cref="targetIndex"/> is plain data and survives instantiation;
    /// <see cref="actorRoot"/> is to be rewritten after ECB instantiation by
    /// <c>RigBindingSystem</c> (section 5.3), which build step C4 will add.
    /// </summary>
    public struct RigPartBinding : IComponentData
    {
        /// <summary>The actor-root entity that owns this part.</summary>
        public Entity actorRoot;

        /// <summary>The part's dense target index (position in <see cref="ClipRegistryBlob.sortedTargetIds"/>).</summary>
        public int targetIndex;
    }

    /// <summary>
    /// The part's authored rest pose, captured from the authoring transform at bake
    /// (architecture section 5.2). Composition starts from this pose every sample; host
    /// design/skin systems change the base look by writing <see cref="restSliceIndex"/>
    /// (section 5.7).
    /// </summary>
    public struct TargetRestPose : IComponentData
    {
        /// <summary>Rest local position; z is the 2.5D draw-layer order.</summary>
        public float3 localPosition;

        /// <summary>Rest rotation about z in radians.</summary>
        public float rotationZ;

        /// <summary>Rest non-uniform x/y scale.</summary>
        public float2 scale;

        /// <summary>Rest sprite slice index used when no sprite track overrides it.</summary>
        public int restSliceIndex;
    }

    /// <summary>
    /// The part's sampled output pose, written by <c>TransformSampleSystem</c> and consumed by the
    /// apply/material systems (architecture sections 5.2, 5.6, 5.7).
    /// </summary>
    public struct TargetPose : IComponentData
    {
        /// <summary>Sampled local position; z is the 2.5D draw-layer order.</summary>
        public float3 localPosition;

        /// <summary>Sampled rotation about z in radians.</summary>
        public float rotationZ;

        /// <summary>Sampled non-uniform x/y scale; negative values flip.</summary>
        public float2 scale;

        /// <summary>
        /// Sampled sprite slice index. Composition seeds it from
        /// <see cref="TargetRestPose.restSliceIndex"/> and only a slice-mode sprite key with a
        /// non-negative index overwrites it, so the value is always a renderable frame (the −1
        /// "no change" convention lives on the authored key, never on the pose).
        /// </summary>
        public int sliceIndex;

        /// <summary>Sampled atlas rect: scale.xy, offset.zw.</summary>
        public float4 atlasRect;
    }

    /// <summary>
    /// Part-level declaration of which playback layer drives a VAT part
    /// (architecture section 5.8). Added by <c>RigTargetBaker</c> to
    /// <see cref="TargetKind.VatMesh"/> parts; read by <c>VatMaterialSystem</c>.
    /// </summary>
    public struct VatDriven : IComponentData
    {
        /// <summary>Index of the playback layer whose clip drives this part's VAT frames.</summary>
        public byte layerIndex;
    }
}
