# Shader Integration Contract

This document is the integration contract for the four standalone HLSL includes under
`Packages/com.dotsanimationtoolkit/Shaders/Includes/`:

- `ToolkitBillboard.hlsl`
- `ToolkitFlipbook.hlsl`
- `ToolkitVat.hlsl`
- `ToolkitInstancing.hlsl`

**Design intent** (package owner, verbatim): *"maybe build out the shader in a way so that it has
parts that other users could plug into their future shader projects"* and *"different preset hlsl
files of components we make that I can throw into custom shaders i build."* Each include was
written to satisfy that: it declares nothing of its own (`#include`, `Properties`, globals) and
takes every input as a function parameter, so you can drop exactly one — flipbook only, VAT only,
billboard only — into a lit shader, a toon shader, or any other shader you already own, without
adopting the rest of this package.

The executable form of everything below is
`Packages/com.dotsanimationtoolkit/Shaders/HandWritten/ToolkitCompositeExample.shader`,
which composes `ToolkitBillboard.hlsl` and `ToolkitFlipbook.hlsl` in one hand-written shader and is
explicit that if this document and that file ever disagree, the file is the one that renders.

---

## 1. The per-instance property contract (CPU → GPU)

Every row below is a DOTS-instanced material property: a `[MaterialProperty]` component in
`Packages/com.dotsanimationtoolkit/Runtime/Components/MaterialPropertyComponents.cs`
feeds a `UNITY_DOTS_INSTANCED_PROP` declared in
`Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitInstancing.hlsl`, and the
shader property name is frozen — it must match **exactly**, character for character.

| Shader property | Type | C# component | Field | Written by |
|---|---|---|---|---|
| `_ImageIndex` | `float` | `SpriteSliceProperty` | `Value` (float) | `SpriteMaterialSystem` (per `MaterialPropertyComponents.cs:14-19`) |
| `_AtlasFrame` | `float4` | `AtlasFrameProperty` | `Value` (float4: scale.xy, offset.zw) | `SpriteMaterialSystem` (`MaterialPropertyComponents.cs:26-31`) |
| `_VatFrameA` | `float` | `VatFrameAProperty` | `Value` (float) | `VatMaterialSystem` (`MaterialPropertyComponents.cs:38-43`) |
| `_VatFrameB` | `float` | `VatFrameBProperty` | `Value` (float) | `VatMaterialSystem` (`MaterialPropertyComponents.cs:49-54`) |
| `_VatBlend` | `float` | `VatBlendProperty` | `Value` (float, 0–1) | `VatMaterialSystem` (`MaterialPropertyComponents.cs:61-66`) |
| `_BillboardParams` | `float4` | `BillboardParamsProperty` | `Value` (float4: x=mode, y=frozen yaw rad, zw reserved) | host/game, or a baked constant (`MaterialPropertyComponents.cs:73-78`) |
| `_BaseColor` | `float4` | *(none shipped)* | — | Entities Graphics' own `URPMaterialPropertyBaseColor`; a host tint component binds to the same name unchanged (documented in `ToolkitCompositeExample.shader:307-311`) |

`_BaseColor` is the one row in the include's instancing block
(`ToolkitInstancing.hlsl:35-43`) with no dedicated component in
`MaterialPropertyComponents.cs` — I read that file in full and confirmed it declares
`SpriteSliceProperty`, `AtlasFrameProperty`, `VatFrameAProperty`, `VatFrameBProperty`,
`VatBlendProperty`, and `BillboardParamsProperty`, and nothing named `BaseColor...Property`.
The shader-side explanation (`ToolkitCompositeExample.shader:307-311`) is that Entities Graphics
already provides `URPMaterialPropertyBaseColor` for this name, so a second component would be
redundant. I have not read the Entities.Graphics package source myself to confirm that type name
— treat that one clause as documented-by-the-shader-comment, not independently verified.

Colour values uploaded from C# for `_BaseColor` must be **linear** — `ToolkitCompositeExample.shader`
does no colour-space conversion (`ToolkitCompositeExample.shader:310-312`).

### What "per-instance DOTS-instanced property" means for you

These seven names are not ordinary material uniforms. They are declared inside a
`UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)` / `UNITY_DOTS_INSTANCING_END(...)` block
(`ToolkitInstancing.hlsl:35-43`) so that Entities Graphics (the Batch Renderer Group) can upload a
different value per entity in one instanced draw. Two consequences follow directly from that:

1. **They must be declared inside the DOTS instancing block, not as a plain uniform.** A plain
   `float4 _BillboardParams;` in your `CBUFFER_START(UnityPerMaterial)` with no matching
   `UNITY_DOTS_INSTANCED_PROP` entry compiles fine and renders fine in the preview window — and
   then **silently never receives a per-entity value** once DOTS instancing turns on. Every
   instance will show the material's single default forever. This is the single most likely
   integration mistake and it produces no error, warning, or Console message.
