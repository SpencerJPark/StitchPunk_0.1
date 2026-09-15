# A103 — Unified authoring and the whole-animation VAT preview

> **Status:** 📝 specced 2026-09-15 against `556823b7` (package `0.54.0`), takes `0.55.0`; not built. Merges
> [`UnifiedClipAuthoring_System.md`](../NewPlans/UnifiedClipAuthoring_System.md) P1–P5 (UA) with
> [`Amendment_A79_VatPreviewModes_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A79_VatPreviewModes_Spec.md), per
> `Code_Audit_2026-09.md` §3 item 2. Both source specs are superseded by this one.
> **Executor:** one `spec-lead` in its own worktree for §5 (three worker waves), the **stage orchestrator** for §6
> (window wiring, drives, close). No tab is added. No Play mode.
> **Why one spec:** the Clip Editor cannot preview VAT, and the VAT Bake preview cannot show the cutout half of the
> same animation. Both come from one coupling: posing needs the baked cutout registry, and only the Clip Editor's
> controller knows how to build and apply it. One poser split serves both windows.

## 0. Session prompt

See `Assets/_Vault/Spencer/next-session-a103-prompt.md`.

## 1. Decisions (do not re-ask)

The first four settle UA's three ← DECISION markers. The owner delegated them, and the audit (§3.2) records them.
The rest were recorded 2026-09-15 under the standing delegation.

- **A103-D1 — A targetless rig is fine when the bound sets animate bones or VAT (UA P1, option b).** Show no banner
  when any bound clip has `boneTracks`, a `vatSource`, or a `vatTracks` row, or when a bound set has `vatTextures`.
  Otherwise show one informational status line: *"Rig 'X' has no parts, and nothing in this set keys bones or
  names a VAT source — nothing here will move."* It is not a warning and not a validation rule, and it adds no
  Health finding (none exists today — §1.1 drift 2).
- **A103-D2 — Every rig builds a registry, including a rig with no targets (UA P2).** The builder already produces
  a zero-target blob (`ClipRegistryBuilderTests.Build_ProducesAZeroBox_WhenTheRigDeclaresNoTargets`). The gate is
  `ClipPreviewController.Refresh`'s `rigMirror.PartCount == 0` early return, and it goes. `ClipRegistryBuilder` is
  not edited. One code path serves every kind, and "no registry" now only means no rig, no set, or a bind that
  fails validation.
- **A103-D3 — The poser split's shape.** The split creates a new `RegistryTargetPoser` class
  (`Editor/ClipEditor/Preview/`). It owns the `Persistent` blob and rebuilds it from a rig plus sets, reporting a
  `RegistryBuildOutcome` (`Built`, `NoRig`, `NoClipSets`, `ValidationErrors`, `Failed`) so each host words its own
  status. It resolves a clip index, and poses every dense target through
  `ClipSampler.SamplePose` into a nested `ITargetPoseWriter`
  (`bool TryGetRestPose(uint targetId, out TargetRestPose restPose)` returning false to skip a target, and
  `void WritePose(uint targetId, in TargetPose pose)`). The per-kind split inside `ClipPreviewController.SamplePose`:
  - **Bones:** `skeletonMirror.ApplyBoneTracks`, registry-free, already so since P0.
  - **Targets:** transforms and flipbooks together through the poser. They are **one** `TargetPose` per sampler
    call (`sliceIndex`/`atlasRect` come out of the same call), so splitting sprites off would need a second
    sampler, which A79-D5 forbids.
  - **Billboards:** stay in `Render`, after the camera, in the runtime's order.

  `CutsceneSlotClipPreview` is not moved onto the poser: it also owns a profile blob and layers, and its migration
  would be hygiene, not this spec.
- **A103-D4 — Imported clips are read-only lanes, with no import action (UA P4).** Lanes cover
  `vatSource.sourceClip` and every `VatTrack.sourceClip`:
  - **Rows:** one per animated node path (`AnimationUtility.GetCurveBindings` grouped by `path`), labelled with the
    node's name.
  - **Look:** hollow, dimmed diamonds at the union of that path's key times. Rows cannot be selected, dragged or
    box-selected.
  - **Tooltip:** names the `AnimationClip`.
  - **Time mapping:** by the `ClipAsset`'s duration, **not** the imported clip's length.
    - The bake samples `sourceClip.length` seconds at `clip.frameRate` (`VatTextureBaker.cs:395-397`).
    - The runtime maps playback over `clip.duration` and reads frame `mappedTime * fps` (`VatMaterialSystem.cs:190-199`).
    - So imported key time *k* plays at clip time *k*. A key past the duration never plays; it is counted in the
      tooltip, not drawn.
  - **Focus:** these rows hide under a focus selection like authored rows, and count into the
    "track(s) hidden" figure (UA P5).
- **A103-D5 — A78's T8 checkpoint closes as accepted under the standing rule** (unseen, not game breaking). A79's
  "must not start until A78 has closed its owner checkpoint" is therefore satisfied. The stage writes that into
  A78's status line at close.
- **A103-D6 — A79's decisions carried unchanged:**
  - **D1:** two toggles, *VAT parts* and *Other parts*, plus the existing Ghost.
  - **D2:** both new toggles default on.
  - **D3:** two drawn glyphs, `ToolkitIcons.VatPartsGlyph` (rounded quad, 3×3 texel grid, one cell lit) and
    `CutoutPartsGlyph` (two stacked rounded rectangles, pivot dot between). Both follow `BuildGhostGlyph`'s
    32×32 `RGBA32`, `HideAndDontSave`, distance-field, `(0.77, 0.77, 0.77)` pattern.
  - **D5:** sampling goes through `ClipSampler.SamplePose` off a registry built for the open set.
  - **D7:** toggles are visibility, not playback.
  - **D8:** *Other parts* is disabled with a tooltip when the set has no non-VAT content.
- **A103-D7 — A79-D4 corrected: the VAT preview poses the source copy's real nodes with the cutscene preview's
  convention, not the Clip Editor's.**
  - **Why not the Clip Editor's:** `ClipPreviewController` measures rest poses **root-relative**
    (`ClipPreviewController.cs:1265-1287`) because its mirror is flat. Written onto a nested real node, that places
    every child wrong.
  - **The precedent that works:** `CutscenePreviewController` captures local-space rest with
    `RestPoseCapture.FromTransform(node, restSliceIndex)` (`:179`, the same function entity baking uses). Its
    `WritePose` (`:1164-1190`) sets `localPosition`, `localRotation` from `math.degrees(pose.rotation)` and
    `localScale`, then `_ImageIndex` and the atlas frame on a property block.
  - **Node lookup:** nodes resolve by `RigTargetDefinition.sourceNodePath`; an unresolved target is skipped.
  - **VAT exclusion:** targets whose id matches a `VatPartTextures.targetId` in the previewed set are excluded, so
    the sampler never writes a node the baked mesh owns.
  - **A79-D6 survives, and its T0 is answered.** The question was whether a registry can be built outside the
    controller. It can: `CutsceneSlotClipPreview.RebuildIfBindChanged` does it in about 40 lines and disposes it
    in `Dispose` (`:60-130`).
- **A103-D8 — One source copy, split appearance.** `VatPreviewElement` keeps its single copy (the vault's
  domain-reload ownership trap still applies). Renderers are split by whether they belong to a VAT part: the part's
  `SkinnedMeshRenderer` at its `VatBakeSource.SourceNodePath`.
  - **VAT-part renderers:** keep today's rule — the subject before a bake, the frozen Ghost after.
  - **Every other renderer:** the posed *Other parts* half, with authored materials, visible before a bake or
    while *Other parts* is on.
  - **Visibility mechanism:** per `Renderer.enabled`, not `SetActive` on the root.
  - **Before a bake:** both new toggles are disabled, because nothing plays.
- **A103-D9 — The VAT Bake preview plays every baked part, by name.**
  - **Parts drawn:** today's `previewedPart = parts[0]` ("Part switching is a later amendment's work") becomes
    every entry in `textureSet.parts`, each with its own `VatPreviewMaterial`.
  - **Clip picker:** the dropdown lists each **clip** once, by its asset name; today it lists every
    `(clipId, targetId)` range as `clip 0x…` / `target 0x…`, against names-never-numbers.
  - **Clock:** one clock per clip. Each part reads its own range for that clip id through
    `VatPreviewFrameResolver.GlobalFrameForRange`.
  - **Status line:** names the part count and frame count, never hex.
- **A103-D10 — VAT in the Clip Editor viewport (UA P3, preview half).**
  - **Toggle:** a *Baked VAT* rail toggle (`VatPartsGlyph`), **default off**, disabled with a tooltip when the open
    set has no `vatTextures`.
  - **When on:**
    - Each baked part's live `SkinnedMeshRenderer` in the skeleton instance is disabled.
    - Its `runtimeMesh` is drawn through `VatPreviewMaterial` at the **skeleton instance root's**
      `localToWorldMatrix`, because the bake writes root space (`VatTextureBaker` `worldToRoot`).
    - The drawn frame is the playhead's.
    - Scrubbing moves the baked mesh and every cutout on one playhead.
  - **When off:** renderers are re-enabled — the toggle must un-write (HANDOFF §9 lesson 3).
  - **Frame resolution:** the runtime's two-step rule — the target range by dense index first, then the clip-wide
    range, clamped to `frameCount - 1`. It is mirrored in an editor static `VatPreviewFrameResolver`, not shared,
    because `VatMaterialSystem.TryResolveGlobalFrame` is `private static` inside a Burst system.
    `ClipSampler.MapTime` is left out: the Clip Editor playhead is already a mapped normalized time. Fixture F3
    pins the rule.
  - **Draw order:** between `BeginPreview` and `camera.Render`, the order `VatPreviewElement.RenderViewport`
    already uses, so no platform probe is needed. ⚠ Default-off and replace-not-overlay are interpretations (S6 Q1).
- **A103-D11 — The VAT binding row (UA P3, authoring half).**
  - **Where offered:** a new `ClipComponentKind.VatBinding = 6`, on a hierarchy item whose node path matches a
    `VatBakeSource` from `VatBakeSourceResolver.TryResolve(activeRig)`.
  - **Fields:** *Source* (`AnimationClip`; empty reads "authored bone tracks") and *Loop safe*.
  - **What it writes:** the row for that target in `vatTracks` (found by target, shown by name), or `vatSource`
    for the untargeted single-mesh source (`TargetId == 0`). Nothing is typed as a number.
  - **No `sampleFps` field:** the fields exist on both types, but nothing in the bake reads them —
    `VatBakeClipBuilder` passes `samplesPerSecond = clip.frameRate` (§1.1 drift 4).
  - **Length check:** a line appears when the source clip's length and the clip's duration differ by more than half
    a frame: "the last 0.20 s never plays" or "holds its last frame for 0.20 s".
  - Serialized fields are untouched; no migration.
- **A103-D12 — UA P5 invariants, most already true.**
  - **One playhead:** `SamplePose` feeds bones, targets and the Baked VAT overlay from the same `normalizedTime`.
  - **Transport frame count:** already the clip's (`ClipEditorTransport.cs:58-60` reads
    `selectedClip.FrameCount`), so nothing to build.
  - **Focus:** the only new work is D4's focus rule for imported rows.
- **A103-D13 — No new tab, no runtime or `Authoring/` change, no serialized-field change.**

### 1.1 Grounding done while specifying (2026-09-15, `556823b7`) — both source specs had drifted

1. UA §2.2 names the `SamplePose` registry gate. That gate was fixed in P0; the gate that still stops a
   targetless rig is `Refresh`'s `PartCount == 0` return (`ClipPreviewController.cs:940-945`), which runs before
   `RebuildRegistry`.
2. UA P1 says "the banner … and its validation warning". No validation rule, Health finding or `ValidationCode`
   mentions a targetless rig; the banner at `:943` is the only surface.
3. UA P2 says the empty-target blob is "more code in the builder". It is none: the builder and a fixture already
   cover zero targets.
4. `VatTrack.sampleFps` and `VatClipSource.sampleFps` are written by `MirrorClipUtility` and
   `VatSampleTentacleUtility` and read by nothing. The bake rate is `clip.frameRate`
   (`VatBakeClipBuilder.cs:60,83`).
5. A79 §3's line numbers are stale. The Ghost toggle is now `ViewportFrameElement.AddRailToggle(Texture, tooltip,
   fallbackText, startsRun)` (A101, `VatPreviewElement.cs:78-86`), not a hand-built `ToolbarToggle` block;
   `GhostGlyph` is `ToolkitIcons.cs:79`, `BuildGhostGlyph` `:93`.
6. A79 §5.3 assumes `Show` knows the rig. `Show(VatTextureSetAsset, ClipSetAsset, SkinnedMeshRenderer)` does not;
   its one caller is `VatBakePanel.RefreshPreview` (`:458-468`), which has `rigField`.
7. A79 §5.4 says two doc pages claim the bake preview "shows only the baked mesh". Neither does. The paragraph to
   rewrite is `rigged-characters.md:150` ("Preview limitation"), which says the **Clip Editor** never samples a
   VAT texture; D10 changes that.
8. A79 §5.2's poser, as drawn, reuses `ClipPreviewController`'s rest-pose table, which is root-relative
   (see D7).
9. `VatPreviewElement` previews `parts[0]` only, and its dropdown and status print hex ids (see D9).
10. `Documentation~/clip-editor.md:153` still says the window "carries New Rig, VAT Bake, Actor Editor and Cutscene
    Editor tabs" — pre-A75 wording. It is outside this spec; T14 corrects that one sentence while in the file.

## 2. Files (all under `Packages/com.dotsanimationtoolkit/` unless rooted)

**Lead (worktree):**
- `Editor/ClipEditor/Preview/RegistryTargetPoser.cs` — new (T1); `Tests/EditMode/RegistryTargetPoserTests.cs` — new.
- `Editor/ClipEditor/Shared/ToolkitIcons.cs` — two glyphs (T2).
- `Editor/VatBaking/VatPreviewFrameResolver.cs` — new (T3); `Tests/EditMode/VatPreviewFrameResolverTests.cs` — new.
- `Editor/ClipEditor/ImportedClipLaneResolver.cs` — new (T4); `Tests/EditMode/ImportedClipLaneResolverTests.cs` — new.
- `Editor/ClipEditor/Editing/ClipVatBindingEditing.cs` — new (T5); `Tests/EditMode/ClipVatBindingEditingTests.cs` — new.
- `Editor/ClipEditor/Shared/ClipSetContentResolver.cs` — new (T6).
- `Editor/ClipEditor/Preview/ClipPreviewController.cs` — T7, then T13; `Tests/EditMode/ClipPreviewRegistryTests.cs` — new (T7).
- `Editor/VatBaking/VatPreviewPartPoser.cs` — new (T8).
- `Editor/ClipEditor/Preview/ClipEditorVatOverlay.cs` — new (T9).
- `Editor/ClipEditor/Components/VatBindingComponentElement.cs` — new; `Editor/ClipEditor/Components/ClipComponentKind.cs` (T10).
- `Editor/ClipEditor/Panes/TimelinePane.cs`; `Editor/ClipEditor/ImportedClipLaneElement.cs` — new (T11).
- `Editor/VatBaking/VatPreviewElement.cs`; `Editor/VatBaking/VatBakePanel.cs` (T12).
- `Documentation~/clip-editor.md`; `Documentation~/rigged-characters.md` (T14).

**Stage only:**
- `ClipEditorWindow.uxml`, `ClipEditorWindow.cs`, `ClipEditorWindow.ComponentStack.cs`, `ClipEditorWindow.uss`, and
  `ClipEditorLayoutTests` — the S2 wiring.
- `CHANGELOG.md`, `package.json`, the conformance pin, `Docs/AnimationToolkit/HANDOFF.md`,
  `Docs/AnimationToolkit/Amendment_A78_VatBakeSourceFromRig_Spec.md` (status line), the roadmap,
  `Code_Audit_2026-09.md`, and `Assets/_Vault/Memories/Code/AnimationToolkit.md`.
- `Assets/A103Scratch/` — temporary, deleted after S4.

## 3. Read (line ranges; greps are against `556823b7` and may have moved by a few lines)

- `ClipPreviewController.cs`:
  - `124-140` — the `registry` field;
  - `265-295` — `HasRegistry`, `IsClipInRegistry`;
  - `375-405` — `RebuildRegistry`;
  - `900-1100` — `FindClipById`, `Refresh`, `SamplePose`, `SampleCompositedPose`;
  - `1175-1290` — `ApplyHeldTargetPose` and the rest-pose table;
  - `1495-1570` — `Render`;
  - `1765-1790` — the billboard settings' registry read;
  - `2020-2066` — `ReleaseRegistry`, `Dispose`.
- `Authoring/Build/ClipRegistryBuilder.cs` `78-97` (`Build`); `Tests/EditMode/ClipRegistryBuilderTests.cs` `369-385`.
- `Editor/ClipEditor/Cutscene/CutsceneSlotClipPreview.cs` `60-130` (a registry outside the controller);
  `CutscenePreviewController.cs` `160-185` and `1164-1195` (local rest capture, `WritePose`, property-block ids —
  grep `ImageIndexPropertyId`, `AtlasFramePropertyId`); `Authoring/Baking/RestPoseCapture.cs` whole (small).
- `Editor/VatBaking/VatPreviewElement.cs`:
  - `15-130` — fields, constructor, rail;
  - `130-252` — `Show`, `DescribeRange`, `SelectRange`;
  - `290-360` — `Tick` tail, `RenderViewport`;
  - `420-545` — Ghost, source copy, appearance, dispose.
- `VatPreviewPlayback.cs` whole (112 lines); `VatBakePanel.cs` `455-475`; `VatBakeSourceResolver.cs` `15-30`.
- `Runtime/Systems/VatMaterialSystem.cs` `144-202` (the rule D10 mirrors).
- `Editor/VatBaking/VatBakeClipBuilder.cs` `45-90`; `VatTextureBaker.cs` `390-425` (D4's time mapping).
- `Authoring/Assets/ClipAsset.cs` `395-440` (`VatClipSource`, `VatTrack`); `VatTextureSetAsset.cs` `255-290`
  (`VatPartTextures`); `RigAsset.cs` `201-245` (`RigTargetDefinition`).
- `Editor/ClipEditor/Shared/ToolkitIcons.cs` `36-45`, `79-145`; `Shared/ViewportFrameElement.cs` `55-80`.
- `Editor/ClipEditor/Panes/TimelinePane.cs` `560-700` (`hiddenTrackCount`, `AddTrackRow`, `SyncGhostLanes`);
  `GhostLaneStripElement.cs` `1-80` (a lane strip that follows `viewZoom`/`viewPan`/`viewLaneWidth`);
  `TrackLaneElement.cs` `1-60`.
- `Editor/ClipEditor/Components/ClipComponentKind.cs` whole.
- Test helpers: grep `public static` in `Tests/EditMode/AuthoringTestAssets.cs` and `TestBlobFactory.cs`.
- `Assets/_Vault/Memories/Code/AnimationToolkit.md` lines `590-700`: the preview source copy's ownership sweep,
  the two registry gates, `BoneKey.localPosition` assigned outright, and cover-pane disposal.

## 4. Fixtures (each proves itself by failing when its fix is reverted, or is deleted)

- **F1 `ClipPreviewRegistryTests.Refresh_TargetlessRigWithABoneTrackSet_BuildsARegistryHoldingTheClipAndSaysNothing`**
  (T7).
  - **Setup:** a zero-target rig and a set with one bone-tracked clip, all `CreateInstance`.
  - **Asserts:** `HasRegistry`, `IsClipInRegistry(clipId)`, and an empty `StatusMessage`.
  - **Revert-to-fail:** reinstate the `PartCount == 0` return.
- **F2 `RegistryTargetPoserTests.PoseTargets_SkipsATargetTheWriterDeclines_AndWritesTheOthersAsClipSamplerDoes`**
  (T1).
  - **Setup:** two targets with keyed transforms; the writer declines one.
  - **Asserts:** exactly one write, equal to a direct `ClipSampler.SamplePose` for that target.
  - **Revert-to-fail:** ignore `TryGetRestPose`'s false (fall back to identity rest), and the declined target is
    written.
- **F3 `VatPreviewFrameResolverTests.TryResolveGlobalFrame_UsesTheTargetRangeFirst_ThenTheClipRange_ClampedToTheLastRow`**
  (T3).
  - **Setup:** a blob with a clip-wide range and one target range.
  - **Asserts:**
    - the targeted index reads the target range;
    - another index reads the clip range;
    - `normalizedTime` 1 lands on `frameStart + frameCount - 1`.
  - **Revert-to-fail:** delete the target-range loop.
- **F4 `ImportedClipLaneResolverTests.Resolve_OneRowPerAnimatedPath_KeyTimesNormalisedByTheClipDuration`** (T4).
  - **Setup:** an in-memory `AnimationClip`, built with `AnimationUtility.SetEditorCurve`, holding two paths and
    three curves, 1.2 s long, against a 1.0 s clip.
  - **Asserts:** two rows, times divided by 1.0, and one key counted past the end.
  - **Revert-to-fail:** normalise by `sourceClip.length`.
- **F5 `ClipVatBindingEditingTests.SetSourceClip_TargetedWritesOneVatTrackRow_UntargetedWritesVatSource`** (T5).
  - **Asserts:**
    - target 7 gets one `vatTracks` row with `targetId == 7` and `vatSource` stays null;
    - target 0 writes `vatSource.sourceClip`;
    - clearing target 7's source removes its row.
  - **Revert-to-fail:** route target 0 into `vatTracks`. That row fails at bake: `VatTrack.targetId` "0 always
    fails".

**Regression fixtures gated with wave 2 and 3** (unchanged, must stay green): `ClipPreviewCompositeTests`,
`ClipPreviewControllerPoseTests`, `SocketPreviewParityTests`, `ActorPreviewComposerTests`, `ActorPreviewParityTests`,
`ClipRegistryBuilderTests`, `PackagingConformanceTests`. No PlayMode fixture is touched: no runtime file changes.

## 5. Tasks — lead

- [ ] **T0 — Ground.**
  - `git rev-parse --show-toplevel`; claim `a103`.
  - Grep every name in §1–§3; confirm the §1.1 drifts still hold.
  - Check whether `AuthoringTestAssets` has a bone-track helper (F1) and whether `TestBlobFactory` can write
    `vatTargetRanges` (F3). Otherwise the fixture builds them inline.
  - Log drift in §7.

**Wave 1 — six workers, disjoint files, no cross-dependency.** Commit stubs for `RegistryTargetPoser` (its
public surface from D3, empty bodies) before spawning, so wave 2's briefs can paste real signatures.

- [ ] **T1 — `RegistryTargetPoser` + F2** `[parallel-safe]` (2 files).
  - **Build:** D3's surface, plus `Registry` (the raw reference, for `IActorPosePresenter` and
    `SampleCompositedPose`), `Release()` and `Dispose()`.
  - `Rebuild` disposes the previous blob before building and catches exactly what `RebuildRegistry` catches today.
  - The nested interface carries no `<summary>` (Conformance_F: one per file).
- [ ] **T2 — Two glyphs** `[parallel-safe]` (`ToolkitIcons.cs`). D6/A79-D3; one comment on the pair. No fixture.
- [ ] **T3 — `VatPreviewFrameResolver` + F3** `[parallel-safe]` (2 files). Two methods:
  - `TryResolveGlobalFrame(ref ClipBlob clip, int targetIndex, float normalizedTime, out float globalFrame)`, where
    `targetIndex` −1 means untargeted;
  - `GlobalFrameForRange(in VatClipRange range, float timeSeconds)`.

  Paste `VatMaterialSystem.cs:166-199` into the brief.
- [ ] **T4 — `ImportedClipLaneResolver` + F4** `[parallel-safe]` (2 files).
  - `Resolve(AnimationClip sourceClip, float clipDurationSeconds)` → a list of `ImportedClipLane`, a readonly struct:
    - `nodePath`;
    - `displayName` (last path segment, or the clip's root name for an empty path);
    - `normalizedKeyTimes`, deduplicated within 1e-4;
    - `keysPastClipEnd`.
  - Paste D4's mapping paragraph into the brief.
- [ ] **T5 — `ClipVatBindingEditing` + F5** `[parallel-safe]` (2 files). `GetSourceClip`, `SetSourceClip`,
  `GetLoopSafe`, `SetLoopSafe` (each `(ClipAsset clip, uint targetId, …)`), plus
  `DescribeLengthMismatch(ClipAsset clip, AnimationClip sourceClip)`.
  - Every write is one `Undo.RecordObject(clip, …)` plus `EditorUtility.SetDirty(clip)`.
  - `SetSourceClip(null)` on a targeted row removes the row.
- [ ] **T6 — `ClipSetContentResolver`** `[parallel-safe]` (1 file):
  - `HasBoneOrVatContent(IReadOnlyList<ClipSetAsset> clipSets)` — D1's predicate;
  - `HasNonVatContent(IReadOnlyList<ClipSetAsset> clipSets)` — any keyed transform, sprite or billboard track (A79-D8).

  No fixture (trivial predicates).

**Gate W1:** `RegistryTargetPoserTests`, `VatPreviewFrameResolverTests`, `ImportedClipLaneResolverTests`,
`ClipVatBindingEditingTests`, `PackagingConformanceTests`. Then one mutation commit carrying F2–F5's four
reverts. Gate it: exactly those four tests fail. Hard reset, then check each file's sha256.

**Wave 2 — five workers, after W1 is green.**

- [ ] **T7 — Controller on the poser, P1 + P2 + F1** `[parallel-safe]` (`ClipPreviewController.cs`,
  `ClipPreviewRegistryTests.cs`).
  - **Replace:** the `registry` field becomes a `RegistryTargetPoser`. The controller implements the writer
    explicitly: `TryGetRestPose` succeeds only for a target `rigMirror` holds, via `ResolveRestPose`; `WritePose`
    is `rigMirror.ApplyPose`.
  - **Keep:** `HasRegistry`, `IsClipInRegistry`, `IActorPosePresenter.Registry` and `SampleCompositedPose`'s loop,
    reading `targetPoser.Registry`. Map `RegistryBuildOutcome` to today's three status strings.
  - **Drop:** the `PartCount == 0` return (D2). Add D1's status line after the build.
  - **`SamplePose` return:** true when bones posed or the clip is in the registry.
- [ ] **T8 — `VatPreviewPartPoser`** `[parallel-safe]` (1 new file). Implements the writer (D7):
  - `Rebuild(RigAsset rig, ClipSetAsset clipSet, VatTextureSetAsset textureSet, GameObject sourceCopyRoot)` →
    `bool`: builds the path → `Transform` map and the local rest table, and excludes VAT-part target ids;
  - `HasNonVatParts`;
  - `IsVatPartRenderer(Renderer renderer)` — D8's split, resolved through `VatBakeSourceResolver.TryResolve(rig)`;
  - `PoseAt(ulong clipId, float normalizedTime)`;
  - `RestoreRestPose()`, which re-writes rest **and** clears the property blocks it set;
  - `Dispose()`.

  Paste `CutscenePreviewController.WritePose` into the brief.
- [ ] **T9 — `ClipEditorVatOverlay`** `[parallel-safe]` (1 new file). D10:
  - `Enabled`;
  - `Rebind(RigAsset rig, ClipSetAsset clipSet, GameObject skeletonInstanceRoot)` — one material per part, live
    renderer per part;
  - `Sync(BlobAssetReference<ClipRegistryBlob> registry, ulong clipId, float normalizedTime)`;
  - `Draw(PreviewRenderUtility renderUtility)`;
  - `Dispose()`, which re-enables every renderer it disabled and disposes the materials.
- [ ] **T10 — The binding row** `[parallel-safe]` (`VatBindingComponentElement.cs` new, `ClipComponentKind.cs`).
  - **Enum:** `VatBinding = 6`, documented like its siblings.
  - **Element:** built as `VatBindingComponentElement(ClipAsset clip, VatBakeSource source, System.Action onEdited)`,
    with D11's two fields, the length line and the part's `DisplayName`. It uses existing `toolkit-` USS classes
    only (Conformance_I bans inline visual styles).
  - **Detached:** construct → set fields → read the clip back.
- [ ] **T11 — Imported read-only lanes, P4 + P5** `[parallel-safe]` (`TimelinePane.cs`, `ImportedClipLaneElement.cs` new).
  - **Rows:** for the selected clip, resolve `vatSource.sourceClip` and each `vatTracks[i].sourceClip` (rows named
    by the rig target's `displayName`) through T4. Add read-only rows after the authored rows, before
    `SyncGhostLanes`.
  - **Drawing:** hollow diamonds in `ImportedClipLaneElement`, which follows
    `viewZoom`/`viewPan`/`viewLaneWidth` exactly as `GhostLaneStripElement` does.
  - **Focus:** hidden under focus and counted into `hiddenTrackCount`.
  - **Styling:** the header needs one modifier class `clip-editor__track-header--imported`. List it for the stage's
    USS; do not edit `ClipEditorWindow.uss`.

**Gate W2:** `ClipPreviewRegistryTests` plus every §4 regression fixture. F1's revert commit fails exactly F1.

**Wave 3 — three workers.**

- [ ] **T12 — The whole-animation VAT Bake preview** `[parallel-safe]` (`VatPreviewElement.cs`, `VatBakePanel.cs`).
  - **`Show`:** gains `RigAsset rig` as its third parameter; `RefreshPreview` passes `rigField.value as RigAsset`.
  - **Rail:** *VAT parts* and *Other parts* toggles are created **before** `ghostToggle`, through
    `viewportFrame.AddRailToggle(Texture, …)`, named `vat-parts-toggle` and `other-parts-toggle`, defaulting on
    (D6).
  - **D8 split:** every part is drawn (D9); the poser is rebuilt after `RebuildSourceCopy`.
  - **`Tick`:** `PoseAt(selectedClipId, playback.Time / clip.duration)` while *Other parts* is on.
  - **Off states:** *VAT parts* off skips the part draws; *Other parts* off calls `RestoreRestPose` and hides the
    non-VAT renderers. Neither touches `playback`, `isPlaying` or the frame readout.
  - **`Dispose`:** disposes the poser and every material.
- [ ] **T13 — Baked VAT in the Clip Editor controller** `[parallel-safe]` (`ClipPreviewController.cs`).
  - **Property:** `public bool BakedVatPreviewEnabled` (default false) forwards to a `ClipEditorVatOverlay`.
  - **`Rebind`:** after each registry build and in `SetSkinnedSource`.
  - **`Sync`:** in `SamplePose` right after the bone pose.
  - **`Draw`:** in `Render` and `RenderCaptureFrame`, between `BeginPreview` and `camera.Render`.
  - **`Dispose`:** in `Dispose`.
- [ ] **T14 — Docs** `[parallel-safe]` (`clip-editor.md`, `rigged-characters.md`). Cover:
  - the binding row;
  - the *Baked VAT* toggle;
  - read-only imported lanes and their time rule;
  - the targetless-rig line;
  - the VAT Bake preview's three toggles and every-part playback;
  - a rewrite of the "Preview limitation" paragraph (`rigged-characters.md:150`);
  - the stale tab sentence (§1.1 drift 10).

**Gate W3:** the W2 set plus `PackagingConformanceTests`.

- [ ] **T15 — Close text.** `### For integration` in §7:
  - the CHANGELOG `## [0.55.0] — Unified authoring and VAT preview` text;
  - `Conformance_G` names (expected none: `ClipSetContentResolver`, `ImportedClipLaneResolver`,
    `VatPreviewFrameResolver` and `ClipVatBindingEditing` carry allowed suffixes);
  - the exact S2 wiring (toggle name, icon call, tooltip strings, the ComponentStack case and picker label, the USS
    modifier);
  - vault traps;
  - a HANDOFF paragraph.

  Then `status a103 ready`.

## 6. Tasks — stage orchestrator (Editor-bound, never a lead)

- [ ] **S0 — Phase 0.**
  - `ListAgents`; `doctor --json`.
  - Baseline: compile gate, EditMode 868 (zero failures), PlayMode 285.
  - CHANGELOG top `## [0.54.0]`; both registry sha256s.
  - Record all of it in §7; commit; push.
- [ ] **S1 — Hand gates.** None expected (no PlayMode fixture, no runtime file). Answer any "gate needed" per
  trap 25.
- [ ] **S2 — Merge and wire** (merge `a103`, then one integration commit):
  - **UXML:** `baked-vat-preview-toggle` plus `baked-vat-preview-icon` in `overlay-tool-row` after
    `ragdoll-preview-toggle`.
  - **`ClipEditorWindow.cs` toggle binding:**
    - icon via `ToolkitIcons.SetToggleIcon(…, ToolkitIcons.VatPartsGlyph, "VAT")`;
    - value → `previewController.BakedVatPreviewEnabled`;
    - disabled with a tooltip when `clipSet.vatTextures` is null.
  - **`ClipEditorWindow.ComponentStack.cs`:** the `VatBinding` case and a picker entry.
  - **USS:** the lane-header modifier.
  - **Tests:** `ClipEditorLayoutTests` gains the toggle name.
  - **Gate:** `ClipEditorLayoutTests`, all five §4 fixtures, the regression set.
- [ ] **S3 — Full suites once.** EditMode ≥ 868 + the new tests, zero failures; PlayMode 285.
- [ ] **S4 — Drives** (scratch only: `Assets/A103Scratch/`; never the docked Clip Editor, never
  `MaleCitizen.prefab` / `NewRig.asset`, no `SaveAssets`, no Play mode). Record each in §7:
  - **(a) P1/P2:**
    - A detached `ClipPreviewController` on the real `VatSampleTentacleRig` + `VatSampleTentacleClips`: `HasRegistry`
      true, `StatusMessage` empty.
    - Scrub to t = 0.1 / 0.5 / 0.9 through `SamplePose`, then read bone transforms in a **separate** `execute_code`
      call (vault trap); all three differ.
    - A scratch targetless rig with an event-only set shows D1's line.
  - **(b) A79:**
    - Build the scratch set with `VatSampleTentacleUtility.CreateTwoPartSampleAssets` into scratch.
    - Add a non-skinned child quad to the scratch prefab as a rig target (with `sourceNodePath`), plus a keyed
      transform track on the clip.
    - Bake through the scratch `VatBakePanel` path.
    - Drive a floating `VatBakeWindow` (not the docked Clip Editor): four states — both on, VAT only, Other only,
      Ghost on. Confirm the quad moves, the fin and body play, and toggling never moves the playhead.
    - Close the window.
  - **(c) P3 preview:**
    - Detached controller on (b)'s set, `BakedVatPreviewEnabled` true; `Render` at three times, and pixel hashes
      differ.
    - The live `SkinnedMeshRenderer`s are disabled while on and re-enabled after off.
  - **(d) P3 binding:**
    - `ClipVatBindingEditing.SetSourceClip` on the scratch clip for the fin target.
    - `Resources.UnloadAsset` + reload; the YAML shows one `vatTracks` row with the fin's id.
  - **(e) P4:**
    - A scratch `AnimationClip` (two bone paths, `SetEditorCurve`) as `vatSource.sourceClip`.
    - `ImportedClipLaneResolver` rows match.
    - A detached `ImportedClipLaneElement` in a temporary utility window renders.

  Captures only when `EditorApplication.isFocused`, scaled by `pixelsPerPoint`. Delete scratch; re-check both
  registry sha256s.
- [ ] **S5 — Close.**
  - **Versions:** CHANGELOG `0.55.0` from T15; `package.json` and the conformance pin at `0.55.0`.
  - **HANDOFF:** §4 paragraph on top.
  - **Status lines and boxes:** A78's status line gains "T8 closed under the standing rule 2026-09-15 (A103-D5)";
    this spec's status line and ticked boxes; the roadmap box ticked.
  - **Audit:** `Code_Audit_2026-09.md` §3.2 and §5.3 marked done.
  - **Vault:** `AnimationToolkit.md` gains a section with D4's time rule, D7's two rest conventions, D10's
    root-space draw, and the dead `sampleFps` fields.
  - Commit; push.
- [ ] **S6 — ⏸ owner checkpoint** (closes as accepted under the standing rule unless game breaking; ask anyway, with
  the S4 captures):
  - **Q1.** In the Clip Editor, *Baked VAT* is off by default and **replaces** the live skinned mesh with the baked
    one rather than overlaying both. Is that the view you want for spotting bake drift, or should it default on,
    or ghost one over the other?
  - **Q2.** Do the two drawn symbols read at 16 px in the VAT Bake rail, and is *Other parts* the right name for
    the quads-and-flipbooks half?
  - **Q3.** Imported clips show one read-only row per animated bone with hollow keys. Is one row per bone enough,
    or do you want a row per channel?

## 7. Build log

*(T0 grounding, gate verdicts, revert-to-fail results, drift, the For-integration block and S0–S6 — written by the
sessions that run this.)*

**S0 — Phase 0 (stage, 2026-09-15).** Trunk `a44c034b` (`A103-S0: vault metas` on top of the spec commit `4364abb0`). `ListAgents`: one busy peer, `stitch-punk-ae`, confirmed it drives no Editor and edits no code (two uncommitted vault files: `Tasks/Plans/PlayerResource_System.md`, `Spencer/next-session-player-resource-prompt.md` — never stage them; it waits for the A103 merge before committing). `doctor`: git 2.43.0, long paths on, hooks installed, `brokerAlive` true, no stage blockers, no worktrees. Compile gate clean (forced refresh, zero console errors). Baseline: `DotsAnimationToolkit.Tests.EditMode` **868 of 868, zero failures**; `.PlayMode` **285 of 285**. CHANGELOG top `## [0.54.0]`, `package.json` `0.54.0`. Registry sha256: `DotsAnimationToolkitAnimEventKeyRegistry.asset` `3bdb420d55b808ecfd9251ab144ac89645c4d6f903b4a8a3498a42aa76d14701`; `DotsAnimationToolkitTargetTagRegistry.asset` `dbec3d5f6d31db02891682e7f88e6011f7317658f1d29753a0185ff2ebd1eb4f` (both unchanged since A102). `Assets/ScriptableObjects/Animations/VatSampleTentacle/` holds `VatSampleTentacleRig.asset`, `VatSampleTentacleClips.asset`, `VatSampleWave.asset`, the prefab, mesh and material.

**T0 — Grounding (lead, worktree `spec/a103` off `d8c0c3b0`, 2026-09-15).** Claimed (`source-only`). Every §1–§3 name grepped. All ten §1.1 drifts still hold (gate at `ClipPreviewController.cs:941`, `sampleFps` written by `MirrorClipUtility:477,517` and `VatSampleTentacleUtility:75,230,241`, read by nothing; `rigged-characters.md:150` unchanged). New drift, logged:
11. `TestBlobFactory.ClipSpec` carries `vatFrameStart/Count/Fps` but cannot write `vatTargetRanges` — F3 builds its `ClipBlob` inline with a `BlobBuilder`.
12. `AuthoringTestAssets` has no bone-track helper (`AddTransformTrack/Key`, `AddSpriteTrack/Key`, `AddEvent` only) — F1 builds its `BoneTrack` inline.
13. `TimelinePane.cs` anchors moved: `hiddenTrackCount` `:444`, the second `SyncGhostLanes` call `:595`, `AddTrackRow` `:605`, `ActiveRig` `:257` (§3 said `560-700`).
14. `AnimatedChannels` has no `Position` member (`PositionXY | PositionZ`) — noted by T1, cost nothing.
15. A `[Serializable]` class field (`ClipAsset.vatSource`) deserializes non-null, so D1's "a `vatSource`" is implemented as `vatSource != null && vatSource.sourceClip != null`; `ScriptableObject.CreateInstance` runs the same serialization init, so even a fresh in-memory clip has a non-null empty `vatSource` — F5's "`vatSource` stays null" is asserted as "`vatSource.sourceClip` stays null" (found by W1 gate 2).

Lead calls (code-reading, not owner questions): `RegistryTargetPoser` exposes `LastOutcome` and `FailureMessage` (the exception text for `Failed`, which the controller's third status string needs) plus `TryResolveClipIndex`/`IsClipInRegistry`; `Rebuild` returns `NoRig`/`NoClipSets` before building rather than relying on the builder's `ArgumentNullException` (still caught). `ImportedClipLane.displayName` for an empty path is the `AnimationClip`'s own name; rows sort by path ordinal; past-end keys count distinct times. `ClipVatBindingEditing.SetLoopSafe` is a no-op when no row/source exists (a loop-safe row with no clip would fail at bake).

**W1 (T1–T6)** — stub `f36e2260`, wave `7e7a57b4`. Gate 1: compile error CS0540 in `ImportedClipLaneResolverTests.cs:56` (explicit `IComparer.Compare` without declaring the interface); fixed by a fresh worker (`aa4b7fee`). Gate 2: 15 passed, 1 failed — F5's null assert (drift 15); fixed by a fresh worker.
Gate 3 (`f07e8366`): **pass, 16 of 16** (4 new fixtures + 12 `PackagingConformanceTests`). Revert-to-fail: mutation commit `2252110f` (F2 declined target falls back to identity rest; F3 target-range loop disabled via `rangeIndex < 0`; F4 normalises by `sourceClip.length`; F5 target 0 routed into `vatTracks`) gated **12 passed, 4 failed — exactly F2, F3, F4, F5** (F5 `Expected <AnimationClip> but was null`; F4 `0.5 vs 0.4167`; F2 `Expected 1 but was 2`; F3 `38.5 vs 15`). `git reset --hard HEAD~1` back to `f07e8366`; `git hash-object` equals the HEAD blob for all four files; sha256 `RegistryTargetPoser.cs` `309d1f50…cd460`, `VatPreviewFrameResolver.cs` `3880b338…1a616`, `ImportedClipLaneResolver.cs` `1dcb732c…367b3`, `ClipVatBindingEditing.cs` `a6841ba5…1eee`.
