# Phase B Architecture — DOTS Animation Toolkit

**Repo:** `C:\Users\spenc\Documents\GitHub\Stitch_Punk` · **Date:** 2026-07-26 · **Author:** Architect agent (Phase B)
**Inputs honored:** `Docs/AnimationToolkit/Phase_A_Audit.md` (approved), `Docs/AnimationToolkit/Phase_A_Review.md`, `Assets/_Vault/Memories/Code/RULES.md`, `Assets/CLAUDE.md`, `Assets/_Scripts/Systems/SystemGroups.cs`.

This document is self-contained: a build agent implements any module from its contract (§8) plus the referenced sections without reading the audit or the host codebase. Where the audit's preserve/absorb/replace verdicts are overruled, the overrule is stated inline and collected in §10.

**Section anchors**
1. [Package identity & layout](#s1) · 2. [Domain model & glossary](#s2) · 3. [Authoring data model](#s3) · 4. [Bake pipeline](#s4) · 5. [Runtime architecture](#s5) · 6. [Shader architecture](#s6) · 7. [Editor architecture](#s7) · 8. [Integration contracts](#s8) · 9. [Module build plan](#s9) · 10. [Answers to the audit's open questions](#s10) · 11. [Test strategy](#s11) · 12. [Risks & documented limitations](#s12) · 13. [Stitch Punk migration appendix](#s13)

---

<a name="s1"></a>
## 1. Package identity & layout

### 1.1 Identity

| Field | Value |
|---|---|
| Display name | **DOTS Animation Toolkit** (final — product-owner decision, 2026-07-27) |
| Package id | `com.stitchpunk.dotsanimationtoolkit` (final, same decision) |
| Version | `0.1.0` during Phase C; `1.0.0` at first publish |
| Unity | `6000.5` minimum (`"unity": "6000.5"` in package.json) |
| Root namespace | `StitchPunk.AnimationToolkit` (+ `.Authoring`, `.Editor` sub-namespaces) |
| Dependencies (package.json) | `com.unity.entities: 6.5.0`, `com.unity.entities.graphics: 6.5.0`, `com.unity.burst: 1.8.29`, `com.unity.collections: 6.5.0`, `com.unity.mathematics: 1.4.0`, `com.unity.render-pipelines.universal: 17.5.0` |
| Forbidden dependencies | Anything under `Assets/` of the host project; `com.unity.physics`; UniTask; Reflex; Rive. Zero references to Stitch Punk game code — enforced by asmdef reference lists and by the packaging conformance test (§8 M6). |

Development happens as an **embedded package** at `Packages/com.stitchpunk.dotsanimationtoolkit/` inside this repo (standard UPM embedded-dev workflow). The host project may reference it, never the reverse.

**Naming note (product-owner decision, 2026-07-27):** the product name is **DOTS Animation Toolkit** / `com.stitchpunk.dotsanimationtoolkit`. The C# root namespace and asmdef prefix deliberately remain `StitchPunk.AnimationToolkit` — inside code the DOTS qualifier is redundant (everything in the package is DOTS), and shorter type-qualified names read better in user projects. This is an intentional id↔namespace divergence, not an incomplete rename.

### 1.2 Folder tree

```
Packages/com.stitchpunk.dotsanimationtoolkit/
├── package.json
├── CHANGELOG.md
├── LICENSE.md
├── README.md
├── Runtime/
│   ├── StitchPunk.AnimationToolkit.Runtime.asmdef
│   ├── Identity/            (ClipId, TargetId, StableIdUtility-runtime half)
│   ├── Components/          (all IComponentData / IBufferElementData / enableables)
│   ├── Blobs/               (ClipRegistryBlob + child structs)
│   ├── Sampling/            (ClipSampler static Burst functions, easing, wrap math)
│   ├── Systems/             (all ISystems + AnimationToolkitSystemGroups.cs)
│   └── Api/                 (AnimationCommandUtil, ClipRegistryUtil, PlaybackQuery,
│                             ToolkitWorldControl)
├── Authoring/
│   ├── StitchPunk.AnimationToolkit.Authoring.asmdef
│   ├── Assets/              (RigAsset, ClipAsset, ClipSetAsset, VatTextureSetAsset)
│   ├── Build/               (ClipRegistryBuilder — SO → BlobBuilder, shared by Baker & editor preview)
│   ├── Validation/          (ClipValidation rule set, ValidationMessage)
│   └── Baking/              (ActorAuthoring, RigTargetAuthoring, all Bakers, RigBindingBakingSystem)
├── Editor/
│   ├── StitchPunk.AnimationToolkit.Editor.asmdef        (Editor-only)
│   ├── ClipEditor/          (ClipEditorWindow + UXML/USS + timeline elements)
│   ├── Preview/             (PreviewPlaybackDriver, PreviewRigMirror, thumbnail renderers)
│   ├── VatBaking/           (VatTextureBaker core + VatBakeWindow)
│   ├── Inspectors/          (RigAsset/ClipSetAsset/VatTextureSetAsset custom editors)
│   └── Migration/           (StableId regeneration tooling, id-collision postprocessor)
├── Shaders/
│   ├── Includes/            (ToolkitVat.hlsl, ToolkitBillboard.hlsl, ToolkitFlipbook.hlsl,
│   │                         ToolkitInstancing.hlsl)
│   ├── SubGraphs/           (VatBoneSkin, VatVertexFetch, FlipbookSliceUV, AtlasFrameUV,
│   │                         BillboardTransform .shadersubgraph)
│   ├── Graphs/              (ToolkitVatLit, ToolkitVatUnlit, ToolkitSpriteLit .shadergraph)
│   └── HandWritten/         (ToolkitVatCrowdUnlit.shader — explicit DOTS-instancing macro reference)
├── Tests/
│   ├── EditMode/
│   │   └── StitchPunk.AnimationToolkit.Tests.EditMode.asmdef
│   └── PlayMode/
│       └── StitchPunk.AnimationToolkit.Tests.PlayMode.asmdef
├── Samples~/
│   ├── CutoutCharacter/     (2D paper-doll rig, transform+flipbook clips, billboard, events)
│   ├── VatCrowd/            (bone-VAT: source skinned mesh + prebaked textures + 1000-instance scene)
│   └── CompositeActor/      (one actor mixing transform tracks + VAT sub-part + flipbook face
│                             + billboard + LOD + camera-sync sample MonoBehaviour ToolkitCameraSync)
└── Documentation~/
    ├── index.md             (manual: quick start, per-technique guides)
    ├── shader-contract.md   (the §6 property table, normative copy)
    ├── integration.md       (system-group insertion, visibility provider, event consumption)
    └── platform-notes.md    (Switch/console format + budget guidance, §12 excerpts)
```

### 1.3 Assembly definitions

| asmdef | References | Platforms | Notes |
|---|---|---|---|
| `StitchPunk.AnimationToolkit.Runtime` | Unity.Entities, Unity.Entities.Graphics, Unity.Burst, Unity.Collections, Unity.Mathematics, Unity.Mathematics.Extensions, Unity.Transforms | All | No UnityEditor usage anywhere. `allowUnsafeCode: true` (blob building helpers). |
| `StitchPunk.AnimationToolkit.Authoring` | Runtime, Unity.Entities, Unity.Entities.Hybrid, Unity.Burst, Unity.Collections, Unity.Mathematics, Unity.Mathematics.Extensions | All (bakers/SO classes compile for players; Unity strips baking execution from builds — this is the standard Entities authoring layout and avoids the host project's mistake of *editor tooling* in an unrestricted assembly) | SOs, Bakers, `ClipRegistryBuilder`, `ClipValidation`. Contains **zero** `UnityEditor` references — anything needing UnityEditor goes to the Editor asmdef. |
| `StitchPunk.AnimationToolkit.Editor` | Runtime, Authoring, Unity.Entities, Unity.Entities.Hybrid, Unity.Burst, Unity.Collections, Unity.Mathematics | **["Editor"] only** | Windows, preview, VAT texture baker, inspectors, id tooling. This is the fix for the audit §1/§4 finding (host `StitchPunk.Editor.asmdef` ships in builds — the package must never repeat it; enforced by test, §8 M6). |
| `StitchPunk.AnimationToolkit.Tests.EditMode` | Runtime, Authoring, Editor, UnityEngine.TestRunner, UnityEditor.TestRunner, Unity.Entities, Unity.Collections, Unity.Mathematics, Unity.Mathematics.Extensions, Unity.Burst | ["Editor"] | Pure math/data/determinism/validation tests (§11). |
| `StitchPunk.AnimationToolkit.Tests.PlayMode` | Runtime, Authoring, UnityEngine.TestRunner, Unity.Entities, Unity.Entities.Hybrid, Unity.Entities.Graphics (amendment A33), Unity.Collections, Unity.Mathematics, Unity.Mathematics.Extensions, Unity.Burst, Unity.Transforms | **[]** (amendment A25, superseding A17) | World/system integration tests (§11). |

**Amendment A17 (C3 gate, 2026-07-30 — product-owner approved): the PlayMode test assembly is Editor-only.** Its `includePlatforms` is `["Editor"]`, not all platforms. The suite bakes prefabs, and Unity's baking pipeline has no player-side equivalent: `BakingUtility` lives in editor code, so a player build of these tests cannot run whatever its platform list claims. Shipping an assembly whose declared platforms are false is exactly the defect this package's §1.3 exists to prevent — and the one the host project it was extracted from actually has. "Document it and move on" was rejected for that reason; moving the suite to EditMode was rejected because it costs more normative surgery (§8 M2 and §11.2 both place these tests in PlayMode, and §1.3's EditMode reference list lacks `Unity.Entities.Hybrid` and `Unity.Transforms`). Consequence to accept knowingly: this restricts C4's M3 PlayMode suite too, and no §8 bullet requires a player test run — M6's player evidence is a `VatCrowd` build, not a test pass.

**Amendment A25 (C3 Gate 4, 2026-08-01 — product-owner ratified 2026-08-01; supersedes A17's platform cell).** `Tests.PlayMode`'s `includePlatforms` is `[]`, not `["Editor"]`.

**A17 was self-defeating, and nothing detected it for a whole build step.** Setting `includePlatforms: ["Editor"]` does not merely narrow which players may run the suite — it makes the assembly *editor-only*, and Unity's Test Framework classifies an editor-only test assembly as an **EditMode** assembly. So A17 did not restrict the PlayMode suite; it **abolished** it. Measured at `bce381e` via the Test Runner: a project-wide PlayMode run discovered **zero** tests and reported `Passed`, while all 27 acceptance tests turned up under EditMode. A17's own text rejects "moving the suite to EditMode" as costing too much normative surgery — and then its implementation performed exactly that move, silently and without the surgery, leaving §8 M2 and §11.2 describing a PlayMode suite that no longer existed. The owner's "both tabs green" reading on 2026-07-31 is consistent with this: a tab that discovers nothing reports success.

**A17's underlying concern is real but narrower than it assumed.** `BakingUtility` is editor-only, so a *player* test build genuinely cannot run these fixtures. But a test assembly carries `"defineConstraints": ["UNITY_INCLUDE_TESTS"]` and so never enters an ordinary player build; the exposure is limited to someone explicitly building a player test run, which A17 itself notes no §8 bullet requires. The honest trade is therefore: an `includePlatforms` of `[]` overstates the suite's player-side capability *only under a player test build that nothing asks for*, whereas `["Editor"]` destroys the suite's mode under the run everybody actually performs. Losing PlayMode entirely is far the worse defect, and — unlike the one A17 feared — it had already happened.

**Consequences accepted knowingly:** a player test build of this assembly would fail on the baking fixtures; this is a documented limitation rather than a silent falsehood, and §8 requires no such run. C4's M3 suite is unblocked and will genuinely run in PlayMode.

**Guarded, not just fixed.** `PlayModeAssemblySmokeTest` now asserts `Application.isPlaying` and that a yielded frame advances `Time.frameCount` — both false in EditMode — so a future re-restriction fails loudly instead of moving the suite's mode in silence. The prior version asserted only the assembly's *name*, which is equally true in either mode; that is why it passed throughout.

**To revert:** set the cell above and `PackagingConformanceTests.AsmdefExpectations` back to `["Editor"]`, restore the asmdef, and delete the two mode assertions in `PlayModeAssemblySmokeTest`. Doing so knowingly re-adopts a suite that does not run in PlayMode; if that is the intent, §8 M2 and §11.2 must be rewritten to say the acceptance tests are EditMode tests, and §1.3's EditMode reference list gains `Unity.Entities.Hybrid` and `Unity.Transforms` — the surgery A17 declined.

**`Unity.Mathematics.Extensions` is mandatory, not optional.** `Unity.Mathematics.AABB` is defined in that assembly (it ships inside the Entities package, not inside Unity.Mathematics), and C# requires a reference to the *defining* assembly. `ClipBlob.offsetBounds` is an `AABB` (§4.2) and §5.9's `RenderBoundsUpdateSystem` must write `RenderBounds.Value`, which is also an `AABB`; without the reference that mandated system cannot compile. The Authoring assembly needs it because `ClipRegistryBuilder` writes `offsetBounds` at bake (§4.6), and both test assemblies need it because they assert on the blob layout and on sampled bounds. Every reference list places it immediately after `Unity.Mathematics`, matching the convention already used for `Unity.Entities.Graphics` / `Unity.Entities.Hybrid`.

**Amendment A33 (C4.7, 2026-08-05): the PlayMode test assembly references `Unity.Entities.Graphics`.** The same "reference the *defining* assembly" rule that made `Unity.Mathematics.Extensions` mandatory applies to `Unity.Rendering.RenderBounds`, which is defined in `Unity.Entities.Graphics` rather than in `Unity.Entities`. §5.8's `RenderBoundsUpdateSystem` writes that component, so the fixtures asserting on the box it publishes must name the assembly. The Runtime list has carried the reference since §1.3 was written; only the test list was short, and nothing surfaced it until a test first read a `RenderBounds` back. The reference is placed after `Unity.Entities.Hybrid`, matching the ordering convention noted below.

Shader folders carry no asmdef (no C#). Samples carry their own small asmdefs referencing Runtime+Authoring only.

---

<a name="s2"></a>
## 2. Domain model & glossary

### 2.1 Concept graph

```
RigAsset ──defines──> Targets (stable-id'd named slots) + Layers (ordered priority slots)
   ▲                        ▲
   │ scoped to              │ tracks bind to targets by TargetId
ClipAsset ──contains──> TransformTracks / SpriteTracks / EventMarkers / VatSource
   ▲
   │ registered in
ClipSetAsset ──references──> RigAsset + N ClipAssets + (optional) VatTextureSetAsset
   ▲                                                        ▲
   │ bound by                                               │ produced by VatTextureBaker from
ActorAuthoring (prefab root) ── children ──> RigTargetAuthoring parts (quads / VAT meshes / flipbook quads)
   │
   └─ bakes to ──> Actor entity (ClipRegistry blob + PlaybackLayer buffer + command/event buffers)
                       └── part entities (RigPartBinding + TargetPose + material-property components)
```

### 2.2 Glossary (final names — used consistently package-wide)

| Term | Final type name | Definition |
|---|---|---|
| **Rig** | `RigAsset` | The authoring definition of an animatable thing: its named **targets**, its ordered **layers**, and per-target defaults (kind, bounds extents). One rig serves many clips and many actors. |
| **Target** | `RigTargetDefinition` (row in `RigAsset`) | A named, stable-id'd slot a track can animate — a 2D cutout part quad, a flipbook plane, or a VAT sub-mesh. Identified by `TargetId` (never by name or list position). |
| **Layer** | `LayerDefinition` (row in `RigAsset`) | A playback slot on the actor. Layer identity **is** its list position (index = priority; higher index composites later, i.e. wins). Names are cosmetic. Max 8 layers per rig. |
| **Clip** | `ClipAsset` | One animation: duration, loop default, blend defaults, tracks, event markers, optional VAT source. Identified by `ClipId` (§3.4). |
| **Track** | `TransformTrack` / `SpriteTrack` (serialized rows in `ClipAsset`) | A keyed curve bound to one target: TRS keys (transform technique) or sprite-frame keys (flipbook technique). |
| **Technique** | `AnimTechnique` (enum: `TransformTracks`, `FlipbookSlice`, `FlipbookAtlas`, `BoneVat`, `VertexVat`) | How a target's animation reaches the screen. One clip may span several techniques across its targets. Billboarding is not a technique — it is a per-target render modifier (§6). |
| **Clip set** | `ClipSetAsset` | The registry: rig + clips + optional VAT texture set. Replaces the audit's `AnimationLibrarySO` (audit §8: Replace — honored). Bakes to one `ClipRegistryBlob`. |
| **Texture set** | `VatTextureSetAsset` | The baked VAT product: textures + runtime mesh + per-clip frame ranges + bounds + `setKey`. Generated only by `VatTextureBaker`; treated read-only by hand. |
| **Actor** | entity baked from `ActorAuthoring` | A composed character instance: one root entity carrying playback state, N part entities carrying presentation state. "Composition" from the product scope = an actor whose parts use different techniques driven by the same layers. |
| **Part** | entity baked from `RigTargetAuthoring` | One child entity bound to a rig target (`RigPartBinding`). |
| **Event** | `EventMarker` → `AnimEventOutput` | A typed, keyed marker on a clip's timeline; emitted into a per-actor buffer at runtime. `ClipFinished` is a reserved built-in event. |
| **Command** | `AnimationCommand` | The request API games write: Play / Queue / Stop / SetSpeed / SetTime per layer. |

Naming rules: assets end in `Asset`; runtime components are nouns (`PlaybackLayer`); enableable tags are adjectives/states (`AnimVisible`, `AnimEventsPending`, `RigBindingUninitialized`); material-property components end in `Property`; systems end in `System`; blob structs end in `Blob`. `EnabledRefRW/RO` parameters are named `<component>Enabled` (host convention, adopted package-wide).

---

<a name="s3"></a>
## 3. Authoring data model

All authoring types live in `StitchPunk.AnimationToolkit.Authoring`. All are plain `ScriptableObject`s; tracks/keys/markers are `[Serializable]` classes **inline in the ClipAsset** (audit §8 absorbs the inline-track shape — sub-asset-per-keyframe was the orphaned `KeyframeSO` design and stays dead). The one sub-asset relationship: **ClipAssets may optionally be created as sub-assets of their ClipSetAsset** via the editor's "New Clip in Set" action, keeping a set self-contained for distribution; free-standing clip assets are equally valid — the builder only follows references.

### 3.1 `RigAsset`

```csharp
public sealed class RigAsset : ScriptableObject
{
    [SerializeField] internal ulong stableId;                 // §3.4; asset-level identity
    public List<RigTargetDefinition> targets;                 // unique stableId per row (uint)
    public List<LayerDefinition> layers;                      // index = priority; max 8
    public MirrorPair[] mirrorPairs;                          // user-configured L/R table for Mirror Clip
}

[Serializable] public sealed class RigTargetDefinition
{
    public string displayName;                                // freely renameable
    [SerializeField] internal uint stableId;                  // generated on add; never edited
    public TargetKind kind;                                   // Quad, VatMesh, FlipbookPlane
    public float3 boundsExtents;                              // conservative local half-extents used
                                                              // by bake-time bounds math (§4.6); default 0.5
}

[Serializable] public sealed class LayerDefinition
{
    public string displayName;                                // cosmetic only
    public bool defaultActive;                                // seeded into PlaybackLayer at bake
}

[Serializable] public struct MirrorPair { public uint leftTargetId; public uint rightTargetId; }
```

Validation (surfaced per §7.6, enforced at bake per §4.1 — the rule table itself is §3.5): target `stableId` unique within rig; ≤ 8 layers; ≥ 1 layer; `boundsExtents` ≥ 0.

Design note — layers are deliberately **positional**, not stable-id'd: a layer's meaning *is* its compositing priority, so reordering is a semantic edit (unlike renaming a clip). Targets, whose meaning is independent of order, get stable ids.

### 3.2 `ClipAsset`

```csharp
public sealed class ClipAsset : ScriptableObject
{
    [SerializeField] internal ulong stableId;                 // the ClipId value (§3.4)
    public RigAsset rig;                                      // scope: tracks reference this rig's targets
    [Min(0.001f)] public float duration;                      // seconds; validation floor 1 ms
    public LoopMode defaultLoop;                              // Once, Loop, PingPong
    [Min(0f)] public float defaultBlendIn;                    // seconds; 0 = pop
    [Min(0f)] public float defaultBlendOut;
    public List<TransformTrack> transformTracks;
    public List<SpriteTrack> spriteTracks;
    public List<EventMarker> events;
    public VatClipSource vatSource;                           // optional; used only by VatTextureBaker
}

[Serializable] public sealed class TransformTrack
{
    public uint targetId;                                     // RigTargetDefinition.stableId
    public TrackBlendOp blendOp;                              // Override, Additive
    public AnimatedChannels channels;                         // [Flags]: PositionXY, LayerZ, RotationZ, Scale, SpriteFrame-excluded
    public List<TransformKey> keys;                           // kept time-sorted by editor; bake re-verifies
}

[Serializable] public struct TransformKey
{
    public float normalizedTime;                              // [0,1]
    public float3 position;                                   // x/y local offset, z = draw-layer order (audit §2.5 convention absorbed)
    public float rotationZ;                                   // degrees
    public float2 scale;                                      // negative = flip (applied via PostTransformMatrix, §5.6)
    public Interpolation interpolation;                       // Linear, Step, EaseIn, EaseOut, EaseInOut (audit easings absorbed)
}

[Serializable] public sealed class SpriteTrack
{
    public uint targetId;
    public SpriteFrameMode mode;                              // Slice (Texture2DArray index) or AtlasRect
    public List<SpriteKey> keys;
}

[Serializable] public struct SpriteKey
{
    public float normalizedTime;
    public int sliceIndex;                                    // Slice mode; -1 = no change (audit convention absorbed)
    public float4 atlasRect;                                  // AtlasRect mode: scale.xy, offset.zw
}

[Serializable] public struct EventMarker
{
    public float normalizedTime;
    public uint eventKey;                                     // user key ≥ 16; 0–15 reserved (§5.5, V09)
    public int intParam;
    public float floatParam;
}

[Serializable] public sealed class VatClipSource                // consumed only in-editor by VatTextureBaker
{
    public AnimationClip sourceClip;                          // Unity clip sampled at bake time
    [Min(1f)] public float sampleFps;                         // default 30
    public bool loopSafe;                                     // bake duplicate first frame at the end (§4.7)
}
```

### 3.3 `ClipSetAsset` and `VatTextureSetAsset`

```csharp
public sealed class ClipSetAsset : ScriptableObject
{
    [SerializeField] internal ulong stableId;
    public RigAsset rig;
    public List<ClipAsset> clips;                             // all must reference the same rig
    public VatTextureSetAsset vatTextures;                    // required iff any clip has a VatClipSource
}

public sealed class VatTextureSetAsset : ScriptableObject     // GENERATED by VatTextureBaker; fields read-only in inspector
{
    [SerializeField] internal ulong setKey;                   // stable id; the blob↔texture link key (§4.4)
    public VatFlavor flavor;                                  // BoneMatrix, VertexPosition
    public Texture2D boneTexture;                             // BoneMatrix flavor
    public Texture2D positionTexture;                         // VertexPosition flavor
    public Texture2D normalTexture;                           // VertexPosition flavor, optional
    public Mesh runtimeMesh;                                  // static mesh with indices/weights packed in UV1/UV2 (bone flavor)
    public int boneCount;                                     // bone flavor
    public int vertexCount;                                   // vertex flavor
    public int textureWidth;                                  // texel addressing params, mirrored into blob
    public int rowsPerFrame;                                  // 1 for bone flavor; ceil(vertexCount/width) for vertex flavor
    public List<VatClipRange> clipRanges;                     // one per baked clip
    public ulong sourceHash;                                  // hash of (source mesh + clips + settings); staleness detection
    public int schemaVersion;
}

[Serializable] public struct VatClipRange
{
    public ulong clipId;                                      // ClipAsset.stableId this range belongs to
    public int frameStart;                                    // first global frame index in the texture
    public int frameCount;                                    // includes the duplicated loop frame when loopSafe
    public float fps;
    public Bounds bounds;                                     // object-space AABB over all baked frames
}
```

### 3.4 Identity scheme (the enum-ordinal replacement — exact specification)

The audit (§3.1, §7, open question 1) shows enum-ordinal identity corrupts data on mid-enum insertion. Replacement:

**ID type.** `ClipId` is a 64-bit value type in the Runtime asmdef:

```csharp
public readonly struct ClipId : IEquatable<ClipId>
{
    public readonly ulong Value;
    public ClipId(ulong value) { Value = value; }
    public bool IsValid => Value != 0;                        // 0 reserved = "none/invalid"
}
```

`TargetId` is the analogous 32-bit struct wrapping `uint` (per-rig scope makes 32 bits ample; 0 reserved).

**Generation.** When an identity-bearing asset (`ClipAsset`, `RigAsset`, `ClipSetAsset`, `VatTextureSetAsset`) or a `RigTargetDefinition` row is created and its `stableId == 0`, the Authoring layer assigns `stableId = Fold(Guid.NewGuid())` where `Fold` xors the GUID's high and low 64 bits (targets truncate to 32 bits). Assignment happens in `OnValidate` (covers Reset/duplication-then-clear) and in the editor asset factories. IDs are **random, not name-derived** — renames can never change identity.

**Persistence.** `stableId` is a `[SerializeField] internal` field, hidden from direct editing, drawn read-only (hex) in a foldout by the custom inspectors. It never changes after creation except through the explicit remap tooling below.

**Stability guarantees.** Rename-safe (id ≠ name), reorder-safe (lookups by id, never list index), move-safe (id ≠ path/GUID). Duplicating an asset copies the id: an `AssetPostprocessor` in the Editor asmdef maintains a project-wide id→GUID index; on import, if two assets share a `stableId`, the **newer** asset (by creation, i.e. the one just imported that isn't the indexed owner) gets a fresh id and a console warning naming both assets. This makes duplicate-then-edit workflows safe by default.

**Remapping.** `Editor/Migration/StableIdRemapUtility` supports (a) "Adopt id from other asset" (delete-and-recreate recovery: point the new asset at the old id, with collision check) and (b) regenerate-with-log. There is no runtime remap table — ids are the canonical key end-to-end.

**Amendment A14 (C2 gate, 2026-07-29 — product-owner approved): who persists a minted id.** §3.4 says identity-bearing assets self-assign a stable id when theirs is 0, but never says who *writes that back to disk*. Minting inside a getter or `OnValidate` without marking the asset dirty means the id lives only in memory: the asset silently receives a **different** id every session, which defeats the entire point of stable ids and would corrupt any baked reference. Normative: whenever an id is minted for an asset whose stored id is 0, the minting path must persist it — `EditorUtility.SetDirty` plus save in editor contexts. Because that is an Editor-only API and §1.3 keeps Authoring player-safe, the Authoring-side minting path must expose the "an id was minted" fact so the Editor layer can persist it; a runtime-only context must never mint silently. A fixture must prove that an asset whose id starts at 0 reports the mint, and that two independent loads of the same persisted asset observe the same id.

**Collision policy.** 64-bit random ids make collisions negligible (< 10⁻⁹ at 100k clips); bake still validates uniqueness within a `ClipSetAsset` and **fails the bake** on violation (rule V05 — target-id uniqueness within a rig, §3.5). The failure names the offending *assets* — `ValidationMessage.assetContext` carries the `UnityEngine.Object` and the message its name — but **not their asset paths**: resolving a path needs `AssetDatabase`, and §1.3 keeps the Authoring assembly free of editor-only APIs so it stays valid in player builds. The editor layer, which holds the context object, is what turns it into a path for the CI-readable text described in §7.6. Duplicating an asset copies its id by design; separating the copy is the editor's import-time collision postprocessor, not the bake's job.

**Ergonomics.** Games reference clips as constants: the Editor menu action "Generate Clip Id Constants" writes a `public static class` of `public static readonly ClipId` fields (identifier-sanitized clip names) for a selected `ClipSetAsset` into a user-chosen file. Regeneration is idempotent; ids in the file are stable because the underlying ids are.

**Runtime cost.** Commands carry `ClipId`; resolution to a dense blob index happens **once per Play/Queue command** via binary search over the blob's sorted id array (§4.3); per-frame code uses the cached dense index. This preserves the audit's O(1) steady-state praise (audit §7 "enum-indexed blob… O(1) clip fetch") while removing ordinal identity — a **partial overrule** of the audit's Absorb of the pre-filled-slot pattern: placeholder slots per enum value are replaced by resolve-time failure handling (§5.4), because a package cannot enumerate a closed clip universe.

### 3.5 Validation rules (authoritative list; shared by inspectors, clip editor, and bake)

`ClipValidation.Validate(...)` (Authoring asmdef, pure static, no UnityEditor) returns `ValidationMessage { Severity, code, assetContext, text }` lists. Rules:

| Code | Severity | Rule |
|---|---|---|
| V01 | Error | `ClipAsset.duration < 0.001f` |
| V02 | Error | Track `targetId` not present in `clip.rig` |
| V03 | Error | Keys not strictly time-sorted (editor auto-sorts on edit; hand-edited assets fail here, mirroring audit §3.2's unvalidated-hand-edit gap) |
| V04 | Error | `normalizedTime` outside [0,1] on any key/marker |
| V05 | Error | Duplicate `ClipId` inside a `ClipSetAsset`; duplicate `TargetId` inside a `RigAsset` |
| V06 | Error | Clip in set whose `rig != set.rig` |
| V07 | Error | Set contains a VAT-sourced clip but `vatTextures == null`, or `vatTextures` lacks a `VatClipRange` for that `clipId` |
| V08 | Error | `VatTextureSetAsset.sourceHash` mismatch vs current sources (stale bake) — recompute on demand in editor; at entity-bake time V08 downgrades to Warning (textures still work, just outdated) — **see A12: unreachable until the Editor-side baker exists** |
| V09 | Error | `eventKey < 16` (0 = invalid, 1–15 reserved for built-ins, §5.5) |
| V10 | Warning | Empty clip (no tracks, no events) — legal (poses = rest), flagged |
| V11 | Warning | Clip listed twice in one set (deduped at bake) |
| V12 | Warning | Blend default exceeds clip duration (clamped at bake) |
| V13 | Error | Rig has 0 layers or > 8 layers |
| V14 | Warning | Sprite `sliceIndex < -1` |

**Amendment A36 (C4.9, 2026-08-06): V07's "is this clip VAT-sourced" test was wrong, and it broke every non-VAT project.** The rule asked `clip.vatSource == null`. `ClipAsset.vatSource` is a plain `[Serializable]` class field, **not** `[SerializeReference]`, and Unity cannot represent null for one: it writes a default block to the asset and materialises a non-null instance on load. Every `ClipAsset` that has ever been saved and re-read therefore carries a `VatClipSource` with a null `sourceClip`, reads as VAT-sourced, and raises V07 against any set with no texture set — which is every ordinary cutout project. V07 is an **Error**, so `ClipRegistryBuilder` throws `ClipValidationException`, **no registry is baked at all**, and every actor in the game holds its rest pose forever.

The fix is the semantic one, not a serialization change: a VAT source counts as present only when it **names a `sourceClip`**. An empty source gives `VatTextureBaker` nothing to sample, so it carries no VAT intent. Making the field `[SerializeReference]` was rejected — it changes the on-disk format of every existing clip to repair a predicate that was simply asking the wrong question.

**Why nothing caught it.** Every fixture in the package builds clips with `ScriptableObject.CreateInstance` and never writes one to disk, and in memory the field genuinely *is* null. The two existing V07 fixtures went further and asserted the broken reading directly, using a bare `new VatClipSource()` — the exact shape deserialization produces for a clip that never opted in — as their "VAT-sourced" clip. Both now build a source that names an `AnimationClip`, and `V07_DoesNotFireForAnEmptyVatSource_WhichIsWhatDeserializationProduces` pins the case that was broken.

**This is the same shape as A35, and the second instance in two days:** a rule exercised only under a precondition production never presents. There the precondition was an unbound `RigPartRef` buffer; here it is an in-memory asset. Both were found the moment something built *real* assets — the C4.9 smoke scene — rather than fixtures. **The generalisation worth acting on: a package whose entire suite constructs its inputs in memory has no coverage of the serializer, and the serializer is part of the authoring contract.** §11.1 should grow a small disk-round-trip tier; recorded here as owed rather than built, since it is M1/M6 scope rather than C4's.

**Amendment A12 (C2 gate, 2026-07-29 — product-owner approved): V08's real scope.** V08 is **unreachable at bake time**, and the row above overstates it. Detecting a stale `sourceHash` means *recomputing* it from the current sources, which requires `VatTextureBaker` — Editor assembly, §4.1/M2. §1.3 forbids the Authoring assembly (where `ClipRegistryBuilder` runs) from referencing it. So during an M1 bake V08 is **silent**, not "downgraded to Warning": nothing can evaluate it. It fires only where a caller has already recomputed the hash and passes that fact in, which today means editor tooling. Until the Editor-side baker ships, treat V08 as an editor-only rule; a bake cannot protect against stale VAT textures. Any module that starts relying on V08 at bake must first move the recomputation somewhere Authoring can legally reach.

---

<a name="s4"></a>
## 4. Bake pipeline

### 4.1 Inventory

| Piece | Kind | Asmdef | Responsibility |
|---|---|---|---|
| `ActorAuthoring` + `ActorBaker` | MonoBehaviour + `Baker<ActorAuthoring>` | Authoring | Root entity: builds the `ClipRegistryBlob` from the referenced `ClipSetAsset` (via `ClipRegistryBuilder`), registers it with `AddBlobAssetWithCustomHash`, adds all root components/buffers (§5.2), seeds starting layer state. `DependsOn` the set, rig, every clip, and the VAT texture set so edits retrigger baking. |
| `RigTargetAuthoring` + `RigTargetBaker` | MonoBehaviour + Baker | Authoring | Part entity: `RigPartBinding` (bake-time `targetId`; dense index resolved by the baking system), `TargetRestPose` captured from the authoring transform, technique material-property components per `TargetKind`, `TransformUsageFlags.Dynamic | NonUniformScale` for quads (scale/flip support — fixing audit §2.1's dead-scale regression), identity `PostTransformMatrix`. |
| `RigBindingBakingSystem` | `ISystem`, `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]`, `[UpdateInGroup(typeof(PostBakingSystemGroup))]`, `[BurstCompile]` | Authoring | Cross-entity pass: walks each actor's baked children, resolves each part's `targetId` → dense target index (order defined in §4.5), fills the root's `RigPartRef` buffer and each part's `RigPartBinding.targetIndex` + `actorRoot`. Errors are reported via `Debug.LogError` and the part is skipped — **which errors belong here rather than in a managed Baker is settled by amendment A22 below, which supersedes the "unknown targetId, duplicate binding" pairing this row originally named.** **Pure entity-data pass — Burst-compatible throughout; it touches no managed objects.** All managed validation (including the material↔texture-set check) lives in the managed Bakers (§4.4). |
| `VatTextureBaker` | static editor class | Editor | Texture/mesh generation from skinned mesh + clips (§4.7). *Not* part of entity baking — it produces the `VatTextureSetAsset` that entity baking then consumes. Owned by module M2 (§8) even though it lives in the Editor asmdef. |

**Amendment A19 (C3 gate, 2026-07-30 — product-owner approved): what "touches no managed objects" means for the binding pass.** The row above asks `RigBindingBakingSystem` to be Burst-compiled and free of managed objects *and* to report errors with entity and asset context. Taken literally those conflict: naming an asset needs a managed reference. The resolution: the job holds no managed reference and allocates no managed object; it reports through Burst-supported logging over blittable values only — target id, and the authoring object's **hierarchy path carried as a `FixedString128Bytes`** (see A21) — and every message that needs a real asset reference is emitted by the managed Bakers, which have one and can pass it as the log context. A reader of a binding error is therefore given the object's path and the target id, which is the field they must fix, rather than an object reference the job cannot legally hold.

**Amendment A21 (C3 re-review, 2026-07-31 — product-owner approved): a Bursted baking system names objects by path, not by hash or entity index.** A19 as first written let the binding pass identify a part by its `AuthoringPathHash` and its entity index/version. Both are unactionable in a message: `authoring path hash 2463534242` and `entity 41:1` tell a user that something is wrong and nothing about where to look, and the entity index is not even stable between bakes. `RigPartBakeLink` therefore carries `authoringPath`, a `FixedString128Bytes` rendering of the hierarchy path (`Rig/Torso/LeftArm`), truncated from the left so the leaf survives — a blittable value, so the job stays Burst-pure and managed-reference-free, while every one of its diagnostics names the offending object. (A21 was written when the binding pass had four; A22 below deletes two as unreachable by construction and moves a third to `RigTargetBaker`, leaving three. The rule is per-diagnostic, so the count is incidental — but stating it wrongly is how three successive gates were failed, so it is stated once, here, and not repeated.) `AuthoringPathHash.Of` remains for the one thing that genuinely needs a *number*: `SampleSettings.phase01` (A18). The path is diagnostic text and nothing may key off it; identity is what §3.4's stable ids are for.

**Amendment A22 (C3 re-review, 2026-07-31 — product-owner decision, see note below): which pass owns the unknown-target-id error.** The `RigBindingBakingSystem` row above gives the binding pass both the unknown-`targetId` error and the duplicate-binding error. The rework that closed B3 moved the first of those into `RigTargetBaker` without recording it, leaving the row, three doc comments and the code all stating different things. This amendment blesses the move and states the split normatively:

| Failure | Reported by | Why there |
|---|---|---|
| Part references a target id its rig does not declare | **`RigTargetBaker`** (managed) | It can name the GameObject, the rig and the id, and pass the part itself as the log context so clicking the message selects it. The Bursted pass can name only blittable values. The part is then left **without** `RigPartBakeLink`, so the binding pass never sees it and the same mistake is never reported twice in two vocabularies. |
| Actor's own bake reported a failure and stopped | **`ActorBaker`** (managed), once per actor | It holds the asset that failed and the rule that failed. It additionally writes the `[BakingType] ActorBakeFailed` tag on the actor entity. |
| Actor has no usable `ClipRegistry` **and carries no `ActorBakeFailed` tag** | **`RigBindingBakingSystem`** | Nothing has explained the failure, and every part under that actor is about to stop animating. |
| Two parts of one actor claim the same target | **`RigBindingBakingSystem`** | Only a cross-entity pass can see it. |
| Rig declares an id the actor's baked registry does not carry | **`RigBindingBakingSystem`** | A different check from the first row: the baker validates against the `RigAsset`, this validates against the **baked registry**. They are the same set by construction, so this is the guard on that construction holding — not a content error. |

**The `ActorBakeFailed` tag is the substance of this amendment, not bookkeeping.** Before it, the binding pass simply stayed silent whenever an actor carried no registry, which was correct only because each of `ActorBaker`'s bail-outs happened to log first — a coupling nothing asserted, commented or tested. A fourth bail-out added without a log, or any baking system that stripped `ClipRegistry`, would have left every part under that actor unbound with **zero** diagnostic output anywhere in the project. The tag makes silence a claim someone made rather than a gap, and rows 3 and 5 above are the branches that survive because of it. Rows 1 and 2 of the pre-amendment binding pass — "has no baked actor to bind to" and "clip registry failed to build" — were unreachable by construction and have been **deleted**; unreachable user-facing error strings are shipped dead code.

**How this was decided.** The C3 handoff put it to the product owner as two coherent options — (a) bless the managed-baker location, (b) revert to spec and report from the Bursted pass — with a recommendation of (a). It was drafted while the owner was away and **ratified by them on 2026-07-31**, on the grounds that decide the case: only managed code can name the offending GameObject, name the rig that fails to declare the id, tell the user which field to fix, and pass the object as a log context so clicking the message selects it. A Bursted pass can print a path string and a number. For a diagnostic whose entire purpose is to send someone to the right object, that difference is the feature.

**Decision — blob built in the Baker, not a baking system** (explicit deviation from the host's SO→Blob-in-`PostBakingSystemGroup` house pattern, which is a host convention, not an audit verdict): the registry blob is a pure function of ScriptableObject data with no cross-entity input, and `Baker.DependsOn` + `AddBlobAssetWithCustomHash` give incremental-rebake correctness and store-level dedup for free — exactly the two things the host's hand-rolled baking system gets wrong per the audit (§3.2: no dedup, multi-holder double-dispose). Cross-entity work (part index resolution) stays in `PostBakingSystemGroup` where it belongs. The blob's lifetime is owned by the BlobAssetStore — **no manual dispose anywhere**, which structurally eliminates the audit's double-dispose latent bug.

### 4.2 Blob structs (exact sketches)

```csharp
public struct ClipRegistryBlob
{
    public int schemaVersion;                     // bump on any layout change; bake stamps it
    public ulong setKey;                          // ClipSetAsset.stableId
    public ulong vatSetKey;                       // VatTextureSetAsset.setKey or 0
    public byte layerCount;
    public BlobArray<ulong> sortedClipIds;        // ascending; binary-search key
    public BlobArray<ClipBlob> clips;             // SAME ascending-clipId order as sortedClipIds
    public BlobArray<uint> sortedTargetIds;       // ascending; targetId → dense index (position)
    public BlobArray<float3> targetBoundsExtents; // per dense target index, from RigTargetDefinition
    public VatTextureInfoBlob vatInfo;            // addressing params mirrored from the texture set
}

public struct ClipBlob
{
    public ulong clipId;
    public FixedString64Bytes debugName;
    public float duration;                        // seconds, ≥ 0.001
    public LoopMode defaultLoop;
    public float defaultBlendIn;                  // clamped ≤ duration
    public float defaultBlendOut;
    public BlobArray<TransformTrackBlob> transformTracks;   // sorted by dense target index
    public BlobArray<SpriteTrackBlob> spriteTracks;         // sorted by dense target index
    public BlobArray<EventMarkerBlob> events;               // sorted by normalizedTime, stable
    public int vatFrameStart;                     // -1 when clip has no VAT range
    public int vatFrameCount;
    public float vatFps;
    public AABB offsetBounds;                     // conservative bounds in OFFSET space, not actor space (§4.6)
}

public struct TransformTrackBlob
{
    public int targetIndex;                       // dense
    public TrackBlendOp blendOp;
    public AnimatedChannels channels;
    public BlobArray<TransformKeyBlob> keys;      // sorted by time; interpolation resolved per key at bake
}

public struct TransformKeyBlob
{
    public float normalizedTime;
    public float3 position;
    public float rotationZ;                       // radians (converted at bake; authoring is degrees)
    public float2 scale;
    public Interpolation interpolation;
}

public struct SpriteTrackBlob
{
    public int targetIndex;
    public SpriteFrameMode mode;
    public BlobArray<SpriteKeyBlob> keys;         // sorted by time
}

public struct SpriteKeyBlob { public float normalizedTime; public int sliceIndex; public float4 atlasRect; }

public struct EventMarkerBlob { public float normalizedTime; public uint eventKey; public int intParam; public float floatParam; }

public struct VatTextureInfoBlob
{
    public VatFlavor flavor;
    public int textureWidth;
    public int rowsPerFrame;                      // 1 = bone flavor
    public int boneOrVertexCount;
}
```

**Textures never live in blobs** (hard constraint — honored): the blob stores only `vatSetKey` + addressing metadata. Texture objects reach the GPU via the material (§4.4).

**Amendment A11 (C2 gate, 2026-07-29 — product-owner approved).** `clipIndexById` is **deleted**. It existed to map a sorted-id position onto a dense clip index, but §4.5.1's canonical ordering sorts `clips` by ascending `clipId`, so the two orders are identical and the array was the identity map in every blob the package can emit. `clips` and `sortedClipIds` are now normatively parallel: **a clip's dense index is its position in both**, and `TryResolveClip` returns the binary-search position directly. This resolves a direct contradiction between §4.2 (which previously implied an independent authoring order) and §4.5.1 (which mandates the id sort). Consequence to respect in later modules: appending a clip whose id sorts before an existing one **renumbers dense indices**, so any cached `clipIndex` is valid only against the blob it was resolved from — which is already the §4.3 contract.

**Amendment A13 (C2 gate, 2026-07-29 — product-owner approved).** `localBounds` is renamed **`offsetBounds`** to name the space it is actually computed in. See §4.6.

### 4.3 Lookup contract

`ClipRegistryUtil.TryResolveClip(ref ClipRegistryBlob registry, ClipId clipId, out int clipIndex)` — binary search over `sortedClipIds`, `O(log n)`, Burst-compatible, called only when commands are applied (§5.4). `ResolveTargetIndex(ref registry, TargetId)` identical shape, used only at bind/bake time. Per-frame paths always use cached dense indices.

### 4.4 Texture-set key scheme (blob metadata ↔ textures)

Link key = `VatTextureSetAsset.setKey` (ulong, §3.4 identity rules), stored in `ClipRegistryBlob.vatSetKey` and in a per-actor component:

```csharp
public struct VatTextureBinding : IComponentData
{
    public ulong setKey;
    public UnityObjectRef<Texture2D> boneOrPositionTexture;   // resolved from the SO at bake
    public UnityObjectRef<Texture2D> normalTexture;           // Entity.Null-equivalent when absent
}
```

Primary GPU binding is **material-level**: the actor's VAT material references the textures directly (shared material → shared batch). `VatTextureBinding` exists for (a) bake-time validation — performed in **`RigTargetBaker`** (Bakers are managed code, so Material access is legal there; the Bursted `RigBindingBakingSystem` never touches managed objects, §4.1): for `TargetKind.VatMesh` parts it reads the part Renderer's shared material (or the authoring `expectedMaterial` override when set) and the actor's `VatTextureSetAsset` via `GetComponentInParent<ActorAuthoring>()` (dependency-tracked by the Baker), and logs a warning when the material's `_VatBoneTex`/`_VatPosTex` slot differs from the set's textures — and (b) advanced hosts that build materials at runtime (sample shown in `CompositeActor`). Rationale: per-instance texture properties would break BRG batching; keys + shared materials keep one crowd = one batch.

### 4.5 Determinism strategy

A given set of source assets must produce a byte-identical blob on every bake, on every machine:

1. **Canonical ordering.** Clips sorted by `clipId` ascending (list order irrelevant — duplicates deduped, V11). Targets sorted by `targetId` ascending; dense target index = position in that sorted order. Tracks sorted by dense target index (ties: transform before sprite; two tracks on the same target+kind sorted by authoring list order and **both kept** — see §5.6 multi-track rule, fixing the audit §3.4 editor/runtime divergence). Keys sorted by `normalizedTime` with stable original-order tie-break. Events likewise.
2. **Canonical values.** Degrees→radians at bake; per-key interpolation stored resolved (audit's bake-resolved interpolation absorbed); blend defaults clamped; floats written as-is (no re-quantization — same input bits, same output bits).
3. **Content hash.** `Unity.Collections.xxHash3` (the collections package's 64-bit xxHash3 — no managed hashing) over a canonical stream covering **every field of the finished blob** (with one documented exception, `sortedClipIds`, explained in (3d) below). Floats are hashed **by bit pattern** via `math.asuint` — same input bits, same hash, no re-quantization. The `Unity.Entities.Hash128` passed to `AddBlobAssetWithCustomHash` is `new Hash128(lo32(contentHash), hi32(contentHash), (uint)schemaVersion, lo32(setKey) ^ hi32(setKey))` — **this is the BlobAssetStore dedup key**. Two actors sharing a `ClipSetAsset` share one blob; re-bakes with unchanged content are no-ops. Because the folded `setKey` is part of the dedup key *and* `setKey` is in the hashed stream, dedup is scoped to a single set: two different sets never collapse onto one blob even with identical content.

   **Amendment A5 (C2 gate, 2026-07-29 — product-owner approved): mechanism.** The stream is accumulated with **`xxHash3.StreamingState`**, not an `UnsafeAppendBuffer`. The buffer route is not implementable where it is needed: hashing a buffer's *contents* requires the `Hash64(void*, long)` overload, whose pointer parameter demands `allowUnsafeCode`, and §1.3 grants that only to the Runtime assembly while the builder lives in Authoring (`PackagingConformanceTests` asserts `allowUnsafeCode: false` there). The `Hash64<T>(in T)` overload would hash the buffer *struct's own bytes* — a pointer plus lengths — destroying determinism outright. The two mechanisms are **byte-for-byte equivalent**: `StreamingState.Update<T>(in T)` writes exactly `sizeof(T)` tightly-packed bytes, identical to what `UnsafeAppendBuffer.Add<T>` would have written, with the same xxHash3-64, the same seed, and the same secret; streaming-equals-one-shot is xxHash3's defining contract. The little-endian assumption is the one this section already made. **This is a documentation correction, not a hash-format change.**

   **Amendment A10 (C2 gate, 2026-07-29 — product-owner approved): coverage.** The previously normative append order omitted `sortedTargetIds`, `targetBoundsExtents`, every `vatInfo` field, and `ClipBlob.debugName` — all of which are *in the blob the hash keys*. Since the hash is the dedup key, a change confined to those fields returned a **stale blob**: rebaking VAT textures to a new `textureWidth` with unchanged frame ranges is the concrete case. The stream is now, in order:

   **(3a) Registry header** — `schemaVersion` (int32), `setKey` (uint64), `vatSetKey` (uint64), `layerCount` (byte).
   **(3b) Target block** — `sortedTargetIds.Length` (int32), then **per dense target, interleaved**: its id (uint32) followed by the three `asuint` components of its `targetBoundsExtents`. One count covers both arrays; they are parallel and always the same length, so a second count would be redundant.
   **(3c) VAT info** — `flavor` (byte), `textureWidth` (int32), `rowsPerFrame` (int32), `boneOrVertexCount` (int32).
   **(3d) Clip count** (int32), then **per clip in ascending-`clipId` order**: `clipId` (uint64); `debugName` length (int32) then its bytes; `asuint(duration)`; `defaultLoop` (byte); `asuint(defaultBlendIn)`; `asuint(defaultBlendOut)`; **transform tracks** (count int32, then per track `targetIndex` int32, `blendOp` byte, `channels` byte, key count int32, then per key the `asuint` bits of `normalizedTime`, `position.xyz`, `rotationZ`, `scale.xy` plus `interpolation` byte); **sprite tracks** (count int32, then per track `targetIndex` int32, `mode` byte, key count int32, then per key `asuint(normalizedTime)`, `sliceIndex` int32, and the four `asuint` lanes of `atlasRect`); **events** (count int32, then per marker `asuint(normalizedTime)`, `eventKey` uint32, `intParam` int32, `asuint(floatParam)`); and finally `vatFrameStart` (int32), `vatFrameCount` (int32), `asuint(vatFps)`, and `offsetBounds` Center then Extents as `asuint` components.

**The per-clip VAT and bounds fields come after the track and event blocks, not before them** — that is the shipped order and it is normative. **Every array is preceded by its element count**, so a boundary shift cannot alias. `sortedClipIds` is not hashed separately: it is byte-for-byte the `clipId` of each entry of `clips` in the same order, and its length is the already-hashed clip count, so hashing it again would add no discriminating power. Any change to this order or coverage is a format change and **must** bump `schemaVersion` (now **2**). A conformance fixture pins a known set's hash to a literal constant so a Collections-side change cannot silently re-validate itself while invalidating every baked subscene.
**Amendment A18 (C3 gate, 2026-07-30 — product-owner approved): baked per-object numbers must be stable.** No baked value may derive from `Object.GetInstanceID` or `Object.GetEntityId`. Both are session-local — reassigned on every project load — so baking either makes the same prefab produce different bytes each session, which costs the reproducible bakes this section exists to guarantee. Where a bake needs a per-object number it uses `AuthoringPathHash`: FNV-1a over the authoring hierarchy path, folding in each node's sibling index so identically named siblings differ, written out longhand so a dependency update cannot silently move baked values.

`SampleSettings.phase01` is derived this way, and the derivation is normative: **`phase01 = (AuthoringPathHash.Of(actorTransform) >> 8) × 2⁻²⁴`**. FNV-1a ends on a multiply, and a multiply propagates carries only upward, so the low bits of the finished hash are the least mixed whichever input contributed them; bits 8–31 are exactly the 24 the phase needs, so discarding the bottom byte costs no range. **The shift has no observable effect in this walk order** — see A-4 below; it is a defensible micro-improvement, not a correctness fix, and A21's earlier description of it as "the load-bearing part" overstated it. No mask is applied — `>> 8` on a `uint` already yields at most `0x00FFFFFF`, so one would be a no-op implying a constraint the expression is not enforcing. The result lands in `[0, 1)`, which §5.6 requires.

The path walk **takes bake dependencies on what it reads**: each node's name through `IBaker.GetName` and the ancestor chain through `IBaker.GetParents`. Reading `Transform.name` directly would produce the same number without registering anything, so an ancestor rename would leave an incremental bake and a clean bake of the same scene disagreeing — the exact reproducibility this amendment exists to protect. Entities exposes no sibling-index dependency, so reordering siblings without renaming or reparenting is the one edit that does not retrigger the bake; that is accepted, and bounded, because the only baked value affected is the sampling phase.

The remaining consequence is accepted knowingly: renaming or reparenting an actor changes its sampling phase, which is harmless because the phase only spreads sampling load across frames and carries no visual or identity meaning — identity is what §3.4's stable ids are for.

**Open item A-4, ruled and closed (C3 Gate 4, 2026-08-01): the `>> 8` shift stays, and no test covers it because none can.** `AuthoringPathHash.Of` walks **leaf first**, so whichever character distinguishes two actors is hashed first and then passes through every remaining node — its own sibling index, the separator, and all of its ancestors — reaching the final multiply thoroughly mixed under either derivation. The low-bit weakness the shift avoids can only surface when the differing input is mixed **last**, i.e. on the outermost ancestor, which this walk order puts furthest from the leaf that varies.

A fixture (`TwoActorsWhoseNamesDifferOnlyInTheLastCharacter_GetWellSeparatedPhases`) was written to pin this at the third C3 gate and **deleted** at the same gate: evaluating both derivations over 200 container positions showed the pre-A18 masked derivation passing at every one, so reverting A18 entirely left the test green. It discriminated nothing while claiming to pin A18. **Do not rewrite it** — a discriminating fixture is not constructible for the reason above; write one only if the walk order changes.

The shift is retained rather than reverted because it is never worse, and because `AuthoringPathHash` is a general-purpose helper: a future caller that mixes its discriminator last would need the better-mixed bits, and that caller would have no reason to suspect the low byte. A-4 is therefore closed as *untestable by construction*, not as *unresolved*.

4. **Determinism test** (§11): build the blob twice from the same fixture SOs in one editor session and from a shuffled clip/track list; assert identical content hash and identical `sortedClipIds`/`clips` streams.

### 4.6 Bounds at bake

Per clip: `offsetBounds` = union over transform tracks of, per key, `position.xy` offset ⊕ target's `boundsExtents` scaled by `max(|scale.x|, |scale.y|, 1)`, **plus, for every rig target the clip does not key, that target's `boundsExtents` box centred at the origin** (an unkeyed target still renders, and in offset space it sits at its rest pose, i.e. offset zero); VAT clips union their `VatClipRange.bounds`. Conservative by construction (keys bound the extremes of all five interpolation modes because every easing here is monotonic between keys). This answers audit open question 13: **yes, bake-time conservative bounds**.

**Amendment A13 (C2 gate, 2026-07-29 — product-owner approved): the bounds frame.** The previous wording said "actor-space bounds … plus the rest-pose bounds of untracked targets", which the M1 bake **cannot** produce. Transform keys hold *local offsets* from a target's rest pose (§3.2), and rest poses live on the prefab, read by `RigTargetBaker` (§4.1) in the Editor/entity-baking step — the Authoring assembly that runs `ClipRegistryBuilder` cannot see them. The union M1 computes is therefore in **offset space**, centred on the origin. Left undocumented, a cutout character whose parts sit away from the origin would get a box smaller than its true silhouette and cull visibly — reintroducing exactly the gap §5.8 claims to close.

Resolution: the blob's `offsetBounds` is normatively **offset space**, and the entity-baking step (§4.1 / M2), which does see rest poses, supplies the other half — the actor-space rest frame. No runtime system may treat `offsetBounds` as actor space.

**Amendment A24 (C3 re-review, 2026-07-31 — product-owner approved): where the two halves are combined.** A13's closing sentence said the entity-baking step produces actor-space bounds "by combining `offsetBounds` with the rest-pose positions of the targets each clip touches", carried by `ActorRestBounds`. That is not what §5.8 asks for and not what M2 can do: `offsetBounds` is **per clip**, `ActorRestBounds` is **per actor**, and an actor's clip set may hold hundreds of clips — folding them together at bake would collapse every clip onto one worst-case box and throw away the per-clip tightness §5.8 exists to exploit. The shipped split, which is normative from here:

- **`ClipBlob.offsetBounds`** (M1, this section) — per clip, offset space, centred on the origin.
- **`ActorRestBounds`** (M2, §5.2) — per actor, actor space: the union over every *bound part* of that part's rest position relative to the actor root, inflated by its target's `boundsExtents` scaled by `max(|scale.x|, |scale.y|, 1)`. It is a function of the prefab's rest poses and of `targetBoundsExtents`, and it never reads `offsetBounds`. A part whose target id does not resolve contributes nothing; an actor with no bound parts gets a zero-extent box rather than an inverted one.
- **`RenderBoundsUpdateSystem`** (M3, §5.8) — combines them **at runtime**, per clip change, as the active clips' `offsetBounds` union translated by `ActorRestBounds`. This is the step that keeps the box tight per clip, and it is why the combination cannot be baked.

### 4.7 VAT texture baking design (`VatTextureBaker`)

**Inputs:** a source prefab with `SkinnedMeshRenderer` (bone flavor) or any `Mesh`+clips (vertex flavor), the list of `ClipAsset`s (their `VatClipSource`s), a `VatBakeSettings { flavor, sampleFps default 30, precision, maxInfluences (1/2/4, default 4), bakeNormals }`.

**Resampling policy:** each clip sampled at `sampleFps` (per-clip override wins over settings): `frameCount = round(duration × fps) + 1` — the last frame samples `t = duration` exactly. Clips marked `loopSafe` append **one more frame duplicating frame 0**, so the shader's `floor/floor+1` row lerp never reads across a clip boundary at the loop seam (§6.4). Sampling uses `clip.SampleAnimation(instance, t)` on a hidden temp instance inside `AnimationMode`-guarded scope; the temp instance is destroyed in a `finally`.

**Bone flavor layout (priority flavor):**
- One `Texture2D`, linear (sRGB off), no mips, point filter on X / bilinear conceptually on Y is done manually in-shader (§6.4), wrap = Clamp.
- Texel `(b*3 + c, frameRow)` holds column `c` (0..2) of bone `b`'s object-space 3×4 skinning matrix rows (`float4` per texel: matrix row values; translation in the 4th components across the three texels). `width = boneCount × 3`; hard error if width > 2048 (682-bone ceiling — ample). `rowsPerFrame = 1`; global frame `f` occupies row `f`. Height = Σ frameCount over clips; error if > 8192 (platform-safe texture ceiling), with guidance to split sets.
- Matrices are `worldToLocal-free`: `boneMatrix = bindPoseInverse × boneObjectSpaceTransform` — object-space in, object-space out; entity `LocalToWorld` does the rest. Object-space storage is the half-precision mitigation (§12 R2).
- **Mesh conversion:** the baker writes a static `Mesh` asset: positions/normals/tangents/UV0 copied from the skinned mesh; `UV1 (TEXCOORD1) = float4 bone indices`, `UV2 (TEXCOORD2) = float4 bone weights` (top-`maxInfluences` weights renormalized, rest zeroed) — the product-scope-mandated UV-channel encoding.

**Vertex flavor layout:**
- Position texture: `width = min(nextPow2(vertexCount), 2048)`, `rowsPerFrame = ceil(vertexCount / width)`; vertex `v` of frame `f` at `(v % width, f × rowsPerFrame + v / width)`. Absolute object-space positions (not offsets) — simpler shader, same precision class. Optional normal texture, same layout.
- `UV1.x = v` (vertex's own index) written into the runtime mesh so the shader can address its texel without SV_VertexID plumbing.

**Precision/format choices per platform:**

| Data | Default format | Rationale / platform notes |
|---|---|---|
| Bone matrix texture | `RGBAHalf` (R16G16B16A16_SFloat) | Universally supported incl. **Switch**; object-space translations of character-scale rigs stay ≪ half's 0.0005–0.001 precision band within ±2 (§12 R2). `RGBAFloat` opt-in per set for large creatures (memory ×2; discouraged on Switch in `platform-notes.md`, not blocked). |
| Vertex position texture | `RGBAHalf`; `RGBAFloat` opt-in | Same. |
| Vertex normal texture | `RGBAHalf` default; `RGBA8` octahedral-encoded opt-in ("compact normals") | RGBA8-octahedral halves memory for Switch crowds; decode is 6 ALU ops in `ToolkitVat.hlsl`. |
| Block compression (BC/ASTC) | **Never** for VAT data | Block artifacts corrupt matrices/positions; enforced by the baker creating textures uncompressed and marking them `TextureImporterCompression.Uncompressed` (assets are generated with correct importer settings programmatically — the user never touches them). |

All generated textures: sRGB **off** (carrying forward the audit's verified linear-data rule), readable **off**, mips **off**.

**Outputs:** `VatTextureSetAsset` (+ textures + mesh as sub-assets of it, single self-contained artifact), `clipRanges` filled with per-clip `frameStart/frameCount/fps/bounds` (bounds measured from the sampled frames — exact, not estimated), `sourceHash` = `Unity.Collections.xxHash3.Hash64` over source mesh GUID+hash, clip GUIDs+lengths, and settings (same primitive as §4.5). Baking is deterministic given identical sources (same sampling times, same ordering: clips baked in `clipId` order).

**Core is headless:** `VatTextureBaker.Bake(VatBakeInput, out VatBakeResult)` has no GUI dependency — EditMode tests drive it against a procedurally-built skinned mesh (§11). `VatBakeWindow` (§7) is a thin UI over it.

---

<a name="s5"></a>
## 5. Runtime architecture

### 5.1 System groups & host insertion

```csharp
namespace StitchPunk.AnimationToolkit
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AnimationToolkitSystemGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(AnimationToolkitSystemGroup), OrderFirst = true)]
    public partial class AnimationToolkitBindingSystemGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(AnimationToolkitSystemGroup))]
    [UpdateAfter(typeof(AnimationToolkitBindingSystemGroup))]
    [UpdateBefore(typeof(AnimationToolkitPresentationSystemGroup))]
    public partial class AnimationToolkitLogicSystemGroup : ComponentSystemGroup { }

    [UpdateInGroup(typeof(AnimationToolkitSystemGroup), OrderLast = true)]
    public partial class AnimationToolkitPresentationSystemGroup : ComponentSystemGroup { }
}
```

- **No scene gating, no host tags.** The group runs whenever its queries match; an empty world costs nothing. Hosts that gate features (like Stitch Punk's `GameSceneSystemGroup`) disable the group wholesale via the provided helper `ToolkitWorldControl.SetEnabled(World world, bool enabled)` (sets `AnimationToolkitSystemGroup.Enabled`) — this replaces the audit §6.1 `GameSceneTag` coupling.
- **Insertion into a host pipeline:** the package declares only `[UpdateInGroup(typeof(SimulationSystemGroup))]` — no Before/After edges, so it cannot conflict with host ordering. The host orders **its own** groups relative to the package type (e.g. Stitch Punk adds `[UpdateBefore(typeof(AnimationToolkitSystemGroup))]` to its `DesignSystemGroup`) — attributes live on host types referencing the package type, requiring zero package changes. Documented with the Stitch Punk-shaped example in `Documentation~/integration.md`.

**Execution order diagram** (arrows = `UpdateBefore/After` edges inside the groups):

```
AnimationToolkitSystemGroup
├─ AnimationToolkitBindingSystemGroup            (OrderFirst)
│    └─ RigBindingSystem                          — spawn-time part re-binding
├─ AnimationToolkitLogicSystemGroup               (ungated by visibility — timers run off-screen)
│    ├─ CommandApplySystem                        — AnimationCommand → PlaybackLayer
│    ├─ PlaybackTimeSystem        (after CommandApply)   — time/loop/pingpong/blend/finish/queue
│    └─ EventEmissionSystem       (after PlaybackTime)   — clears + emits AnimEventOutput
└─ AnimationToolkitPresentationSystemGroup        (gated on AnimVisible where marked ◆)
     ├─ AnimLodDistanceSystem                     — optional, default-disabled
     ├─ TransformSampleSystem     ◆ (after AnimLodDistance) — layers → TargetPose per part
     ├─ TransformApplySystem      ◆ (after TransformSample) — TargetPose → LocalTransform + PostTransformMatrix
     ├─ SpriteMaterialSystem      ◆ (after TransformSample) — sprite frames → _ImageIndex / _AtlasFrame
     ├─ VatMaterialSystem         ◆ (after PlaybackTime, i.e. any presentation slot) — layers → _VatFrameA/B/_VatBlend
     └─ RenderBoundsUpdateSystem    (after TransformSample; gated on the BoundsDirty enableable, §5.8 —
                                     clip changes only, never per frame)
```

The ungated-logic / gated-presentation split absorbs the audit's praised design (audit §7 "visibility-gated presentation with ungated timers") verbatim.

### 5.2 Runtime components (complete inventory)

**Actor root** (added by `ActorBaker`):

```csharp
public struct ClipRegistry : IComponentData
{
    public BlobAssetReference<ClipRegistryBlob> Value;        // BlobAssetStore-owned; never manually disposed
}

[InternalBufferCapacity(8)]
public struct PlaybackLayer : IBufferElementData              // one element per rig layer, index = layer index
{
    public ClipId clip;             public int clipIndex;     // -1 = none/unresolved
    public float time;              public float speed;       // seconds; speed may be negative (reverse)
    public float advanceStartTime;  // time at the start of this frame's advance — the event window's
                                    // opening edge, written by PlaybackTimeSystem (§5.5, A27)
    public LoopMode loop;
    public ClipId previousClip;     public int previousClipIndex;   // blend source
    public float previousTime;      public float previousSpeed;
    public LoopMode previousLoop;   // the mode the outgoing clip was actually playing under
    public float blendElapsed;      public float blendDuration;     // 0 = not blending
    public ClipId queuedClip;       public float queuedSpeed;
    public LoopMode queuedLoop;     public float queuedBlend;
    public PlaybackFlags flags;     // Active | Blending | HasQueued | Finished | FinishedThisFrame
}

[InternalBufferCapacity(4)]
public struct AnimationCommand : IBufferElementData
{
    public CommandKind kind;        // Play, Queue, Stop, SetSpeed, SetTime
    public byte layerIndex;
    public ClipId clip;
    public float speed;             public LoopMode loop;
    public float blendDuration;     // Play/Queue: crossfade in; Stop: fade-out; NaN = clip default
    public float time;              // SetTime
}
public struct AnimationCommandPending : IComponentData, IEnableableComponent { }   // baked disabled

[InternalBufferCapacity(4)]
public struct AnimEventOutput : IBufferElementData
{
    public uint eventKey;           // user keys ≥ 16 (V09); 0 invalid, 1–15 reserved (ClipFinished = 1, ClipResolveFailed = 2)
    public byte layerIndex;
    public ClipId clip;
    public int intParam;            public float floatParam;
}
public struct AnimEventsPending : IComponentData, IEnableableComponent { }         // baked disabled

[InternalBufferCapacity(16)]
public struct RigPartRef : IBufferElementData { public Entity part; public int targetIndex; }

public struct RigBindingUninitialized : IComponentData, IEnableableComponent { }   // baked ENABLED (§5.3)
public struct AnimVisible : IComponentData, IEnableableComponent { }               // baked ENABLED (§5.9)
public struct BoundsDirty : IComponentData, IEnableableComponent { }               // baked ENABLED; clip-change signal (§5.8)
public struct ActorRestBounds : IComponentData { public AABB value; }              // A13: rest-pose bounds in ACTOR space,
                                                                                   // written by the entity baker (§4.1/M2) which
                                                                                   // can see prefab rest poses; §5.8 unions it with
                                                                                   // each clip's offset-space offsetBounds (§4.6)
public struct SampleSettings : IComponentData { public float rateHz; public float phase01; }  // 0 rate = every frame
public struct AnimLod : IComponentData { public byte level; }                      // 0..3 (§5.10) — OPT-IN, see A23
public struct VatTextureBinding : IComponentData { /* §4.4 */ }
```

**Amendment A23 (C3 re-review, 2026-07-31 — product-owner approved): `AnimLod` is opt-in, and its absence is conformant.** The inventory above lists every root component unconditionally, which reads as "the archetype has all of these". `AnimLod` is the exception: `ActorBaker` adds it only when `ActorAuthoring.addDistanceLod` is set, and §5.10 already describes distance LOD as optional and default-disabled. Adding it unasked would enrol every actor in the `AnimLodDistanceSystem` query for a feature most consumers will not turn on, and would change the chunk layout of every crowd to carry a byte nothing writes. The exact-archetype assertion of §8 M2 therefore pins the other **thirteen** components as the baseline root archetype and `AnimLod`'s *absence* as conformant, with a second fixture pinning its presence when the authoring asks. Recorded because the C3 gate first noted this as a handoff note and the rework then hardened it into a test assertion — a spec/reality conflict codified rather than resolved, which is precisely what §9 calls stop-the-line.

**Why `previousLoop` exists.** During a crossfade the outgoing clip's time must map through the loop mode it was *actually* playing under, not through its authored default. `CommandApplySystem` overwrites `layer.loop` when the incoming Play command carries a mode (§5.4), so the outgoing clip's mode is destroyed unless it is preserved here. Without the field, a Loop-default clip that was played `Once` and is then crossfaded out past its duration wraps to t = 0 instead of holding at the end — a pop in exactly the transition the blend exists to smooth (§10 answer 2 rates popping transitions as disqualifying). `CommandApplySystem` copies the outgoing `loop` into `previousLoop` at the same moment it copies `clip`/`time`/`speed` into the `previous*` slots; `UseClipDefault` in this field still resolves to the outgoing clip's authored default.

**Part child** (added by `RigTargetBaker`):

```csharp
public struct RigPartBinding : IComponentData { public Entity actorRoot; public int targetIndex; }
public struct TargetRestPose : IComponentData { public float3 localPosition; public float rotationZ; public float2 scale; public int restSliceIndex; }
public struct TargetPose : IComponentData { public float3 localPosition; public float rotationZ; public float2 scale; public int sliceIndex; public float4 atlasRect; }
public struct VatDriven : IComponentData { public byte layerIndex; }   // VatMesh parts only: which layer drives the frames (§5.8)
// + AnimVisible (propagated), + technique material-property components (§6.2), + PostTransformMatrix (identity)
```

**World singletons:**

```csharp
public struct AnimationToolkitConfig : IComponentData          // auto-created with defaults by
{                                                              // ConfigBootstrapSystem if absent (zero-setup)
    public float defaultSampleRateHz;                          // 0 = every frame (default)
    public bool distanceLodEnabled;                            // default false
    public float4 lodDistancesSq;                              // thresholds for AnimLodDistanceSystem
}
public struct AnimationToolkitCameraData : IComponentData      // written by host or by the sample
{                                                              // ToolkitCameraSync MonoBehaviour (Samples~)
    public float3 position;                                    // consumed only by AnimLodDistanceSystem
}
```

### 5.3 `RigBindingSystem` (spawn remap)

ECB-instantiate does not remap entity references inside dynamic buffers — the audit's proven fix (audit §3.3 `BodyPartInitSystem`) is absorbed: prefabs bake with `RigBindingUninitialized` **enabled**; instantiated copies therefore start enabled. `RigBindingSystem` queries enabled roots, rebuilds `RigPartRef` from `LinkedEntityGroup` (matching children by their `RigPartBinding.targetIndex`, which survives instantiation because it is plain data), rewrites `RigPartBinding.actorRoot`, then disables the tag. `[BurstCompile]`, `IJobEntity`, `ScheduleParallel` with `[NativeDisableParallelForRestriction]` lookups (each worker touches only its own actor's children).

**Amendment A35 (C4.9, 2026-08-05): the first sentence above is false for Entities 6.5, and the part rebuild is therefore redundant on the production path. Not removed — see the decision below.**

`Instantiate` **does** remap entity references held in dynamic buffers, not only those held in components, whenever the referenced entity is a member of the instantiated `LinkedEntityGroup`. `EntityComponentStoreCreateDestroyEntities.InstantiateEntitiesGroup` calls `EntityRemapUtility.PatchEntitiesForPrefab` with `dstArchetype->BufferEntityPatches` alongside the scalar patches, and that function walks every element of every buffer carrying an `Entity` field. `RigPartRef.part` points at a part that is a group member, so it is patched.

Verified by execution, not by reading: an acceptance fixture written to *assert the mis-binding as a guard* failed with the instance already correctly bound —

```
Guard: a fresh instance must start mis-bound to the prefab's part…
  Expected: Entity(207:211)   (the prefab's part)
  But was:  Entity(209:101)   (the instance's own part)
```

— read before any system had run.

**Why this was not caught by `RigBindingSystemTests`, which has seven fixtures pointed straight at it.** Its fixture actors are hand-built and their `RigPartRef` buffer starts **empty**; only `RigBindingBakingSystem` populates that buffer, and these fixtures deliberately bypass the bakers. So the system they exercise is doing *first-time binding of an unbound actor*, not *re-binding a mis-bound copy* — a different operation that happens to run the same code. Deleting the rebuild fails four of them, but every failure reads `Expected: 2, But was: 0`: the buffer was never filled, not wrongly filled. A real baked actor arrives at `Instantiate` with the buffer already populated (`RigBindingBakingSystem`, and `ActorBakingAcceptanceTests.BakingAnActor_ResolvesEveryPartToItsDenseTargetIndex` pins it), and on that path Entities does the remap before this system sees the entity.

This is a **new shape of non-discriminating test** for the standard in §11: *the fixture exercises the system under a precondition production never presents.* Both prior shapes were about the values a fixture chose; this one is about the state it started from. The four earlier shapes would all have been caught by reading the fixture carefully. This one could not be — the fixture is correct about everything it asserts, and only the claim that those assertions cover the production path is wrong.

**Decision: the rebuild stays, and §5.3's rationale is corrected rather than its code.** Reasons, in order of weight: (1) the rebuild is genuinely load-bearing for actors that reach the system by any route other than `Instantiate`-of-a-baked-prefab — a host pooling pass that re-parents parts and re-enables the tag is the case the API invites, and `ReBindingAnActorTwice_DoesNotDuplicateItsParts` exists because that route was anticipated; (2) it costs one pass over `LinkedEntityGroup` once per spawn, never again, which is not a cost worth trading correctness-under-an-unproven-assumption for; (3) `phase01` re-derivation and the tag disable must happen here regardless, so removing the rebuild saves the loop, not the system. **What to revert if this is reconsidered:** delete the `partRefs.Clear()` + `LinkedEntityGroup` walk in `RebindActorPartsJob.Execute`, keep `DerivePhaseFromEntity` and the tag disable, and expect four `RigBindingSystemTests` fixtures to fail on empty buffers — those fixtures must then seed `RigPartRef` the way the baker does before they mean anything.

**Owed, and deliberately not done in C4.9:** `RigBindingSystemTests` should gain one fixture that starts from a *populated* buffer, so the suite covers the path production actually takes. It is a test-integrity gap rather than a defect, and folding a rewrite of C4.2's suite into the acceptance step would blur what C4.9 verified.

### 5.4 Playback state machine (`CommandApplySystem` + `PlaybackTimeSystem`)

Per layer, the state machine has three states — **Stopped** (`!Active`), **Playing**, **Blending** (Playing + previous clip fading out) — plus a queued slot:

- **Play(layer, clip, speed, loop, blend):** resolve `clip` → `clipIndex` (binary search, §4.3). Resolution failure ⇒ layer untouched, one `AnimEventOutput { eventKey = ReservedEventKeys.ClipResolveFailed }` emitted (reserved keys: 0 = invalid, `ClipFinished = 1`, `ClipResolveFailed = 2`, 3–15 reserved for future built-ins; user keys start at 16 — validation rule V09). Success ⇒ current clip (if any and `blend > 0`) is demoted to the `previous*` fields with its running time/speed and the loop mode it was playing under (`previousLoop`, §5.2) — the demotion must happen **before** `layer.loop` is overwritten with the incoming request; new clip starts at `time = 0` (or `duration` when `speed < 0`); `blendElapsed = 0`, `blendDuration = blend` (NaN ⇒ new clip's `defaultBlendIn`). `blend == 0` ⇒ hard cut (the old `SetLayer` pop, still available).
- **Queue(...):** stores into `queued*`; `HasQueued` flag. One-deep by design (a deeper queue is game-side state; documented).
- **Stop(layer, blend):** `blend == 0` ⇒ immediate deactivate; else current clip becomes `previous*` fading to nothing over `blend`, `clipIndex = -1`. The mirror case — a `Play` with a blend onto an **idle** layer — fades the new clip *in* from the pose the layers below composited, via an empty `previous*` slot (amendment A32).
- **PlaybackTimeSystem** advances `time += dt × speed` and `previousTime` likewise; advances `blendElapsed`; when `blendElapsed ≥ blendDuration` clears the blend. Loop handling per `LoopMode`: `Loop` = fmod wrap (wrap count preserved for event emission); `Once` = clamp at end, set `Finished | FinishedThisFrame`, deactivate after emitting; `PingPong` = time accumulates and sampling reflects it (`SamplePingPong(t) = duration − |duration − fmod(t, 2·duration)|`), never finishes. On finish with `HasQueued`: the layer stays active holding its final pose, and the queued clip is promoted with its blend at the top of the **next** advance (amendment A30) — a finish-triggered crossfade from the final pose. Empty-duration guard: durations are ≥ 1 ms by validation (V01), so the audit's `float.MaxValue` completion hack is structurally impossible — a resolve failure emits `ClipResolveFailed` and the layer stays inactive, and `ClipFinished` is a real event, replacing the comment-mediated combat contract (audit §6.6).
- Timers are **never** gated on `AnimVisible` (audit-absorbed contract): off-screen actors keep exact time, and events keep firing.

**API surface** (games never touch buffers by hand):

```csharp
public static class AnimationCommandUtil        // Burst-compatible; callable from jobs holding the buffer + enabled-ref
{
    public static void Play(ref DynamicBuffer<AnimationCommand> commands, EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
                            byte layerIndex, ClipId clip, float speed = 1f, LoopMode loop = LoopMode.UseClipDefault, float blendDuration = float.NaN);
    public static void Queue(/* same shape */);
    public static void Stop(ref DynamicBuffer<AnimationCommand> commands, EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
                            byte layerIndex, float blendDuration = float.NaN);
    public static void SetSpeed(...); public static void SetTime(...);
}

public static class PlaybackQuery               // read-side helpers over the PlaybackLayer buffer
{
    public static bool IsPlaying(in DynamicBuffer<PlaybackLayer> layers, byte layerIndex, ClipId clip);
    public static float NormalizedTime(in DynamicBuffer<PlaybackLayer> layers, ref ClipRegistryBlob registry, byte layerIndex);   // A26
    public static bool FinishedThisFrame(in DynamicBuffer<PlaybackLayer> layers, byte layerIndex);
}
```

**Amendment A26 (C4.3, 2026-08-01 — recorded under the owner's standing delegation of architecture calls; ratify or revert).** `PlaybackQuery.NormalizedTime` takes the clip registry.

The signature as first written cannot be implemented. `PlaybackLayer.time` is **seconds on the un-wrapped timeline**; normalising it requires the clip's `duration`, which lives in `ClipBlob` inside the registry blob and is reachable from nothing the layer buffer holds. The three candidate ways to honour the original signature are all worse than changing it: caching a duration on the layer duplicates blob data and gives it a second place to go stale; returning raw seconds makes the name a lie; and returning 0 for "unknown" hands callers a plausible-looking wrong number, which is the failure mode this package has spent four gates removing.

Callers already hold the registry — it is a component on the same actor root (`ClipRegistry.Value`), so the parameter costs a caller nothing they were not already carrying.

Behaviour pinned: an inactive layer, an unresolved `clipIndex` (−1), or a zero/negative duration returns 0. Validation rule V01 guarantees durations of at least 1 ms, so the zero-duration branch is unreachable through the authoring pipeline and exists only to keep a hand-built or corrupted registry from dividing by zero.

**To revert:** restore the two-parameter signature and pick one of the three alternatives above, accepting its cost explicitly.

**Amendment A27 (C4.3, 2026-08-02 — recorded under the owner's standing delegation of architecture calls; ratify or revert).** `PlaybackLayer` gains a `float advanceStartTime`, and event markers are collected from the current clip only.

§5.5 collects marker crossings "between pre- and post-advance time", and `EventWrapMath.CollectCrossings` takes both edges — but `EventEmissionSystem` runs *after* `PlaybackTimeSystem`, on a layer whose only record of the opening edge is the field the advance just overwrote. As specified the window is not reconstructible. Recomputing it as `time − dt × speed` fails in exactly the frames that matter: a `Once` clip clamps its time at the end and a queue promotion resets it, so the subtraction describes a window the layer never travelled and drops the markers inside it — including the final markers of a finishing clip, which is where hit frames live. The field costs four bytes on a buffer element that already carries seventeen, and it is written by `PlaybackTimeSystem` alone: once immediately before the advance, and again on promotion so the promoted clip's window starts where the promoted clip starts. Commands need no special handling, because `CommandApplySystem` runs first and the snapshot taken afterwards is of the post-command value.

The second half is a scope decision, not a mechanism: **the crossfade source does not emit markers.** Doing so would need a second snapshot for `previousTime` and would make every crossfade produce two overlapping streams of gameplay events. An outgoing clip is one the game has already replaced; its remaining footsteps and hit frames belong to a decision that has been superseded. Recorded here and in §12 as a documented limitation rather than left to be discovered.

**To revert:** delete the field, restore the `previousTime` symmetry if the second half is what is being rejected, and give §5.5 some other way to learn where the frame began — the only alternative that survives inspection is merging emission into `PlaybackTimeSystem`, which §5.1 splits deliberately.

### 5.5 Events (`EventEmissionSystem`)

Reserved keys: `ReservedEventKeys.ClipFinished = 1`, `ClipResolveFailed = 2`; user keys ≥ 16 (V09). Each frame `CommandApplySystem` **clears** each actor's `AnimEventOutput` buffer and disables `AnimEventsPending` (amendment A28), and `EventEmissionSystem` then emits: marker crossings between `PlaybackLayer.advanceStartTime` and `PlaybackLayer.time` (the audit's wrap-correct crossing math absorbed verbatim as a pure function `EventWrapMath.CollectCrossings` — including multi-wrap on large dt, and reverse-direction crossing when `speed < 0`), plus `ClipFinished` from `FinishedThisFrame`. Emission enables `AnimEventsPending` — consumers query the enableable, so event-less actors cost nothing. **Latency contract (documented):** events are valid from `EventEmissionSystem`'s execution until the next frame's `CommandApplySystem`; host systems running earlier in the frame see the previous frame's events (1-frame latency); hosts that need same-frame events order themselves after `AnimationToolkitSystemGroup`.

**Amendment A28 (C4.3, 2026-08-02 — recorded under the owner's standing delegation of architecture calls; ratify or revert).** The per-frame clear of `AnimEventOutput` moves from `EventEmissionSystem` to `CommandApplySystem`.

As originally specified the two sections contradict each other. §5.4 requires `CommandApplySystem` to emit `ClipResolveFailed` when a Play or Queue names a clip the actor's registry does not contain; §5.5 required `EventEmissionSystem` — which runs *after* it, in the same group, in the same frame — to clear the buffer before emitting. Every resolve-failure event was therefore destroyed in the frame it was raised, and the request would fail exactly as silently as it does with no event mechanism at all: an animation that simply never plays. The defect is invisible to a reader of either system alone, and to any test that runs only one of them.

Moving the clear to the first system in the group makes the rule simple — *everything written during the logic group survives to the end of it* — and costs nothing: the clear iterates the enabled `AnimEventsPending` set, which is exactly the actors that have something to drop, so actors that emitted nothing are not visited at all. `EventEmissionSystem` (C4.4) therefore **appends and enables only**; it must not clear and must not disable.

The alternatives were worse. Emitting the failure as a per-layer flag consumed by `EventEmissionSystem` loses the `ClipId` that failed — the layer was deliberately left untouched, so its `clip` field still names the clip that is playing fine. A dedicated clearing system at the top of the group adds a fifteenth piece to a fourteen-piece module to express what `OrderFirst` already expresses.

**To revert:** move the clear back into `EventEmissionSystem` and choose a different transport for `ClipResolveFailed`, accepting the loss of the failing id.

**Amendment A29 (C4.3, 2026-08-02 — clarifications, same delegation).** Three command-application behaviours §5.4 leaves unstated:

- **A command naming a layer index the actor does not have is dropped**, leaving every layer untouched, and emits nothing. Minting a reserved event key for it (3–15 are held for future built-ins) was rejected for C4.3: those values are public contract that consumers switch on, so adding one is a versioned decision that belongs with the §5.5 emission pass and with the owner, not as a side effect of the apply system. Reusing `ClipResolveFailed` was rejected as a lie — nothing failed to resolve. The behaviour is pinned by a fixture rather than left implicit.
- **Queue resolves its clip when it is queued**, emitting `ClipResolveFailed` and leaving the slot empty on failure. §5.4 describes Queue as storing into `queued*` without mentioning resolution, but a NaN `blendDuration` has to resolve against the incoming clip's `defaultBlendIn` regardless — and a failure surfaced seconds later, as a promotion that does nothing, has no observable connection to the call that caused it.
- **Stop clears the queue slot**, in both the immediate and the fading branch. A stopped layer that keeps a queued clip is armed to restart on its own the next time anything finishes.

**To revert:** each is independent; the layer-index one is the only one with a defensible alternative (a new reserved key).

**Amendment A30 (C4.4, 2026-08-02 — recorded under the owner's standing delegation of architecture calls; ratify or revert).** Queue promotion is deferred by one advance.

§5.4 as written promotes the queued clip in the same advance that finishes the current one. `EventEmissionSystem` runs *after* that advance, in the same group, and reads the layer to decide what to emit — so an immediate promotion hands it a layer whose `clip`, `clipIndex`, `time` and `advanceStartTime` all belong to the follow-up. Two things break at once, both silently:

- `ClipFinished` names the promoted clip, telling a game its *next* animation ended before it played a frame. This is the event combat code is built on (§6.6 in the audit), so the wrong id is worse than no event.
- Every marker in the finishing clip's last segment is collected against the promoted clip's timeline instead — i.e. dropped. That segment is one frame long, but it is the frame containing anything authored at or near normalized time 1, which is where hit frames sit.

`PlaybackTimeSystem` therefore raises `Finished | FinishedThisFrame`, leaves the layer active on its final pose, and promotes at the top of the next advance (guarded by `Finished && HasQueued`, so the completion cannot re-fire while it waits). The cost is one extra frame of the final pose, on hard-cut queues only — a blended promotion enters at weight 0, so nothing is visible either way, and a `Once` clip holds that pose regardless.

The alternative was a `finishedClip` field on `PlaybackLayer`. It fixes the wrong-id half and not the dropped-marker half: recovering those needs the finished clip's dense index and its window as well, which is three more fields on the buffer element §12 R7 already names as the size risk.

**To revert:** promote inside `AdvanceCurrentClip` again, and pay for it with either the three extra fields or a documented loss of both properties.

### 5.6 Transform technique (`TransformSampleSystem` → `TransformApplySystem`)

`TransformSampleSystem` (`IJobEntity` over actor roots, `[WithAll(AnimVisible)]`, parts written via `[NativeDisableParallelForRestriction] ComponentLookup<TargetPose>` — each actor owns its parts): for each part, start from `TargetRestPose`, then composite layers **bottom-up (lowest index first)**:

- For each active layer, sample each track bound to the part's `targetIndex` at that layer's effective time (PingPong-reflected; blend = also sample `previous*` and lerp poses by `w = blendElapsed / blendDuration` before compositing).
- `Override` tracks replace exactly the channels in their `AnimatedChannels` mask, writing `rest + key` into them (scale: `rest × key`); `Additive` tracks add position/rotation and multiply scale **onto the composited result of the layers below** — the sampler-semantics decision (audit open question 3): documented intent wins over the shipped claim-mask code; the claim mask is gone, replaced by natural bottom-up ordering (upper layers still win on contested channels because they composite later). Both ops treat a key as a **delta from a frame**; they differ only in which frame (amendment A31).

**Amendment A31 (C4.5, 2026-08-02 — product-owner approved). Transform keys are offsets from the rest pose, in the sampler as well as in the spec.**

C4.5 found the spec and the shipped `ClipSampler` disagreeing about what a transform key *is*, with both positions load-bearing:

- The spec says **offsets from rest**, in three independent places: §3.2 types `TransformKeyBlob.position` as "x/y local offset"; §4.6 says an unkeyed target "in offset space sits at its rest pose, i.e. offset zero"; and amendment **A13 exists entirely** because "transform keys hold local offsets from a target's rest pose, and rest poses live on the prefab" — which is why `offsetBounds` is offset space and why `ActorRestBounds` had to be invented at all.
- `ClipSampler.ApplyClipToPose` implemented **absolute local values**: it seeded the pose from rest and then, for an `Override` track, overwrote the masked channels with the raw key. `ClipSamplerTests.SamplePose_OverrideTrack_ReplacesOnlyItsMaskedChannels` locked that in deliberately — rest `rotationZ` 0.5, key 1.5, asserting 1.5.

**Resolved in favour of the spec.** `ApplyClipToPose` now takes the rest pose and an `Override` track writes `rest + key` (scale `rest × key`); `Additive` is unchanged and still anchors to the composited pose below. The consequence that decided it: under absolute semantics, re-posing a rig's rest — moving a shoulder, re-proportioning a limb — is silently ignored the moment any clip plays, which defeats the point of a cutout rig and would have made every clip re-authoring work. Under offset semantics the rest pose stays the rig's single source of proportion and clips are deltas over it. It also leaves A13 and `offsetBounds` correct as written rather than requiring both to be rewritten.

**Why three gates missed it:** every fixture in `LayerCompositionTests` used a rest pose at the origin with unit scale, where `rest + key` and `key` are the same number — the "passes under both the correct and the broken implementation" shape §11 exists to forbid. The suite now carries an `OffsetRestPose` fixture and three composition tests that fail under the absolute reading, plus the corrected `ClipSamplerTests` assertion.

**To revert:** drop the `restPose` parameter from `ApplyClipToPose`, restore the four `Override` branches to raw assignment, and rewrite §3.2's "offset" wording, §4.6's offset-space paragraph, and A13's rationale to match — the revert is not local to the sampler, which is itself evidence for which way the conflict pointed.

**Amendment A32 (C4.5, 2026-08-02 — recorded under the owner's standing delegation; ratify or revert). A layer fades *in* as well as *out*.**

§5.4 specifies Stop's fade-out ("current clip becomes `previous*` fading to nothing over `blend`") but says nothing about the mirror case, and the shipped `CommandApplySystem` demoted a clip into the blend source only when the layer already had one. A `Play` with a blend arriving on an **idle** layer therefore hard-cut, silently ignoring the blend duration the caller asked for. Fade-out was graceful; fade-in popped.

That asymmetry lands squarely on layer mixing, which is the main way a rig with more than one layer is driven: bringing an upper layer in over the ones below — an attack over a walk, a reaction over an idle — is exactly the "activate a layer with a crossfade" case, and it was the one case that could not be done smoothly.

No new state was needed. `ClipSampler.CompositeLayers` already reads a blending layer with `previousClipIndex = −1` as "lerp from the incoming pose", and the incoming pose at that point *is* whatever the layers below composited. `CommandApplySystem` now starts the blend in that state instead of clearing it. A `Play` arriving during a Stop fade keeps the still-fading clip as its crossfade source rather than dropping it.

**Layer weight remains out of scope, deliberately.** Layers are binary — active or not — and a layer's strength over time is expressed by its own crossfade, not by a sustained weight. Per-part masking already gives the common case for free: an upper layer's clip simply carries no track for the targets it should not touch, so lower layers survive on those parts untouched. A persistent partial weight (a permanent 30% additive breathing layer) is not expressible and would need a `weight` field on `PlaybackLayer` plus a weight argument through `CompositeLayers`; recorded here as a known limitation rather than built unasked.

**To revert:** restore the single `hasOutgoingClip && blendDuration > 0f` condition; a blend requested onto an idle layer becomes a hard cut again.
- **All** tracks bound to a target apply, in canonical order — no first-match `break` — eliminating the audit §3.4 editor/runtime divergence by specification: there is exactly one sampler (§5.11).
- Sample-rate quantization: an actor samples only on frames where `floor((elapsedTime + phase01 / rateHz) × rateHz)` advances, with `rateHz` from `SampleSettings` (fallback `AnimationToolkitConfig.defaultSampleRateHz`; 0 = every frame). `phase01` is baked from a hash of the authoring instance id and re-randomized by `RigBindingSystem` at spawn — per-entity phase spreads crowd sampling across frames (fixes the audit's global same-tick spike, open question 9). The sampled `normalizedTime` is **not** quantized (matches shipped runtime; the old editor-only quantization is dropped).

`TransformApplySystem` writes, per part: `LocalTransform.Position` (xy offset + z layer order added to rest), `LocalTransform.Rotation` (Z), `LocalTransform.Scale = 1f`, and `PostTransformMatrix = float4x4.Scale(pose.scale.x, pose.scale.y, 1f)` — scale and flip (negative scale) are **live**, resolving the audit §2.1 dead-scale regression via the already-baked `NonUniformScale` path (open question 4 decision).

### 5.7 Flipbook technique (`SpriteMaterialSystem`)

Sprite tracks sample to `TargetPose.sliceIndex` / `atlasRect` (the key at or before the time holds until the next key — see amendment A43, which corrects this from nearest-key; `-1` = keep current, audit convention absorbed) in `TransformSampleSystem` (same job — sprite keys are just another track kind). `SpriteMaterialSystem` copies pose → material-property components **directly**: `SpriteSliceProperty.Value = pose.sliceIndex` and `AtlasFrameProperty.Value = pose.atlasRect`. The audit's two-hop `ImageIndex → ImageIndexOverride` staging with its rotted dirty flag is **replaced** (audit §8 verdict honored) by this single write. Design/skin systems in a host change the base look by writing `TargetRestPose.restSliceIndex` (same contract the host's DesignSystem relies on today, §13).

**The `-1` "no change" convention lives on the authored key, never on the pose.** Composition seeds every pose from the rest pose (`ClipSampler.RestToPose` writes `pose.sliceIndex = restPose.restSliceIndex`) before any track is applied, and a slice-mode sprite key only overwrites it when its own `sliceIndex >= 0`. A negative pose slice is therefore unreachable, and a `sliceIndex >= 0 ? sliceIndex : restSliceIndex` guard in `SpriteMaterialSystem` would be dead code. Every path — no sprite track at all, a `-1` key, one side of a blend, or a host that rewrote `restSliceIndex` — already resolves to the rest slice through the seed. `LerpPose` picks whole slice values (never interpolates them), so it cannot introduce a negative either.

**Amendment A37 (migration, 2026-08-06 — product-owner approved): the slice channel becomes a sum, not a replacement.** §5.7 above describes *absolute* slice keys, which is what both this package and the host game shipped. That model cannot express the case a variant-based 2D cutout rig actually needs, and the host has the same limitation today (`AnimationSamplingSystem` does `if (sampled.imageIndex >= 0) imageIndex = sampled.imageIndex`) — so this is a **missing capability, not a migration regression**.

**The case.** A design-driven target's texture array is laid out in *variant blocks*: for ears, `[typeA_front, typeA_back, typeB_front, typeB_back, …]`. The host's design system rolls a variant per character and writes its slice into `TargetRestPose.restSliceIndex` (`DesignApplyUtil.ApplyDesign`). Facing then needs the *other view of that same variant* — `restSliceIndex + 1` — and an absolute key cannot say that without destroying the character's rolled appearance.

**The decision: `finalSlice` is the sum of three terms, each owned by whoever knows it.**

| Term | Meaning | Written by |
|---|---|---|
| `TargetRestPose.restSliceIndex` | which **variant** this character has (pointy / round / small ear) | host design system (unchanged) |
| `PartFacing.viewOffset` (new, per part) | which **alt view** the part shows (ear from front / from behind) | host facing system |
| the sprite key, in relative mode | which **frame** the animation is on | the clip |

**Amendment A37a (2026-08-06): facing changes a cutout part in two unrelated ways, and the first draft conflated them.** Some parts have a genuinely different **alt view** drawn for them — an ear seen from behind is *different art*, so it is a different slice, and that is what `viewOffset` addresses. Other parts are simply **mirrored** — a nose seen from the left is *the same art reflected*, which is a negative scale and no slice change at all. The two are independent: a part may need either, both, or neither.

The component is therefore `PartFacing { int viewOffset; bool mirrorX; }` rather than a lone slice offset. `mirrorX` **negates** the composited `scale.x` rather than assigning −1, so it composes with a part authored already-flipped (a left ear built by mirroring the right) instead of swallowing it — mirroring a flipped part unflips it, which is the correct reading. The package already carried negative scale live through `PostTransformMatrix` (§5.6, fixture `Apply_PreservesNegativeScaleSoPartsCanFlip`); what it lacked was a facing-driven way to *set* it.

**Do not confuse this with the Mirror Clip utility.** Both are called "flip". Mirror Clip is an *authoring* action that bakes reflected keys into a new clip once; `mirrorX` is a *runtime* state that reflects a part for as long as it faces that way. Neither replaces the other.

**First, what facing is *not*: an offset applied to one clip.** An earlier draft of this amendment proposed carrying both a slice offset and a `LayerZ` offset on the component, on the theory that "direction is state, not animation". **The product owner rejected that and was right.** A rig seen from the front does not move like the same rig seen from the side *plus a nudge* — the arms arc in one and shuffle up and down in the other, and the legs likewise. **The motion is different data, and no z-shuffle or index offset turns an arc into a shuffle.** Direction therefore selects *which keyframes play*, which is exactly the "directional **clip-set** convention (N clips + a pick-nearest-facing helper)" §10 answer 7 already specifies: `Walk_N` / `Walk_E` / `Walk_S` / … on the **Base** layer, with the game choosing the `ClipId`.

Two consequences follow. **The host's `Direction` layer disappears** — it exists to composite an offset over a direction-agnostic Base clip, and that model is what has just been rejected; §13.1's "`AnimationLayerType` (7 layers) → `LayerDefinition` list, direct" becomes six layers. And **`LayerZ` needs no component term at all**: draw-order shuffling is authored directly in each per-direction clip, where it is already rest-relative (A31) and composes correctly. The component narrows to the slice term alone.

**Why the *view* term is nonetheless a component and not clip data.** Because facing multiplies into every clip that touches a sprite. If `Walk_N` keys the ear's back view, then a blink must also know which way the character faces — so `BlinkNormal` becomes `BlinkNormal_N`, `BlinkNormal_S`, `BlinkNormal_E`, … and the same for talking and every expression. **Applied after composition, one facing term is inherited by every clip on every layer for free, so only *locomotion* needs direction variants** and blinks, talking and expressions stay single clips. It also cannot be clobbered by layer priority, which the clip-data alternative cannot promise: `Direction` is priority 1 while `Eyes`/`Mouth` are 4 and 5, so an upper-layer blink would silently drop the facing view on exactly the parts carrying the most view variants.

*Owner decision (2026-08-06): **all 8 directions are authored explicitly per locomotion state, and mirroring is an authoring accelerator rather than a runtime derivation.** Every direction is a real, hand-editable `ClipAsset`; the Mirror Clip utility (§7.1, M5) is used to *seed* the mirrored ones — duplicate a clip, press Flip, then tune — instead of deriving NW/W/SW at runtime from five authored clips. The distinction matters: a runtime mirror is a permanent constraint that cannot express an asymmetric costume detail or a limp, whereas a bake-in-place flip is a starting point the animator owns from that moment on.*

**Amendment A38 (2026-08-07): the host's direction sets, and why runtime mirroring replaces most direction clips.** The host carries two enums (`Assets/_Scripts/Data/Enums/Direction.cs`): `Direction` — which of eight ways a character faces — and `AnimationDirections { One, Two, Four, Six, Eight }` — how many variants that *character* has. The second is **per character, not per rig**: a citizen and a boss share a rig and differ in how many directions were drawn for them, which is exactly the "directional clip-set convention + pick-nearest-facing helper" of §10 answer 7.

**The six-direction set is (owner, 2026-08-07), with south facing the camera and north away:** `South, SouthEast, NorthEast, North, NorthWest, SouthWest`. **East and West are absent** — a six-direction character never shows a pure side view.

**That set is mirror-symmetric, and the consequence is large.** `South` and `North` are self-symmetric; `SouthEast`↔`SouthWest` and `NorthEast`↔`NorthWest` are mirror pairs. So six directions need **four authored clips**, not six:

| Facing | Clip played | `mirrorX` |
|---|---|---|
| South | `Walk_S` | false |
| SouthEast | `Walk_SE` | false |
| NorthEast | `Walk_NE` | false |
| North | `Walk_N` | false |
| NorthWest | `Walk_NE` | **true** |
| SouthWest | `Walk_SE` | **true** |

Eight directions extend the same way: author `N, NE, E, SE, S` and mirror three.

**This settles how `mirrorX` and the Mirror Clip utility divide.** They are not alternatives and not duplicates:

- **`mirrorX` (runtime) is the default path.** One clip serves two facings, and because it mirrors the *composited pose* it flips transforms and art together — no second clip, no duplicated keys to keep in sync when the walk is retimed.
- **Mirror Clip (authoring) is the escape hatch**, for a facing that must *deviate* from a pure reflection: an asymmetric costume detail, a satchel on one hip, a limp. Press Flip to get the mirrored keys as a real clip, then hand-tune. That is precisely the owner's "then I can make my own adjustments after".

**They must never both apply to one facing** — baked mirrored keys plus a runtime `mirrorX` is a double reflection, which is no reflection at all. A facing uses one or the other: the runtime mirror, or its own authored clip with `mirrorX` false.

**The other sets (owner, 2026-08-07) — and both of my guesses were wrong, which is why they are recorded rather than inferred:**

- **`Two` = `SouthWest`, `SouthEast`.** A two-direction character is *not* a pure side-on profile: it is the classic side-scroller three-quarter view, **always tilted slightly toward the camera so the face stays visible**. My first two guesses — `South`/`North`, then literal `East`/`West` — were both wrong, and the reason is the same each time: I was reasoning from compass geometry when the sets are chosen by *what reads well on screen*.
- **`Four` = `SouthEast`, `NorthEast`, `NorthWest`, `SouthWest`.** Diagonals only — **no head-on animations at all**, just front and back at an angle.

**The sets nest, and each level adds one mirror-symmetric pair:**

| Count | Members | Adds | Authored | Derived by `mirrorX` |
|---|---|---|---|---|
| `Two` | SE, SW | the front three-quarter pair | **1** — SE | SW |
| `Four` | + NE, NW | the back three-quarter pair | **2** — SE, NE | SW, NW |
| `Six` | + S, N | head-on and head-away | **4** — SE, NE, S, N | SW, NW |
| `Eight` | + E, W | true profile | **5** — SE, NE, S, N, E | SW, NW, W |

**Every set is closed under mirroring, without exception** — that is the invariant the whole scheme rests on. Authored count = (self-symmetric members: only S and N are their own mirrors) + (mirror pairs). A four-direction character costs **two** locomotion clips per state; a two-direction character costs **one**. That is a far larger saving than "mirroring halves the work", and it is the strongest argument for `mirrorX` being runtime state rather than a second set of baked clips.

**The nesting also resolves the quantization worry.** Because every set is built from left/right pairs, the facing decision is fundamentally *"which side is the character moving toward"* plus *"how much toward or away from the camera"* — not "which of N compass points is nearest". Quantizing by the **sign of horizontal movement** for the mirror, and by the vertical component for which pair, never ties and never flickers on a boundary. Nearest-angle would leave a `Four` character walking straight at the camera undefined between `SE` and `SW`, which under this scheme is the same clip with `mirrorX` on or off.

**`One` = `South`, and it is deliberately outside the nesting.** It is not "the first direction of a turning character" — it is *this thing does not turn*: a boss at the top of the screen, or a stationary effect with no directional views. So it faces the camera head-on rather than taking `Two`'s three-quarter view. **The complete table:**

| Count | Members | Authored | Derived by `mirrorX` |
|---|---|---|---|
| `One` | S | **1** — S | — |
| `Two` | SE, SW | **1** — SE | SW |
| `Four` | SE, NE, NW, SW | **2** — SE, NE | SW, NW |
| `Six` | S, SE, NE, N, NW, SW | **4** — SE, NE, S, N | SW, NW |
| `Eight` | S, SE, E, NE, N, NW, W, SW | **5** — SE, NE, S, N, E | SW, NW, W |

**`One` needs no facing machinery at all** — no `PartFacing`, no quantization, no mirror. A one-direction actor is simply an actor, so the cheapest content in the game also costs the least at runtime. Worth stating because it is exactly the case a facing system is most likely to over-serve.

**Host camera (corrected 2026-08-07):** it *does* move — a tilt in one direction, or a rotation change — rather than a full orbit. So billboarding is genuinely exercised by the game and stays in scope; an earlier note here claiming a fixed camera was wrong and is withdrawn.

**The host's existing `BillboardSystem` is more capable than §13.1's row assumed, and it disagrees with §6.3 on one point that will change the look.** Two findings, both C5's to resolve:

1. **The death-freeze already exists and matches the spec's plan.** Alive parts take a full billboard; dead ones freeze *yaw* (no horizontal spin) while keeping camera *pitch*, by decomposing the target into yaw and pitch and substituting the entity's current yaw. §13.1 anticipated exactly this as "the host's death handler writes mode 3 + current yaw into `BillboardParamsProperty`", so the migration is a re-expression rather than a redesign. The decomposition maths is worth lifting verbatim.
2. **It billboards from the camera's *forward vector*, not its position.** `quaternion.LookRotation(Camera.main.transform.forward, up)` gives every quad the same rotation — screen-aligned. §6.3's normative rule is `_WorldSpaceCameraPos` **only**, which makes each quad face the camera *point* — spherical. These are different looks, and they diverge most for quads far from screen centre. §6.3 forbids `UNITY_MATRIX_V` precisely because the view matrix is what yields the screen-aligned result, and the rule exists so a billboarded quad's shadow re-orients with the camera rather than with the light. **So migrating billboarding will visibly change the host's look**, and that is a §12 R10-class migration cost that has not been recorded until now. C5 must decide whether the package ships the position-based rule only (and Stitch Punk accepts the change), or whether screen-aligned becomes a fourth billboard mode.

**Sequencing consequence: Mirror Clip is pulled forward from C7.** It is M5 work, but it depends only on M1 (`ClipAsset`, `RigAsset.mirrorPairs` — both closed and shipping) and touches neither shaders nor VAT, so it does not violate dependency order. It is worth pulling forward because the migration's content step is "author 8 directions per locomotion state", which is materially harder without it. **It is not, however, useful before the clip converter runs** — there are no `ClipAsset`s to flip until then. Order: A37 runtime → converters (§13.2 step 1) → Mirror Clip → C5.

`RigAsset.mirrorPairs` is currently read by nothing (its own doc comment says so), which makes it the one piece of authoring data in the package with no consumer and therefore no test. Building the utility discharges that.

**Owner decisions, 2026-08-06, on the three open points:**

1. **Eye targets do not change with facing.** `framesPerVariant = 1` for them and no direction row. Only ears/nose/hair carry views. This also removes the `[variant][view][blinkframe]` nesting question entirely — blink frames are plain animation frames inside a single view.
2. **Mirroring must flip *everything* the viewer sees, nose included** — "if facing from the side and that side gets flipped, everything should be flipped". **This falls out of A37 for free and the Mirror Clip utility must not implement it.** The view term lives in `SpriteViewOffset`, written by the facing system, so facing west instead of east flips the nose's art whatever clip is playing. Were the utility to *also* rewrite slice keys, the two mechanisms would both move the same value and fight. **Mirror Clip therefore mirrors transforms only** (negate position x, negate rotation z, swap paired targets) and leaves every slice key untouched — which is also what makes §11.1's involution test hold exactly rather than approximately.
3. **Mirroring is a toggle plus an Apply, not a one-shot button.** The owner asked to "set it to flip", see the result, and "make my own adjustments after". So the clip carries a non-destructive mirrored-preview state; an **Apply** action appears while it is on and bakes the mirror down into real keys, after which the clip is ordinary hand-editable data with no lingering flag. That preserves the "author all 8 explicitly" decision — Apply is the moment a derived clip becomes an authored one. **Un-applied preview state must never reach a bake**: a clip whose preview is on but un-applied bakes its *authored* keys, because a preview that silently changed shipped data would be the A17 mistake again (a convenience that performs the thing it claims to avoid).

**Consequence for §13.1's Direction row.** The host has exactly one direction clip on disk (`Direction/Male_SouthWest.asset`), so the 8-way system is **greenfield content, not a migration**. Nothing has to be preserved, and the converter has no direction-layer semantics to reproduce.

**Wrapping, and why `framesPerVariant` is per target.** `RigTargetDefinition` gains `framesPerVariant` (default 1). When it is > 1 the offset wraps *inside the character's own block*, so an over-large offset can never display another variant's art — the failure it prevents is a character growing someone else's ears, which no test can see and a player immediately can:

```
blockBase = (restSliceIndex / framesPerVariant) * framesPerVariant
viewIndex = (restSliceIndex % framesPerVariant) + viewOffset + relativeKey
finalSlice = blockBase + positiveMod(viewIndex, framesPerVariant)
```

When `framesPerVariant <= 1` there are no blocks: `finalSlice = restSliceIndex + viewOffset + relativeKey`, clamped at 0. Per-target rather than global because the strides genuinely differ (ears front/back = 2; hair = 4). Rejected: a single global stride in `AnimationToolkitConfig` (wrong for any rig with mixed layouts), and host-side block maths in the design system (it would have to know about facing, fusing two concerns and making a slice unanimatable while facing is active).

**Absolute mode is retained and stays the default.** `SpriteTrack` gains `SpriteSliceSpace { Absolute, RelativeToRest }`. In `Absolute` nothing changes — the `-1` sentinel and existing content behave exactly as §5.7 describes. `RelativeToRest` keys are offsets, where `0` is the natural no-op and negatives are legal.

**Consequence for the paragraph above, which must be revisited rather than left standing.** §5.7 argues `SpriteMaterialSystem` needs no `-1` guard because all four routes to a negative pose slice are closed by construction, and C4.6 shipped a fixture pinning that *reasoning*. Relative offsets open a **fifth route** (`rest 0 + key −1`), so the guard stops being dead code and a clamp becomes load-bearing. The C4.6 fixture and its stated rationale move with it.

**To revert:** drop `SpriteViewOffset`, `RigTargetDefinition.framesPerVariant` and `SpriteSliceSpace`; sprite keys return to absolute-only and the §5.7 no-guard argument is restored intact. Nothing else depends on the sum.

**`ClipSampler.IdentityAtlasRect`** = `new float4(1f, 1f, 0f, 0f)` — scale `(1, 1)`, offset `(0, 0)`, i.e. the full texture. `RestToPose` seeds `pose.atlasRect` with it, so it is the visible default for an atlas-mode actor until an atlas-mode sprite key writes a rect: an actor with no atlas track renders its full texture rather than an undefined sub-rect. There is no rest-pose equivalent of `restSliceIndex` for atlas rects; this constant is that default.

### 5.8 VAT technique (`VatMaterialSystem`) and bounds

For each actor whose registry has VAT clips: take the driving layer (parts declare it via `TargetKind.VatMesh` + the part-level `VatDriven { byte layerIndex }` component added by `RigTargetBaker`), compute:

```
localFrame(clip, t)  = clamp(fmod-or-clamp(t × clip.vatFps, per LoopMode), 0, clip.vatFrameCount − 1)
_VatFrameA           = clip.vatFrameStart + localFrame(current)        // fractional global frame
_VatFrameB           = previous clip active ? previousClip.vatFrameStart + localFrame(previous) : _VatFrameA
_VatBlend            = blending ? blendElapsed / blendDuration : 0
```

written to the part's `VatFrameAProperty` / `VatFrameBProperty` / `VatBlendProperty`. The full CPU→GPU walk-through is §6.5. Loop seams are safe because loop-safe clips carry the duplicated final frame (§4.7), so `floor(frame)+1` never leaves the clip's row range.

`RenderBoundsUpdateSystem`: gated on the **`BoundsDirty` enableable** — never a change-version filter on `PlaybackLayer`, which cannot work: `PlaybackTimeSystem` writes `time` into that buffer every frame for every active actor, so its change version bumps every frame and the filter would degenerate to always-true. The signal contract: `BoundsDirty` is baked **enabled** (guarantees a first-frame bounds write); it is **enabled by** `CommandApplySystem` whenever an applied Play/Stop changes any layer's `clipIndex`, and by `PlaybackTimeSystem` on queue promotion, on Once-completion deactivation, and on blend completion (the previous clip leaves the union in all three). `RenderBoundsUpdateSystem` queries actors with `BoundsDirty` enabled (it runs after both writers — presentation group follows logic group, §5.1), computes the union of `offsetBounds` of all clips still referenced (current + blending previous) **translated into actor space by `ActorRestBounds`** (§5.2 — the baker-supplied rest-pose bounds; `offsetBounds` alone is offset space, §4.6/A13), writes the root's `RenderBounds`, mirrors the union onto parts (conservative; per-part tightening is a non-goal), then **disables `BoundsDirty`** — the sole reset path. A frame with only time advance leaves the tag disabled and touches no bounds. Large-offset clips can no longer cull visibly (audit §7 bounds gap closed).

### 5.9 Visibility boundary

The package **owns** the enableable `AnimVisible` (default enabled = everything animates). It never sets it — any provider may: the host's culling system (Stitch Punk bridges `CameraVisible` → `AnimVisible`, §13), or nothing at all. Contract (documented, audit open question 8): presentation systems (◆ in §5.1) skip disabled actors; logic systems never look at it. Re-enable is self-healing: presentation systems run every frame for enabled actors, so the first visible frame re-samples and re-writes all properties — no dirty tracking needed.

### 5.10 LOD design

`AnimLod.level` (0–3) affects **CPU presentation only** — never timers, never events (gameplay correctness is LOD-independent):

| Level | Effect |
|---|---|
| 0 | Full quality. |
| 1 | Effective sample rate halved (`rateHz / 2`, or 30 Hz cap when rate = 0/uncapped). |
| 2 | Quarter rate; transform blending visually snapped (weights clamp to 0/1; blend **timers** still advance normally — so a LOD swap mid-blend rejoins the correct weight, tested §11). |
| 3 | Pose frozen unless the layer's clip changes; VAT properties still update at quarter rate (GPU cost is unaffected by CPU LOD). |

Level is written by the host or by the optional `AnimLodDistanceSystem` (enabled via `AnimationToolkitConfig.distanceLodEnabled`, reads `AnimationToolkitCameraData`, squared-distance thresholds). Mesh-level LOD (swapping VAT mesh detail) is delegated to Entities Graphics' own LOD Group / MeshLOD path — the package does not duplicate it; the `VatCrowd` sample shows the combination.

**Amendment A34 (C4.8, 2026-08-05): the table above is implemented by `AnimationLodPolicy`, and level 3 needs one new root component.** Three decisions, recorded because each had a plausible cheaper alternative:

1. **The table is a set of pure functions, not logic inside the systems that obey it.** `TransformSampleSystem`, `VatMaterialSystem` and any host that writes its own level all consume the same `EffectiveSampleRateHz` / `SnapsBlendWeights` / `FreezesPose` / `LevelForDistanceSq`. A level meaning "quarter rate" in one system and something marginally different in another is a divergence nobody could see — both look plausible in motion — which is the same argument §5.11 makes for one sampler. It also puts the arithmetic in EditMode where it belongs.
2. **An uncapped actor gets an outright cap from the level (30 Hz at 1, 15 Hz at 2 and 3), and level 3 reports the quarter rate rather than 0.** Halving a `rateHz` of 0 is still 0, i.e. "every frame" — so a LOD system that only scales explicit rates would be a no-op on essentially all content while appearing to work. And because `ClipSampler.ShouldSample` reads 0 as "sample every frame", expressing the level-3 freeze as a rate of 0 would make the most expensive level the *only* unquantized one. Freezing is expressed by `FreezesPose`, never by a rate.
3. **Level 3's "unless the layer's clip changes" needs state the archetype did not have, so `AnimSampleState` was added** — one `int` holding an order-sensitive fold of the clips the last sample was taken from. `BoundsDirty` is enabled on a superset of the right moments and was the obvious substitute, but `RenderBoundsUpdateSystem` clears it, so reading it during sampling would work only while those two systems keep their current order and would otherwise freeze distant actors on the wrong pose in silence — the failure shape A28 and A30 already cost this package twice. Unlike `AnimLod` (opt-in per A23) it is added unconditionally: nothing queries on it, so a conditional add would split the root archetype in two to save four bytes. **The §5.2 root archetype is therefore fourteen components, not thirteen.**

`ClipSampler.CompositeLayers` gains a `bool snapBlendWeights` parameter, with deliberately **no** defaulting overload: one production caller exists, and a silent default is how half the callers would stop honouring LOD.

**To revert:** delete `AnimationLodPolicy` and `AnimLodDistanceSystem`, drop the parameter from `CompositeLayers`, remove `AnimSampleState` from `ActorBaker` / the §5.2 inventory / `DataContractTests` / the archetype assertion in `ActorBakingAcceptanceTests`, and remove the LOD branches from `TransformSampleSystem` and `VatMaterialSystem`. Doing so leaves `AnimLod.level` written by hosts only and read by nothing — which is the state C4.7 shipped and §5.10 does not describe.

### 5.11 One sampler, everywhere

`ClipSampler` (Runtime asmdef, `[BurstCompile]` static class, pure functions): `SamplePose(ref ClipBlob, int targetIndex, float normalizedTime, in TargetRestPose rest, out TargetPose)`, `CompositeLayers(...)`, easing functions, `EventWrapMath`, PingPong reflection, LoopMode time mapping. Runtime jobs, PlayMode tests, EditMode tests, and the editor preview (§7.3) all call these same functions — sampler divergence (audit §3.4) is eliminated structurally, not by discipline.

### 5.12 Managed / un-Bursted inventory

| Piece | Why managed | Player build? |
|---|---|---|
| `ToolkitCameraSync` MonoBehaviour | Reads `UnityEngine.Camera`; writes the `AnimationToolkitCameraData` singleton | Ships in **Samples~** only — not in Runtime/. Hosts with ECS-side camera data write the singleton themselves. |
| `ConfigBootstrapSystem.OnCreate` | Singleton creation (main thread, once) | Runtime; `ISystem`, `[BurstCompile]` — not actually managed, listed for transparency: it does structural work in `OnCreate` only. |
| Bakers, `ClipRegistryBuilder`, validation | Baking is managed by nature | Authoring asmdef; execution stripped from players by Unity. |
| Everything under `Editor/` | Editor tooling | Never (Editor-only asmdef). |

Every runtime per-frame system is `ISystem` + `[BurstCompile]` (struct and every method), jobs via `IJobEntity` `Schedule`/`ScheduleParallel` assigned to `state.Dependency`, never `.Run()`, `[ReadOnly]` from `Unity.Collections`, no `var`, no single-letter names, no per-frame managed allocations, structural changes via ECB only (the only structural ops at runtime are none — the package performs zero runtime structural changes; buffers and enableables cover everything). `SystemBase` count: **zero**.

---

<a name="s6"></a>
## 6. Shader architecture

### 6.1 Inventory

| Artifact | Kind | Purpose |
|---|---|---|
| `ToolkitInstancing.hlsl` | include | The guarded DOTS-instancing block: `#ifdef UNITY_DOTS_INSTANCING_ENABLED` → `UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)` / one `UNITY_DOTS_INSTANCED_PROP` per row of §6.2 / `UNITY_DOTS_INSTANCING_END` + access macros. For hand-written shaders; ShaderGraph generates its own equivalent from Hybrid-Per-Instance declarations. |
| `ToolkitVat.hlsl` | include | `VatBoneSkin(...)`, `VatVertexFetch(...)`: row addressing, manual frame lerp, dual-clip crossfade, octahedral normal decode. Pure functions — usable from Custom Function nodes and hand-written passes alike. |
| `ToolkitBillboard.hlsl` | include | `BillboardTransform(positionOS, pivotOS, billboardParams, cameraPositionWS)` → camera-facing rotation in the vertex stage. **Facing source: `_WorldSpaceCameraPos` exclusively — never `UNITY_MATRIX_V`.** The per-camera position global holds the rendering camera's position throughout that camera's render, including the ShadowCaster/DepthOnly/DepthNormals/MotionVectors passes, whereas the view matrix belongs to the *light* during shadow rendering — a view-matrix billboard silently orients quads toward the light (forbidden by contract, §6.3). Modes (`billboardParams.x`): 0 = off, 1 = full (spherical: face `normalize(cameraPositionWS − pivotWS)`), 2 = Y-axis/upright (facing direction projected onto XZ), 3 = frozen-yaw (yaw locked to `billboardParams.y` radians, pitch follows camera — preserves the audit's dead-unit corpse behavior shader-side). |
| `ToolkitFlipbook.hlsl` | include | `SliceUV(uv, imageIndex)` pass-through helpers + `AtlasFrameUV(uv, atlasRect)`. |
| SubGraphs (`VatBoneSkin`, `VatVertexFetch`, `BillboardTransform`, `FlipbookSliceUV`, `AtlasFrameUV`) | `.shadersubgraph` | Custom Function nodes over the includes; the composable authoring surface for users' own graphs. **Standard Custom Function nodes, not the host's reflection-API node system** — the package must work in any Unity 6.5 project; the host may wrap the same includes in its reflection nodes locally. |
| `ToolkitVatLit` / `ToolkitVatUnlit` | `.shadergraph` | Reference URP graphs, bone + vertex VAT variants via a static branch on a `_VatFlavor` material float; vertex stage = `VatBoneSkin`/`VatVertexFetch` (+ optional `BillboardTransform`); Lit = Opaque, AlphaClip optional, CastShadows on. |
| `ToolkitSpriteLit` | `.shadergraph` | Quad/flipbook reference: slice (Texture2DArray) **and** atlas modes, billboard, `_BaseColor` multiply-tint (property name kept host-compatible per audit §5). |
| `ToolkitVatCrowdUnlit.shader` | hand-written | The performance/crowd reference and the normative example of the explicit macro block + **all passes hand-declared**: `UniversalForward`, `ShadowCaster`, `DepthOnly`, `DepthNormals`, `MotionVectors` — each pass's vertex function calls the same VAT displacement. |

### 6.2 Per-instance property table (normative — the CPU↔GPU contract)

Every row is Hybrid-Per-Instance in graphs (`hlslDeclarationOverride: 3`) and in `ToolkitInstancing.hlsl`; every row has exactly one `[MaterialProperty]` component in the Runtime asmdef. Names are frozen at 1.0.

| Shader property | Type | Component (Runtime) | Written by | Technique |
|---|---|---|---|---|
| `_ImageIndex` | float | `SpriteSliceProperty { float Value; }` | `SpriteMaterialSystem` | Flipbook (slice). Name preserved verbatim for host-shader compatibility (audit §8 Preserve). |
| `_AtlasFrame` | float4 (scale.xy, offset.zw) | `AtlasFrameProperty { float4 Value; }` | `SpriteMaterialSystem` | Flipbook (atlas). |
| `_VatFrameA` | float | `VatFrameAProperty { float Value; }` | `VatMaterialSystem` | VAT (both flavors): fractional **global frame index** of the current clip sample. |
| `_VatFrameB` | float | `VatFrameBProperty { float Value; }` | `VatMaterialSystem` | VAT crossfade target sample. |
| `_VatBlend` | float | `VatBlendProperty { float Value; }` | `VatMaterialSystem` | 0..1 crossfade weight A→B. |
| `_BillboardParams` | float4 (x = mode, y = frozenYaw radians, zw reserved) | `BillboardParamsProperty { float4 Value; }` | game/host (or baked constant) | Billboard modifier. |
| `_BaseColor` | float4 | *host-owned* (e.g. Stitch Punk's `BodyPartTint`) | host | Documented, not shipped — reference graphs declare it Hybrid-Per-Instance so host tint components bind unchanged. |

Material-level (shared, **not** per-instance — anything per-instance here would break batching): `_VatBoneTex` / `_VatPosTex` / `_VatNormTex` (Texture2D), `_VatTexelParams` (float4: textureWidth, textureHeight, rowsPerFrame, boneOrVertexCount), `_VatFlavor`, `_MainTexArray` (Texture2DArray), `_MainTex`.

### 6.3 Displacement in all passes

- **ShaderGraph path:** vertex-stage position/normal/tangent modification in URP ShaderGraph is emitted into every generated pass — ShadowCaster, DepthOnly, DepthNormals, and MotionVectors included — so the reference graphs get correct shadows/depth/mv-depth by construction. Verified per-graph in Phase C by inspecting the generated shader (`Show Generated Code`) as a review gate (§9 C5/C6 DoD).
- **Hand-written path:** `ToolkitVatCrowdUnlit.shader` declares each pass explicitly and calls the shared displacement — the file *is* the documentation of the pattern.
- **Billboard facing across passes:** `BillboardTransform` derives facing exclusively from `_WorldSpaceCameraPos` (§6.1), so a billboarded quad presents the *same camera-facing geometry* in ShadowCaster, DepthOnly, DepthNormals, and MotionVectors as in the color pass. **Intended shadow behavior:** the quad casts the true shadow of its camera-facing orientation — silhouettes are self-consistent between what the camera sees and what the light shadows. Normative caveats (also in `shader-contract.md`): (a) shadows re-orient as the camera moves — inherent to imposters, negligible under the mostly-fixed 2.5D cameras this targets, visible under free orbit; (b) a camera-facing quad viewed edge-on by the light casts a near-degenerate thin shadow; (c) in renders not owned by a camera loop (e.g. lightmap baking), `_WorldSpaceCameraPos` is meaningless for facing — implementations must treat mode as 0 there (baked lighting of billboards is unsupported, documented).
- **Amendment A39 (2026-08-07 — product-owner directive): screen-aligned billboarding is a supported mode, and the host's look must not change.** The owner's existing `BillboardSystem` derives facing from `Camera.main.transform.forward`, so every quad takes the same rotation — **screen-aligned**. The bullet above mandates `_WorldSpaceCameraPos`, which makes each quad face the camera *point* — **spherical**. These are visibly different, diverging most for quads far from screen centre, and §13.1 previously assumed the host would simply adopt the package's. **It will not: "I like how my current billboarding system works… I do not want that to change."** A migration that silently re-orients every character in the game is not a migration.

  **This costs the rule nothing, because the rule was never about position.** Re-read what it buys: `_WorldSpaceCameraPos` is required so that a billboarded quad presents *the same geometry in every pass*. `UNITY_MATRIX_V` is forbidden because the ShadowCaster pass substitutes the **light's** view matrix, so a quad facing "the camera" there would cast the shadow of a shape the camera never sees. The invariant is **pass-invariance**, not position-versus-forward. A camera forward vector written once per frame by the host is exactly as pass-invariant as the camera position.

  **So:** `AnimationToolkitCameraData` gains a `forward` field (host-written, like `position` — the package still never touches a `Camera`), plumbed to a shader global alongside `_WorldSpaceCameraPos`. Billboard mode selects which the facing derives from: spherical for hosts that want imposters, screen-aligned for hosts like this one. **`UNITY_MATRIX_V` stays forbidden**, and shadows stay correct in both modes. Zero `forward` degrades to spherical rather than to a degenerate quad.

  **What C5 owes:** the mode enumeration in §6.2's `_BillboardParams` grows a screen-aligned value, `BillboardTransform`'s signature takes the forward vector, and the human-verified checklist gains "screen-aligned mode reproduces the host's pre-migration framing side by side". The death-freeze (mode 3 + frozen yaw, §13.1) is unaffected — it constrains *which* rotation is applied, not where facing comes from.

- **Amendment A41 (2026-08-07 — product-owner directive): transform-level billboarding is the default for actors, and §13.1's deletion of the host's CPU system is withdrawn.** The owner: *"the billboarding might have made more sense outside the material space and on the entities as a group themselves like how I originally designed it… characters are layered billboards and it looks way better to move them as a group from a single parent."*

  **This is correct on the merits, not a preference.** Per-vertex billboarding rotates **each quad about its own pivot**. A cutout character is a dozen-plus layered quads at different offsets, so every layer would turn independently and the authored arrangement would fan apart — the parts stop occupying their relative positions the moment the camera moves off axis. Rotating the **actor root** keeps the rig rigid and turns it as one, which is the only thing that preserves a layered composition.

  §13.1 deleted the host's `BillboardSystem` and overruled the audit's advice to absorb it, on the grounds that a shader-side property was the more general mechanism. **It is more general and it is wrong for this content**: generality bought nothing here and cost the composition. The row is withdrawn; the host's system is absorbed as the audit originally recommended, generalised into the package.

  **The package therefore ships both, because they serve genuinely different content:**

  | Path | Rotates | Right for |
  |---|---|---|
  | **`ActorBillboardSystem`** (CPU, transform) | the actor root — whole rig as one | **layered cutout characters** — the default |
  | `BillboardTransform` (shader, per-vertex) | each quad about its own pivot | single-quad imposters, grass, VAT crowd meshes where CPU cost per instance is the constraint |

  Neither replaces the other, and an actor must use exactly one — a root rotated on the CPU *and* quads rotated in the shader is a double rotation, the same trap A38 records for `mirrorX` versus baked mirrored clips.

  **The shader path stays** — it is already built, already tested, and is the right answer for the crowd/VAT cases C6 targets. Nothing in C5 is wasted; what changes is which path an *actor* uses by default.

- **Motion-vector velocity caveat:** the MV pass renders VAT-displaced positions (correct depth), but the **velocity** it writes derives from the entity transform delta, not the deformation delta. Deformation-accurate velocity (previous-frame `_VatPrevFrameA/B/Blend` per-instance props + dual sampling in the MV pass) is designed but ships **default-off** behind the `_VAT_DEFORMATION_MV` shader feature in v1 — see §12 R6 for the risk entry and mitigation. This is a documented limitation, not an oversight; the audit found the host has motion vectors entirely unhandled today (§5), so v1 is a strict improvement.

### 6.4 VAT sampling math (in `ToolkitVat.hlsl`)

Bone flavor, per vertex:
```
frameA0 = floor(_VatFrameA); frameA1 = frameA0 + 1; fA = frac(_VatFrameA)
row(f) = f                                  // rowsPerFrame == 1
boneMatrix(b, f) = { tex[(b*3+0, f)], tex[(b*3+1, f)], tex[(b*3+2, f)] }   // 3 texel loads (Load, not Sample)
skin(f) = Σ_i weight_i × boneMatrix(index_i, f) × positionOS               // indices/weights from UV1/UV2
posA = lerp(skin(frameA0), skin(frameA1), fA)                              // manual frame lerp
pos  = _VatBlend > 0 ? lerp(posA, posB /* same math on _VatFrameB */, _VatBlend) : posA
```
Worst case texel loads: 2 clips × 2 frames × 3 texels × 4 influences = 48; the crowd shader's 2-influence variant halves it; `_VatBlend == 0` branch skips the B path (dynamic branch, uniform per instance — cheap on all targets). Vertex flavor replaces the inner product with a direct position fetch at `(v % W, f × rowsPerFrame + v / W)` — 2–4 loads. Loop seams never read a neighboring clip because loop-safe clips duplicate frame 0 at their end (§4.7) and `Once` clips clamp CPU-side to `frameCount − 1` (frameA1 then reads one row past — with weight `fA = 0`, and the row exists because it is either the next clip's first row (valid numbers, zero weight) or the clamped texture edge).

### 6.5 CPU↔GPU walk-through (the contract, end to end)

Play `ClipId(0xA3…)` on layer 0 of a bone-VAT actor:
1. Game calls `AnimationCommandUtil.Play(commands, commandPendingEnabled, 0, walkClipId)`.
2. `CommandApplySystem` binary-searches `ClipRegistryBlob.sortedClipIds` → `clipIndex = 7`; writes `PlaybackLayer[0] { clip, clipIndex = 7, time = 0, … }`.
3. `PlaybackTimeSystem` advances `time`.
4. `VatMaterialSystem` reads `clips[7] { vatFrameStart = 120, vatFrameCount = 31, vatFps = 30 }`, computes `_VatFrameA = 120 + fmod(time × 30, 30)`, writes `VatFrameAProperty` on the VAT part entity.
5. Entities Graphics uploads the per-instance property through BRG; the material's `_VatBoneTex` (bound once, shared) is read by `VatBoneSkin` at row `_VatFrameA` — displacement identical in Forward, ShadowCaster, DepthOnly, MotionVectors passes.

### 6.6 Batching constraints (normative)

- All per-frame variation travels through §6.2 per-instance properties — never material swaps, never `MaterialPropertyBlock`s, never per-instance textures. One clip set + one material = one BRG batch regardless of crowd size or per-actor clip/time/LOD.
- Color-typed properties uploaded from C# must be **linear** (audit's verified sRGB rule carried forward; documented in `shader-contract.md`).
- Texture2DArray flipbooks: array membership is material-level; `_ImageIndex` selects the slice per instance — batch-safe by design (host-proven, audit §5).

---

<a name="s7"></a>
## 7. Editor architecture

### 7.1 Window inventory

| Window | Menu | Purpose |
|---|---|---|
| `ClipEditorWindow` | Window ▸ Animation Toolkit ▸ Clip Editor | The timeline editor + live preview + clip browser. Feature parity target = the audit §4 IMGUI window's verified feature list (clip selector, transport, zoomable timeline, draggable keys, double-click add, context inspector, full keyboard map, copy/paste, mirror/duplicate) rebuilt in UI Toolkit with complete Undo. Replaces the IMGUI window (audit §8 verdict honored). |
| `VatBakeWindow` | Window ▸ Animation Toolkit ▸ VAT Baker | Wizard over `VatTextureBaker.Bake`: source prefab picker, clip list (from a `ClipSetAsset`), settings (§4.7), validation preflight, progress, result inspector link. |

Plus custom inspectors (Editor/Inspectors/): `RigAsset` (target/layer lists with stable-id badges, mirror-pair table), `ClipSetAsset` (clip roster with per-clip validation status column, "New Clip in Set", "Generate Clip Id Constants"), `VatTextureSetAsset` (read-only stats: formats, memory, per-clip ranges; "Rebake" button when `sourceHash` is stale), `ActorAuthoring` (layer seed editor + "Open in Clip Editor").

### 7.2 UXML/USS structure

```
Editor/ClipEditor/
├── ClipEditorWindow.uxml        — root: <Toolbar> (transport, clip picker, snap/zoom, validation badge)
│                                  <TwoPaneSplitView vertical>
│                                    <TwoPaneSplitView horizontal>
│                                      #clip-browser   (ListView of set's clips, search field)
│                                      #timeline-pane  (see below)
│                                      #inspector-pane (context inspector: clip / track / key / marker)
│                                    #preview-pane     (Image bound to preview RT + preview toolbar)
├── TimelinePane.uxml            — #track-headers (ListView) | #ruler (TimeRulerElement)
│                                  #lanes (ScrollView of TrackLaneElement, custom VisualElement per track)
│                                  #playhead (PlayheadElement, generateVisualContent painter)
├── ClipEditor.uss               — all styling; USS variables for both editor themes
└── Elements/                    — TrackLaneElement, KeyElement, TimeRulerElement, PlayheadElement,
                                   EventLaneElement (markers), ValidationBadgeElement
```

No IMGUI anywhere (`OnGUI`, `GUILayout`, `Handles` are forbidden in package Editor code; the packaging conformance test greps for them, §8 M6). Custom drawing (key diamonds, ruler ticks, playhead) uses `generateVisualContent` mesh/painter APIs. Layout math lives once, in the elements — the audit's draw/hit-test drift bug class (duplicated rect math) is designed out by using the element tree's own layout for hit-testing.

### 7.3 Preview strategy

**Decision (audit open question 10): no ECS world, no play mode, no baking.** The preview is a **GameObject mirror** driven by the exact runtime math:

1. On selection, `PreviewPlaybackDriver` builds a **transient `BlobAssetReference<ClipRegistryBlob>`** from the edited SOs via the same `ClipRegistryBuilder` the Baker uses (rebuilt debounced on any edit, ~µs for a clip set; disposed explicitly — transient blobs are the one manually-owned blob, and only in the editor).
2. `PreviewRigMirror` instantiates a hidden preview instance — the `ActorAuthoring` prefab when one is assigned, else a flat auto-rig of unit quads from `RigAsset` targets — inside a `PreviewRenderUtility` scene.
3. A 30 Hz editor tick samples via `ClipSampler.SamplePose/CompositeLayers` (the runtime functions, §5.11) at the scrubbed/playing time — including layer-stack preview mode (multiple clips on multiple layers, the audit's absorbed preview mode) — and applies poses to the mirror transforms + `MaterialPropertyBlock` writes for `_ImageIndex`/`_AtlasFrame`/`_VatFrameA/B`/`_VatBlend` (MPBs are fine here — preview GameObjects don't use BRG).
4. `PreviewRenderUtility` renders to a RenderTexture shown in `#preview-pane`; orbit/zoom via pointer manipulators; repaint capped at 30 Hz (absorbing the audit's 20 Hz throttle lesson).

Why this beats an editor ECS world: Entities Graphics in a non-default editor world is unsupported-territory; baking-in-edit-mode is exactly the dependency Q10 asks to avoid; and **visual parity is guaranteed by construction** because the pose math is the runtime's own Burst functions and the pixels go through the shipped shaders. What preview *cannot* show — BRG batching itself — is a profiling concern, not an authoring one; the live-SO-sampling authoring loop (zero rebake — the audit's "single best idea") is preserved, upgraded from a divergent managed sampler to the real one.

### 7.4 Undo & multi-select

- All field edits go through `SerializedObject`/`SerializedProperty` bindings (UI Toolkit `BindProperty`) — undo, dirtying, and prefab-override handling for free.
- Gesture edits (key drag, marker drag, box-select move, paste, delete): `Undo.RecordObject(clipAsset, actionName)` **before** mutation, one `Undo.IncrementCurrentGroup` per gesture with `Undo.SetCurrentGroupName` + `CollapseUndoOperations` on pointer-up — a drag is one undo step. This closes the audit's partial-Undo gap (drag/inspector edits were SetDirty-only).
- Multi-select: selection model = `HashSet<KeyAddress { trackKind, trackIndex, keyIndex }>`; operations iterate the set inside a single undo group. Cross-asset multi-select is out of scope for v1 (documented; per-asset selection only).
- `Undo.undoRedoPerformed` → rebuild transient blob + repaint (preview always reflects undo state).

### 7.5 Thumbnails / icons

Custom editors override `RenderStaticPreview`: `ClipAsset` renders the preview mirror at `normalizedTime = 0.4` (mid-motion reads better than frame 0) through `PreviewRenderUtility`; `ClipSetAsset` renders its rig's rest pose; `VatTextureSetAsset` returns a downsampled copy of its primary texture. Static package icons (`.png` under `Editor/Icons/`, referenced via the asmdef-safe `EditorGUIUtility.TrIconContent` path) for windows and asset types.

### 7.6 Validation surfacing

One source of truth — `ClipValidation` (§3.5) — surfaced three ways: (a) inline `HelpBox`-equivalent UI Toolkit elements in inspectors, per offending row; (b) the Clip Editor toolbar badge (`ValidationBadgeElement`: error/warning count, click = popover listing messages, click message = focus offending key/track); (c) bake failure messages carry the same codes + asset paths, so an error seen in CI text matches what the editor shows.

---

<a name="s8"></a>
## 8. Integration contracts

Each module lists: OWNS (types it implements — exclusive write access), EXPOSES (the surface others compile against), DEPENDS (allowed references), ACCEPTANCE (tests + evidence that gate its Definition of Done). Types are specified in the referenced sections; a build agent needs only this section plus the referenced sketches. **No module may reference another module's internals — only its EXPOSES list.** Property names in §6.2 and validation codes in §3.5 are shared normative tables; changing either requires updating this document first.

### M1 — Authoring & Data (asmdef: Authoring, plus identity structs in Runtime/Identity)

- **OWNS:** `RigAsset`, `RigTargetDefinition`, `LayerDefinition`, `MirrorPair`, `ClipAsset`, `TransformTrack`, `TransformKey`, `SpriteTrack`, `SpriteKey`, `EventMarker`, `VatClipSource`, `ClipSetAsset`, `VatTextureSetAsset`, `VatClipRange` (§3.1–3.3); `ClipId`, `TargetId`, `StableIdUtility` (§3.4); `ClipValidation` + `ValidationMessage` + `ValidationCode` + `ValidationSeverity` + `ValidationStage` + `ClipValidationException` (§3.5); `ClipRegistryBuilder` (SO graph → `BlobBuilder`-populated `ClipRegistryBlob` + content hash, implementing §4.2/§4.5 exactly).
- **EXPOSES:** all of the above types public (asset fields public per sketches; `stableId` fields internal with `[InternalsVisibleTo]` for Editor + Tests asmdefs); `ClipRegistryBuilder.Build(ClipSetAsset, out BlobAssetReference<ClipRegistryBlob>, out Unity.Entities.Hash128 contentHash)`; `ClipRegistryBuilder.TryComputeContentHash(ClipSetAsset, out Unity.Entities.Hash128 contentHash)` and `ClipRegistryBuilder.SchemaVersion` (**amendment A16**, C2 gate — the dedup key without a blob to own, so a baker can probe the `BlobAssetStore` before deciding to build; without this on the EXPOSES list M2's baker cannot legally call the API §4.5 documents it around, and the canonical `TryGet`/build/`TryAdd` pattern is unreachable without either leaking a discarded blob or hand-disposing one the store is documented to own); `IStableIdMintReporter` with `HasUnpersistedStableId` / `MarkStableIdPersisted` (**amendment A16** — the §3.4/A14 contract by which the authoring layer reports a minted id for the editor layer to persist; M5 is its intended consumer); `ClipValidation.ValidateClip / ValidateSet / ValidateRig`; `ValidationMessage` with its `assetContext` object, `ValidationCode`, `ValidationSeverity`, `ValidationStage`, and `ClipValidationException` with its `Messages` list (**amendment A16** — a consumer cannot act on a finding or catch a failed bake without these).
- **DEPENDS:** Runtime asmdef (blob structs, enums, id structs), Unity.Entities, Unity.Collections, Unity.Mathematics. **No UnityEditor.**
- **ACCEPTANCE (EditMode):** id auto-assignment on creation; id survives rename/reorder/move (serialize→deserialize fixture); every V-code in §3.5 has a fixture that triggers exactly it; builder determinism (§4.5 point 4); builder canonical ordering (shuffled input → identical hash); builder rejects V-errors by throwing `ClipValidationException` listing codes.

### M2 — Baking (asmdef: Authoring/Baking + Editor/VatBaking)

- **OWNS:** `ActorAuthoring`, `StartingLayerState`, `ActorBaker`, `RigTargetAuthoring`, `RigTargetBaker`, `RigBindingBakingSystem`, and the two bake-time-only entity types the pair of bakers uses to talk to that system, `RigPartBakeLink` and `ActorBakeFailed` (both `[BakingType]`, §4.1); `AuthoringPathHash` and `AuthoringPathText` (the bake-stable path hash of A18 and the diagnostic path text of A21, split so the text renderer carries no `Unity.Entities.Hybrid` dependency and stays reachable from the EditMode suite, whose §1.3 reference list does not include it); `VatTextureBaker`, `VatBakeSettings`, `VatBakeInput`, `VatBakeResult` (§4.7); the `VatBakeWindow` UI shell is M5's, but its backend API is M2's.
- **EXPOSES:** the two authoring MonoBehaviours (inspector-facing fields: `ActorAuthoring { ClipSetAsset clipSet; List<StartingLayerState> startingLayers; SampleSettings sampleOverride; bool addDistanceLod; }`, `RigTargetAuthoring { RigAsset rig; uint targetStableId; bool useKindOverride; TargetKind kindOverride; int restSliceIndex; int vatDrivingLayerIndex; Material expectedMaterial; }`); `VatTextureBaker.Bake(VatBakeInput, out VatBakeResult)` headless API; component/buffer layouts it must produce on baked entities are **M3's types** — M2 writes them per §4.1/§5.2 but does not define them. `RigPartBakeLink` and `ActorBakeFailed` are **internal** (corrected at C3 Gate 4). They were briefly public on the stated grounds that "Entities requires baking types to be reachable from the system that queries them" — which is not a constraint here: writer, reader and the querying job all live in the Authoring assembly, the job is itself internal, and the test assemblies reach both through the contracted `InternalsVisibleTo`. They carry no contract for a consumer and never reach a built entity scene, so they are not part of the public surface this package must support.
- **DEPENDS:** M1 (assets, builder, validation), M3's public components, Unity.Entities(+Hybrid); VatTextureBaker additionally UnityEditor (Editor asmdef only).
- **ACCEPTANCE:** (PlayMode/baking tests) baking an `ActorAuthoring` prefab yields the §5.2 root archetype exactly (assert component-by-component incl. enableable initial states: `RigBindingUninitialized` enabled, `AnimationCommandPending`/`AnimEventsPending` disabled, `AnimVisible` enabled, `BoundsDirty` enabled); **`ActorRestBounds` is present and holds the actor-space union of every bound part's rest pose scaled by its `boundsExtents` (amendment A13) — a fixture rig with a part offset well away from the origin must produce a box that contains it, which is the case that fails if `offsetBounds` is mistaken for actor space**; two actors sharing a set share one blob (reference equality via content hash); part entities carry `RigPartBinding` with correct dense indices for a 3-target fixture rig; unknown-target part logs error and is skipped without failing the bake; a material↔texture-set mismatch fixture logs exactly one warning from `RigTargetBaker` (§4.4). (EditMode) `VatTextureBaker` on a procedural 2-bone skinned cylinder: texture dimensions per §4.7 layout math; sampled matrix at (bone 1, frame k) reproduces the clip's transform at t = k/fps within half-precision tolerance; loop-safe clip's last frame equals frame 0 bit-exactly; `sourceHash` changes when any input changes; zero-bone input → `VatBakeResult.failed` with message (never throws past the API); vertex flavor row-wrapping addressing round-trips for vertexCount > width.

### M3 — Runtime (asmdef: Runtime)

- **OWNS:** everything in §5.2 (components, buffers, enableables, singletons), §4.2 blob structs, §5.1 system groups, all systems (`RigBindingSystem`, `CommandApplySystem`, `PlaybackTimeSystem`, `EventEmissionSystem`, `TransformSampleSystem`, `TransformApplySystem`, `SpriteMaterialSystem`, `VatMaterialSystem`, `RenderBoundsUpdateSystem`, `AnimLodDistanceSystem`, `ConfigBootstrapSystem`), `ClipSampler`/`EventWrapMath`/`ClipRegistryUtil` (§4.3, §5.11), `AnimationCommandUtil`/`PlaybackQuery`/`ToolkitWorldControl` (§5.4), material-property components (§6.2 rows), enums (`LoopMode { UseClipDefault, Once, Loop, PingPong }`, `AnimTechnique`, `TargetKind`, `TrackBlendOp`, `AnimatedChannels`, `SpriteFrameMode`, `VatFlavor`, `CommandKind`, `PlaybackFlags`, `Interpolation`, `ReservedEventKeys`).
- **ADDENDUM (amendment A20, C3 gate, 2026-07-30 — product-owner approved):** `SampleSettings` carries `[System.Serializable]`. §8 M2 exposes it directly as `ActorAuthoring.sampleOverride`, an inspector-facing field, and Unity does not persist a custom struct without the attribute. It has no runtime effect and does not change the component's layout, but it is a change to an M3-owned type made for M2's benefit, so it is recorded here rather than left as an unexplained attribute.
- **EXPOSES:** all component/buffer/singleton types, both static API classes, `ClipSampler` (for M5 preview + tests), the system group types (for host ordering attributes), `ToolkitWorldControl.SetEnabled`.
- **DEPENDS:** Unity.Entities, Unity.Entities.Graphics, Unity.Burst, Unity.Collections, Unity.Mathematics, Unity.Transforms. Nothing else. **No UnityEditor, no Authoring.**
- **ACCEPTANCE:** (EditMode, pure functions) all five easings; loop/clamp/pingpong time mapping incl. negative speed; wrap-correct event crossing (single wrap, multi-wrap on large dt, reverse, marker exactly at 0 and at 1); bottom-up layer composition — Override channel masking, Additive-over-lower-layers (a fixture reproducing audit Q3's scenario asserts the *documented* semantics), blend-weight lerp; multi-track same-target applies all tracks; single-frame clip returns its only pose at every t; binary-search resolve hit/miss. (PlayMode, World tests) Play → layer active, correct clipIndex; blend: mid-blend pose is the lerp of both samples; queue promotes on finish with crossfade; Stop fade-out deactivates; `ClipFinished` emitted exactly once per Once-completion; `ClipResolveFailed` on unknown id, layer stays inactive; events cleared next frame, `AnimEventsPending` toggles correctly; ECB-instantiated actor re-binds parts (RigBinding) and animates; `RenderBounds` updates on clip change to the blob's `offsetBounds` union translated by `ActorRestBounds` via `BoundsDirty` (enabled by Play/queue-promotion/finish/blend-completion, disabled after the bounds write; a frame with only time advance leaves `BoundsDirty` disabled and `RenderBounds` untouched — asserted explicitly); `AnimVisible` disabled → `TargetPose` frozen while `time` keeps advancing; re-enable → next-frame refresh; LOD 2 mid-blend swap → blend completes on schedule (§5.10); sample-rate phase spreads two actors onto different sample frames. Burst gate: all systems compile under Burst with safety checks on (no `BC` errors in test run logs).

### M4 — Shaders (Shaders/ folders; no asmdef)

- **OWNS:** every file in §6.1; the normative property table §6.2 (jointly with M3, which owns the C# side of each row).
- **EXPOSES:** the include-function signatures (`VatBoneSkin`, `VatVertexFetch`, `BillboardTransform(positionOS, pivotOS, billboardParams, cameraPositionWS)`, `SliceUV`, `AtlasFrameUV` — exact parameter lists frozen in `shader-contract.md`, including the normative billboard facing rule: `_WorldSpaceCameraPos` only, `UNITY_MATRIX_V` forbidden, §6.3), the subgraph assets, the three reference graphs, the hand-written crowd shader, and the property/material-slot names.
- **DEPENDS:** URP 17.5 shader library only. No C#.
- **ACCEPTANCE:** all shaders compile for the URP target with zero warnings-as-errors; each reference graph's generated code contains the `UNITY_DOTS_INSTANCING_START` block with exactly the §6.2 properties (verified by a grep-style EditMode test over `ShaderUtil`-compiled output or the saved generated code); vertex displacement present in ShadowCaster/DepthOnly/DepthNormals/MotionVectors of both the generated graphs and the hand-written shader (grep the displacement function name per pass); **human-verified in-editor** (§11.4): VAT playback visually matches the source clip side-by-side, shadows follow displacement, billboard modes 0–3 behave **and a billboarded quad's shadow re-orients with camera orbit, not with the light** (the §6.3 facing contract, observed), slice + atlas flipbooks animate, one material + N actors = 1 BRG batch in the Rendering Debugger (screenshot evidence).

### M5 — Editor UI (asmdef: Editor)

- **OWNS:** `ClipEditorWindow` + all §7.2 elements/UXML/USS, `PreviewPlaybackDriver`, `PreviewRigMirror`, `VatBakeWindow` (UI shell), all custom inspectors (§7.1), thumbnail renderers (§7.5), `StableIdRemapUtility` + duplicate-id `AssetPostprocessor` (§3.4), clip utilities (Mirror-Clip using `RigAsset.mirrorPairs` — user-configured table, honoring the audit's Absorb condition — and Duplicate-Clip which mints a fresh `ClipId`).
- **EXPOSES:** menu items and asset context actions only — no public API other classes depend on. (`VatBakeWindow` calls M2's `VatTextureBaker.Bake`; the timeline calls M1's validation + builder and M3's `ClipSampler`.)
- **DEPENDS:** M1, M2 (headless baker API), M3 (`ClipSampler`, enums), UnityEditor, UI Toolkit.
- **ACCEPTANCE:** (EditMode UI tests where automatable) window opens without exceptions on: empty selection, clip with 0 tracks, 200-track stress clip; every gesture edit produces exactly one undo step and `Undo.PerformUndo` restores byte-identical serialized asset (serialize-compare fixture for: key drag, key add, key delete, multi-key move, paste, marker drag); duplicate-asset import triggers id regeneration warning; Mirror Clip round-trips (mirror twice = original values within float tolerance) using a fixture mirror-pair table; preview transient blob rebuilds on edit and disposes on window close (no blob leaks — assert via `BlobAssetReference.IsCreated` tracking). **Human-verified:** timeline UX walkthrough (keyboard map parity with the audit §4 feature list), preview parity spot-check vs play-mode runtime for one fixture clip.

### M6 — Packaging (package root)

- **OWNS:** `package.json`, all asmdefs (§1.3), `Samples~` (three samples §1.2 incl. `ToolkitCameraSync`), `Documentation~` (four docs §1.2), CHANGELOG/LICENSE/README, and the **packaging conformance tests** (in Tests.EditMode): (a) every asmdef's reference list matches §1.3 exactly; (b) Editor asmdef is Editor-platform-only; (c) no file outside Editor/ or Tests/ references `UnityEditor` (regex scan); (d) no package file references `StitchPunk.` game namespaces or `Assets/` paths; (e) no `OnGUI`/`GUILayout`/`Handles.` in Editor sources; (f) Samples compile via their own asmdefs.
- **EXPOSES:** the installable package.
- **DEPENDS:** everything (assembles all modules).
- **ACCEPTANCE:** conformance tests green; package passes Unity's package validation suite; a **clean Unity 6000.5 project** (no Stitch Punk code) with only the §1.1 dependencies compiles the package with zero errors/warnings and all EditMode tests pass there (the standalone-ness proof); samples import and their scenes open without missing references. Human evidence: player build (Windows) of the `VatCrowd` sample scene completes and contains no Editor asmdef (build report screenshot/log).

---

<a name="s9"></a>
## 9. Module build plan (Phase C)

Dependency-ordered; each step gates on its Definition of Done (DoD = module acceptance criteria from §8 that are implementable at that step, plus listed evidence). "Reviewer evidence" = what the adversarial reviewer receives.

| Step | Module slice | Builds on | Definition of Done | Reviewer evidence |
|---|---|---|---|---|
| **C0** | M6 skeleton: package.json, folder tree, all 5 asmdefs (§1.3), empty test fixtures, conformance tests (a)–(e) | — | Package compiles empty in host repo; conformance tests green | Test Runner screenshot/log; asmdef file review |
| **C1** | M3 data slice: enums, `ClipId`/`TargetId`, blob structs (§4.2), component/buffer definitions (§5.2), `ClipSampler` + `EventWrapMath` + `ClipRegistryUtil` pure functions | C0 | M3 EditMode pure-function acceptance list green (easings, wrap, composition, pingpong, resolve) | EditMode test run (list of test names + pass) |
| **C2** | M1 complete: SOs, identity, validation, `ClipRegistryBuilder` | C1 | M1 acceptance green incl. determinism + id-stability fixtures | EditMode run; determinism test output showing identical hashes |
| **C3** | M2 entity-baking slice: authorings, bakers, `RigBindingBakingSystem` | C2 | M2 baking acceptance green (archetype assertions, blob sharing, dense-index resolution) | PlayMode/baking test run |
| **C4** | M3 systems slice: groups + all systems; transform + flipbook techniques end-to-end; events; bounds; LOD; visibility | C3 | M3 PlayMode acceptance green; Burst-clean; a host-shaped smoke scene (subscene with one cutout actor) animates in this repo | Test run; Editor.log grep clean of `error CS`/`BC`; user-confirmed on-screen clip playback |
| **C5** | M4 slice 1: includes, `ToolkitInstancing.hlsl`, subgraphs, `ToolkitSpriteLit`, billboard; M3's sprite/billboard property components wired | C4 | M4 compile + instancing-block + pass-grep tests green for sprite graph; billboard modes human-verified | Generated-code excerpts; screenshots (billboard modes, flipbook anim, batch count) |
| **C6** | M2 VAT slice (`VatTextureBaker`) + M3 `VatMaterialSystem` + M4 slice 2 (VAT includes/graphs/crowd shader) — bone flavor first, vertex flavor second | C5 | M2 VAT acceptance green (procedural-mesh fixtures); M4 VAT tests green; `VatCrowd` scene: 1000 instances, 1 batch, human-verified vs source clip | Test runs; Rendering Debugger screenshot; side-by-side clip comparison screenshot |
| **C7** | M5 complete: Clip Editor, preview, VAT window, inspectors, id tooling, utilities | C6 | M5 acceptance green (undo fixtures, blob-leak check); human UX walkthrough signed off | Test run; undo serialize-compare output; user walkthrough notes |
| **C8** | M6 completion: samples, Documentation~, clean-project verification, player-build check, **naming conformance check (name finalized 2026-07-27: DOTS Animation Toolkit / `com.stitchpunk.dotsanimationtoolkit`, §1.1)**, version 1.0.0 | C7 | Full M6 acceptance incl. clean-project run and Windows player build of VatCrowd | Clean-project test log; build report; final package validation output |

Rules: no step starts before its predecessor's DoD evidence is filed; any §8 contract change discovered mid-build is a **stop-the-line** doc amendment (this file), not a silent divergence; every step lands with its tests in the same change set (tests are not a trailing phase).

---

<a name="s10"></a>
## 10. Answers to the audit's open questions

1. **Clip identity** — **Decided: 64-bit random stable ids** (`ClipId`, §3.4): generated from folded GUIDs at asset creation, serialized on the asset, never name-derived; blob carries a sorted id array; commands resolve to dense indices once per command via binary search. Not generics-over-user-enums: a package cannot force a closed enum universe on content teams, and generic component types would fragment the system/query surface. Migration for the ~21 host clips: converter tool + generated constants class, §13. *Partial overrule of the audit's Absorb on the pre-filled-slot blob:* placeholder-per-enum-value slots are impossible without a closed enum; graceful degradation is preserved via `ClipResolveFailed` events + inactive layers instead (§5.4).
2. **Blend-in/out** — **Decided: in scope for v1.** Per-layer crossfade (current + previous clip with weights, §5.4/§5.6); the authored `allowBlendIn/Out` become real `defaultBlendIn/Out` seconds. VAT crossfades via dual-sample `_VatFrameB`/`_VatBlend` (§6.4); flipbook frames never blend — nearest wins at the blend midpoint (snap, documented). Rationale: "every transition pops" is a disqualifying defect in a commercial animation product; the data fields already exist and the audit flagged them as dead weight otherwise.
3. **Additive semantics** — **Decided: additive over the composited lower layers** (the documented intent), implemented by bottom-up layer iteration replacing the claim mask (§5.6). Rationale: additive-over-rest makes an "add a bob on top of walk" layer ignore the walk — the observed behavior is a bug class, not a feature. Existing host clips authored against the accidental semantics are re-verified during migration (§13); the audit found no shipped content that depends on the difference on purpose.
4. **Scale/flip** — **Decided: `PostTransformMatrix`** (§5.6): keeps `LocalTransform.Scale` uniform (child transforms unpolluted), and the authoring already bakes `NonUniformScale` flags for exactly this. Mirror Clip becomes visually real; the migration pass (§13) reviews clips whose flips were invisible-by-accident before turning them on.
5. **Completion signaling** — **Decided: all three, layered:** `PlaybackFlags.Finished/FinishedThisFrame` on the layer (poll), the reserved `ClipFinished` event in `AnimEventOutput` (push, gated by `AnimEventsPending`), and `PlaybackQuery.NormalizedTime` (query). The duration-0/`float.MaxValue` hack is deleted; combat-style consumers subscribe to `ClipFinished` or hit-frame markers (§5.5).
6. **Event system scope** — **Decided: generalized typed markers** (`EventMarker { eventKey, intParam, floatParam }`, §3.2/§5.5). Sound, hit-frames, VFX, footsteps are all user keys; the package owns emission (wrap-correct math absorbed verbatim), games own consumption. If Stitch Punk moves combat to hit-frame markers, `AttackRequestSystem`'s delta-time timing migrates to an event consumer — game-side change, §13.
7. **Direction/8-way facing** — **Decided: data + helpers in the package, selection logic game-side.** The package ships `MirrorPair` tables (authored per rig) and the Mirror Clip utility; a directional *clip-set* convention (N clips + a `PlaybackQuery`-style pure helper for pick-nearest-facing) ships as part of the `CutoutCharacter` sample, not as a runtime system. Rationale: facing is driven by movement/AI state the package cannot know; the dead host system showed exactly this coupling failing. Deferred beyond v1: a first-class directional-variant asset — explicitly justified as sample-provable without new runtime surface.
8. **Visibility boundary** — **Decided: package-owned enableable `AnimVisible` + external providers** (§5.9). Default-enabled, self-healing on re-enable, logic never gated. The host bridges its `CameraVisible` with a two-line system (§13). The package ships no culling provider of its own in v1 beyond the optional LOD distance system (which is LOD, not culling).
9. **Frame-rate quantization** — **Decided: keep the retro look as an option, per-actor, phase-offset.** `SampleSettings { rateHz, phase01 }` (§5.6) with world default in `AnimationToolkitConfig`; per-entity phase from a spawn-time hash kills the all-rigs-same-tick spike. Time itself is never quantized — only sampling frequency (matches shipped runtime behavior; the editor-only normalizedTime quantization is dropped as the divergence it was).
10. **Editor preview world** — **Decided: no ECS world at all** — GameObject mirror + transient blob + the runtime `ClipSampler` (§7.3). Parity is structural (same math, same shaders); baking-in-edit-mode and scene furniture (`GameSceneTag`, `GameSettings`, play mode) are all eliminated.
11. **Shader ownership** — **Decided: both.** The package ships reference graphs/subgraphs/includes **and** the normative property contract (§6.2, mirrored in `Documentation~/shader-contract.md`) so hosts can keep their own graphs (Stitch Punk keeps its cel-shaded graphs and only consumes property names). Motion vectors: displacement in the MV pass is in scope (correct depth); deformation-accurate *velocity* ships default-off behind `_VAT_DEFORMATION_MV` (§6.3, risk R6).
12. **`_UseAltShape`** — **Decided: game concern.** It is a Stitch Punk design-system switch, not an animation primitive. The package documents the `[MaterialProperty]`-component-per-named-property pattern (one page in `shader-contract.md`); the host adds its own component (§13 lists it as host work).
13. **Bounds** — **Decided: yes** — conservative per-clip bounds at bake (§4.6), applied on clip change by `RenderBoundsUpdateSystem` via the `BoundsDirty` enableable (§5.8). VAT bounds are exact (measured from baked frames); transform bounds are conservative from key extremes ⊕ authored target extents.
14. **Tests** — **Agreed and expanded:** the sampler is pure Burst functions with EditMode coverage from day one (§5.11, §11); determinism, VAT layout math, and undo integrity are additionally test-gated; tests land with their module, never after (§9 rules).

---

<a name="s11"></a>
## 11. Test strategy

### 11.1 Edit-mode (fast, no World unless noted)

- **Pure math:** easings, loop/pingpong/negative-speed time mapping, event wrap crossings (incl. multi-wrap, reverse, boundary markers at t=0/t=1), layer composition semantics (Override masks, Additive-over-lower, blend lerp), multi-track ordering, binary-search resolve. (M3, §8.)
- **Identity & data:** id generation/stability/duplication handling; all validation codes V01–V14 (positive + negative fixtures). (M1.)
- **Determinism:** `ClipRegistryBuilder` double-build + shuffled-input build → identical content hash and blob streams; `VatTextureBaker` double-bake → identical `sourceHash` + texel data hash. (M1/M2.)
- **VAT layout math:** bone/vertex texel addressing round-trips; loop-frame duplication; precision tolerance vs source clip on a procedural 2-bone rig; zero-bone and oversize-width failure paths. (M2.)
- **Editor integrity:** one-undo-step-per-gesture with serialized round-trip equality; preview blob lifecycle (no leaks); mirror-clip involution. (M5.)
- **Packaging conformance:** asmdef references, editor-only restriction, no-UnityEditor-outside-Editor, no-IMGUI, no-host-namespace scans. (M6.)

### 11.2 Play-mode (World integration)

Command→state machine transitions, blend/queue/stop flows, event emission + clearing + enableable gating, spawn re-binding after ECB instantiate, RenderBounds-on-clip-change via `BoundsDirty`, visibility freeze/refresh, LOD behaviors, sample-rate phase spread, Burst-clean system compilation — the M3 acceptance list (§8) verbatim. Baking tests (M2 acceptance) run here too.

### 11.3 Product-owner edge cases (explicit fixtures, all automated)

| Edge case | Test |
|---|---|
| Empty clip (no tracks/events) | Validates with V10 warning; bakes; playing it holds rest pose; `ClipFinished` fires at `duration` for Once. |
| Single-frame clip | 1-key tracks return that pose at every t (EditMode); VAT `frameCount = 1` clamps addressing, no out-of-range row read (EditMode math + PlayMode property values). |
| Zero-bone mesh | `VatTextureBaker` fails soft with message (bone flavor); vertex flavor proceeds. |
| LOD swap mid-blend | Blend timer advances through the swap; final weight = 1 exactly at `blendDuration`; no pose discontinuity beyond the documented LOD-2 snap. |
| Hot reload of authoring assets | Editor: transient preview blob rebuilds on `Undo.undoRedoPerformed`/edit (M5 test). Entity path: modifying a `ClipAsset` retriggers `ActorBaker` via `DependsOn` and produces a new content hash (baking test asserts hash change after a scripted field edit); live worlds keep the old blob until rebake — documented behavior, asserted not-crashing. |

### 11.4 Human-verified in-editor (the documented handoff)

Claude/CI cannot see pixels; the following are verified by the human per step (§9 evidence column) with screenshots into `Docs/AnimationToolkit/PhaseC_Evidence/`: on-screen playback parity (preview vs play mode vs source clip for VAT), shadow/depth correctness of displaced geometry, billboard modes under camera orbit, BRG batch counts in the Rendering Debugger, editor UX walkthrough (keyboard map, drag feel), player-build smoke of `VatCrowd`, and any Switch-hardware verification (format support is spec-safe per §4.7, but device memory/perf numbers are hardware-only — flagged as such in `platform-notes.md`).

---

<a name="s12"></a>
## 12. Risks & documented limitations

| # | Risk / limitation | Impact | Mitigation |
|---|---|---|---|
| R1 | **VAT crossfade blends matrices/positions linearly** — interpolating two unrelated poses distorts volume (no per-bone quaternion slerp on the GPU path); long crossfades between very different clips look rubbery. | Visual quality | Documented caveat with guidance (≤ 0.25 s crossfades between related clips; hard-cut for unrelated); `_VatBlend = 0` fast path costs nothing when unused; transform-track rigs blend properly and are the recommended technique where blend quality is paramount. |
| R2 | **Half-float precision** — RGBAHalf bone/position texels quantize to ~0.0005–0.001 within ±2 units; rigs much larger than a few meters, or far-from-pivot verts, show stepping. | Visual quality on large creatures | Object-space storage keeps magnitudes small (§4.7); per-set `RGBAFloat` opt-in; baker logs a warning when any baked value exceeds ±32 (precision cliff heuristic). |
| R3 | **Switch memory/bandwidth** — dual-clip, 4-influence bone VAT = up to 48 texel loads/vertex; vertex-flavor textures reach tens of MB for dense meshes. | Switch perf/memory | 2-influence bake option (default recommendation for crowds in `platform-notes.md`); crossfade branch skipped when `_VatBlend == 0`; uncompressed-format memory table per mesh size published in `platform-notes.md`; vertex flavor flagged as PC/console-first. |
| R4 | **Batching pitfalls** — any per-instance texture or material swap splits BRG batches; a host writing colors without linear conversion ships wrong colors. | Perf / correctness | §6.6 normative rules; conformance is structural (no per-instance texture properties exist in the contract); sRGB rule documented and carried from the audit. |
| R5 | **Blob schema evolution** — future field changes silently mis-read old baked scenes. | Data corruption | `schemaVersion` stamped in blob + checked by `ConfigBootstrapSystem`-adjacent validation at first access in dev builds (`UNITY_ASSERTIONS`), with a clear "rebake subscenes" error. |
| R6 | **Motion-vector velocity for VAT** is transform-derived in v1 (deformation velocity off by default, §6.3) — TAA/motion-blur ghosting on fast deforming crowds. | Visual quality under TAA | Documented limitation; `_VAT_DEFORMATION_MV` feature designed (per-instance prev-frame props) and scheduled post-1.0; workaround guidance (reduce TAA feedback for crowd layers). |
| R7 | **PlaybackLayer element size** (~88 B × 8 layers) exceeds comfortable `InternalBufferCapacity` in-chunk budgets on huge crowds. | Chunk occupancy | `[InternalBufferCapacity(8)]` is measured in C4 against a 10k-actor stress scene; falls to heap-backed buffers gracefully; if chunk pressure shows, the C4 DoD includes splitting blend state into a parallel buffer — decided by measurement, not speculation. |
| R8 | **Editor preview ≠ BRG path** — preview renders via GameObjects, so BRG-specific defects (instancing macro bugs) don't show in preview. | Late defect discovery | The play-mode smoke scenes (C4/C6 DoD) are the BRG gate; preview is explicitly an authoring aid, stated in §7.3. |
| R9 | **One-frame event latency** for consumers ordered before the package group (§5.5). | Gameplay timing nuance | Documented contract; hosts needing same-frame events order after the group (Stitch Punk's combat consumer does exactly this, §13). |
| R10 | **Additive/scale semantic changes vs host content** (Q3/Q4 fixes change on-screen results of existing clips). | Host migration cost | Contained to the migration pass (§13): clip-by-clip visual review with the old tool still runnable side-by-side until cutover. |
| R11 | **The crossfade source emits no event markers** (amendment A27). A clip crossfaded out stops firing its markers the moment it is replaced, so a footstep or hit frame in the last fraction of an outgoing clip never fires. | Gameplay timing on long crossfades | Deliberate: the alternative makes every crossfade emit two overlapping streams of gameplay events for a clip the game has already superseded. Blend durations are authored short (R1 recommends ≤ 0.25 s), so the unfired window is small and bounded. A host that needs the outgoing clip's tail events plays a hard cut, or moves the marker earlier. |

---

<a name="s13"></a>
## 13. Stitch Punk migration appendix (host-side; ships in `Docs/`, never in the package)

Migration happens **after** Phase C, as host work. Old and new systems can coexist during it (different components, different groups).

### 13.1 Mapping

| Old (host) | New (package) | Action |
|---|---|---|
| `AnimationClipSO` (+ inline tracks/keys) | `ClipAsset` | One-shot converter (editor script, host-side): copies tracks/keys/easings; degrees kept (authoring stays degrees); `soundMarkers` → `EventMarker { eventKey = SoundEventKeys.For(SoundType), floatParam = 0 }` with a generated `SoundEventKeys` constants class (keys ≥ 16). |
| `AnimationType` enum | `ClipId` + generated constants class (`StitchPunkClips.Walk` …) | Converter assigns fresh ids; "Generate Clip Id Constants" replaces enum call-sites mechanically. The 8 dead Direction values and dead blink variants are simply not converted. |
| `AnimationTarget` enum (35 slots) | `RigAsset` targets ("Humanoid" rig asset) | Converter builds the rig from the enum, records enum→`TargetId` map for track conversion; mirror table from `AnimationClipUtilities`' hard-coded switch → `RigAsset.mirrorPairs`. |
| `AnimationLayerType` (7 layers) | `LayerDefinition` list (same order = same priorities) | Direct. |
| `AnimationLibrarySO` + `AnimationLibraryBakingSystem` + `AnimationLibrary(Reference)` | `ClipSetAsset` + `ActorBaker` blob | Delete after cutover (audit Replace verdict). |
| `SetAnimation`/`AnimationRequest`/`AnimationUtils.SetLayer` | `AnimationCommand`/`AnimationCommandPending`/`AnimationCommandUtil` | `BehaviorExecutionSystem`, `BehaviorInterruptSystem`, `PlayerAttackSystem` call-sites rewritten (same shape: layer + clip + speed + loop, now + blend). `AnimationType.None`-clears-layer becomes `Stop(layer)`. |
| `AnimationLayer` buffer | `PlaybackLayer` | State reads (e.g. `UnitAnimationAssignmentSystem`'s never-clobber-active-Action check) move to `PlaybackQuery`. |
| `UnitAnimationAssignmentSystem` | stays host-side (audit verdict), re-targeted to the new API | Rewrite against `AnimationCommandUtil` + `UnitLibraryBlob` storing `ClipId` instead of `AnimationType`. |
| `AnimationTimeSystem` duration-0 completion hack ↔ `AttackRequestSystem` | `ClipFinished` event / hit-frame `EventMarker` | Combat consumer system ordered **after** `AnimationToolkitSystemGroup` (R9); the stale-comment contract dies. |
| `BodyPart`/`BodyPartInfo`/`BodyPartInitSystem` (animation slice) | `RigPartRef`/`RigPartBinding`/`RigBindingSystem` | Host keeps `BodyPart` for design/ragdoll/socket concerns; animation reads move to package components. `CharacterRigAuthoring`'s animation slice moves to `ActorAuthoring`; `BodyPartAuthoring`'s to `RigTargetAuthoring` (both host bakers slim down, audit §6.10). |
| `CameraVisible` gating | `AnimVisible` | Two-line host bridge system in `GameManagerSystemGroup` after `CameraVisibilitySystem`: copy enabled-state root+parts. `CameraVisible` itself stays host-owned for non-animation presentation. |
| `BillboardSystem` + `Billboard` | `_BillboardParams` per-instance property (shader-side) | Host system deleted; death-freeze behavior: the host's death handler writes mode 3 + current yaw into `BillboardParamsProperty`. *(Overrules the audit's Absorb of the CPU system — mandated by product scope §4; behavior preserved via mode 3.)* |
| `ImageIndex` + `UpdateImageIndexSystem` + `ImageIndexOverride` | `SpriteSliceProperty` written by `SpriteMaterialSystem`; design changes write `TargetRestPose.restSliceIndex` | Delete two-hop path (audit Replace verdict). `DesignApplyUtil` retargets to `TargetRestPose` + tint components (tints are untouched — host-owned `_BaseColor`/`_Secondary`/`_Tertiary` keep working against package reference graphs or host graphs alike). |
| `GameSettings.animationFrameRate` | `AnimationToolkitConfig.defaultSampleRateHz` (+ per-actor `SampleSettings`) | Save-file field deprecated (audit Replace verdict). |
| Editor: `AnimationClipEditorWindow` + preview controller/systems/scenes (9 files) | `ClipEditorWindow` + package preview | Delete entire `Assets/_Scripts/Editor/AnimationEditor/` + both editor scenes after content team signs off on the new editor; this also removes the editor-code-in-builds leak for these files (the asmdef-wide fix remains separate host work). |
| `KeyframeSO`/`DOTSKeyframe` | — | Delete (audit verdict; orphaned). |
| `_UseAltShape` gap | host adds its own `[MaterialProperty("_UseAltShape")]` component per the documented pattern | Host work item (Q12). |

### 13.2 Cutover order

1. Install package; run converters (clips, rig, constants classes) — old pipeline untouched.
2. Author one pilot unit on `ActorAuthoring`; verify side-by-side vs old pipeline in the test scene, including the Q3/Q4 semantic-change review of converted clips (R10).
3. Rewrite the three request call-sites + `UnitAnimationAssignmentSystem`; add the `CameraVisible→AnimVisible` bridge and reorder the combat event consumer.
4. Flip remaining units; delete the old systems/components/SOs listed above; move anything historical to `Core/Unused/` per host rules; update `_Vault/Memories/Code/Systems_Animation.md`.
5. Only then: adopt VAT for crowds (new content, not a migration).

---


---

<a name="a42"></a>
## Amendment A42 (2026-08-09 — product-owner correction): bone tracks are authored in the Clip Editor, and bake to VAT

**The owner, on being shown a guide that said the opposite:** *"it definitely should be able to author bones, that's literally the whole point of what I've been having you build, an animation tool that allows me to mix all the different methods… it is meant to make me making these hybrid animations in one place easy."*

**This was stated at the outset and I narrowed it away.** The original framing was *"I do all animation regardless of type in one location then it gets baked/saved in a format that makes sense for, is bone to VATs, movements to position, flipbook, socket data, and index nudges for direction."* I later offered a choice between *one authoring surface* and *one composition surface*, recommended the latter, and built to it — then wrote `rigged-characters.md` asserting the Clip Editor "cannot author a rigged character's motion", which hardened a wrong call into documentation. The requirement never changed; my reading of it did.

### What was actually blocking it

`TransformKey` carries `float3 position`, **`float rotationZ`**, `float2 scale`. One rotation axis cannot express a joint orientation. That is real — but it is a property of *that key type*, not of the toolkit, and the fix is a new track kind rather than a redesign.

### The shape

**A new track kind, `BoneTrack`, parallel to the existing three.** Not an extension of `TransformKey`:

- Extending `TransformKey` with a quaternion and a `float3` scale would grow **every cutout key** — the format most clips use — to carry channels they never set, for a technique they never use. Blob size is the one cost a crowd actually pays per clip.
- The toolkit already separates track kinds by what they drive (`transformTracks`, `spriteTracks`, `vatTracks`). A fourth is the consistent move, and it means a cutout clip's on-disk layout does not change at all.

```
BoneTrack { string boneName; List<BoneKey> keys; }
BoneKey   { float normalizedTime; float3 localPosition;
            quaternion localRotation; float3 localScale; Interpolation interpolation; }
```

**Bones are named, targets are id'd** — the same asymmetry sockets already carry (A-socket note). A rig target is a row this package owns and can assign a stable id to; a bone lives in an imported hierarchy this package does not own, so the name is the only handle Unity offers. The consequence is identical: renaming a bone in the DCC tool breaks the binding, and the bake must **report** the unresolved name rather than silently baking a bone pinned to rest.

### Where it bakes

**Authored bone tracks are a second *source* for the existing VAT bake, not a second output format.** `VatTextureBaker` already walks a clip frame by frame, poses the hierarchy, and reads `bones[i].localToWorldMatrix`. Today the posing step is `AnimationMode.SampleAnimationClip`. With authored tracks it becomes "apply the sampled `BoneKey` values to the named bone transforms". **Everything downstream — matrix capture, texel layout, loop-safe duplication, socket sampling — is unchanged.**

That is what makes this tractable rather than a rewrite: the insertion point is one method, and the output is the same texture the imported-clip path already produces. A clip may therefore draw its bone motion from an imported `AnimationClip`, from authored `BoneTrack`s, or from both on different bones.

### Why this is not "rebuilding Blender"

It is worth being explicit, because the earlier recommendation leaned on this and it was the wrong objection. The goal is **not** a rigging suite — no weight painting, no IK solving, no constraint graph. It is *keyframing an existing skeleton on a timeline that also carries the flipbook, sprite, socket and event rows for the same character*. The value is entirely in the composition: a hit frame whose event marker, arm swing, cape VAT and weapon socket all sit on one timeline and scrub together. Doing that across two tools is exactly the friction this toolkit exists to remove.

Blender remains the better tool for authoring a walk cycle from nothing, and importing one stays fully supported. Both routes bake to the same texture.

### Build order

| Phase | Delivers |
|---|---|
| **B1** | `BoneTrack`/`BoneKey` authoring types and validation rules |
| **B2** | Bake path — `VatTextureBaker` poses from authored tracks; a clip may mix authored and imported bone sources |
| **B3** | Clip Editor — bone rows on the timeline, key editing through the context inspector, bone picker sourced from the rig's skinned prefab |
| **B4** | Preview — the mirror instantiates the rigged prefab and poses its bones, so bone rows scrub live rather than only after a bake |

### Correction (2026-08-09): authored bone tracks never reach the blob

B1 was originally specced to add a `BoneTrackBlob` to `ClipBlob`, bump `SchemaVersion` to 5, and re-record the golden content hash. **All of that is unnecessary, and specifying it was an error.**

Nothing at runtime samples a bone. `VatMaterialSystem` reads frames out of a texture; `TransformSampleSystem` reads transform tracks for cutout parts. A bone track's entire purpose is to *pose a skeleton at bake time* so `VatTextureBaker` can capture matrices — exactly the role an imported `AnimationClip` plays, and that never enters the blob either.

So authored bone tracks are **editor-time bake input**, in the same category as `vatSource`. The blob layout is untouched, the schema stays at 4, the golden hash stands, and `DataContractTests`' `ClipBlob` contract does not change.

The tell was there in A42's own framing — *"a second source for the existing VAT bake, not a second output format"*. A source does not need a runtime representation. Writing the phase table before following that sentence through is what added a schema bump nobody needed.

B4 is what closes the loop the owner asked for: **one timeline, scrubbed once, showing every technique at the same instant.** Until it lands, bone rows are authorable and bakeable but only previewable through a bake.

---

## Amendment A43 (2026-08-15 — product-owner correction): a flipbook index changes on its key, not between keys

§5.7 specified sprite-track sampling as **nearest-key**: of the two keys surrounding the playhead, whichever is closer wins. `ClipSampler.SampleSpriteTrack` implemented exactly that, switching at `linearWeight < 0.5f`.

**That is wrong, and the owner named the reason: index changes are hard updates that do not blend.**

Nearest-key is the right rule for a value being *approximated* — pick whichever sample is closer to the truth. But a frame index is not an approximation of anything. It is a discrete instruction that fires at a moment. Under nearest-key that moment was **the midpoint of the segment**, half a key-gap away from the key the author placed. On an evenly spaced flipbook every change lands half a step late, which reads as the whole animation running out of time with its own timeline — and worse, out of time with the transform and event rows on the same clip, which do land on their keys.

The rule is now: **the key at or before the time holds until the next key's own time is reached.** Before the first key, the first key holds. This is the same shape as `Interpolation.Step` on a transform key, which §5.7 already specified and which `SampleTransformTrack` already implemented — the two were inconsistent with each other, and the flipbook was the one that was wrong.

**Consequences:**

- `FindSpriteKeySegment` is replaced by `FindHoldingSpriteKey`, returning one index. A flipbook has no segment to interpolate across, so returning a surrounding pair was itself the shape of the mistake.
- `ClipSpriteEditing.FindEffectiveKeyIndex` carries the identical rule, so the Clip Editor's live index while scrubbing is the frame the runtime plays.
- **No data, blob, schema or hash change.** The authored keys are untouched; only their interpretation in time moves. No re-bake, no migration.
- **Existing clips play differently.** A frame that appeared at the midpoint between two keys now appears at the later key. A clip whose timing was tuned against the old behaviour will read as half a segment slower on each change.

**Cross-clip blending is deliberately untouched.** `LerpPose` still snaps the frame at the blend midpoint (§10 answer 2). That is a different situation: a crossfade between two clips has no key to land on, the two poses come from different timelines, and the midpoint is the only defensible boundary between them.

---

## Amendment A44 (2026-08-17 — product-owner directive): billboarding is a hierarchical, authorable rig feature

**The directive:** *"Before implementing billboard-space ragdoll physics, build billboarding into the animation system itself as an authorable, inheritable property of the rig hierarchy."* The ragdoll work (`_Vault/Tasks/Claude/AnimationRagdoll.md`) then consumes the resolved billboard frame as its gravity reference rather than recomputing facing — *"do not recompute facing or camera-relative orientation independently."*

A41 absorbed the host's `BillboardSystem` and generalised it to **one rotation on the actor root**. That was the right call for a layered cutout character and it stays right. A44 goes one step further: the actor root stops being the only place a billboard can live.

### What the host system actually does, and what carries forward

Recorded here because A44's whole obligation is to preserve it, and because §13.1 and A39 each described it only partially.

| Behaviour | Host `BillboardSystem` | Carried into A44 |
|---|---|---|
| Facing source | `Camera.main.transform.forward` — **screen-aligned**, every quad takes the same rotation | Yes; `ScreenAligned` is the default mode for a new billboard root |
| Target rotation | `quaternion.LookRotation(cameraForward, math.up())` | Yes, sign included — see the correction below |
| Space of the write | Converted into the parent's space via `parentTransform.InverseTransformRotation(...)`, then written to the **child's** `LocalTransform.Rotation` | Yes — and this is precisely the mechanism a nested root needs |
| Dead units | Freeze yaw, keep camera pitch, by decomposing the target into yaw and pitch and substituting the entity's *current* yaw | Yes; already `BillboardMode.FrozenYaw`, math lifted verbatim in A41 |
| Degenerate camera | Camera pointing straight up or down leaves the transform untouched rather than snapping | Yes |
| Visibility | Skips while the owning rig's `CameraVisible` is disabled | Yes, as `AnimVisible` |

**The host's system was already per-node, not per-actor.** Its `Billboard` component carries a `parentEntity` and it rotates a *child* into that parent's space. A44 is not inventing hierarchical billboarding — it is generalising the one level the host already had into arbitrary depth, with declared roots and inheritance.

### Correction: the shipped facing sign is inverted

`FaceCameraJob.ResolveFacing` returns `-cameraForward` for `ScreenAligned` and `cameraPosition - actorPosition` for the spherical modes, and `ToolkitBillboardFacing` in `ToolkitBillboard.hlsl` does the same. The host returns `+cameraForward`. Both then feed `LookRotation`, which maps **+Z** onto that vector — so the two are 180° apart.

Unity's `PrimitiveType.Quad`, which `CompositeActorBuilder` builds every sample part from, has its visible normal on **−Z**. For that mesh the host's sign is the one that presents the drawn face to the viewer and the package's is the one that presents its back. A44 adopts the host convention in both the CPU and HLSL paths, and states it once, normatively:

> **The facing vector handed to the basis builder is the direction the node's local +Z must point — *away* from the viewer.** `ScreenAligned` → `+cameraForward`. Spherical → `nodePositionWS − cameraPositionWS`.

This is a **visible change to any content already using `Full` or `ScreenAligned`**, which is why it is called out rather than folded in silently. It is also the one item in this amendment that cannot be settled by a test: whether a quad shows its face or its back depends on the mesh, and the package cannot see the host's meshes. `ShaderConformanceTests` and the CPU tests can only assert that the three paths **agree with each other**; the human-verified checklist (§11.4) gains "a billboarded part faces the camera rather than showing its back, on a `PrimitiveType.Quad`".

### The data model — on the rig asset, addressed two ways

Billboard configuration lives on `RigAsset`, alongside targets, layers, sockets and mirror pairs, so it travels with the rig and is shared by every actor instanced from it. It is the **first** such block — there are as yet no constraints and no collision shapes on the rig for it to sit beside (`rigged-characters.md`: no IK, no constraint graph, no ragdoll blending). The ragdoll work will add its own rows to the same asset later.

```
RigAsset
  billboardRoots: List<BillboardRootDefinition>

BillboardRootDefinition {
    string displayName;                  // cosmetic
    uint   stableId;                     // identity of the ROOT (what a clip track binds to)
    BillboardNodeAddress address;
    BillboardMode mode = ScreenAligned;
    float3 constraintAxis = (0,1,0);     // AxisConstrained only
    float  angleOffsetDegrees;           // authored rest offset; clips key on top of it
    bool   snapEnabled;  int snapSteps = 8;  float snapOffsetDegrees;
    bool   clampEnabled; float clampArcDegrees = 180f;
}

BillboardNodeAddress {
    BillboardAddressKind kind;   // RigTarget | HierarchyPath
    uint   targetId;             // kind == RigTarget
    string hierarchyPath;        // kind == HierarchyPath
}
```

**Two address kinds, because the rig has two kinds of node and only one of them has an id.** `RigAsset.targets` is a *flat* list — the rig asset carries no hierarchy at all. The hierarchy is the authoring prefab's transforms, which targets already bind to *by name* (`PrefabAuthoringBridge.FindByName`) and which `RigStructureEditor` reparents. A rig target therefore addresses by its stable id, which survives renames; a bare grouping transform that is nobody's animatable part — an `ItemPivot` under a hand — has no id to offer and addresses by path, with the same rename fragility a bone name already carries (A42). The bake **reports** an unresolved address rather than silently dropping the root.

The root's own `stableId` is separate from the addressed node's. A clip track binds to the root, and a root must survive being re-pointed at a different node without every clip that keys it going stale.

**Validation rules** (§3.5's table, continued — a new rule is recorded in the amendment that introduces it):

| Code | Severity | Rule |
|---|---|---|
| V21 | Error | A billboard root addresses a node that does not exist — a `RigTarget` id that is not a target of this rig, or an unresolvable `HierarchyPath` |
| V22 | Error | Two billboard roots address the same node |
| V23 | Error | An `AxisConstrained` root's `constraintAxis` is zero-length |

**V21 is only half-reachable at rig scope, and the reachable half is the target one.** A target address names a row of the rig asset itself and is resolved by `ClipValidation.ValidateRig`. A path address names a transform of the *authoring prefab*, which a `RigAsset` neither references nor can see — so path resolution belongs to the entity bake (D3), which does hold the prefab. This is the same split A12 recorded for V08: a rule whose evidence lives where the validator cannot legally reach is checked where the evidence is, and saying so is what stops a later reader mistaking the silence for "valid".

V22 says nothing when the shared address is itself unresolved. Two roots pointing at one absent target is a single missing target, and reporting it twice buries the fix under its own symptom.

V23 is reported rather than defaulted to world Y, on A34's precedent — a silent default is how half the callers stop honouring a mode they opted into, and an author who chose `AxisConstrained` and left the axis empty wanted an axis, not upright.

### Modes

`BillboardMode` gains one value. **Existing values are not renumbered** — they are shared with `_BillboardParams.x` (§6.2), so the CPU and shader paths cannot drift.

| Value | Mode | Meaning |
|---|---|---|
| 0 | `Off` | No billboard; the node keeps its animated orientation |
| 1 | `Full` | Spherical — faces the camera *point* on every axis |
| 2 | `Upright` | Turns about world Y only |
| 3 | `FrozenYaw` | Holds an authored yaw, pitch still follows the camera (the corpse case) |
| 4 | `ScreenAligned` | Faces the camera's *forward* — every root takes the same rotation. **Default.** |
| 5 | `AxisConstrained` | **New.** Turns about an arbitrary authored axis |

`Upright` is exactly `AxisConstrained` with axis `(0,1,0)`. It keeps its own value because it is shipped, because it is the common 2.5D case, and because naming the common case is worth one enum entry.

**The shader path does not gain hierarchy.** `ToolkitBillboard.hlsl` rotates each quad about its own pivot; it has no way to know what a node's ancestor is. It gains `AxisConstrained` for mode parity and nothing else. Hierarchical billboarding is CPU-only, and the existing rule stands: an actor uses the CPU path or the shader path, never both.

### Resolution — nearest ancestor, and what an override actually costs

At bake, `ActorBaker` walks the prefab hierarchy once and, for each node, finds the nearest ancestor **inclusive of itself** that is a declared root. That produces three runtime components:

```
BillboardRoot   : IComponentData   // on a declared root node — the resolved config
BillboardFrame  : IComponentData   // on a declared root node — the resolved WORLD rotation
BillboardMember : IComponentData   // on every node under a root: { Entity root; }
```

A node with no billboarded ancestor gets none of the three and transforms normally in its parent's space, paying nothing — the opt-in precedent `AnimLod` and `PartFacing` already set (A23).

**Inheritance is nearly free; override is the part that costs something.** Descendants are Transform children, so a root's rotation already propagates down through `LocalToWorld` composition — an inheriting node needs no rotation write of its own. What inheritance buys is the *query* (`BillboardMember.root`) that the hierarchy panel and the ragdoll read.

A **nested** root is the real work. Its ancestor's rotation is already in its parent chain, so writing a second billboard rotation on top would compose the two and double-rotate. It must therefore compute its target in world space and then cancel its parent's world rotation before writing local — which is exactly `parentTransform.InverseTransformRotation(...)`, the host's own line, applied at arbitrary depth. This is the mechanism that lets a character billboard as a whole while a held item billboards independently.

To make that cancellation cheap and correct, the bake stores each root's **hierarchy depth**, and `BillboardResolveSystem` processes roots in ascending depth. An inner root then reads its ancestor's already-resolved `BillboardFrame` instead of re-walking the chain — O(n), with no `LocalToWorld` dependency and so no frame of staleness from `TransformSystemGroup`.

### Evaluation order (normative)

The directive asks for this to be explicit, because it decides what animated rotation on a billboarded node *means*.

1. `TransformSampleSystem` — composites the clip layers into `TargetPose`.
2. `TransformApplySystem` — writes `LocalTransform` + `PostTransformMatrix` from `TargetPose`.
3. **`BillboardResolveSystem`** *(new)* — resolves and applies billboard orientation on top.

Steps 1–2 are unchanged and already in this order; step 3 replaces `ActorBillboardSystem` at the same slot in `AnimationToolkitPresentationSystemGroup`, `[UpdateAfter(TransformApplySystem)]`, gated on `AnimVisible`.

> **So: the animated pose is the billboard's *rest orientation*, not a rotation the billboard adds to.** At `blendWeight` 1 the billboard **replaces** the node's animated rotation outright. Keying rotation on a fully-billboarded node changes nothing visible — which is why the blend weight and the angle offset exist, and why the hierarchy panel must show at a glance that a row is billboarded, before someone spends an afternoon keying a rotation the billboard is about to discard.

Position, scale and every other channel are untouched. A billboard is an orientation, and only an orientation.

Within step 3, per root, in this order:

```
target = LookRotation(facing(mode), up)          // world space
target = constrain to the mode's axis            // Upright, AxisConstrained, FrozenYaw
target = target * angleOffset                    // about the frame's own up axis
target = snap(target, steps, phase)              // if snapping enabled
target = clamp(target, restRotation, arc)        // if clamping enabled
result = slerp(animatedRotation, target, blend)  // blendWeight, then the enable flag
BillboardFrame.rotation  = result                // world space, published
LocalTransform.Rotation  = inverse(parentWorld) * result
```

**Offset before snap, snap before clamp.** A keyed offset is authored facing intent and should land on a snap step with everything else, rather than parking the rig between two steps of an 8-way wheel. The clamp is last because it is a hard guarantee: at the arc boundary the result may sit *off* a snap step, and that is the intended precedence — the clamp is a constraint, the snap is a look, and a constraint a look can violate is not a constraint.

**Clamp reference.** "Rest orientation" is the node's orientation before billboarding — the animated pose composed into its parent — so a clamped billboard turns within an arc *of the animation*, and an animation that turns the node carries the arc with it.

**`angleOffset` is a yaw within the billboard frame**, about the frame's own up axis. For `Upright`, `ScreenAligned` and `AxisConstrained` that is the constraint axis; for `Full` it is the frame's up, i.e. a roll about the view direction.

### Runtime query surface

```
BillboardFrame { quaternion rotation; }                 // world space; right/up/forward derived
BillboardQuery.TryGetFrame(node, out BillboardFrame)    // Runtime/Api, beside PlaybackQuery
```

`TryGetFrame` resolves `node → BillboardMember.root → BillboardFrame` in one hop. The ragdoll spec describes *"walking up the rig hierarchy, exactly as the animation system does"*; it will not need to — the bake already flattened that walk, and a per-body hierarchy climb every physics step is exactly the cost this component exists to remove.

A frozen ragdoll keeps billboarding for free: its bodies go kinematic under a root whose `BillboardFrame` is still resolved every frame, so the frozen pose rotates as a unit with no further work. That is the failure the ragdoll doc flags as easy to miss, and the hierarchy is what prevents it rather than a special case.

### Clip format — a new track kind, and this one *does* reach the blob

Billboard angle offset, blend weight, and enable/disable are keyable on the timeline like any other property. A new track kind parallel to the existing four, for A42's reason exactly: extending `TransformKey` would grow **every cutout key** — the format most clips use — with channels they never set.

```
BillboardTrack { uint rootStableId; List<BillboardKey> keys; }
BillboardKey   { float normalizedTime; float angleOffsetDegrees; float blendWeight;
                 bool enabled; Interpolation interpolation;
                 float2 bezierStartHandle; float2 bezierEndHandle; }
```

**Unlike bone tracks, these are runtime data.** A42's correction established that authored bone tracks never enter the blob because nothing samples a bone at runtime — they are bake input, like `vatSource`. Billboard tracks are the opposite: `BillboardResolveSystem` samples them every frame. So this is a genuine format change, and it is the expensive part of this amendment:

- `ClipBlob` gains a `BillboardTrackBlob` array.
- **`SchemaVersion` bumps.**
- The golden content hash is **re-recorded** (`ContentHashGoldenTests`).
- `DataContractTests`' `ClipBlob` contract, `ClipRegistryDeterminismTests` and `DiskRoundTripTests` all move with it.

`enabled` is **always step-interpolated**, never eased, on A43's reasoning: an enable flag is a discrete instruction that fires at a moment, not an approximation of anything between two moments. `angleOffsetDegrees` and `blendWeight` interpolate normally.

### Editor surface

**Hierarchy panel** (`ClipEditorWindow`, `MakeHierarchyRow` / `BindHierarchyRow`). Three states, visually distinct, on every row kind — rig target, socket, and bare prefab transform alike:

| State | Indicator | Tooltip |
|---|---|---|
| Declared root | Billboard icon, full strength; `clip-editor__hierarchy-row--billboard-root` | The mode, e.g. "Billboard root — Screen Aligned" |
| Inheriting | Subtler dimmed glyph; `--billboard-inherited` | **Names the source root** — "Billboards with «Torso»" |
| Not billboarded | Nothing | — |

Right-click gains *Make Billboard Root* / *Clear Billboard Root*, writing `RigAsset.billboardRoots` with the correct address kind for the row. `RigAssetEditor` gains a Billboard Roots section for direct editing, with mode-dependent fields shown conditionally the way `ActorAuthoringEditor` already hides Frozen Yaw.

**Preview.** `ClipPreviewController` already orbits — yaw, pitch clamped to ±85°, distance, focused on the rig bounds. It does no billboarding today. It gains live billboarding driven by the **same static math helper the Burst job calls**, so preview and runtime cannot drift, plus a viewport toggle to suppress it while inspecting an authored pose. `SocketPreviewParityTests` is the precedent; `BillboardPreviewParityTests` is the obligation.

### Build order

| Phase | Delivers |
|---|---|
| **D1** | `BillboardRootDefinition` / `BillboardNodeAddress` on `RigAsset`; `AxisConstrained`; validation rules for unresolved and duplicate addresses |
| **D2** | `BillboardMath` — one static, Burst-compatible helper: facing, axis constraint, offset, snap, clamp, blend. Pure logic, fully unit-testable, no World |
| **D3** | Bake — nearest-ancestor resolution, depth assignment, `BillboardRoot` / `BillboardFrame` / `BillboardMember`; `ActorBillboard` reinterpreted as sugar for "the actor root is a billboard root"; `ActorBillboardSystem` deleted, one code path |
| **D4** | `BillboardResolveSystem` + `BillboardQuery`; the facing-sign correction in both CPU and HLSL |
| **D5** | `BillboardTrack` / `BillboardKey`, blob, schema bump, golden re-record; timeline rows and key editing |
| **D6** | Hierarchy panel indicators, rig asset section, live preview billboarding, parity test, `Documentation~/billboarding.md` |

D1–D4 alone unblock the ragdoll work: the frame is queryable at the end of D4.

### Not in scope

**The host game is not migrated.** `Assets/_Scripts/.../BillboardSystem.cs`, `Billboard` and `BillboardAuthoring` stay exactly as they are and keep running. Stitch Punk's look does not change in this pass. Migration is a separate, verifiable cutover with a side-by-side comparison, and folding a visible in-play-mode change into new feature work is how a regression gets attributed to the wrong thing.

### Risks

| Risk | Mitigation |
|---|---|
| The facing-sign correction changes the look of existing `Full` / `ScreenAligned` content | Called out normatively above; on the §11.4 human-verified checklist, which is the only place it can actually be settled |
| Schema bump + golden hash re-record | Confined to D5; D1–D4 touch no blob |
| A blob layout change makes the first EditMode run after it fail spuriously in untouched fixtures | Known behaviour — recompile and re-run before debugging anything |
| Path-addressed roots break when a grouping transform is renamed | The bake reports the unresolved address; same contract as an unresolved bone name (A42) |
| Deep nesting costs a rotation write per root per frame | Roots are declared, not implicit — a rig pays for the roots it declares and nothing for the nodes that merely inherit |

### D2 note (2026-08-17): two decisions the build forced

**Snapping and clamping share one reference and one axis, and that was not obvious.** The arc is measured from the rest orientation because A44 said so; making the snap wheel share that reference was a choice. It is the right one — a character keyed to turn on the spot should click through its eight facings relative to *its own* forward, not relative to where it happens to stand — and it collapses two features into one swing-twist decomposition. Only the twist about the reference axis is quantised or limited; the swing carries every other component of the pose through untouched, so a rig whose rest pose is tilted keeps its tilt instead of being flattened onto the wheel.

**`BillboardSettings.enabled` needs `[MarshalAs(UnmanagedType.U1)]`.** A plain C# `bool` has no fixed width, so a struct containing one is not blittable, and this struct crosses a `[BurstCompile]` external entry point. Without the attribute the *entire* Runtime assembly fails Burst compilation with BC1063 — the same blast radius, and the same misleading error list, that a by-value vector parameter produced in `FacingResolver` before A38's fix. By-ref is necessary but not sufficient; blittable is the other half of the rule.

---

*End of Phase B architecture. Contract changes during Phase C amend this document first (§9 rules).*
