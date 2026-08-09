// Copyright (c) 2026 Stitch Punk. All rights reserved.
//
// =====================================================================================
// DOTS Animation Toolkit — COMPOSED EXAMPLE: flipbook + billboard in one shader
//
// This file is a TEACHING ARTIFACT. It is not a production shader and it is not the one
// to ship a game on — "DOTS Animation Toolkit/Sprite Unlit" is. What this file exists to
// show is the thing neither reference shader shows on its own: how TWO independent
// toolkit includes compose inside a single hand-written pass set, and which stage each
// one belongs in.
//
// The written form of the rules below is Docs/AnimationToolkit/shader-contract.md. This
// file is the executable form of the same document; if the two ever disagree, this one is
// the one that renders.
//
// -------------------------------------------------------------------------------------
// THE FOUR THINGS TO COPY OUT OF HERE
//
//   1. Two includes, two stages, no interaction.
//        ToolkitBillboard.hlsl  runs in the VERTEX stage — it rewrites geometry.
//        ToolkitFlipbook.hlsl   runs in the FRAGMENT stage — it rewrites a UV.
//      They never talk to each other. That is the whole reason they are separate files
//      and the whole reason a host can adopt one without the other.
//
//   2. ONE shared displacement function, called by EVERY pass.
//      Billboarding is geometry. Geometry that differs between passes is a shadow of a
//      shape the camera never sees, a depth buffer that disagrees with the colour buffer,
//      and a normals buffer that poisons SSAO. Declaring the displacement once above the
//      passes is what makes "every pass displaces" structural instead of a thing four
//      vertex functions each have to remember.
//
//   3. Facing comes from pass-invariant inputs only.
//      _WorldSpaceCameraPos and the host-written _ToolkitCameraForward global hold the
//      SAME values in the shadow pass as in the colour pass. The view matrix does not —
//      during shadow rendering it belongs to the light — which is why deriving facing
//      from it is banned outright and asserted against by an EditMode conformance test.
//
//   4. The per-instance block is included, not re-declared.
//      ToolkitInstancing.hlsl carries the whole DOTS-instancing declaration. Include it
//      exactly once per hand-written shader and read values only through the TOOLKIT_*
//      accessors, never the raw uniform names. A ShaderGraph must NOT include it — a
//      graph emits its own equivalent block from Hybrid-Per-Instance properties, and two
//      blocks declaring the same names do not compile.
//
// -------------------------------------------------------------------------------------
// WHAT THIS SHADER DELIBERATELY DOES NOT DO
//
//   - No lighting. A wrap term or a full URP lit stack would be the biggest thing in the
//     file and would teach nothing about the toolkit.
//   - No VAT. Flipbook and VAT are alternative ways to answer the same question ("what
//     does this mesh look like this frame"), so composing them in one shader would be a
//     demonstration of a mistake. See "DOTS Animation Toolkit/VAT Crowd Unlit" for VAT.
//   - No MotionVectors pass. The four passes below are the set that a *displacing* shader
//     must declare; the VAT crowd reference adds MotionVectors because a deforming mesh
//     has more to say there.
// =====================================================================================

