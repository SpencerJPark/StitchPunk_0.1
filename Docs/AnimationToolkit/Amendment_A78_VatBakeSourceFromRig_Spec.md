# Amendment A78 — the rig says what to bake, and the bake does every VAT part

> **Status:** 📝 specced 2026-09-08, not built. Takes `0.26.0` unless `CHANGELOG.md` has moved.
> **Prompt:** [`Amendment_A78_VatBakeSourceFromRig_Prompt.md`](Amendment_A78_VatBakeSourceFromRig_Prompt.md).
> **Successor:** [`Amendment_A79_VatPreviewModes_Spec.md`](Amendment_A79_VatPreviewModes_Spec.md) — the
> preview toggles the owner asked for in the same breath. Split on his instruction: baking is
> provable from the produced assets and fixtures, the preview needs his eye, so it gets its own round.
> **Predecessor:** [`Amendment_A76_RigsTab_Spec.md`](Amendment_A76_RigsTab_Spec.md) — A76 is what makes
> this possible: a rig now always carries a Source Prefab and a tickable target list.
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files in
> four waves and never touch MCP.

---

## 1. What the owner asked for (2026-09-08, verbatim)

First:

> "I want you to make me a spec for removing the input for skinned mesh renderer on the Vat baker
> tab, the rig will already hold the info for which of its child objects will be turned into vat
> textures so there shouldnt be a need for a new input. again use the tenticle as the example for
> testing this but there shouldnt be a need for more input as far as my understanding goes. I still
> want all the same functionality"

Then, correcting a first draft that had scoped multi-part baking out:

> "my understanding is that animations can be a combo of vats, flipbooks, and transforms, ect... for
> the baking what we are doing it pointing the clipset and the rig at it, each part that uses vats
> will be baked with vats, this texture is then applied to only that part … it just bakes out the
> parts that can, if there are no animations in its clipset that use vats and line up to the rig then
> a warning should happen and at bake nothing will be created."

He is right, and the first draft was wrong. Most of his model already ships — it is the **baker**
that is single-mesh, not the format and not the runtime. What A78 builds:

```
 Source                                  Source
   Clip Set    [MyCharacterClips     ]     Clip Set  [MyCharacterClips]
   Rig         [MyCharacterRig       ]     Rig       [MyCharacterRig  ]
   Skinned Mesh[Cape (scene object)  ]  →  baking 2 of 3 VAT parts · Cape, Hair
                                             (Tail — no clip in this set animates it)
```

---

## 2. What is already true (verified 2026-09-08 — do not re-derive)

Read this before §4; half of what looks like new work is not.

- **Per-part frame ranges already work end to end.** `ClipAsset.vatTracks` lets a part name its own
  source clip; the bake writes one `VatClipRange` per (clip, target); `ClipRegistryBuilder` mirrors
  them into `ClipBlob.vatTargetRanges` (`:870-910`); and at run time each VAT part resolves **its
  own** range by its target index before falling back to the clip-wide one
  (`VatMaterialSystem.cs:145-152`, `TryResolveGlobalFrame`).
- **Per-part textures already reach the GPU.** `VatTextureBinding`'s own summary says it: *"The
  primary GPU binding is material-level (shared material, shared batch); this component exists for
  bake-time validation and for advanced hosts building materials at runtime."* Nothing reads it — the
  only writer is `ActorBaker.cs:117`. Each VAT part already carries its own material with its own
  texture.
- **Mixed clip sets already work.** VAT meshes, flipbook planes and transform quads are separate part
  entities driven by separate systems off one clip. `RigTargetBaker.AddTechniqueComponents` (`:270-296`).
- **A clip set with no VAT content already refuses.** `VatBakePanel.cs:234-243`. What it does not do
  is check that the targets those clips name exist on the rig.
- **Frame indices are already per-texture-scoped.** Each `VatTextureBaker.Bake` call numbers frames
  from 0. A per-part bake therefore needs **no change to the blob, the registry builder's range
  filling, `TryGetTrackRange`, or any runtime system** — each part indexes into its own texture and
  only ever reads its own rows.

**What genuinely does not exist:** one bake run produces one texture and one runtime mesh.
`VatTextureSetAsset.boneTexture` / `.runtimeMesh` / `.boneCount` / `.textureWidth` are single fields
(`:22-47`), and `RigTargetBaker.ValidateVatMaterial` (`:328-380`) checks *every* VAT part's material
against that one texture. That is the whole gap.

---

## 3. Decisions (recorded — do not re-ask)

- **A78-D1 — The `Skinned Mesh` `ObjectField` is deleted, not disabled.** A disabled control showing
  a derived value is worse than no control. It is replaced by a read-only summary line (§5.4).