2. **Always read through the `TOOLKIT_*` accessor macros, never the raw name.** `ToolkitInstancing.hlsl`
   defines `TOOLKIT_IMAGE_INDEX`, `TOOLKIT_ATLAS_FRAME`, `TOOLKIT_VAT_FRAME_A`, `TOOLKIT_VAT_FRAME_B`,
   `TOOLKIT_VAT_BLEND`, `TOOLKIT_BILLBOARD_PARAMS`, `TOOLKIT_BASE_COLOR`
   (`ToolkitInstancing.hlsl:49-55` instanced path, `61-67` non-instanced fallback). Under
   `UNITY_DOTS_INSTANCING_ENABLED` they resolve to
   `UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT`, which falls back to the material's own value for
   any entity that carries no matching component — so mixed rigs (some entities with a
   `VatFrameAProperty`, some without) work against one shader (`ToolkitInstancing.hlsl:45-48`).
   Without DOTS instancing (preview window, non-Entities scene, material inspector) the same names
   resolve to the plain uniforms instead (`ToolkitInstancing.hlsl:57-68`), so one shader body serves
   both paths. Reading the raw `_ImageIndex` name directly in your shader body bypasses this switch
   and will read the wrong value (or fail to compile) in one of the two contexts.
3. `ToolkitInstancing.hlsl` is **for hand-written shaders only** (`ToolkitInstancing.hlsl:12-17`). A
   Shader Graph must **not** `#include` it — Shader Graph emits its own equivalent block from
   properties marked Hybrid Per Instance (`hlslDeclarationOverride: 3`), and two blocks declaring
   the same names fail to compile.
4. Even when DOTS instancing is on, every one of these properties must **also** be declared inside
   `CBUFFER_START(UnityPerMaterial)` in the ordinary material-uniform way
   (`ToolkitCompositeExample.shader:130-144`). The SRP Batcher needs it there, and it doubles as the
   fallback default value the `_WITH_DEFAULT` accessor reads for uncomponented entities — it is not
   dead weight to remove.

`_ToolkitCameraForward` (a `float4`, `.xyz` used) is declared as a **plain global**, not a
per-instance property, right after the accessor block (`ToolkitInstancing.hlsl:82-84`). It is
written once per frame by the host from its own camera, not per entity, so it never affects
batching. See §2 for why it exists.

---

## 2. Per-include reference

### 2.1 `ToolkitBillboard.hlsl` — turn a quad to face the viewer

**Provides** (`ToolkitBillboard.hlsl`):

```hlsl
float3x3 ToolkitBillboardBasis(float3 forward, float3 up)                                  // :45
float3   ToolkitBillboardFacing(float mode, float3 pivotWS, float3 cameraPositionWS, float3 cameraForwardWS)  // :69
float3   BillboardTransform(
    float3 positionOS, float3 pivotOS, float4 billboardParams,
    float3 cameraPositionWS, float3 cameraForwardWS,
    float4x4 objectToWorld, float4x4 worldToObject)                                          // :105
float3   BillboardTransformSpherical(
    float3 positionOS, float3 pivotOS, float4 billboardParams,
    float3 cameraPositionWS, float4x4 objectToWorld, float4x4 worldToObject)                 // :186
```

`BillboardTransform` is the entry point. `BillboardTransformSpherical` is a convenience overload
for callers who never use `TOOLKIT_BILLBOARD_SCREEN_ALIGNED` mode and have no camera-forward value
to pass — it is a separate function rather than a defaulted parameter, deliberately, "so a silent
default is [not] how half the callers would stop honouring a mode they had opted into"
(`ToolkitBillboard.hlsl:180-185`).

Mode constants (must match `BillboardParamsProperty.Value.x` on the CPU side,
`ToolkitBillboard.hlsl:30-34`):

| Constant | Value | Meaning |
|---|---|---|
| `TOOLKIT_BILLBOARD_OFF` | 0.0 | no rotation, vertex passes through unchanged |
| `TOOLKIT_BILLBOARD_FULL` | 1.0 | full spherical: quad always faces the camera point |
| `TOOLKIT_BILLBOARD_UPRIGHT` | 2.0 | spherical, but yaw only — Y stays fixed (trees, standing characters) |
| `TOOLKIT_BILLBOARD_FROZEN_YAW` | 3.0 | pitch tracks the camera, yaw is held at an authored angle (`billboardParams.y`, radians) — the corpse case |
| `TOOLKIT_BILLBOARD_SCREEN_ALIGNED` | 4.0 | every quad takes the same orientation (classic 2.5D look), from `-cameraForwardWS` |

