// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Per-instance <c>_ImageIndex</c> shader property: the Texture2DArray slice a flipbook part
    /// shows (architecture section 6.2 — name preserved verbatim for host-shader compatibility).
    /// Written by <c>SpriteMaterialSystem</c>.
    /// </summary>
    [MaterialProperty("_ImageIndex")]
    public struct SpriteSliceProperty : IComponentData
    {
        /// <summary>The slice index, uploaded as a float per the shader contract.</summary>
        public float Value;
    }

    /// <summary>
    /// Per-instance <c>_AtlasFrame</c> shader property: the atlas rect of a flipbook part in
    /// atlas mode — scale.xy, offset.zw (architecture section 6.2). Written by
    /// <c>SpriteMaterialSystem</c>.
    /// </summary>
    [MaterialProperty("_AtlasFrame")]
    public struct AtlasFrameProperty : IComponentData
    {
        /// <summary>The atlas rect: scale.xy, offset.zw.</summary>
        public float4 Value;
    }

    /// <summary>
    /// Per-instance <c>_VatFrameA</c> shader property: the fractional global frame index of the
    /// current clip sample, both VAT flavors (architecture section 6.2). Written by
    /// <c>VatMaterialSystem</c>.
    /// </summary>
    [MaterialProperty("_VatFrameA")]
    public struct VatFrameAProperty : IComponentData
    {
        /// <summary>Fractional global VAT frame index of the current clip sample.</summary>
        public float Value;
    }

    /// <summary>
    /// Per-instance <c>_VatFrameB</c> shader property: the fractional global frame index of the
    /// crossfade-target sample (architecture section 6.2). Written by <c>VatMaterialSystem</c>.
    /// </summary>
    [MaterialProperty("_VatFrameB")]
    public struct VatFrameBProperty : IComponentData
    {
        /// <summary>Fractional global VAT frame index of the crossfade-target sample.</summary>
        public float Value;
    }

    /// <summary>
    /// Per-instance <c>_VatBlend</c> shader property: the 0–1 crossfade weight from frame A to
    /// frame B (architecture section 6.2). Written by <c>VatMaterialSystem</c>; 0 skips the B
    /// sampling path in the shader.
    /// </summary>
    [MaterialProperty("_VatBlend")]
    public struct VatBlendProperty : IComponentData
    {
        /// <summary>Crossfade weight A→B in [0, 1].</summary>
        public float Value;
    }

    /// <summary>
    /// Per-instance <c>_BillboardParams</c> shader property: x = mode (0 off, 1 full spherical,
    /// 2 Y-axis upright, 3 frozen-yaw), y = frozen yaw in radians, zw reserved
    /// (architecture sections 6.1, 6.2). Written by the game/host or baked constant.
    /// </summary>
    [MaterialProperty("_BillboardParams")]
    public struct BillboardParamsProperty : IComponentData
    {
        /// <summary>Billboard parameters: x = mode, y = frozen yaw radians, zw reserved.</summary>
        public float4 Value;
    }
}
