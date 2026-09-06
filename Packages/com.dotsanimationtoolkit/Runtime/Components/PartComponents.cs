// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Part-entity binding back to its actor. <see cref="targetIndex"/> is plain data and survives
    /// instantiation; <see cref="actorRoot"/> is rewritten after ECB instantiation by <c>RigBindingSystem</c>.
    /// </summary>
    public struct RigPartBinding : IComponentData
    {
        public Entity actorRoot;

        public int targetIndex; // position in ClipRegistryBlob.sortedTargetIds
    }

    /// <summary>
    /// How a part is presented for the direction it faces; written by the host, never by this
    /// package. Optional — access via a <c>ComponentLookup</c> with <c>HasComponent</c>, never as a
    /// job parameter, which would silently exclude every part that never opted in.
    /// </summary>
    public struct PartFacing : IComponentData
    {
        public int viewOffset; // frames stepped from the rest slice for an alt view; 0 = art doesn't change with facing

        // Reflects the composited pose about the actor's vertical axis: negates localPosition.x,
        // rotationZ, and scale.x — not merely the drawn art.
        public bool mirrorX;
    }

    /// <summary>
    /// Marks a part whose mirror is already applied by a <c>facesDirection</c> ancestor. Baked by
    /// <c>RigTargetBaker</c>; <c>TransformSampleSystem</c> skips this part's own negation, since
    /// scale composes down the hierarchy and a second negation would cancel the first.
    /// </summary>
    public struct PartMirrorFromAncestor : IComponentData
    {
    }

    /// <summary>
    /// The part's authored rest pose, captured from the authoring transform at bake. Composition
    /// starts from this pose every sample; host design/skin systems change the base look by writing
    /// <see cref="restSliceIndex"/>.
    /// </summary>
    public struct TargetRestPose : IComponentData
    {
        public float3 localPosition; // z is the 2.5D draw-layer order

        public float3 rotation; // radians, Euler ZXY

        public float3 scale;

        public int restSliceIndex; // used when no sprite track overrides it
    }

    /// <summary>The part's sampled output pose, written by <c>TransformSampleSystem</c> and consumed by the apply/material systems.</summary>
    public struct TargetPose : IComponentData
    {
        public float3 localPosition; // z is the 2.5D draw-layer order

        public float3 rotation; // radians, Euler ZXY

        public float3 scale; // negative components flip

        public int sliceIndex; // seeded from TargetRestPose.restSliceIndex; only a non-negative slice key overwrites it, so this is always a renderable frame

        public float4 atlasRect; // scale.xy, offset.zw
    }

    /// <summary>Part-level declaration of which playback layer drives a VAT part. Added by <c>RigTargetBaker</c> to <see cref="TargetKind.VatMesh"/> parts; read by <c>VatMaterialSystem</c>.</summary>
    public struct VatDriven : IComponentData
    {
        public byte layerIndex;
    }
}