**Requires of the host:**
- `_WorldSpaceCameraPos` (URP global, pass-invariant across all render passes in a frame).
- A camera-forward vector for screen-aligned mode — the include does not derive one itself. Unity
  ships no built-in camera-forward global, so the host must write one; if screen-aligned mode is
  requested but the forward is left at zero, the mode **degrades to spherical** rather than
  collapsing the quad (`ToolkitBillboard.hlsl:79-83`).
- `UNITY_MATRIX_M` / `UNITY_MATRIX_I_M` (object-to-world and its inverse) — the rotation happens in
  world space, about the pivot, then returns to object space, so the result is independent of the
  object's own orientation (`ToolkitBillboard.hlsl:171-177`).
- Every render pass must call the same displacement function with the same inputs (§3.3 below).

**Must NOT be given:** `UNITY_MATRIX_V` or `UNITY_MATRIX_I_V` (view matrix or its inverse), under
any circumstance — see the sub-section below.

#### The one rule that matters: never `UNITY_MATRIX_V`

`ToolkitBillboard.hlsl`'s header states the rule outright (`ToolkitBillboard.hlsl:15-26`): a
billboard must present the **same geometry in every pass**. During shadow rendering, the view
matrix belongs to the **light**, not the camera. A billboard that derives facing from
`UNITY_MATRIX_V` will silently turn its quads to face the light and cast the shadow of a shape the
camera never actually sees on screen — correct-looking in the colour pass, wrong in exactly the
pass nobody screenshots. The fix used throughout this package is to pass in only values that are
constant across every pass of a frame: `_WorldSpaceCameraPos` and a host-written camera-forward
global (`_ToolkitCameraForward`, added in "amendment A39" per the header comment).

This is enforced structurally, not just documented. `ShaderConformanceTests.cs` has a dedicated
test, `TheBillboardInclude_NeverUsesTheViewMatrix`
(`ShaderConformanceTests.cs:148-160`), which strips comments from `ToolkitBillboard.hlsl` and then
asserts the resulting source contains neither `UNITY_MATRIX_V` nor `UNITY_MATRIX_I_V`. The test's
own comment explains why comments are stripped first: an earlier version of the fixture searched
the raw file and failed on the include's own header — which documents the ban in prose — so a
naive test "would push the explanation out of the file, the exact opposite of what it is for"
(`ShaderConformanceTests.cs:140-147`). The reasoning it encodes is exactly the shadow-vs-light
argument above: this is a structural guarantee, not a style preference, because the failure mode
is invisible in a screenshot of the colour pass and only shows up as a wrong shadow silhouette.

### 2.2 `ToolkitFlipbook.hlsl` — frame addressing

**Provides** (`ToolkitFlipbook.hlsl`):

```hlsl
float3 SliceUV(float2 uv, float imageIndex)                                    // :28
float2 AtlasFrameUV(float2 uv, float4 atlasRect)                               // :40
float4 AtlasRectFromGrid(float frameIndex, float columns, float rows)          // :53
```

Two independent addressing modes, either of which can be used alone:

- **Slice mode** (`SliceUV`): frames are layers of a `Texture2DArray`; the frame index becomes the
  array slice. No UV math — the sampler does the work. The index is carried as a `float` (matching
  the `_ImageIndex` per-instance upload path) and **rounded, not truncated**
  (`ToolkitFlipbook.hlsl:22-26`): an index arriving as `2.9999` from float packing is meant to be
  frame 3, and truncating would show frame 2 for one frame, reading as a flicker rather than a
  clean off-by-one.
- **Atlas mode** (`AtlasFrameUV` + optionally `AtlasRectFromGrid`): frames are cells of one texture.
  `AtlasFrameUV` maps a mesh UV into one cell given a rect (`atlasRect.xy` = scale, `.zw` =
  offset) — this matches the `_AtlasFrame` property, whose identity value `(1, 1, 0, 0)` means "the
  whole texture" for an actor with no atlas track. `AtlasRectFromGrid` derives that rect from a
  `columns × rows` grid description and a frame index instead of requiring the caller to compute
  one; it treats **row 0 as the top row** (`ToolkitFlipbook.hlsl:50-51`), because that is how sprite
  sheets are authored and read, even though UV space itself runs upward from the bottom — getting
  this backwards produces a whole-sheet vertical flip.

**Requires of the host:** nothing beyond the parameters — no textures, no globals. Sampling itself
(`SAMPLE_TEXTURE2D` / `SAMPLE_TEXTURE2D_ARRAY`) is the caller's job.

**Must NOT be given:** nothing forbidden; this include has no equivalent of the view-matrix hazard.