- **A78-D2 — A bake source is a (targetId, `SkinnedMeshRenderer`) pair resolved from
  `rig.sourcePrefab`.** Every rig target whose `sourceNodePath` resolves to a node carrying a
  `SkinnedMeshRenderer` is one source. If **no** target resolves to one and the prefab holds exactly
  one skinned mesh, that mesh is a single source with `targetId = 0` — the untargeted case, which is
  what the sample tentacle is (`targets: []`, one `TentacleMesh`) and what every VAT rig authored
  before A78 is.
- **A78-D3 — Resolution does not test `TargetKind.VatMesh`.** It reads like the right question and is
  the wrong one: nothing in the editor authors `kind`. Only the two `Samples~` builders assign a
  non-`Quad` kind; every rig built through the Rigs tab leaves every target at `Quad`
  (`RigTargetRowBuilder` never sets it). Gating on `VatMesh` would make every rig a customer can
  actually author unbakeable. **Carrying a skinned mesh is the signal.**
- **A78-D4 — `VatTextureBaker` is not touched.** The panel calls `Bake` once per source, each call
  with a different renderer out of the **same** posed instance and that source's own clip list. Every
  call numbers its own frames from 0, which is exactly what per-part textures need. This is why A78
  is an editor-and-asset amendment with no runtime system changes.
- **A78-D5 — Sockets are sampled on the first source's call only.** `VatTextureBaker` samples them
  inside `Bake` (`:212-213`), so passing the socket list N times would write N copies of every track.
  Bone sockets resolve against the hierarchy **root**, not the renderer (`ResolveSocketBones`, `:177`),
  so one pass captures them correctly for the whole rig.
- **A78-D6 — One prefab instance for the whole bake, not one per part.** A clip poses the entire
  hierarchy at once; every source reads its own bones out of that same pose. Instantiated once,
  destroyed in a `finally`.
- **A78-D7 — The bake poses a throwaway `Object.Instantiate`, never the prefab asset and never a
  scene object.** `VatTextureBaker` writes local TRS onto every bone of what it is handed
  (`:190-249`, and it restores them in a `finally` precisely because it does); doing that to an asset
  writes the last sampled frame into the `.prefab` on disk. `Object.Instantiate`, **not**
  `PrefabUtility.InstantiatePrefab`: a copy with no prefab link cannot write back even if a later
  change forgets the rule. `HideFlags.HideAndDontSave`, the pattern `PreviewSkeletonMirror.Rebuild`
  (`:48-83`) already ships.
- **A78-D8 — Baking a rig that exists only in an open scene is gone, deliberately.** The one
  functional subtraction, and it is the owner's stated premise. A rig with no Source Prefab refuses
  with a message naming the field to set. No migration path — HANDOFF §5, "new rigs are created fresh".
- **A78-D9 — `VatTextureSetAsset` grows a `parts` list and loses the singular texture/mesh/count
  fields.** A generated asset is re-baked, not migrated (HANDOFF §5), so there is no compatibility
  shim. `clipRanges` **stays flat and set-level** — each row already carries its `targetId`, each
  row's `frameStart` indexes that target's own texture, and leaving it alone is what keeps the
  registry builder, `TryGetTrackRange`, `ClipValidation` and every runtime system untouched.
- **A78-D10 — A part with nothing to bake is skipped with a named warning; a bake with no parts left
  creates nothing.** The owner asked for exactly this. "Nothing to bake" means: no clip in the set
  supplies this target either a `vatTracks` row naming it, an imported `vatSource.sourceClip`, or
  authored `boneTracks`.
- **A78-D11 — A `vatTracks` row naming a target the rig does not have, or one with no skinned mesh,
  is a named warning, not a failure.** Same reasoning as the existing unresolved-socket warning: the
  other parts baked fine and the textures are valid. Silence is the only wrong answer.
- **A78-D12 — `VatTextureBinding` keeps `setKey` and loses its two texture references.** With N parts
  there is no one actor-level texture, and filling it from "the first part" would be a lie in the
  data. Nothing reads the fields (grep: the only writer is `ActorBaker.cs:117`), so a per-part
  `VatPartTextureBinding` added by `RigTargetBaker` to each VAT part entity is both honest and more
  useful — it sits where the material already is.
