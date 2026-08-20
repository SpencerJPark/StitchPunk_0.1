#ifndef DOTS_ANIMATION_TOOLKIT_INSTANCING_INCLUDED
#define DOTS_ANIMATION_TOOLKIT_INSTANCING_INCLUDED

// =====================================================================================
// DOTS Animation Toolkit — the per-instance property block (architecture section 6.2)
//
// This is the GPU half of the CPU↔GPU contract. Every row of the section 6.2 table has
// exactly one [MaterialProperty] component in the Runtime assembly and exactly one
// UNITY_DOTS_INSTANCED_PROP below, and the names are frozen at 1.0.
//
// ------------------------------------------------------------------------------------
// FOR HAND-WRITTEN SHADERS ONLY.
//
// ShaderGraph generates its own equivalent block from properties marked Hybrid Per
// Instance (`hlslDeclarationOverride: 3`), so a graph must NOT include this file — it
// would declare the same names twice. Include it when you are writing a .shader by hand
// and want Entities Graphics to feed it per-entity values.
//
// ------------------------------------------------------------------------------------
// HOW TO USE IT IN YOUR OWN SHADER
//
//   1. Declare the properties in your Properties block with the names below.
//   2. #include this file after the URP core includes.
//   3. Read values through the TOOLKIT_* accessors, never the raw names — the accessors
//      resolve to the instanced fetch when DOTS instancing is on and to the plain
//      material constant when it is off, so one shader serves both paths.
//
// The guard matters: without DOTS instancing (a preview window, a non-Entities scene, a
// material inspector) the same shader must still render from ordinary material values
// rather than failing to compile or sampling garbage.
// =====================================================================================

#if defined(UNITY_DOTS_INSTANCING_ENABLED)

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float,  _ImageIndex)
    UNITY_DOTS_INSTANCED_PROP(float4, _AtlasFrame)
    UNITY_DOTS_INSTANCED_PROP(float,  _VatFrameA)
    UNITY_DOTS_INSTANCED_PROP(float,  _VatFrameB)
    UNITY_DOTS_INSTANCED_PROP(float,  _VatBlend)
    UNITY_DOTS_INSTANCED_PROP(float4, _BillboardParams)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

// UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT falls back to the material's own value for
// any property an entity does not carry a component for. That is what lets a rig mix
// flipbook parts and VAT parts against one shader without every entity carrying every
// property — an actor with no VatFrameAProperty simply reads the material's.
#define TOOLKIT_IMAGE_INDEX       UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  _ImageIndex)
#define TOOLKIT_ATLAS_FRAME       UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AtlasFrame)
#define TOOLKIT_VAT_FRAME_A       UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  _VatFrameA)
#define TOOLKIT_VAT_FRAME_B       UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  _VatFrameB)
#define TOOLKIT_VAT_BLEND         UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  _VatBlend)
#define TOOLKIT_BILLBOARD_PARAMS  UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BillboardParams)
#define TOOLKIT_BASE_COLOR        UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)

#else

// Non-instanced path: the same accessor names resolve to the plain uniforms, so nothing
// downstream branches on whether DOTS instancing is active.
#define TOOLKIT_IMAGE_INDEX       _ImageIndex
#define TOOLKIT_ATLAS_FRAME       _AtlasFrame
#define TOOLKIT_VAT_FRAME_A       _VatFrameA
#define TOOLKIT_VAT_FRAME_B       _VatFrameB
#define TOOLKIT_VAT_BLEND         _VatBlend
#define TOOLKIT_BILLBOARD_PARAMS  _BillboardParams
#define TOOLKIT_BASE_COLOR        _BaseColor

#endif // UNITY_DOTS_INSTANCING_ENABLED

// -------------------------------------------------------------------------------------
// The camera forward vector screen-aligned billboarding needs (amendment A39).
//
// Declared here rather than in ToolkitBillboard.hlsl so that include stays free of
// globals and remains liftable into any project. A host writes this once per frame
// alongside the camera singleton; it is NOT per-instance, so it never affects batching.
//
// Unity provides no built-in camera-forward global, which is exactly why the host must
// supply one — and why it must be a global rather than derived from UNITY_MATRIX_V, which
// belongs to the light during shadow rendering (section 6.3).
// -------------------------------------------------------------------------------------
float4 _ToolkitCameraForward;

#define TOOLKIT_CAMERA_FORWARD (_ToolkitCameraForward.xyz)

#endif // DOTS_ANIMATION_TOOLKIT_INSTANCING_INCLUDED