### 2.3 `ToolkitVat.hlsl` — vertex animation texture playback

**Provides** (`ToolkitVat.hlsl`):

```hlsl
float2  ToolkitVatTexelUV(float elementIndex, float frame, float rowInFrame, float4 vatTexelParams)  // :49
float3x4 ToolkitVatReadBoneMatrix(
    Texture2D vatTexture, SamplerState vatSampler,
    float boneIndex, float frame, float4 vatTexelParams)                                              // :65
float3x4 VatBoneMatrixAtFrame(
    Texture2D vatTexture, SamplerState vatSampler,
    float boneIndex, float globalFrame, float4 vatTexelParams)                                        // :84
float3   VatBoneSkin(
    Texture2D vatTexture, SamplerState vatSampler,
    float3 positionOS, float4 boneIndices, float4 boneWeights,
    float globalFrameA, float globalFrameB, float blendWeight,
    float4 vatTexelParams)                                                                             // :109
float3   VatVertexFetch(
    Texture2D vatTexture, SamplerState vatSampler,
    float vertexIndex, float globalFrameA, float globalFrameB, float blendWeight,
    float4 vatTexelParams)                                                                             // :150
float3   ToolkitVatDecodeNormal(float2 encoded)                                                        // :188
```

Two flavours, and you use one or the other per mesh, not both:

- **Bone flavour** — `VatBoneSkin` (entry point) skins a vertex against up to four bone influences,
  reading a baked 3×4 matrix per bone per frame via `VatBoneMatrixAtFrame` /
  `ToolkitVatReadBoneMatrix`. It supports an optional crossfade between two global frames
  (`globalFrameA`, `globalFrameB`, `blendWeight`): `blendWeight <= 0` skips every `B` fetch
  entirely, which is the fast path a non-crossfading crowd relies on
  (`ToolkitVat.hlsl:104-108`).
- **Vertex flavour** — `VatVertexFetch` reads a baked position per vertex per frame directly (no
  skinning), with the same A/B crossfade support. `vertexIndex` is the mesh's own per-vertex index,
  carried in a UV channel because vertex-id semantics are not available in every pass on every
  target (`ToolkitVat.hlsl:146-148`).
- `ToolkitVatDecodeNormal` decodes an octahedral-encoded normal (2 channels instead of 3), used by
  the vertex flavour's normal texture to save roughly a third of the memory a 3-channel normal
  texture would cost on a dense mesh (`ToolkitVat.hlsl:181-186`).

Frame interpolation is **linear component-wise matrix/position lerp between adjacent frames**, not
correct rotational interpolation — `VatBoneMatrixAtFrame`'s comment is explicit that a per-bone
quaternion slerp would be more correct but the GPU cannot afford it here, and that between adjacent
frames of one clip the resulting error is negligible; long crossfades between very different poses
are documented to look "rubbery" (`ToolkitVat.hlsl:75-83`).

**Requires of the host:** a `Texture2D` + `SamplerState` pair for the VAT texture, and a
`vatTexelParams` float4 packing `(textureWidth, textureHeight, rowsPerFrame, boneOrVertexCount)`
(`ToolkitVat.hlsl:35-40`). For bone flavour, the mesh must carry bone indices/weights as described
in §4. `VatBoneSkin`/`VatVertexFetch` must be called in the vertex stage (they return an
object-space position), and every render pass of the shader must call it identically (§3.3).

**Must NOT be given:** a bilinear/trilinear sampler for the VAT texture — sampling must be point
filtered (see §5 and `VatTextureBaker.CreateTexture`, `VatTextureBaker.cs:567-579`), or the GPU
will blend two unrelated bones/vertices into a melting average.

### 2.4 `ToolkitInstancing.hlsl` — the per-instance property block

Covered fully in §1. Summary: for hand-written shaders only; include once; read only through the
`TOOLKIT_*` accessors; a Shader Graph must never include it.

---

## 3. Minimal integration recipes

Each recipe is the smallest correct snippet for one technique in isolation, extracted from the
patterns `ToolkitSpriteUnlit.shader`, `ToolkitVatCrowdUnlit.shader`, and
`ToolkitCompositeExample.shader` all follow. All three techniques are vertex-or-fragment-stage-only
as noted — get the stage wrong and the code either does not compile (no vertex position exists in
the fragment stage) or silently does the wrong amount of work per pixel vs. per vertex.

### 3.1 Billboard only (vertex stage)

Billboarding **must** run in the vertex stage, because `BillboardTransform` produces a new
object-space **vertex position** — there is no vertex position to produce in the fragment stage.

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitInstancing.hlsl"
#include "Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitBillboard.hlsl"