- **A78-D13 — `VatTextureInfoBlob` keeps its layout; only what fills it changes.** Its addressing
  numbers describe the untargeted entry when a set has one, and are `0 / 1 / 0` for a per-part set
  (`rowsPerFrame` stays 1 so a consumer dividing by it cannot trip). Nothing reads them
  (`vatInfo`'s only readers are the builder itself and its hash), the hash covers them so a change
  re-bakes, and an unchanged struct layout means **no `ClipRegistryBuilder.SchemaVersion` bump**.
- **A78-D14 — Output file names keep today's shape for a single untargeted part** —
  `<ClipSet>VatBone.asset`, `<ClipSet>VatRuntimeMesh.asset`, `<ClipSet>VatSet.asset` — and a
  per-part bake inserts the sanitized part name: `<ClipSet>Vat<Part>Bone.asset`. One `…VatSet.asset`
  either way. So re-baking the tentacle produces byte-for-byte the same filenames it does today.
- **A78-D15 — `VatTextureSetAsset.schemaVersion` is stamped to `1` by the baker.** It is `0` on every
  set ever produced because `SaveResult` never writes it — a latent bug this amendment is the natural
  place to close.
- **A78-D16 — The sample gains a two-part subject.** The existing single tentacle stays exactly as it
  is and remains the `targetId == 0` acceptance case; a new `CreateTwoPartSampleAssets` builds a
  tentacle with a second skinned mesh and a rig with two targets, so per-part baking has something to
  prove itself against. Without it every per-part path in this amendment is untested by anything but
  in-memory fixtures.

---

## 4. Read first (the executor and every subagent — only what your task names)

1. Repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6 (skip §4 history);
   `Assets/_Vault/Memories/Code/RULES.md`.
2. `Assets/_Vault/Memories/Code/AnimationToolkit.md` — the shared-editor-chrome section and the
   "never rebuild a pane from a value-changed callback" one.
3. This spec's **§2** — it is the difference between a two-day amendment and a two-week one.
4. `Editor/VatBaking/VatBakePanel.cs` **in full (569 lines)**: `:19-30` fields, `:60-98` the Source
   rows, `:178-199` `SetSource`, `:210-293` `Bake`, `:300-312` `RefreshPreview`, `:317-379`
   `CollectVatClips`, `:381-455` `SaveResult`, `:486-507` `CollectBoneSockets`.
5. `Authoring/Assets/VatTextureSetAsset.cs` **in full (241 lines)**.
6. `Authoring/Assets/RigAsset.cs:29-40, 200-242`; `Authoring/Assets/ClipAsset.cs`'s `VatTrack` and
   `VatClipSource` declarations only.
7. `Editor/ClipEditor/Authoring/PrefabAuthoringBridge.cs:165-230` — `GetHierarchyPath(node, root)`
   and `ResolveByPath(root, path)`. Both are what §5.1 needs; do not write a second one.
8. `Editor/ClipEditor/Preview/PreviewSkeletonMirror.cs:46-83` — the instantiate/hide/plant pattern.
9. `Editor/VatBaking/VatTextureBaker.cs:141-215` and `:623-645` only. **You are not editing this file.**

---

## 5. Design

### 5.1 New — `Editor/VatBaking/VatBakeSourceResolver.cs`

```csharp
/// <summary>
/// One VAT part a bake will produce: which rig target it stands for, and the skinned mesh in the
/// rig's source prefab that it samples.
/// </summary>
public sealed class VatBakeSource
{
    public uint TargetId;            // 0 = the rig names no VAT target and the prefab has one mesh
    public string DisplayName;       // the target's displayName, else the node's name
    public string SourceNodePath;    // empty = the prefab root itself
    public SkinnedMeshRenderer PrefabRenderer;
}

/// <summary>
/// Reads a rig's source prefab and says which skinned meshes its VAT bake covers, then makes the
/// single throwaway instance the bake poses. The rig is the whole input; nothing is dragged in.
/// </summary>
public static class VatBakeSourceResolver
{
    public static bool TryResolve(RigAsset rig, out List<VatBakeSource> sources, out string failureMessage);
    public static bool TryCreateBakeInstance(RigAsset rig, out GameObject instanceRoot, out string failureMessage);
    public static SkinnedMeshRenderer FindInInstance(GameObject instanceRoot, string sourceNodePath);
}
```

`TryResolve`, with the exact refusal texts:

| Condition | Result |
|---|---|
| `rig == null` | `"Assign the Rig these textures are baked for."` |
| `rig.sourcePrefab == null` | `"Rig '<name>' has no Source Prefab, so there is nothing to sample. Set one in the Clip Editor's Rigs tab."` |
| no `SkinnedMeshRenderer` anywhere under the prefab root | `"'<prefab>' has no skinned mesh. A VAT bake needs one; a rig of cutout quads is not a VAT subject."` |
| one or more targets resolve to a skinned mesh | ✅ one source per such target, in `rig.targets` order |
| none do, exactly one skinned mesh in the prefab | ✅ one source, `TargetId = 0` |
| none do, more than one skinned mesh | `"'<prefab>' has <n> skinned meshes and none of them is a rig target, so the bake cannot tell which to sample: <paths>. Tick the ones you want in the Clip Editor's Rigs tab."` |

Paths come from `PrefabAuthoringBridge.GetHierarchyPath(rendererTransform, rig.sourcePrefab.transform)`,
joined `", "`. A skinned mesh **on the prefab root** yields the empty path, which is legal and which
`ResolveByPath` maps back to the root — do not skip it the way `RigTargetRowBuilder` skips a root
renderer. It can never match a target (a target's `sourceNodePath` is empty only when unbound), so it
only ever reaches the single-mesh rule. Target matching is `StringComparison.Ordinal`, skipping null
targets and empty paths, and does **not** read `kind` (A78-D3).

`TryCreateBakeInstance` is `Object.Instantiate` → name after the prefab → `HideAndDontSave`, and
`FindInInstance` is `ResolveByPath` then `GetComponent<SkinnedMeshRenderer>()`. Two comments earn
their place and nothing else does: why a copy, and why not `PrefabUtility.InstantiatePrefab`. One
line each — `Conformance_F`.

### 5.2 `Authoring/Assets/VatTextureSetAsset.cs`

Delete `boneTexture`, `positionTexture`, `normalTexture`, `runtimeMesh`, `boneCount`, `vertexCount`,
`textureWidth`, `rowsPerFrame` from the set. Add:

```csharp
/// <summary>One baked VAT part: the textures, the runtime mesh and the addressing numbers for the rig target it covers.</summary>
[Serializable]
public sealed class VatPartTextures
{
    public uint targetId;          // 0 = the set's single untargeted part
    public string displayName;     // cosmetic; identity is targetId
    public Texture2D boneTexture;
    public Texture2D positionTexture;
    public Texture2D normalTexture;
    public Mesh runtimeMesh;
    public int boneCount;
    public int vertexCount;
    public int textureWidth;
    public int rowsPerFrame = 1;
}

/// <summary>One entry per VAT part this set was baked for.</summary>
public List<VatPartTextures> parts = new List<VatPartTextures>();

/// <summary>The part baked for a rig target: the exact match, else the untargeted part when one exists.</summary>
public bool TryGetPart(uint targetId, out VatPartTextures part);
```

`TryGetPart` performs the **same two-step fallback** `TryGetTrackRange` does — exact `targetId`, else
the `targetId == 0` entry — so a part with no dedicated bake keeps resolving the untargeted one. That
symmetry is the contract; write it as one comment and keep the two methods next to each other.

`flavor`, `clipRanges`, `socketTracks`, `sourceHash`, `sourceRigKey`, `setKey`, `schemaVersion`,
`TryGetClipRange` and `TryGetTrackRange` are **unchanged** (A78-D9).

### 5.3 `VatBakePanel.Bake` — `:210-293`

Shape, after the existing clip-set and rig guards:

```
resolve sources           → refuse and return on failure
collect per-source clips  → skip a source with none, warning by name
                          → refuse and create nothing when no source has any (A78-D10)
warn about vatTracks rows naming targets no source covers (A78-D11)
instantiate once
try {
    for each source:
        Bake(that source's renderer, that source's clips, sockets only on the first)
        accumulate ranges, collect the part entry
} finally { DestroyImmediate(instanceRoot) }
SaveResult writes N texture assets, N runtime meshes, one set
```

`CollectVatClips` becomes `CollectVatClipsForSource(ClipSetAsset clipSet, VatBakeSource source)`.
Per clip in the set, in this order:

1. A `vatTracks` row whose `targetId == source.TargetId` (and `source.TargetId != 0`) →
   `animationClip = track.sourceClip`, `boneTracks = null`, `loopSafe = track.loopSafe`.
2. Otherwise, if the clip has `vatSource.sourceClip` or any `boneTracks` → `animationClip =
   clip.vatSource?.sourceClip`, `boneTracks = clip.boneTracks`, `loopSafe = clip.vatSource?.loopSafe`.
   This is the fallback that makes a part with no dedicated track follow the clip-wide source, and it
   is what bakes the tentacle (authored bone tracks, no imported clip).
3. Otherwise the clip contributes nothing to this part.

Every emitted `VatBakeClip` carries `targetId = source.TargetId`, `clipId = clip.Id.Value`,
`samplesPerSecond = clip.frameRate`, `durationSeconds = clip.duration`. Keep the three existing
comments from `:342-353` — the id-provenance one, the per-clip-FPS one and the loop-safe one — they
are all still true.

Hoist `VatFlavor bakeFlavor = (VatFlavor)flavorField.value;` above the try; `SaveResult` needs it
after the instance is gone. Keep the two unresolved-name warning blocks (`:265-285`) per source,
prefixing each with the part's `DisplayName` so N parts do not produce N anonymous warnings; the bone
track one's closing sentence — "Check the names on the clip's bone tracks against the skinned mesh
you assigned" — becomes "…against the rig's source prefab."

`SaveResult` loops: one `VatPartTextures` per source, assets named per A78-D14, `runtimeMesh` from
`VatMeshPreparer.TryCreateRuntimeMesh(source.PrefabRenderer, …)` — the **prefab** renderer, which
reads only `sharedMesh` (`VatMeshPreparer.cs:36-49`) and outlives the instance. Stamp
`schemaVersion = 1` (A78-D15). `sourceRigKey`, `clipSet.vatTextures` assignment and the folder
machinery are unchanged.

One side effect worth knowing rather than fixing: `VatTextureBaker` takes `renderer.transform.root`
as its bake root (`:153`). With a scene object that was whatever the user had dragged the prefab
under; with the instance it is always the prefab root — the space the runtime and the preview
measure from. Root-relative math (`:483`, `:500-503`) makes an unmodified prefab instance produce
identical output either way, and a rig that was nested under a scene parent produces *better* output
than before.

### 5.4 `VatBakePanel` — the Source column, `:19-30` and `:88-98`

Replace `skinnedRendererField` with `private Label resolvedSourceLabel;` and
`private List<VatBakeSource> resolvedSources;`. The field's construction becomes:

```csharp
// Not a field: which meshes a bake covers is a fact about the rig, not a fourth thing to keep in
// step with it. This line is the receipt — what the rig resolved to, or why it did not.
resolvedSourceLabel = new Label(string.Empty);
resolvedSourceLabel.name = "vat-resolved-source-label";
resolvedSourceLabel.AddToClassList("clip-editor__hint");
resolvedSourceLabel.RegisterCallback<ClickEvent>(clickEvent => PingSourcePrefab());
root.Add(resolvedSourceLabel);
```

`.clip-editor__hint` exists (`ClipEditorWindow.uss:597`) and both hosts load that stylesheet
(`VatBakeWindow.CreateGUI`, `:33-37`).

Add `rigField.RegisterValueChangedCallback(changeEvent => RefreshResolvedSources());` at `:78`, have
`clipSetField`'s existing callback call it too (whether a part has anything to bake depends on the
clip set), and end `SetSource` with it instead of `RefreshPreview`.

Label text — one line, three shapes:

- one untargeted part: `VatSampleTentacle ▸ TentacleMesh · 12 bones`
- several parts: `baking 2 of 3 VAT parts · Cape, Hair` with a second line naming what is skipped
  and why: `Tail — no clip in this set animates it`
- unresolved: the refusal text, in `ToolkitPalette.Warning`

`PingSourcePrefab` is `EditorGUIUtility.PingObject` on the rig's `sourcePrefab`, guarded against
null — the one-liner `RigsPanel.cs:165` uses. `RefreshPreview` passes the **first** resolved source's
renderer to `preview.Show` (unchanged signature); showing every part at once is A79's job, and until
then the preview subject is what it has always been.

**Do not** rebuild the Source column from inside the rig callback — set the label's text and colour
in place. That is the pane-rebuild trap the vault note names. And `Bake` calls `TryResolve` itself
rather than trusting `resolvedSources`: a rig edited in the Rigs tab between the last refresh and the
press must not bake a stale set of parts.

### 5.5 The three consumers of the fields §5.2 deleted

- **`Runtime/Components/VatTextureBinding.cs`** — drop `boneOrPositionTexture` and `normalTexture`,
  keep `setKey`, and rewrite the summary to say what it now is (the actor-level link to the set).
  New `VatPartTextureBinding : IComponentData { UnityObjectRef<Texture2D> boneOrPositionTexture;
  UnityObjectRef<Texture2D> normalTexture; }` beside it.
- **`Authoring/Baking/ActorBaker.cs`** — `BuildVatTextureBinding` (`:1030-1045`) returns just the
  key; `DependsOnVatTextures` (`:565-580`) walks `parts` and depends on each texture. `AddSocketRegistry`
  (`:585-605`) is unchanged — `socketTracks` did not move.
- **`Authoring/Baking/RigTargetBaker.cs`** — the `TargetKind.VatMesh` arm of `AddTechniqueComponents`
  (`:283-291`) also adds `VatPartTextureBinding` from `TryGetPart(authoring.targetStableId, …)`.
  `ValidateVatMaterial` (`:328-380`) looks the part up the same way and compares that part's texture,
  naming the part in every message; when `TryGetPart` finds nothing the warning becomes "…and the VAT
  texture set baked no part for this target", which is a real authoring mistake the old code could
  not see.
- **`Authoring/Build/ClipRegistryBuilder.cs`** — `BuildVatInfo` (`:414-436`) per A78-D13. `SchemaVersion`
  stays `10`; the struct layout has not changed.

### 5.6 `Editor/Inspectors/VatTextureSetAssetEditor.cs`

The header numbers become a per-part block — one boxed row per entry: name, target id, flavour,
bone/vertex count, texture dimensions, runtime mesh. The clip-range table (`:204-…`) is unchanged;
add the part's name to each row's target column so a range can be traced to a texture. Keep the
existing "no stale badge" comment at `:375` — it is still true and now more so.

### 5.7 The sample (A78-D16)

`Editor/ClipUtilities/VatSampleTentacleUtility.cs` gains `CreateTwoPartSampleAssets`, mirroring
`CreateSampleAssets` but building a second skinned mesh (`TentacleFin`, weighted to the upper half of
the same bone chain) under the same root, a rig with **two** targets pointing at both node paths, and
one clip whose `vatTracks` names only the fin — so the bake exercises the dedicated-track path for
one part and the clip-wide fallback for the other in a single run. Assets land in a folder the caller
names; the package hardcodes no host path (`Conformance_D`).

### 5.8 Docs

- `Documentation~/rigged-characters.md:105` — the Bake step: assign the clip set and the Rig, the
  bake samples the rig's Source Prefab, the line under the Rig names the parts it will bake. Add a
  paragraph on multi-part: one target per VAT sub-mesh, one texture and one runtime mesh each, a part
  with no clip animating it is skipped with a warning.
- `Documentation~/index.md:86` — already says "pick a source prefab and a clip set"; leave it.
- `CHANGELOG.md` — `## [0.26.0] — A78`, in the shipped voice: the field is gone, the rig's Source
  Prefab is the source, every VAT part of a rig bakes in one run into its own texture and runtime
  mesh, a part nothing animates is skipped by name, a set with no bakeable part creates nothing, and
  the bake no longer needs the character in an open scene.
- `package.json` → `0.26.0`.

---

## 6. Tasks

Wave 1 (`[parallel-safe]`): **T1, T2, T6**. Wave 2: **T3**. Wave 3 (`[parallel-safe]`): **T4, T5**.
Then **T7** (orchestrator) and **T8** (⏸ checkpoint).

Each brief pastes: the spec path, the task text, its "Read" line, the §5 block it builds, this spec's
**§2**, and CLAUDE.md's hard rules (no `var`, no single-letter names, explicit types). Every brief
ends: "at turn 30 stop editing and write your report; report ≤ 30 lines; never call any
`mcp__UnityMCP__*` tool."

### T0 — Baseline and one probe (orchestrator)

Gate per HANDOFF §3; record EditMode/PlayMode discovered totals in §8. `git status` first — on
2026-09-08 the tree carried an unversioned screenshot under `Assets/_Vault/Tasks/Claude/`; if it is
still there it is the owner's, leave it.

**The probe** (memory: probe the platform before designing on it). One `execute_code` call — CodeDom
C# 6, no `using` lines, fully-qualified names, no `out var` — that instantiates
`Assets/ScriptableObjects/Animations/VatSampleTentacle/VatSampleTentacle.prefab`, sets
`HideAndDontSave`, records `EditorSceneManager.GetActiveScene().isDirty` before and after, runs
`AnimationMode.StartAnimationMode()` / `BeginSampling()` / rotate `Bone3` / `EndSampling()` /
`StopAnimationMode()` and reports whether the rotation stuck, then `DestroyImmediate`s it and reports
the dirty flag again.

Looking for: that a `HideAndDontSave` instance is poseable at all, and that creating and destroying
it does not dirty the owner's open scene. If it **does** dirty the scene, record it in §8 — the fix
is a scratch `EditorSceneManager.NewPreviewScene()` the instance is moved into, not abandoning
A78-D7. Do not ship a bake that dirties scenes.

### T1 — `VatBakeSourceResolver` + fixture [parallel-safe]

Files: **new** `Editor/VatBaking/VatBakeSourceResolver.cs`, **new**
`Tests/EditMode/VatBakeSourceResolverTests.cs`. Read `RigAsset.cs:29-40, 200-242`,
`PrefabAuthoringBridge.cs:165-230`, `PreviewSkeletonMirror.cs:46-83`, and §5.1. Build §5.1.

Fixtures build a real in-memory hierarchy (`new GameObject`, `AddComponent<SkinnedMeshRenderer>`,
parented) and a `ScriptableObject.CreateInstance<RigAsset>()` pointing `sourcePrefab` at the root —
no prefab asset on disk, everything `DestroyImmediate`d in `TearDown`.

- `TryResolve_OneSkinnedMeshAndNoTargets_ResolvesItUntargeted` — the tentacle's shape: root +
  `TentacleMesh`, `rig.targets` empty → one source, `TargetId == 0`, path `"TentacleMesh"`.
  *(Revert-to-fail: require a target — the tentacle stops resolving, which is the acceptance case.)*
- `TryResolve_TwoTargetedSkinnedMeshes_ReturnsBothInRigTargetOrder` → two sources carrying the two
  target ids, in `rig.targets` order. *(Revert-to-fail: return only the first.)*
- `TryResolve_TargetOnANodeWithNoSkinnedMesh_IsNotASource` — a target on a `MeshRenderer` node → not
  a source; with one skinned mesh elsewhere and no target on it, the untargeted rule takes over.
- `TryResolve_TwoSkinnedMeshesAndNoTarget_RefusesAndNamesBoth` — false, message contains both paths.
  *(Revert-to-fail: return the first candidate — a silent wrong-mesh bake, which is what this rule
  exists to prevent.)*
- `TryCreateBakeInstance_MakesAHiddenCopy_AndFindInInstanceHitsTheSameNode` — instance root is not
  the prefab root, `hideFlags == HideAndDontSave`, and `FindInInstance` returns a renderer belonging
  to the instance. `DestroyImmediate` it in the test.

Five tests, no more. No fixture for the null-rig guard.

### T2 — The asset shape [parallel-safe]

Files: `Authoring/Assets/VatTextureSetAsset.cs`, `Authoring/Build/ClipRegistryBuilder.cs`. Read
`VatTextureSetAsset.cs` in full, `ClipRegistryBuilder.cs:360-440` only, and §5.2 + A78-D13. Build both.

Do **not** touch `clipRanges`, `TryGetClipRange`, `TryGetTrackRange`, `socketTracks`, or
`ClipRegistryBuilder`'s range-filling at `:860-915` (A78-D9). Do not bump `SchemaVersion`.

- Fixture in `Tests/EditMode/VatTextureSetAssetTests.cs` (append if it exists):
  `TryGetPart_PrefersTheExactTarget_ThenFallsBackToUntargeted` — a set with parts `{0, 7}`: asking
  for 7 gives 7, asking for 9 gives 0, and a set with only `{7}` asked for 9 returns false.
  *(Revert-to-fail: drop the fallback — the "asking for 9 gives 0" case fails, which is the case that
  keeps every pre-A78 single-part rig working.)*

One test. The rest is a field move the compiler checks.

### T6 — The two-part sample [parallel-safe]

Files: `Editor/ClipUtilities/VatSampleTentacleUtility.cs`, `Editor/VatBaking/VatTentacleRigBuilder.cs`.
Read both in full (151 + 227 lines) and §5.7. Build §5.7. **`CreateSampleAssets` and `CreateTentacle`
must behave exactly as they do today** — add alongside, do not refactor through. No fixture; T7 drives
it. No `Assets/`-prefixed literal anywhere (`Conformance_D`).

### T3 — The panel

Files: `Editor/VatBaking/VatBakePanel.cs` only. Read it in full, the public surfaces of
`VatBakeSourceResolver` and `VatTextureSetAsset` (both exist by now), `ToolkitPalette.cs:14-27`, and
§5.3 + §5.4. Build both.

No fixture — UI wiring plus a call-site restructure (HANDOFF §2); T1 and T2 cover the logic and T7
drives the real thing. Do not touch `VatTextureBaker.cs` (A78-D4), `VatBakeWindow.cs` or
`ClipEditorWindow.cs` — neither host knows the field exists.

Check when done: `grep -n skinnedRendererField Editor/VatBaking/VatBakePanel.cs` returns nothing.

### T4 — The bakers [parallel-safe]

Files: `Authoring/Baking/RigTargetBaker.cs`, `Runtime/Components/VatTextureBinding.cs`. Read
`RigTargetBaker.cs:265-401`, `VatTextureBinding.cs` in full, `VatTextureSetAsset.cs`'s new surface,
and §5.5's second and third bullets. Also apply §5.5's `ActorBaker` changes — `ActorBaker.cs:565-580,
1030-1045` are two small methods, so this task is three files by exception; read only those ranges.

No new fixture. `PackagingConformanceTests` and the existing baker fixtures are the gate; if an
existing fixture asserts on `VatTextureBinding`'s texture fields, update it to the per-part component
rather than deleting the assertion.

### T5 — The inspector [parallel-safe]

Files: `Editor/Inspectors/VatTextureSetAssetEditor.cs` only. Read it in full (404 lines),
`VatTextureSetAsset.cs`'s new surface, and §5.6. No fixture — UI. Keep `Conformance_E` in mind: UI
Toolkit only, no `GUILayout`.

### T7 — Gate, drive, docs, version (orchestrator)

1. Full gate per HANDOFF §3, both suites. EditMode gains six; counts must not otherwise drop.
2. **Drive the single tentacle** — the unchanged-behaviour case. VAT Bake window, Clip Set
   `VatSampleTentacleClips`, Rig `VatSampleTentacleRig`, defaults, Bake. Expected, all derivable
   from the code:
   - the line reads `VatSampleTentacle ▸ TentacleMesh · 12 bones` (`SegmentCount = 12`);
   - the log reads `Baked 61 frames of 12 bones into 16x183.` — 2 s × 30 fps = 60 samples, +1 for
     `loopSafe`, width `NextPowerOfTwo(12) = 16`, height 61 × 3;
   - `VatSampleTentacleClipsVatSet.asset`, `…VatBone.asset`, `…VatRuntimeMesh.asset` land beside the
     clip set — **the same three filenames as today** (A78-D14); the set has one part with
     `targetId == 0`; `sourceRigKey` equals the rig's `StableId`; `schemaVersion == 1`;
   - the runtime mesh has UV1 and UV2 populated and `boneWeights.Length == 0`;
   - **nothing was added to the open scene**, and `git status` shows `VatSampleTentacle.prefab`
     unmodified. That last one is the A78-D7 assertion — if the prefab shows modified, stop and
     report; the bake wrote into the asset.
3. **Drive the two-part sample.** Call `CreateTwoPartSampleAssets` into `Assets/A78Scratch/`, bake,
   and assert: two parts in the set, two textures, two runtime meshes, distinct bone counts, and
   `clipRanges` carrying both target ids with each range's `frameStart` inside its own texture's
   height. Then flip one clip so nothing animates the fin, re-bake, and confirm the fin is skipped
   with a named warning and the other part still bakes.
4. **Drive the empty case.** A clip set whose clips have no VAT source and no bone tracks → the panel
   reports it and **no asset is written** (A78-D10). Confirm with `git status`.
5. Prove every write by reloading from disk (HANDOFF §3). Delete `Assets/A78Scratch/` and the produced
   tentacle assets; confirm `git status` is clean of them.
6. §5.8's docs, `CHANGELOG.md`'s `## [0.26.0]`, `package.json` → `0.26.0`.
7. `Assets/_Vault/Memories/Code/AnimationToolkit.md` — one entry: the bake samples one throwaway
   instance of the rig's Source Prefab and calls `VatTextureBaker` once per VAT part, each part
   getting its own texture and its own frame numbering; `VatBakeSourceResolver` is the one place the
   part list is decided; sockets are sampled on the first part's call only.
8. `Docs/AnimationToolkit/HANDOFF.md` §4 — one paragraph.

Commit per task with an `A78-Tn:` prefix, staging paths explicitly, never `git add -A`.

### T8 — ⏸ owner checkpoint

Stop here with this message:

> A78 is in. Open **Window ▸ DOTS Animation Toolkit ▸ VAT Bake** (or the Clip Editor's **VAT Bake**
> tab). The Source column has two fields now, not three — under the Rig is a line saying what the
> bake will cover, `VatSampleTentacle ▸ TentacleMesh · 12 bones` for the sample, or
> `baking 2 of 3 VAT parts · Cape, Hair` with the skipped one named underneath. Click it to ping the
> prefab. Assign `VatSampleTentacleClips` and `VatSampleTentacleRig` and press **Bake VAT Textures**:
> nothing goes into your scene, and the tentacle never has to be dragged in first. Each VAT part of a
> rig now bakes to its own texture and its own runtime mesh in one run.
>
> Two things I would like your eye on before A79 builds the preview toggles — whether that line reads
> as information or as clutter where a field used to be, and whether the skipped-parts second line
> belongs there or in the log below the Bake button.

---

## 7. Out of scope (recorded, with why)

- **The preview toggles and their symbols** — A79, on the owner's instruction. This round's preview
  keeps showing the first resolved part, which is what it has always shown.
- **Authoring `TargetKind` in the Rigs tab.** A78-D3 routes around it. If a target should later
  declare itself a VAT mesh, that is a Rigs-tab amendment and §5.1's target rule gains a `kind` test
  with a fallback, not a rewrite.
- **Any change to `VatTextureBaker`** (A78-D4), including its now-belt-and-braces pose restore. It
  restores the hierarchy it was handed, and it is still the only thing protecting a caller that
  passes a scene object — which the public API still permits.
- **Sharing one texture between two targets.** Two rig targets cannot name the same node
  (`RigAssetUtility.AddTargetToRig` refuses a duplicate path), so the case does not arise. If a rig
  ever wants two parts off one skinned mesh, that is a bone-remapping feature, not a bake loop.
- **Removing the standalone `VatBakeWindow`.** Two hosts for one panel is what A74 shipped.

---

## 8. Build log

*(the executing session fills this in: suite totals at T0, the probe's four readings, any name drift
against §4's line numbers, and the three drives' actual output)*
