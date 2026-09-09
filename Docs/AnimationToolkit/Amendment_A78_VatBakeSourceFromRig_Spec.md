# Amendment A78 — the rig says what to bake, and the bake does every VAT part

> **Status:** ✅ built 2026-09-08 as `0.26.0`. T1–T7 landed and gated (EditMode 814/814, PlayMode
> 283/283, bar the standing `Conformance_A` drift); **T8 ⏸ owner checkpoint is open.** Build log in §8.
> **Prompt:** [`Amendment_A78_VatBakeSourceFromRig_Prompt.md`](Amendment_A78_VatBakeSourceFromRig_Prompt.md).
> **Successor:** [`Amendment_A79_VatPreviewModes_Spec.md`](Amendment_A79_VatPreviewModes_Spec.md) — the
> preview toggles the owner asked for in the same breath. Split on his instruction: baking is
> provable from the produced assets and fixtures, the preview needs his eye, so it gets its own round.
> **Predecessor:** [`Amendment_A76_RigsTab_Spec.md`](Amendment_A76_RigsTab_Spec.md) — A76 is what makes
> this possible: a rig now always carries a Source Prefab and a tickable target list.
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files in
> four waves and never touch MCP. Eleven tasks, none larger than two files.

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
- **A78-D3 — `TargetKind.VatMesh` sharpens resolution but never gates it.** Gating on it outright
  would make every rig authored before T9 unbakeable: nothing set `kind` until this amendment, so
  every rig built through the Rigs tab carries `Quad` on every target and the sample tentacle carries
  no targets at all. So the rule is ordered, not conditional — a `VatMesh` target wins, a target
  merely carrying a skinned mesh is next, a lone skinned mesh is the fallback (§5.1). **Carrying a
  skinned mesh remains the signal; the kind is a tie-breaker.**
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
- **A78-D18 — `Bake` splits into two pure units and a thin panel method** (§5.3). Not for parallelism
  alone: today's `Bake` holds clip selection, orchestration and asset writing in one 84-line method
  inside a 569-line UI file, with **no fixture over any of it** — the two things most likely to be
  wrong per part (which clip feeds which mesh, and what lands where) are today provable only by
  baking and looking. `VatBakeClipBuilder` touches no `AssetDatabase` and no `GameObject`, so its
  every rule is testable; `VatTextureSetBuilder` puts folder creation where folder creation belongs.
  A76 made the same move lifting `RigTargetRowBuilder` out of a panel.
- **A78-D17 — The Rigs tab authors `TargetKind`, because without it A78's textures are correct and
  unusable.** A part only gets `VatDriven` and its VAT shader properties at entity bake when its kind
  resolves to `VatMesh` (`RigTargetBaker.AddTechniqueComponents`, `:283-291`), and the only route
  today is hand-ticking `useKindOverride` on every prefab child. A `Quad`-kinded VAT part renders as a
  motionless clump with no error. One dropdown beside the Tag button closes it (§5.8). This is the
  ninth task and it is not optional: it is what makes the rest of the amendment usable on an actor.
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
| one or more `kind == VatMesh` targets resolve to a skinned mesh | ✅ one source per such target, in `rig.targets` order |
| none do, but one or more targets of any kind resolve to a skinned mesh | ✅ one source per such target, in `rig.targets` order |
| neither, exactly one skinned mesh in the prefab | ✅ one source, `TargetId = 0` |
| neither, more than one skinned mesh | `"'<prefab>' has <n> skinned meshes and none of them is a rig target, so the bake cannot tell which to sample: <paths>. Tick the ones you want in the Clip Editor's Rigs tab, and set their Kind to VAT Mesh."` |