// In the vertex function, after UNITY_SETUP_INSTANCE_ID(input):
float3 displacedOS = BillboardTransform(
    input.positionOS.xyz,
    float3(0, 0, 0),              // pivotOS
    TOOLKIT_BILLBOARD_PARAMS,     // from ToolkitInstancing.hlsl, NOT the raw _BillboardParams name
    _WorldSpaceCameraPos,
    TOOLKIT_CAMERA_FORWARD,       // from ToolkitInstancing.hlsl
    UNITY_MATRIX_M,
    UNITY_MATRIX_I_M);
output.positionCS = TransformObjectToHClip(displacedOS);
```

Pattern source: `ToolkitSpriteUnlit.shader:75-85` (`ToolkitSpriteDisplace`),
`ToolkitCompositeExample.shader:182-192` (`ToolkitCompositeDisplace`). **Every pass** your shader
declares (forward, shadow caster, depth-only, depth-normals, …) must call this same function with
the same inputs — see §3.3.

### 3.2 Flipbook only (fragment stage)

Flipbook addressing **belongs in the fragment stage**: it only decides which texels a fragment
reads, and it does not move any geometry. (Both atlas and slice reduce to an affine UV remap that
could technically be computed in the vertex stage and interpolated, but slice mode's
`Texture2DArray` sample has to happen in the fragment stage anyway, so keeping both there is what
keeps the two addressing modes swappable behind one function —
`ToolkitCompositeExample.shader:194-211`.)

Atlas mode:

```hlsl
#include "Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitFlipbook.hlsl"

// In the fragment function:
float2 atlasUv = AtlasFrameUV(input.uv, TOOLKIT_ATLAS_FRAME);
float4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, atlasUv);
```

Slice mode:

```hlsl
float3 sliceUv = SliceUV(input.uv, TOOLKIT_IMAGE_INDEX);
float4 sampled = SAMPLE_TEXTURE2D_ARRAY(_MainTexArray, sampler_MainTexArray, sliceUv.xy, sliceUv.z);
```

Pattern source: `ToolkitSpriteUnlit.shader:88-97` (`ToolkitSpriteSample`, both modes side by side
behind a `shader_feature_local`). Any pass that alpha-clips must call the **same** sample function
the colour pass uses, so a cutout hole lands in the same place in the shadow map as on screen
(`ToolkitCompositeExample.shader:208-210`).

### 3.3 VAT only (vertex stage, bone flavour)

Like billboarding, VAT skinning **must** run in the vertex stage — `VatBoneSkin` produces the
object-space position.

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitInstancing.hlsl"
#include "Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitVat.hlsl"

// Attributes must carry bone data in a UV channel — see §4:
//   float4 boneData : TEXCOORD1;   // (index0, index1, weight0, weight1)

// In the vertex function, after UNITY_SETUP_INSTANCE_ID(input):
float4 boneIndices = float4(input.boneData.x, input.boneData.y, 0, 0);
float4 boneWeights = float4(input.boneData.z, input.boneData.w, 0, 0);
float3 displacedOS = VatBoneSkin(
    _VatBoneTex, sampler_VatBoneTex,
    input.positionOS.xyz, boneIndices, boneWeights,
    TOOLKIT_VAT_FRAME_A, TOOLKIT_VAT_FRAME_B, TOOLKIT_VAT_BLEND,
    _VatTexelParams);
output.positionCS = TransformObjectToHClip(displacedOS);
```

Pattern source: `ToolkitVatCrowdUnlit.shader:85-95` (`ToolkitVatDisplace`), `66-73` (the
`VatAttributes.boneData` field). `_VatTexelParams` is a **material-level** property (not
per-instance): a per-instance texture or layout would split the BRG batch, exactly the cost this
shader exists to avoid (`ToolkitVatCrowdUnlit.shader:28-30`).

Normals, in any of the three techniques, must be displaced by the same function as the position and
then have the un-displaced origin subtracted out, because these transforms are affine (rotate/skin,
then translate); displacing a direction raw would carry the translation into it incorrectly. See
`ToolkitCompositeExample.shader:486-499` for the worked derivation and
`ToolkitVatCrowdUnlit.shader:106-110` for the VAT case.

### 3.4 Every pass must call the same displacement