Shader "DOTS Animation Toolkit/Composite Example"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite Sheet", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)

        // -------------------------------------------------------------------------
        // PER-INSTANCE rows of the section 6.2 property table. Each of these has
        // exactly one [MaterialProperty] component in the Runtime assembly, and the
        // value set here in the Properties block is the FALLBACK an entity gets when
        // it carries no matching component — never dead weight.
        //
        // Declaring them here is necessary but not sufficient: they only become
        // per-entity because ToolkitInstancing.hlsl also names them inside
        // UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata). Declare one here and
        // forget it there and every instance renders the material value forever,
        // which looks exactly like an animation that never starts.
        // -------------------------------------------------------------------------
        _ImageIndex ("Image Index (frame number)", Float) = 0
        _AtlasFrame ("Atlas Frame (scale.xy, offset.zw)", Vector) = (1, 1, 0, 0)
        _BillboardParams ("Billboard (mode, frozenYaw, _, _)", Vector) = (1, 0, 0, 0)

        // -------------------------------------------------------------------------
        // MATERIAL-LEVEL, on purpose. These are identical for every instance sharing
        // this material, and per-instance data that never varies is pure batching
        // cost — it widens the per-entity upload for nothing.
        // -------------------------------------------------------------------------
        _FlipbookGrid ("Flipbook Grid (columns, rows, _, _)", Vector) = (4, 4, 0, 0)
        _BillboardPivotOS ("Billboard Pivot (object space)", Vector) = (0, 0, 0, 0)

        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        // Two ways to name a frame, one shader. OFF: the CPU computed the rect and
        // sent it in _AtlasFrame (what SpriteMaterialSystem does). ON: the CPU sent a
        // frame NUMBER in _ImageIndex and the GPU derives the rect from the grid.
        [Toggle(_TOOLKIT_GRID_MODE)] _GridMode ("Grid Mode (derive rect from _ImageIndex)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        // -----------------------------------------------------------------------------
        // Everything shared by every pass lives here. This block is the mechanism behind
        // teaching point 2: a pass cannot accidentally use a different displacement,
        // because there is only one to use.
        // -----------------------------------------------------------------------------
        HLSLINCLUDE

        // URP core first — it defines TRANSFORM_TEX, SAMPLE_TEXTURE2D, the space
        // transforms, _WorldSpaceCameraPos, and the UNITY_DOTS_* instancing machinery
        // that ToolkitInstancing.hlsl builds on. The toolkit includes deliberately pull
        // in nothing themselves, so this is the only line that has to come first.
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // The per-instance block. Exactly one shader in a compilation unit may include
        // this, and a ShaderGraph never should.
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitInstancing.hlsl"

        // The two technique includes being composed. Order between them does not matter —
        // neither includes anything, neither declares a property, neither reads a global.
        // Drop either line and the other half of this shader still compiles, which is the
        // property the package promises and an EditMode test enforces.
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitBillboard.hlsl"
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitFlipbook.hlsl"

        // Every material property, per-instance or not, must sit in UnityPerMaterial for
        // the SRP Batcher. The per-instance ones must ALSO be here even when DOTS
        // instancing is on: UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT falls back to
        // the plain uniform of the same name for any entity that carries no component,
        // so the uniform is the default value, not a leftover.
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _BaseColor;
            float _ImageIndex;
            float4 _AtlasFrame;
            float4 _BillboardParams;
            float4 _FlipbookGrid;
            float4 _BillboardPivotOS;
            float _Cutoff;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        // -----------------------------------------------------------------------------
        // VERTEX STAGE — the billboard.
        //
        // This CANNOT move to the fragment stage. It produces a new object-space vertex
        // position, and there is no vertex position in the fragment stage to produce.
        //
        // It also cannot be hoisted to the CPU *for this content*: rotating one quad
        // about its own pivot is what a shader billboard does, and that is right for
        // single-quad imposters, grass, and crowd meshes. A character built from a dozen
        // layered quads wants its ROOT rotated instead (ActorBillboardSystem does that on
        // the CPU) — every layer turning about its own pivot fans the composition apart.
        // An actor uses one path or the other; both at once is a double rotation.
        //
        // Note what is passed in and what is not:
        //   _WorldSpaceCameraPos   — pass-invariant: the rendering camera's position is
        //                            the same value while the shadow pass runs.
        //   TOOLKIT_CAMERA_FORWARD — pass-invariant for the same reason: it is a global
        //                            the HOST writes once per frame from its own camera.
        //                            The package never reads a Camera, because it cannot
        //                            know which of a host's cameras is the one that
        //                            matters. Leave it unwritten and screen-aligned mode
        //                            degrades to spherical rather than to a collapsed
        //                            quad.
        //   UNITY_MATRIX_M / _I_M  — the object transform and its inverse. The rotation
        //                            happens in world space and comes back to object
        //                            space, so the result does not depend on how the
        //                            object itself happens to be oriented.
        //
        // The view matrix is absent from that list and must stay absent. During shadow
        // rendering it is the LIGHT's view matrix, so facing derived from it turns quads
        // toward the light: correct in the colour pass, wrong in the one pass nobody
        // screenshots. A conformance test greps the billboard include for it.
        // -----------------------------------------------------------------------------
        float3 ToolkitCompositeDisplace(float3 positionOS)
        {
            return BillboardTransform(
                positionOS,
                _BillboardPivotOS.xyz,
                TOOLKIT_BILLBOARD_PARAMS,
                _WorldSpaceCameraPos,
                TOOLKIT_CAMERA_FORWARD,
                UNITY_MATRIX_M,
                UNITY_MATRIX_I_M);
        }

        // -----------------------------------------------------------------------------
        // FRAGMENT STAGE — the flipbook.
        //
        // A flipbook does not move anything; it decides which texels a fragment reads.
        // Both branches below end in the same affine remap, `uv * scale + offset`, so
        // this could legally be computed in the vertex stage and interpolated. It is
        // here instead for two reasons worth copying:
        //
        //   - the varying stays the raw mesh UV, so nothing downstream has to know a
        //     flipbook happened; and
        //   - slice mode (Texture2DArray, see the Sprite Unlit reference) has to sample
        //     in the fragment stage anyway, so keeping atlas mode here means the two
        //     addressing modes stay swappable behind one function.
        //
        // What must NOT happen is computing the rect once per pass differently. There is
        // one function; the alpha-clipping passes call the same one the colour pass does,
        // so a cutout hole is in the same place in the shadow map as on screen.
        // -----------------------------------------------------------------------------
        float4 ToolkitCompositeSample(float2 uv)
        {
            float4 atlasRect;

        #if defined(_TOOLKIT_GRID_MODE)
            // GPU-derived rect. AtlasRectFromGrid treats row 0 as the TOP row, because
            // that is how sprite sheets are authored and read, whereas UV space runs
            // upward from the bottom. It does the flip so the caller does not; deriving
            // the rect by hand and forgetting it is a whole-sheet vertical mirror that
            // reads as badly exported art.
            atlasRect = AtlasRectFromGrid(TOOLKIT_IMAGE_INDEX, _FlipbookGrid.x, _FlipbookGrid.y);
        #else
            // CPU-supplied rect — the path SpriteMaterialSystem drives, writing
            // AtlasFrameProperty every frame for every visible flipbook part. Its
            // material default (1, 1, 0, 0) is the identity rect, "the whole texture",
            // which is a valid frame rather than stale garbage for a part whose clip has
            // no atlas track.
            atlasRect = TOOLKIT_ATLAS_FRAME;
        #endif

            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, AtlasFrameUV(uv, atlasRect));
        }
        ENDHLSL

        // =============================================================================
        // The four passes. Every one of them calls ToolkitCompositeDisplace, and that is
        // the only interesting thing about three of them.
        //
        // Every pass also repeats the same three pragmas, and all three are load-bearing:
        //   multi_compile_instancing          — brings in UNITY_SUPPORT_INSTANCING
        //   multi_compile _ DOTS_INSTANCING_ON — the keyword that turns
        //                                       UNITY_DOTS_INSTANCING_ENABLED on, which
        //                                       is what selects the instanced branch of
        //                                       ToolkitInstancing.hlsl
        //   target 4.5                        — the floor for the DOTS instancing path
        // Omit the second and the shader still compiles and still renders — from material
        // values, identically for every entity. That is the failure mode to watch for.
        // =============================================================================

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            // A camera-facing quad can present either face, so neither may be culled.
            // Set per pass rather than once for the SubShader so each pass states its own
            // render state next to the code it governs.
            Cull Off

            HLSLPROGRAM
            #pragma vertex CompositeForwardVertex
            #pragma fragment CompositeForwardFragment
            #pragma shader_feature_local _TOOLKIT_GRID_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings CompositeForwardVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                // UNITY_SETUP_INSTANCE_ID must run before ANY per-instance read. Under
                // DOTS instancing it is what points the instanced-property fetches at
                // this entity's data; without it every TOOLKIT_* accessor reads whatever
                // instance was set up last.
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitCompositeDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 CompositeForwardFragment(Varyings input) : SV_Target
            {
                // Repeated in the fragment stage because TOOLKIT_BASE_COLOR is read here.
                UNITY_SETUP_INSTANCE_ID(input);

                float4 sampled = ToolkitCompositeSample(input.uv);

                // _BaseColor is the one section 6.2 row the package does not ship a
                // component for: Entities Graphics already provides
                // URPMaterialPropertyBaseColor, and a host tint component binds to the
                // same name unchanged. Colour values uploaded from C# must be LINEAR —
                // the shader does no conversion here.
                float4 tinted = sampled * TOOLKIT_BASE_COLOR;

                clip(tinted.a - _Cutoff);
                return tinted;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------
        // The pass that makes the whole displacement contract real. If the billboard
        // rotation is missing here, the quad casts the shadow of its un-rotated pose:
        // a defect that looks like a lighting bug, is a geometry bug, and is invisible in
        // every screenshot of the colour pass.
        //
        // Two consequences of billboarded shadows are inherent rather than fixable, and
        // are named here so nobody files them as bugs: the shadow re-orients as the
        // camera moves (negligible under a mostly-fixed 2.5D camera, visible under free
        // orbit), and a quad viewed edge-on by the light casts a near-degenerate sliver.
        // -----------------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex CompositeShadowVertex
            #pragma fragment CompositeShadowFragment
            #pragma shader_feature_local _TOOLKIT_GRID_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings CompositeShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitCompositeDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 CompositeShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // The same flipbook frame the colour pass shows, so the cutout silhouette
                // that casts the shadow is the silhouette on screen.
                clip(ToolkitCompositeSample(input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex CompositeDepthVertex
            #pragma fragment CompositeDepthFragment
            #pragma shader_feature_local _TOOLKIT_GRID_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings CompositeDepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitCompositeDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 CompositeDepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Depth prepass feeds soft particles, depth-fade, and any depth-sampling
                // effect. An un-displaced depth here is a quad that fades against the
                // wrong surface.
                clip(ToolkitCompositeSample(input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex CompositeDepthNormalsVertex
            #pragma fragment CompositeDepthNormalsFragment
            #pragma shader_feature_local _TOOLKIT_GRID_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings CompositeDepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitCompositeDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);

                // The NORMAL has to be turned by the same rotation as the position. A
                // billboarded quad that reports its un-rotated normal corrupts everything
                // reading the normals buffer, SSAO first.
                //
                // Why the subtraction: BillboardTransform is affine — rotate about a
                // pivot, then translate. Displacing a direction directly would carry that
                // translation into it. Displacing the origin and subtracting cancels the
                // translation exactly and leaves only the linear part, and it does so for
                // ANY pivot, because both terms share the same pivot offset.
                float3 displacedNormalOS =
                    ToolkitCompositeDisplace(input.normalOS)
                    - ToolkitCompositeDisplace(float3(0.0, 0.0, 0.0));

                output.normalWS = TransformObjectToWorldNormal(displacedNormalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 CompositeDepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(ToolkitCompositeSample(input.uv).a - _Cutoff);
                return float4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    // Not a fifth pass — a fallback for targets that reject the SubShader above (its
    // DOTS instancing path needs shader model 4.5). Everything animated is lost there,
    // which beats rendering magenta.
    FallBack "Universal Render Pipeline/Unlit"
}