Paths come from `PrefabAuthoringBridge.GetHierarchyPath(rendererTransform, rig.sourcePrefab.transform)`,
joined `", "`. A skinned mesh **on the prefab root** yields the empty path, which is legal and which
`ResolveByPath` maps back to the root — do not skip it the way `RigTargetRowBuilder` skips a root
renderer. It can never match a target (a target's `sourceNodePath` is empty only when unbound), so it
only ever reaches the single-mesh rule. Target matching is `StringComparison.Ordinal`, skipping null
targets and empty paths. The two target rules differ only in their `kind` filter — collect both lists
in one hierarchy walk and prefer the `VatMesh` one when it is non-empty, rather than walking twice.

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

### 5.3 The bake, in three pieces

`Bake` today is one 84-line method holding clip selection, orchestration and asset writing at once,
in a 569-line UI file, with no fixture over any of it. Multiplying it by N parts without splitting it
would produce the largest single-file change in the amendment and leave its two most error-prone
parts — which clip feeds which mesh, and what gets written where — testable only by baking. So the
logic comes out into two pure units and the panel keeps the orchestration (A78-D18). This is the same
move A76 made when it lifted `RigTargetRowBuilder` and `RigTargetReferenceResolver` out of a panel.

```
5.3a VatBakeClipBuilder   pure     which clips feed which part, and what nothing bakes
5.3b VatTextureSetBuilder assets   N textures + N meshes + one set asset
5.3c VatBakePanel.Bake    UI       guards, one instance, the loop, the reporting
```

#### 5.3a New — `Editor/VatBaking/VatBakeClipBuilder.cs`

```csharp
/// <summary>One VAT part's bake list: the source it samples and the clips that feed it.</summary>
public sealed class VatBakeSourcePlan
{
    public VatBakeSource Source;
    public List<VatBakeClip> Clips;
}

/// <summary>What a bake would do: the parts that have clips, the parts that have none, and the tracks that name nothing.</summary>
public sealed class VatBakePlan
{
    public List<VatBakeSourcePlan> Sources;        // only sources with at least one clip
    public List<string> SkippedPartNames;          // resolved parts no clip in the set animates
    public List<string> UnknownTrackTargets;       // vatTracks rows naming targets no source covers
    public bool HasAnythingToBake { get; }         // Sources.Count > 0
}

/// <summary>Works out which clips feed which VAT part of a rig, and which parts and tracks nothing will bake.</summary>
public static class VatBakeClipBuilder
{
    public static VatBakePlan Build(ClipSetAsset clipSet, List<VatBakeSource> sources);
}
```

Per source, per clip in the set, in this order:

1. A `vatTracks` row whose `targetId == source.TargetId` (and `source.TargetId != 0`) →
   `animationClip = track.sourceClip`, `boneTracks = null`, `loopSafe = track.loopSafe`.
2. Otherwise, if the clip has `vatSource.sourceClip` or any `boneTracks` → `animationClip =
   clip.vatSource?.sourceClip`, `boneTracks = clip.boneTracks`, `loopSafe = clip.vatSource?.loopSafe`.
   This is the fallback that makes a part with no dedicated track follow the clip-wide source, and it
   is what bakes the tentacle (authored bone tracks, no imported clip).
3. Otherwise the clip contributes nothing to this part.

Every emitted `VatBakeClip` carries `targetId = source.TargetId`, `clipId = clip.Id.Value`,
`samplesPerSecond = clip.frameRate`, `durationSeconds = clip.duration`. Move the three existing
comments from `VatBakePanel.cs:342-353` across with the code — the id-provenance one, the
per-clip-FPS one and the loop-safe one — they are all still true and none of them is obvious.

`SkippedPartNames` gets a source whose clip list came out empty (A78-D10). `UnknownTrackTargets` gets
the `displayName`-or-hex of every `vatTracks` row whose `targetId` matches no source in `sources`
(A78-D11) — swept once over the whole set, not per source, so a track naming a missing target is
reported once rather than N times.

This file touches no `AssetDatabase`, no `UnityEditor` UI type, and no `GameObject`. That is what
makes it worth extracting: every rule in it is testable from three `CreateInstance` calls.

#### 5.3b New — `Editor/VatBaking/VatTextureSetBuilder.cs`

```csharp
/// <summary>One part's bake output, paired back with the source that produced it.</summary>
public sealed class VatBakePartResult
{
    public VatBakeSource Source;
    public VatBakeResult Result;
}

/// <summary>Writes a bake's textures, runtime meshes and the VatTextureSetAsset that indexes them.</summary>
public static class VatTextureSetBuilder
{
    /// <returns>The asset path of the written set.</returns>
    public static string WriteSet(
        ClipSetAsset clipSet,
        RigAsset rig,
        VatFlavor flavor,
        string outputFolder,
        List<VatBakePartResult> partResults);
}
```

This is today's `SaveResult` (`:381-455`) with a loop around its middle. Per part: the texture
assets named by A78-D14, and `runtimeMesh` from `VatMeshPreparer.TryCreateRuntimeMesh(
partResult.Source.PrefabRenderer, …)` — the **prefab** renderer, which reads only `sharedMesh`
(`VatMeshPreparer.cs:36-49`) and outlives the bake instance. Then one `VatTextureSetAsset` carrying
every part entry, `clipRanges` concatenated from every part result in part order, `socketTracks` from
the **first** part result only (A78-D5), `sourceRigKey = rig.StableId`, and `schemaVersion = 1`
(A78-D15).

`ResolveOutputFolder` stays on the panel — it reads a text field — and hands the resolved string in.
`EnsureFolderPath`, `CreateOrReplaceAsset`, the `clipSet.vatTextures` assignment and the
`SaveAssets`/`Refresh` pair move here with it; the panel has no business owning folder creation.

Keep the comment at `:423` explaining why bone influences are packed for the bone flavour only, and
the one at `:436-438` explaining why a missing runtime mesh warns rather than fails. Both are still
exactly true, and the second matters more now that one part can fail while others succeed.

#### 5.3c `VatBakePanel.Bake` — `:210-293`

What is left, after the existing clip-set and rig guards:

```
TryResolve                        → ReportFailure and return
VatBakeClipBuilder.Build          → ReportFailure and return when !HasAnythingToBake (A78-D10)
                                  → Debug.LogWarning per SkippedPartName and UnknownTrackTarget
TryCreateBakeInstance             → ReportFailure and return
try {
    for each plan in Sources:
        FindInInstance, then VatTextureBaker.Bake(that renderer, that plan's clips,
                                                  sockets on the first call only)
        → ReportFailure and return on a failed part
        collect a VatBakePartResult
} finally { DestroyImmediate(instanceRoot) }
VatTextureSetBuilder.WriteSet     → ReportSuccess, fill the preview set field, refresh
```

Hoist `VatFlavor bakeFlavor = (VatFlavor)flavorField.value;` above the try. Keep the two
unresolved-name warning blocks (`:265-285`) inside the loop, prefixing each with the part's
`DisplayName` so N parts do not produce N anonymous warnings; the bone-track one's closing sentence —
"Check the names on the clip's bone tracks against the skinned mesh you assigned" — becomes "…against
the rig's source prefab." `ReportSuccess` (`:457-482`) gains the part count and keeps its range table.

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
- **New `Runtime/Components/VatPartTextureBinding.cs`** — `IComponentData` carrying
  `UnityObjectRef<Texture2D> boneOrPositionTexture` and `normalTexture`, on the part entity. Its own
  file, matching the one-component-per-file convention already in that folder, so the task that adds
  it and the task that strips `VatTextureBinding` share nothing.
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

### 5.8 The kind picker — `RigsPanel.cs` and `RigAssetUtility.cs` (A78-D17)

`RigAssetUtility` gains `SetTargetKind(RigAsset rig, uint targetStableId, TargetKind kind)`, a
character-for-character mirror of `SetTargetTag` (`:116-139`) with `"Set Rig Target Kind"` as the undo
label. Same order — `Undo.RecordObject`, mutate, `SetDirty`, `SaveAssetIfDirty` — for the same reason
A76-D5 gives: `stableId` is `internal`, so the `SerializedProperty` route would write a minted id back
to 0.

`RigsPanel.CandidateRow` gains `public TargetKind Kind;` and `public Button KindButton;`. The row
becomes `[toggle | kind | tag]`: a `Button` built beside `tagButton` (`:472-479`) with the same
`flexShrink = 0f`, `marginLeft = 4f` and a `minWidth` of `100f`, disabled by the same `SetEnabled(ticked)`
rule and for the same reason — an unticked node is not becoming a target, so its kind would go
nowhere. Text is `"Kind: Quad"` / `"Kind: VAT Mesh"` / `"Kind: Flipbook"` through a
`RefreshKindButtonText(row)` mirroring `RefreshTagButtonText` (`:389-405`).

Clicking opens a three-item `GenericDropdownMenu`, `DropDown(anchor.worldBound, anchor,
DropdownMenuSizeMode.Auto)` — the UI Toolkit menu, proven at `ActorEditorInspectorColumn.cs:449-469`.
**Not `GenericMenu`**, which is IMGUI and fails `Conformance_E`. Choosing writes through
`SetTargetKind`, refreshes the button and nothing else: the row is mid-dispatch, and rebuilding the
list from inside its own callback is the trap the vault note names.

`RigTargetRowBuilder.RigTargetRow` gains `public TargetKind Kind;`, filled from the matched target in
`BuildForRig` and left `TargetKind.Quad` in `BuildForNewRig` — a node that is not yet a target has no
kind to carry, and create mode's rows are all new. `RigsPanel` copies it into `CandidateRow` where it
already copies `TagId` (`:495`).

Create mode writes the chosen kind into the `RigTargetDefinition`s it builds, so a rig can be created
with its VAT parts already marked rather than needing a second pass.

### 5.9 Docs

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

| Wave | Tasks | Why they can share a wave |
|---|---|---|
| — | **T0** (orchestrator) | Baseline and the probe. Its answer can change T3c. |
| 1 | **T1, T2, T6, T9** | Four disjoint file pairs. T9 touches only Rigs-tab files. |
| 2 | **T3a, T3b** | Two new files. T3a needs T1's `VatBakeSource`; T3b needs T2's `VatPartTextures`. |
| 3 | **T3c** | The only task that edits `VatBakePanel.cs`, and it calls all three of the above. |
| 4 | **T4a, T4b, T5** | Disjoint: `ActorBaker`+`VatTextureBinding`, `RigTargetBaker`+the new component, the inspector. |
| — | **T7** (orchestrator), **T8** (⏸ checkpoint) | |

No task edits a file another task in its wave edits. Eleven subagent tasks, none larger than two
files; the two that were biggest before splitting — the panel and the bakers — are now four.

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
- `TryResolve_PrefersVatMeshTargets_OverTargetsThatMerelyCarryAMesh` — two targeted skinned meshes,
  one `kind == VatMesh` → one source, the `VatMesh` one. Then set both to `Quad` → both come back.
  *(Revert-to-fail: drop the kind pass — the first case returns two sources. This is A78-D3's ordered
  rule, and the second half is what keeps every rig authored before T9 bakeable.)*
- `TryCreateBakeInstance_MakesAHiddenCopy_AndFindInInstanceHitsTheSameNode` — instance root is not
  the prefab root, `hideFlags == HideAndDontSave`, and `FindInInstance` returns a renderer belonging
  to the instance. `DestroyImmediate` it in the test.

Six tests, no more. No fixture for the null-rig guard.

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

### T9 — The kind picker [parallel-safe]

Files: `Editor/ClipEditor/Authoring/RigsPanel.cs`, `Editor/ClipUtilities/RigAssetUtility.cs`. Also
add `RigTargetRow.Kind` in `Editor/ClipEditor/Authoring/RigTargetRowBuilder.cs` — three lines, so
this task is three files by exception; read only `:10-20` and `:95-125` of that one. Read
`RigsPanel.cs:14-27, 356-405, 455-510` and the `Create` method in full, `RigAssetUtility.cs:110-140`,
`ActorEditorInspectorColumn.cs:440-470` (the `GenericDropdownMenu` pattern), and §5.8. Build §5.8.

- Fixture, appended to `Tests/EditMode/RigAssetUtilityTests.cs`:
  `SetTargetKind_WritesTheKind_AndQuadIsLegal` — set `VatMesh` → true and readable; set back to
  `Quad` → true and reads `Quad`. *(Revert-to-fail: early-return on `kind == TargetKind.Quad`, the
  same shape the tag method's zero case has.)*
- Fixture, appended to `Tests/EditMode/RigsPanelTests.cs`:
  `SelectRig_CarriesEachTargetsKindOntoItsRow` — a rig with one `Quad` target and one `VatMesh`
  target; after `SelectRig`, the two rows carry the two kinds. *(Revert-to-fail: leave `Kind` at its
  default in `BuildForRig` — both rows come back `Quad`, which is the bug that would make every VAT
  part on screen a motionless clump.)*

Do not touch `ClipSetsPanel.cs` or `RigCatalogColumn.cs`. **Create mode's existing behaviour must not
change** beyond carrying the chosen kind into the definitions it builds.

### T3a — `VatBakeClipBuilder` + fixture [parallel-safe with T3b]

Files: **new** `Editor/VatBaking/VatBakeClipBuilder.cs`, **new**
`Tests/EditMode/VatBakeClipBuilderTests.cs`. Read `VatBakePanel.cs:317-379` (the method being lifted,
comments and all), `ClipAsset.cs`'s `VatTrack` and `VatClipSource` declarations, `VatBakeSource`'s
declaration in `VatBakeSourceResolver.cs`, and §5.3a. Build §5.3a.

This file must reference no `AssetDatabase`, no `GameObject` and no `UnityEditor` UI type. If you find
yourself needing one, the boundary is wrong — say so in your report rather than widening it.

Fixtures build `ClipAsset`s and `VatBakeSource`s with `CreateInstance` / object initialisers; a
`VatBakeSource` needs no live renderer for these tests, only its `TargetId` and `DisplayName`.

- `Build_ATargetedTrackWins_OverTheClipWideSource` — a clip with `vatSource.sourceClip = walkClip`
  and a `vatTracks` row for target 7 naming `capeClip`; sources `{0…no, 7, 9}`. Target 7's plan
  carries `capeClip` with no bone tracks; target 9's carries `walkClip` **and** the clip's bone
  tracks. *(Revert-to-fail: check `vatSource` first — target 7 bakes the body's animation onto the
  cape, silently.)*
- `Build_APartNoClipAnimates_IsSkippedByName_NotDropped` — a set whose only clip has neither a
  `vatSource`, bone tracks, nor a row for target 9 → target 9 is absent from `Sources` and present in
  `SkippedPartNames`. *(Revert-to-fail: drop the skip list — the part vanishes with no warning, which
  is the silence A78-D10 exists to prevent.)*
- `Build_ATrackNamingNoSource_IsReportedOnce_NotPerSource` — a `vatTracks` row for target 42 with
  three sources present → `UnknownTrackTargets` has exactly one entry.
- `Build_NothingBakeable_ReportsHasAnythingToBakeFalse` — no clip carries any VAT content →
  `HasAnythingToBake == false` and `Sources` empty.

Four tests. Do not test the field-by-field copy into `VatBakeClip`; the compiler covers it.

### T3b — `VatTextureSetBuilder` [parallel-safe with T3a]

Files: **new** `Editor/VatBaking/VatTextureSetBuilder.cs`. Read `VatBakePanel.cs:381-455` (the method
being lifted) and `:540-567` (`EnsureFolderPath`, `CreateOrReplaceAsset`, moving with it),
`VatTextureSetAsset.cs`'s new surface, `VatMeshPreparer.cs:14-30`, and §5.3b. Build §5.3b.

No fixture. This is `AssetDatabase` plumbing whose correctness is "the right files land in the right
folder", which T7 drives three times against real assets — and a fixture would need an `Assets/`
prefixed literal, which `Conformance_D` scans for. Do not write one.

Leave `ResolveOutputFolder` on the panel; it reads a text field.

### T3c — The panel

Files: `Editor/VatBaking/VatBakePanel.cs` only. Read it in full, the public surfaces of
`VatBakeSourceResolver`, `VatBakeClipBuilder` and `VatTextureSetBuilder` (all three exist by now),
`ToolkitPalette.cs:14-27`, and §5.3c + §5.4. Build both.

The file should come out **shorter than it went in**: `CollectVatClips`, `SaveResult`,
`EnsureFolderPath` and `CreateOrReplaceAsset` all leave with T3a and T3b. If it grew, the two
builders are not being called and the logic was reimplemented — report that rather than shipping it.

No fixture — UI wiring plus orchestration (HANDOFF §2); T1, T2 and T3a cover the logic and T7 drives
the real thing. Do not touch `VatTextureBaker.cs` (A78-D4), `VatBakeWindow.cs` or
`ClipEditorWindow.cs` — neither host knows the field exists.

Check when done: `grep -n "skinnedRendererField\|CollectVatClips\|SaveResult" Editor/VatBaking/VatBakePanel.cs`
returns nothing.

### T4a — The runtime component and `ActorBaker` [parallel-safe]

Files: `Runtime/Components/VatTextureBinding.cs`, `Authoring/Baking/ActorBaker.cs`. Read
`VatTextureBinding.cs` in full (21 lines), `ActorBaker.cs:560-580` and `:1025-1045` **only**,
`VatTextureSetAsset.cs`'s new surface, and §5.5's first and second bullets.

No new fixture. If an existing fixture asserts on `VatTextureBinding`'s texture fields, move the
assertion to `VatPartTextureBinding` rather than deleting it.

### T4b — `RigTargetBaker` and the per-part component [parallel-safe]

Files: **new** `Runtime/Components/VatPartTextureBinding.cs`, `Authoring/Baking/RigTargetBaker.cs`.
Read `:265-401`, `VatTextureSetAsset.cs`'s new surface, `VatTextureBinding.cs` in full as the shape to
mirror, and §5.5's third bullet. The component is declared here, not in T4a, so the two tasks share no
file: one component per file is the existing convention in that folder.

No new fixture. `PackagingConformanceTests` and the existing baker fixtures are the gate.

### T5 — The inspector [parallel-safe]

Files: `Editor/Inspectors/VatTextureSetAssetEditor.cs` only. Read it in full (404 lines),
`VatTextureSetAsset.cs`'s new surface, and §5.6. No fixture — UI. Keep `Conformance_E` in mind: UI
Toolkit only, no `GUILayout`.

### T7 — Gate, drive, docs, version (orchestrator)

1. Full gate per HANDOFF §3, both suites. EditMode gains thirteen; counts must not otherwise drop.
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
5. **Drive the kind picker.** On a `CopyAsset` scratch rig under `Assets/A78Scratch/`, set a target's
   Kind to VAT Mesh through the Rigs tab, `Refresh()`, load the rig fresh from its path and assert
   `kind == TargetKind.VatMesh`. "The dropdown says VAT Mesh" is not proof (HANDOFF §3). Then confirm
   `Undo.PerformUndo()` puts it back to `Quad`.
6. Prove every write by reloading from disk (HANDOFF §3). Delete `Assets/A78Scratch/` and the produced
   tentacle assets; confirm `git status` is clean of them.
7. §5.9's docs, `CHANGELOG.md`'s `## [0.26.0]`, `package.json` → `0.26.0`.
8. `Assets/_Vault/Memories/Code/AnimationToolkit.md` — one entry: the bake samples one throwaway
   instance of the rig's Source Prefab and calls `VatTextureBaker` once per VAT part, each part
   getting its own texture and its own frame numbering; `VatBakeSourceResolver` is the one place the
   part list is decided; sockets are sampled on the first part's call only.
9. `Docs/AnimationToolkit/HANDOFF.md` §4 — one paragraph.

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
> The Clip Editor's **Rigs** tab also has a **Kind** button on every target row now, beside the Tag.
> That is what marks a part as a VAT mesh, and without it a baked part renders as a motionless clump
> at run time with no error — so it is worth setting on your rigs before you next bake an actor.
>
> Three things I would like your eye on before A79 builds the preview toggles — whether the
> resolved-source line reads as information or as clutter where a field used to be, whether the
> skipped-parts second line belongs there or in the log below the Bake button, and whether Kind wants
> to be a button like Tag or a plain dropdown.

---

## 7. Out of scope (recorded, with why)

- **The preview toggles and their symbols** — A79, on the owner's instruction. This round's preview
  keeps showing the first resolved part, which is what it has always shown.
- **Authoring `vatTracks`.** "This part plays this source clip" is still a raw `targetId` typed into
  the `ClipAsset`'s default inspector — nothing in `Editor/` writes `vatTracks` or `vatSource`, and a
  raw uint in an editor surface is against the standing names-never-numbers directive (HANDOFF §5).
  A target picker where that uint is belongs with the material-binding gap below, in one authoring
  round after A79. A78 makes it *matter more*, so record it rather than quietly leaving it.
- **Binding each part's baked texture onto its material.** Per-part materials are still wired by hand.
  A78 makes `ValidateVatMaterial` per-part, so the warning finally names the right part instead of
  comparing every part against one texture — but it wires nothing.
- **Any change to `VatTextureBaker`** (A78-D4), including its now-belt-and-braces pose restore. It
  restores the hierarchy it was handed, and it is still the only thing protecting a caller that
  passes a scene object — which the public API still permits.
- **Sharing one texture between two targets.** Two rig targets cannot name the same node
  (`RigAssetUtility.AddTargetToRig` refuses a duplicate path), so the case does not arise. If a rig
  ever wants two parts off one skinned mesh, that is a bone-remapping feature, not a bake loop.
- **Removing the standalone `VatBakeWindow`.** Two hosts for one panel is what A74 shipped.

---

## 8. Build log

### T0 — baseline and probe (2026-09-08)

Head is `0.25.0` (A77), so A78 takes `0.26.0` as specced. Compile gate clean, zero console errors.

**Suite baselines.** EditMode `DotsAnimationToolkit.Tests.EditMode` discovered **801**, one failure —
the standing `Conformance_A` asmdef drift (`DotsAnimationToolkit.Editor` carries an eighth reference,
`Unity.RenderPipelines.Universal.Runtime`, that architecture §1.3 does not list). Not this
amendment's, unchanged since A76. PlayMode `DotsAnimationToolkit.Tests.PlayMode` **283/283**, green.
Both match HANDOFF §4's recorded totals exactly. A78 must land at EditMode 814 (801 + 13).

**The probe** (one `execute_code` call, CodeDom, against the real sample prefab). Four readings:

1. **A `HideAndDontSave` instance is not in any scene at all.** `instance.scene.IsValid()` is false and
   the active scene's `rootCount` is unchanged (2 → 2 → 2) across instantiate and destroy.
2. **It never dirties the open scene.** `isDirty` is `False` at all four measurement points — before,
   after `Instantiate`, after posing, after `DestroyImmediate`. **A78-D7 stands as written and T3c is
   unchanged: no `EditorSceneManager.NewPreviewScene()` is needed.**
3. **It is poseable.** Inside `StartAnimationMode` → `BeginSampling` → write `Bone3.localRotation` →
   `EndSampling`, the rotation stuck (`poseStuck=True`), which is what the bake needs.
4. **`StopAnimationMode()` does not revert that write** (`revertedByAnimationMode=False`) —
   independently reproducing what `VatTextureBaker.cs:186-189` already measured and snapshots around.
   Harmless on a throwaway instance; it is precisely why handing the baker a prefab asset would write
   the last sampled pose into the `.prefab` on disk, which is A78-D7's whole premise.

`Object.Instantiate` also confirmed to produce **no** prefab link (`prefabLinked=False`), as A78-D7
requires. The sample prefab holds 14 transforms: root + `Bone0`…`Bone11` + `TentacleMesh`.

### Wave 1 — T1, T2, T6, T9 (2026-09-08)

All four landed. Compile gate after the wave reported **exactly eight `CS1061` errors, all of them in
`ActorBaker.cs` (571-573, 1040-1042) and `RigTargetBaker.cs` (357-358)** — the deleted singular
fields, which are T4a's and T4b's work. **Zero errors in any file wave 1 wrote**, which is the whole
signal the wave gate exists to give. Committed as `A78-T1`, `A78-T2`, `A78-T6`, `A78-T9`; not pushed,
because the tree is deliberately red until the consumers land.

Two deviations, both recorded rather than silently taken:

- **T2 wrote two fixtures where §6 says one.** Its second test asserted the no-untargeted-part case
  separately. Folded back into `TryGetPart_PrefersTheExactTarget_ThenFallsBackToUntargeted` as three
  more assertions, so the amendment still adds the thirteen EditMode tests §6 budgets.
- **§5.8's create-mode paragraph is stale and was skipped.** A77 removed the Rigs tab's create form:
  `CreateAndSelectNewRig` now calls `RigAssetUtility.CreateRig(assetPath, null, null)` with no
  targets, and `OnRowToggleChanged` early-returns when `SelectedRig == null`. There is no create mode
  left to carry a chosen kind into, so T9 built none. Everything else in §5.8 is unaffected.

**Wave order changed after the wave-1 gate: T4a and T4b moved from wave 4 into wave 2.** The eight
errors are all in the `Authoring` assembly, and `Editor` compiles *against* `Authoring` — so with
T4a/T4b left in wave 4, waves 2 and 3 would have produced no compile signal whatsoever for their own
files, which defeats the per-wave gate. T4a (`VatTextureBinding` + `ActorBaker`) and T4b
(`VatPartTextureBinding` + `RigTargetBaker`) share no file with T3a (`VatBakeClipBuilder`) or T3b
(`VatTextureSetBuilder`), so §6's "no two tasks in one wave edit the same file" invariant still holds.
Revised: **wave 2 = T3a, T3b, T4a, T4b; wave 3 = T3c, T5** (`VatBakePanel.cs` and
`VatTextureSetAssetEditor.cs`, also disjoint). No task was dropped, added or re-scoped.

### Wave 2 — T3a, T3b, T4a, T4b (2026-09-08)

All four landed and committed as `A78-T3a`, `A78-T3b`, `A78-T4a`, `A78-T4b`. Two recorded judgments
and one escalation.

- **`sourceHash` on a multi-part set is taken from the first part result** (T3b's flagged judgment).
  §5.3b's "carrying:" list is silent on the field, and today's `SaveResult` writes the single bake's
  hash, so first-part is the closest preservation of existing behaviour — and it keeps a single-part
  bake's stored hash identical, which A78-D14 cares about. Checked before accepting: the staleness
  comparison in `ClipValidation.cs:146-159` is **dormant in production** — `vatSourceHashRecomputed`
  defaults to false and no shipping caller passes a recomputed hash; only `ClipValidationTests` does.
  Folding every part's hash together would be inventing behaviour the spec did not ask for. Recorded
  rather than done: if multi-part staleness is ever wanted, that fold is the change.
- **`UnknownTrackTargets` emits the hex form only, never a display name.** A `VatTrack` carries no
  display name, and by definition the target it names matches no source, so there is nothing to read
  one from. §5.3a's "displayName-or-hex" is unreachable in its first half.

**Escalation — §5.5's consumer list is incomplete, and the prompt's "A78 does not touch
`VatPreviewElement`" cannot hold.** Two A74-owned preview sources read the set's deleted singular
fields and therefore cannot compile: `VatPreviewElement.cs` (bone count, texture dimensions,
`runtimeMesh` bounds, and the `DrawMesh` call) and `VatPreviewMaterial.cs` (the texture, the texel
params and the runtime mesh). §2's "per-part textures already reach the GPU" is true of the *runtime*
but not of the *editor preview*, which reads the set directly. Resolved by the smallest mechanical
migration that preserves today's behaviour exactly — `VatPreviewMaterial` takes the part alongside the
set, and `VatPreviewElement` resolves the **first** entry in `parts` and uses it at each site, which
is precisely what §5.4 already says the preview shows. **No preview feature was added**; the toggles
remain A79's. Recorded here rather than silently taken, and worth folding into A79's "Read first".

### Wave 3, and the fixture fallout (2026-09-08)

T3c and T5 landed alongside the preview migration. **`VatBakePanel.cs` came out at 555 lines from
569** — the §6 size check passes, so the builders are genuinely being called.

The compile after wave 3 showed **all production code green and every remaining error in a test
fixture** — twenty of them, in seven files §5.5 does not mention: the two shared fixture builders
(`AuthoringTestAssets.cs`, `ActorBakeFixture.cs`) plus `ContentHashGoldenTests`,
`ClipRegistryBuilderTests`, `ClipRegistryDeterminismTests`, `ActorBakingAcceptanceTests`, and
`DataContractTests` (which compiled but would have failed at run time on a stale field contract).
Each migrated mechanically onto one untargeted `VatPartTextures` carrying identical values, so the
golden content hash still reaches the blob by the same path.

**The acceptance test's texture assertion moved rather than being deleted.** The shared PlayMode rig's
three targets are all `Quad`, so no part entity there carries `VatPartTextureBinding`; adding a fourth
target would have perturbed a fixture many tests share. It moved instead into
`AVatPartBoundToTheBakedTexture_WarnsAboutNothing`, which already bakes a correctly configured
`VatMesh` part — and because that set's only part is untargeted, the assertion also exercises
`TryGetPart`'s fallback. It passes.

### Revert-to-fail (2026-09-08)

The five load-bearing fixtures were proven in one pass — five mutations, one compile, one run — then
restored and re-run green. Each failed with the predicted symptom:

| Mutation | Fixture | Observed failure |
|---|---|---|
| Drop the `VatMesh` preference pass | `TryResolve_PrefersVatMeshTargets_…` | Expected 1, was 2 |
| Never find a targeted track | `Build_ATargetedTrackWins_…` | wrong `AnimationClip` — the silent wrong-part bake |
| Drop `TryGetPart`'s fallback | `TryGetPart_PrefersTheExactTarget_…` | Expected True, was False |
| Early-return on `Quad` | `SetTargetKind_WritesTheKind_AndQuadIsLegal` | Expected True, was False |
| Leave `Kind` default in `BuildForRig` | `SelectRig_CarriesEachTargetsKindOntoItsRow` | "Kind: Quad", expected "Kind: VAT Mesh" |

### T7 — the four drives (2026-09-08)

Suites: **EditMode 814/814** (801 + 13, only the standing `Conformance_A` drift), **PlayMode
283/283**. Counts did not drop.

1. **The single tentacle, driven through the real panel** (window opened, fields set, `Bake` invoked).
   The line read exactly `VatSampleTentacle ▸ TentacleMesh · 12 bones`. Reloaded from disk:
   `VatSampleTentacleClipsVatSet.asset`, `…VatBone.asset`, `…VatRuntimeMesh.asset` — **the same three
   filenames as before A78** (A78-D14); one part with `targetId == 0`; `schemaVersion == 1`;
   `sourceRigKey` equal to the rig's `StableId`; texture 16×183 (`NextPowerOfTwo(12)` × 61×3); range
   `frameStart 0, frameCount 61`; runtime mesh 26 verts with both UV channels populated and
   `boneWeights.Length == 0`. Scene `rootCount` unchanged and not dirty, and **`git status` showed
   `VatSampleTentacle.prefab` unmodified** — the A78-D7 assertion.
2. **The two-part sample.** `CreateTwoPartSampleAssets` produced a rig with two `VatMesh` targets, and
   the plan exercised **both clip-selection paths in one run**: `Tentacle` via the clip-wide bone
   tracks, `Fin` via its dedicated `vatTracks` row. The bake wrote two distinct textures (16×183 and
   8×183, from 12 and 6 bones), two distinct runtime meshes, per-part filenames
   `…VatTentacleBone.asset` / `…VatFinBone.asset`, and **both ranges at `frameStart 0`** — each
   indexing its own texture, exactly `183 <= 183`.
3. **The skip case.** With the clip's bone tracks cleared and its one `vatTracks` row retargeted at
   the tentacle, the label read `baking 1 of 2 VAT parts · Tentacle` / `Fin — no clip in this set
   animates it`, the console carried `'Fin': no clip in 'VatSampleTentacleTwoPartClips' animates this
   VAT part. It will not be baked.`, and the reloaded set held one part. The other part still baked.
4. **The empty case.** A clip set whose only clip had no VAT content: bake refused, **no set asset was
   written**, and `clipSet.vatTextures` stayed null (A78-D10). This drive found the one defect of the
   pass — the headline read `baking 0 of 2 VAT parts · ` with a dangling separator — fixed in
   `A78-T7`.
5. **The kind picker**, on a scratch rig: `SetTargetKind` returned true, and the write was proved by
   loading the asset fresh from its path (`kind == VatMesh`) **and** by reading `kind: 1` out of the
   raw YAML — not by the dropdown's own label. `Undo.PerformUndo()` restored the previous kind,
   asserted on the in-memory instance with no `Refresh` in between, per the recorded trap.

Scratch assets deleted and `git status` confirmed clean of them afterwards.

### Foreign working-tree changes — not A78's, deliberately left alone

`Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs` and a new section in
`Assets/_Vault/Memories/Code/AnimationToolkit.md` (making the cutscene inspector pane a draggable
`TwoPaneSplitView`) appeared in the working tree at 21:34, about eight minutes **before** this
session's first subagent wrote anything at 21:42. They are another session's uncommitted work — the
subject matches a peer session named "unify editor window controls". Not reverted, and deliberately
staged into no A78 commit. If a cutscene layout fixture moves at the final gate, that is its doing.

**Drift found at T0 — the sample rig is no longer untargeted.** `git status` carried one uncommitted
modification: `Assets/ScriptableObjects/Animations/VatSampleTentacle/VatSampleTentacleRig.asset` has
gained a single target (`displayName: TentacleMesh`, `sourceNodePath: TentacleMesh`,
`stableId: 708564151`, `kind: 0`/Quad) where the committed asset — and §2 and T7 step 2 of this spec —
says `targets: []`. Almost certainly residue from an A76/A77 Rigs-tab drive, since that tab writes a
tick straight to the asset. It does not affect any subagent task, but it moves the tentacle from
A78-D2's untargeted fallback to its second rule, which under A78-D14 changes the output filenames to
`…VatTentacleMeshBone.asset` and gives the part a non-zero `targetId`. **Resolved before T7's drive**
— see the T7 entry below.