This is not technique-specific — it applies to billboard and VAT alike, and it is the load-bearing
structural idea behind all three hand-written reference shaders. A billboard or a VAT skin that
displaces the colour pass but not the shadow-caster pass produces a shadow cast by the
**undisplaced** geometry: correct in the one pass you screenshot, wrong in the one pass you never
look at directly. All three reference shaders solve this by declaring exactly one displacement
function in the shared `HLSLINCLUDE` block above the passes and having every pass's vertex function
call it (`ToolkitSpriteUnlit.shader:74-85` + every `*Vertex` function below it;
`ToolkitVatCrowdUnlit.shader:85-114`; `ToolkitCompositeExample.shader:182-192`). This is enforced
for the shipped sprite shader by `ShaderConformanceTests.EveryPass_CallsTheSharedDisplacement`
(`ShaderConformanceTests.cs:117-131`) and
`TheSpriteShader_DeclaresEveryPassThatMustDisplace` (`ShaderConformanceTests.cs:90-110`), which
between them assert the shader declares `UniversalForward`, `ShadowCaster`, `DepthOnly`, and
`DepthNormals`, and that the shared displacement function is referenced at least once per pass.

---

## 4. The VAT mesh/vertex-data contract

This is the requirement most likely to trip up an integrator, so read it carefully.

**Bone influences travel in UV channel 1 (`TEXCOORD1`), packed as
`(boneIndex0, boneIndex1, weight0, weight1)` — not through the standard skinning semantics
`BLENDINDICES`/`BLENDWEIGHT`.**

Source, verbatim (`ToolkitVatCrowdUnlit.shader:10-16`):

> THE MESH IS AN ORDINARY MESH. Bone influences travel in UV1 rather than through skinning
> semantics, because a plain MeshRenderer does not bind BLENDINDICES/BLENDWEIGHT — and the whole
> point of VAT is not to need a SkinnedMeshRenderer. VatTentacleBakeRunner packs them:
>
> `uv1 = (boneIndex0, boneIndex1, weight0, weight1)`
>
> Two influences, which is the budget §12 R3 recommends for crowds on constrained hardware.

Why this is the way it is: `BLENDINDICES`/`BLENDWEIGHT` are vertex semantics the GPU skinning
pipeline binds for a `SkinnedMeshRenderer`. VAT's entire value proposition is rendering baked
animation off an **ordinary `MeshRenderer`** with no skeleton and no per-entity CPU skinning cost —
so the mesh cannot rely on the skinned-mesh binding path, because it is not a skinned mesh at
render time. The bone data has to travel as ordinary per-vertex data the shader reads like any
other UV channel, and `TEXCOORD1` is where the shipped bake pipeline puts it. The shader-side
struct confirms the binding: `VatAttributes.boneData : TEXCOORD1` with the comment
`// (index0, index1, weight0, weight1)` (`ToolkitVatCrowdUnlit.shader:71`).

The two-influence limit (versus the four `VatBoneSkin` supports in its general form,
`ToolkitVat.hlsl:109-141`) is the reference shader's own choice for crowd-scale budget, not a limit
of `ToolkitVat.hlsl` itself — `VatBoneSkin` unrolls up to four influences and skips any with
`weight <= 0` (`ToolkitVat.hlsl:118-125`), so a host mesh with genuine four-influence data can pass
a full `float4`/`float4` pair instead of the crowd shader's `(v, 0, 0, 0)` padding pattern shown in
`ToolkitVatCrowdUnlit.shader:87-88`.

**The packing is confirmed in source** — `Assets/AnimationToolkitShaderDemo/Editor/VatTentacleBakeRunner.cs:92-103`:

```csharp
BoneWeight[] sourceWeights = renderer.sharedMesh.boneWeights;
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
```

`SetUVs(1, …)` is `TEXCOORD1`, and the component order is exactly `(idx0, idx1, w0, w1)`. Both
sides of the contract are therefore attested by source, not only by the shader's own comment.

> **Packaging gap, worth knowing before you integrate.** That code lives in the **host project's
> demo folder**, not in the package. A package consumer gets the shader and the include but *no
> shipped helper that packs `uv1`* — you must write the loop above yourself against your own mesh.
> It is a dozen lines and the contract is fully specified here, but it is a manual step, and
> forgetting it produces a mesh that renders as a motionless clump rather than an error.

---

## 5. Texture addressing — frame `f`, element `e` → texel coordinate

This is the same contract from two sides — `ToolkitVat.hlsl` (the read side, GPU) and
`VatTextureBaker.cs` (the write side, CPU) — restated together so both halves are visible at once.
`VatTextureBaker`'s own class doc calls this out explicitly: *"The layout is the contract with
`ToolkitVat.hlsl`. ... Changing either side without the other produces a mesh that renders as
noise, so the two are documented against each other rather than independently"*
(`VatTextureBaker.cs:126-131`).

**Layout rule**, common to both sides:

- A "global frame" is a row block. Frame `f` of a clip occupies `rowsPerFrame` consecutive rows
  starting at row `f * rowsPerFrame`.
- Within a frame, element `e` (a bone or a vertex) sits at column `e % textureWidth`, row offset
  `e / textureWidth` — an element count larger than the texture width wraps onto subsequent rows.
