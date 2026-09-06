// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace DotsAnimationToolkit
{
    /// <summary>Per-instance <c>_ImageIndex</c> shader property: the Texture2DArray slice a flipbook part shows. Written by <c>SpriteMaterialSystem</c>.</summary>
    [MaterialProperty("_ImageIndex")]
    public struct SpriteSliceProperty : IComponentData
    {
        public float Value; // slice index, uploaded as a float per the shader contract
    }

    /// <summary>Per-instance <c>_AtlasFrame</c> shader property: the atlas rect of a flipbook part in atlas mode. Written by <c>SpriteMaterialSystem</c>.</summary>
    [MaterialProperty("_AtlasFrame")]
    public struct AtlasFrameProperty : IComponentData
    {
        public float4 Value; // atlas rect: scale.xy, offset.zw
    }

    /// <summary>Per-instance <c>_VatFrameA</c> shader property: the fractional global frame index of the current clip sample, both VAT flavors. Written by <c>VatMaterialSystem</c>.</summary>
    [MaterialProperty("_VatFrameA")]
    public struct VatFrameAProperty : IComponentData
    {
        public float Value;
    }

    /// <summary>Per-instance <c>_VatFrameB</c> shader property: the fractional global frame index of the crossfade-target sample. Written by <c>VatMaterialSystem</c>.</summary>
    [MaterialProperty("_VatFrameB")]
    public struct VatFrameBProperty : IComponentData
    {
        public float Value;
    }

    /// <summary>Per-instance <c>_VatBlend</c> shader property: the crossfade weight from frame A to frame B. Written by <c>VatMaterialSystem</c>; 0 skips the B sampling path in the shader.</summary>
    [MaterialProperty("_VatBlend")]
    public struct VatBlendProperty : IComponentData
    {
        public float Value; // [0, 1]
    }

    /// <summary>Per-instance <c>_BillboardParams</c> shader property. Written by the game/host or baked constant.</summary>
    [MaterialProperty("_BillboardParams")]
    public struct BillboardParamsProperty : IComponentData
    {
        public float4 Value; // x = mode (0 off, 1 full spherical, 2 Y-axis upright, 3 frozen-yaw), y = frozen yaw radians, zw reserved
    }
}
