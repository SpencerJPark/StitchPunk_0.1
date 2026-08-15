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
    /// How a part is presented for the direction it faces (architecture section 5.7,
    /// amendment A37). Written by the host, never by this package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Facing changes a cutout part in two unrelated ways, and conflating them is a
    /// mistake this amendment made once already.</strong> Some parts have a genuinely different
    /// <em>alt view</em> drawn for them — an ear seen from behind is different art from an ear seen
    /// from the front, so it is a different slice. Other parts are simply <em>mirrored</em> — a nose
    /// seen from the left is the same art as a nose seen from the right, reflected. One needs a
    /// different texture index; the other needs a negative scale. A part may need either, both, or
    /// neither, so they are separate fields rather than one packed value.
    /// </para>
    /// <para>
    /// The final slice is <c>restSliceIndex</c> (which variant this character rolled) +
    /// <see cref="viewOffset"/> (which way the part faces) + the clip's own key (which frame the
    /// animation is on). Three terms, three owners, none able to clobber another's.
    /// </para>
    /// <para>
    /// <strong>Why this is a component and not clip data.</strong> Facing would otherwise multiply
    /// into every clip that touches a sprite — a blink would need one variant per direction, and so
    /// would talking and every expression. Applied after composition, one facing term is inherited
    /// by every clip on every layer for free, so only locomotion needs direction variants. It also
    /// cannot be outranked by layer priority, which clip data cannot promise.
    /// </para>
    /// <para>
    /// Optional, like <c>AnimLod</c> (amendment A23): a part without it faces one way and reads as
    /// offset 0. Consumers must reach it through a <c>ComponentLookup</c> with a
    /// <c>HasComponent</c> check, never as a job parameter — taking it as a parameter would enrol
    /// the job in an <c>All</c> query for an opt-in component and silently exclude every part that
    /// never asked for facing.
    /// </para>
    /// </remarks>
    public struct PartFacing : IComponentData
    {
        /// <summary>
        /// Frames to step from the rest slice for an <em>alt view</em>, wrapped inside the target's
        /// variant block. 0 for a part whose art does not change with facing.
        /// </summary>
        public int viewOffset;

        /// <summary>
        /// Whether the part is <em>mirrored</em> — reflected about the actor's vertical axis.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A mirror reflects the whole part, not only its art, so it negates three channels of the
        /// composited pose:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <c>localPosition.x</c> — the plane moves to the other side. An ear sits left of the head
        /// at rest; mirrored, it belongs on the right. Flipping only the art leaves it pinned to the
        /// wrong side of the skull, which is a half-mirror and looks worse than none.
        /// </description></item>
        /// <item><description>
        /// <c>rotationZ</c> — rotation is handed. An arm swung +30° reflects to −30°.
        /// </description></item>
        /// <item><description>
        /// <c>scale.x</c> — the drawn shape faces the other way, carried live by
        /// <c>PostTransformMatrix</c>.
        /// </description></item>
        /// </list>
        /// <para>
        /// All three are negations of the <em>composited</em> pose rather than assignments over the
        /// rest pose, so they survive a clip that animates the same channels and compose with a part
        /// authored already offset, rotated or flipped — mirroring a flipped part unflips it.
        /// </para>
        /// </remarks>
        public bool mirrorX;
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

        /// <summary>Rest rotation in radians, as Euler angles in Unity's ZXY order.</summary>
        public float3 rotation;

        /// <summary>Rest non-uniform x/y/z scale.</summary>
        public float3 scale;

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

        /// <summary>Sampled rotation in radians, as Euler angles in Unity's ZXY order.</summary>
        public float3 rotation;

        /// <summary>Sampled non-uniform x/y/z scale; negative components flip.</summary>
        public float3 scale;

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