- Bone flavour: `rowsPerFrame = 3` (one row per matrix row of a 3×4 matrix, xyz packed RGB with
  translation in `.w`). Vertex flavour: `rowsPerFrame = 1` (one RGB position per element; a second,
  identically laid-out texture carries octahedral-encoded normals in RG).

GPU read side (`ToolkitVat.hlsl:37-40`, `:49-60`):

```hlsl
// vatTexelParams: x = textureWidth, y = textureHeight, z = rowsPerFrame, w = boneOrVertexCount
float2 ToolkitVatTexelUV(float elementIndex, float frame, float rowInFrame, float4 vatTexelParams)
{
    float column = fmod(elementIndex, width);
    float wrapRow = floor(elementIndex / width);
    float row = frame * rowsPerFrame + rowInFrame + wrapRow;
    return float2((column + 0.5) / width, (row + 0.5) / height);
}
```

Sampling lands on the texel **centre** (`+ 0.5`), not the corner — a corner sample lets a bilinear
filter blend two neighbouring elements, e.g. averaging two unrelated bone matrices into a melting
limb (`ToolkitVat.hlsl:45-48`). This is also why the texture is created point-filtered (§below).

CPU write side (`VatTextureBaker.cs:504-538` for bone, `:540-565` for vertex/normal):

```csharp
int column = elementIndex % width;
int wrapRow = elementIndex / width;
for (int matrixRow = 0; matrixRow < BoneRowsPerFrame; matrixRow++)
{
    int row = frameIndex * BoneRowsPerFrame + matrixRow + wrapRow;
    pixels[row * width + column] = new Color(
        boneMatrix[matrixRow, 0], boneMatrix[matrixRow, 1],
        boneMatrix[matrixRow, 2], boneMatrix[matrixRow, 3]);
}
```

Note `WriteBoneTexture`'s indexing (`row * width + column`) is the exact inverse of
`ToolkitVatTexelUV`'s `(column, row)` derivation — the two are meant to be read side by side.

The GPU reads a **fractional** global frame index (`_VatFrameA` / `_VatFrameB`) and lerps between
`floor` and `floor + 1` itself (`VatBoneMatrixAtFrame`, `ToolkitVat.hlsl:84-98`), which is what
makes playback smooth at any framerate rather than stepping at the bake rate. This is safe at the
clip boundary only because a "loop-safe" clip has its frame-0 row duplicated after its last frame
at bake time (`VatTextureBaker.cs:486-500`, the `bakeClip.loopSafe` branch) — so `floor + 1` at the
last real frame reads the duplicated row, never the next clip's first row.

**Texture filtering/wrapping is part of the contract, not styling.** `VatTextureBaker.CreateTexture`
sets `FilterMode.Point` and `TextureWrapMode.Clamp` unconditionally
(`VatTextureBaker.cs:572-579`), with the comment: *"A bilinear sampler would blend neighbouring
bones or vertices ... and repeat wrapping would make an off-by-one row read the far side of the
texture instead of clamping."* A host that re-imports or re-processes the baked texture and resets
these settings will get the exact "renders as noise" failure mode in §6.

---

## 6. Troubleshooting

| Symptom | Likely cause |
|---|---|
| Mesh renders as visual noise / scrambled geometry | `rowsPerFrame` or `textureWidth` passed to the shader (`_VatTexelParams`) does not match what `VatTextureBaker` actually baked — the layout is a two-sided contract (§5); a mismatch reads matrix/position data at the wrong row/column entirely. |
| Every instance shows the same frame / same billboard mode, none animate or turn individually | A per-instance property (e.g. `_VatFrameA`, `_BillboardParams`) is declared as a plain `CBUFFER`/material uniform but missing from `UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)` in `ToolkitInstancing.hlsl`, or the shader reads the raw name instead of the `TOOLKIT_*` accessor. Confirm the exact name matches the `[MaterialProperty("_Name")]` attribute in `MaterialPropertyComponents.cs` (§1). |
| Limbs/joints appear to melt or average together during VAT playback | The VAT texture's sampler is bilinear/trilinear instead of point-filtered, or the texture's wrap mode is not clamped — see §5; a corner sample or a wrapped row blends two unrelated bones/vertices. |
| Shadow silhouette doesn't match the on-screen billboard/VAT silhouette; shadow appears to face the wrong way or freeze in an undisplaced pose | Either (a) the `ShadowCaster` pass doesn't call the shared displacement function at all, or (b) `ToolkitBillboard.hlsl` (or a caller) derives facing from `UNITY_MATRIX_V`/`UNITY_MATRIX_I_V`, which is the light's view matrix during shadow rendering, not the camera's — see §2.1 and `ShaderConformanceTests.TheBillboardInclude_NeverUsesTheViewMatrix`. |
| Normals buffer looks wrong under a billboarded/skinned mesh (bad SSAO, wrong lighting response in `DepthNormals`) | The `DepthNormals` pass displaces position but forgets to displace the normal by the same transform (position-of-displaced-normal minus displaced-origin, per §3.3/§3.4). |
| Screen-aligned billboard mode looks spherical instead of flat/uniform | Host never wrote `_ToolkitCameraForward` per frame; the include intentionally degrades screen-aligned to spherical rather than collapsing the quad when the forward is zero (`ToolkitBillboard.hlsl:79-83`). Write the global once per frame from the active camera. |
| Billboard flips/snaps for one frame near a specific camera angle | Camera passed through (or very near) the billboard's own pivot, or (upright mode) the camera looked near-straight-down; both are the near-zero-facing-vector degenerate case the `TOOLKIT_BILLBOARD_EPSILON` guard leaves unrotated rather than rotating by a near-zero vector (`ToolkitBillboard.hlsl:36-39`, `:123-137`). |
| Whole sprite sheet appears vertically mirrored | `AtlasRectFromGrid` treats row 0 as the sheet's top row by design (`ToolkitFlipbook.hlsl:50-51`); a caller computing its own rect by hand and getting the row/UV direction backwards produces exactly this symptom. |
| One-frame flicker between two adjacent flipbook frames near a frame boundary | `_ImageIndex` arrives as a float that lands just under an integer (e.g. `2.9999`) from packing/precision; `SliceUV` rounds rather than truncates specifically to avoid this (`ToolkitFlipbook.hlsl:22-26`) — if a *custom* caller re-implements slice addressing with `floor`/cast-to-int instead of `round`, this symptom reappears. |
| Shader Graph fails to compile with a duplicate-property/duplicate-declaration error around DOTS instancing | `ToolkitInstancing.hlsl` was `#include`d from inside a Shader Graph custom function/subgraph. It is for hand-written shaders only — Shader Graph already emits its own instancing block from Hybrid Per Instance properties (§1, `ToolkitInstancing.hlsl:12-17`). |
| Toolkit shader compiles and previews fine in the Material Inspector but never animates once entities render it (or vice versa) | The non-instanced fallback branch (`#else` in `ToolkitInstancing.hlsl:57-68`) and the instanced branch must both exist and must alias to the same names; if you hand-rolled a partial copy of this file, check both branches are present — `ShaderConformanceTests.TheInstancingBlock_GuardsTheNonInstancedPath` (`ShaderConformanceTests.cs:218-228`) is the check the shipped file passes. |
| Reused one of the four includes in another project and it now depends on this package | An include unexpectedly grew an `#include` of another package file or started reading an undeclared global — this is exactly what `ShaderConformanceTests.TheIncludes_StayStandalone` guards against for the shipped copies (`ShaderConformanceTests.cs:168-180`), checking `ToolkitBillboard.hlsl` and `ToolkitFlipbook.hlsl` contain no `#include` after comment-stripping. If you are editing a *copy* you pulled into your own project, this test does not run against it — verify by inspection. |

---

## Sources read for this document

- `Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitBillboard.hlsl`
- `Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitFlipbook.hlsl`
- `Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitVat.hlsl`
- `Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitInstancing.hlsl`
- `Packages/com.dotsanimationtoolkit/Shaders/HandWritten/ToolkitSpriteUnlit.shader`
- `Packages/com.dotsanimationtoolkit/Shaders/HandWritten/ToolkitVatCrowdUnlit.shader`
- `Packages/com.dotsanimationtoolkit/Shaders/HandWritten/ToolkitCompositeExample.shader`
- `Packages/com.dotsanimationtoolkit/Runtime/Components/MaterialPropertyComponents.cs`
- `Packages/com.dotsanimationtoolkit/Editor/VatBaking/VatTextureBaker.cs`
- `Packages/com.dotsanimationtoolkit/Tests/EditMode/ShaderConformanceTests.cs`

**Not verified** (called out inline above, repeated here for visibility):

- ~~`VatTentacleBakeRunner` could not be found.~~ **Resolved.** It is at
  `Assets/AnimationToolkitShaderDemo/Editor/VatTentacleBakeRunner.cs` and its `uv1` packing is
  quoted in §4 above. The real finding underneath the false alarm is a *packaging* one: the packer
  lives in the host demo folder, so the package ships no helper for it.
- The claim that Entities Graphics provides a built-in `URPMaterialPropertyBaseColor` binding for
  `_BaseColor` is sourced from a comment in `ToolkitCompositeExample.shader`, not from reading the
  Entities.Graphics package source itself.
