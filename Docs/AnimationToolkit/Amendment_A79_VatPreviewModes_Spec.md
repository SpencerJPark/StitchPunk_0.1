# Amendment A79 — the VAT preview shows the whole animation, not just the baked half

> **Status:** 📝 outline specced 2026-09-08, **not ready to execute** — §6 T0 is a grounding pass that
> must land before T1 is briefed. Its decisions are settled; its §5.2 design is a route, not verified
> code. Takes `0.27.0` unless `CHANGELOG.md` has moved.
> **Predecessor:** [`Amendment_A78_VatBakeSourceFromRig_Spec.md`](Amendment_A78_VatBakeSourceFromRig_Spec.md)
> — same owner request, split on his instruction: A78 is the bake, A79 is the preview. **A79 must not
> start until A78 has closed its owner checkpoint**, because A78's per-part sets are what this
> previews.
> **Executor:** one Editor-connected orchestrator; `worker` subagents edit files and never touch MCP.

---

## 1. What the owner asked for (2026-09-08, verbatim)

> "I then should be able to preview the animation. one just vats so just the parts with vats will
> move, and then one just the other kinds, and then a combined one and an optional ghost. that is the
> idea … maybe add the aniamtion toggles as buttons on that same part of the preview with the ghost.
> give them all symbols and if there arent ones that make sense feel free to generate new ones"

Today the VAT Bake preview draws exactly two things: the baked VAT mesh playing from its textures,
and — behind the **Ghost** toggle — a rest-pose copy of the source. Transform quads and flipbook
planes are not in it at all. That is the gap.

---

## 2. Decisions (recorded — do not re-ask)

- **A79-D1 — Two toggles, not three, plus the Ghost that already exists.** *VAT parts* on/off and
  *Other parts* on/off give all three of the owner's modes — both on is his "combined", either alone
  is his isolated view — from two buttons instead of a three-state segmented control that would need
  its own selection model. Ghost stays exactly as it is: independent, and only meaningful once
  something is baked.
- **A79-D2 — Both new toggles default on.** The first thing the panel shows after a bake is the whole
  animation; isolating is the deliberate act, not the default.
- **A79-D3 — Two new glyphs, drawn, not resolved.** The editor ships nothing that reads as "vertex
  texture" or "cutout part", and every near-miss already means something else in the same rail — the
  same reason `ToolkitIcons.GhostGlyph` is drawn (`ToolkitIcons.cs:38-43`). Both follow that exact
  pattern: a 32×32 `RGBA32`, `HideAndDontSave`, bilinear, built from a distance field so it stays
  clean at 2×, in the same `(0.77, 0.77, 0.77)` dark-skin grey.
  - **`VatPartsGlyph`** — a rounded quad with a 3×3 texel grid and one cell lit: art driven by a
    texture.
  - **`CutoutPartsGlyph`** — two stacked rounded rectangles with a pivot dot between them: parts on
    hinges.
- **A79-D4 — "Other parts" poses the real prefab nodes, not proxy quads.** `PreviewRigMirror` builds
  untextured quads on purpose — *"Shows motion, not art"* (`:10-13`) — because the Clip Editor has no
  prefab to pose. The VAT preview already instantiates the real source prefab for the Ghost
  (`VatPreviewElement.cs:460`), so posing **those** nodes costs nothing extra and shows real artwork.
- **A79-D5 — Sampling goes through `ClipSampler.SamplePose`, the runtime sampler, off a registry blob
  built for the open clip set** — the same route `ClipPreviewController` uses (`:1019-1033`). A second
  editor-side sampler would drift from the runtime, which is the failure this package has already
  spent a phase on.
- **A79-D6 — Reuse the sampler and the blob builder, not `ClipPreviewController` itself.** That file
  is 2008 lines and owns selection, gizmos, handles, sockets, ragdoll and the bone hierarchy — none
  of which the bake preview wants. A79 writes a small poser that owns a registry blob, a rest-pose
  table and a `sourceNodePath` → `Transform` map. **T0 tests this assumption before T1 is briefed.**
- **A79-D7 — The toggles are visibility, not playback.** One clock, one playhead; a hidden half is
  still being sampled. Turning a half back on must not resynchronise anything.
- **A79-D8 — A clip set with no non-VAT content disables the *Other parts* toggle** the way Ghost is
  disabled before a bake (`VatPreviewElement.cs:186-199`), with a tooltip saying why. A toggle that
  does nothing is worse than a greyed one.

---

## 3. What is already there (verified 2026-09-08)

- The overlay column, the `ToolbarToggle` shape, the `clip-editor__overlay-tool-button` /
  `clip-editor__overlay-run-break` classes and `ToolkitIcons.SetToggleIcon` — all live in
  `VatPreviewElement.cs:95-112`, which is the Ghost toggle. The two new toggles are that block twice
  more, above it in the same column.
- The real source prefab is already instantiated and already tracked, with its renderers and their
  authored materials cached: `sourceCopyRoot`, `sourceCopyRenderers`, `sourceCopyAuthoredMaterials`,
  `RebuildSourceCopy` (`:450-481`), `RefreshSourceCopyAppearance` (`:485-…`). That copy is currently
  either the pre-bake subject or the Ghost overlay; A79 makes it the third thing too — the posed
  non-VAT half.
- `ClipSampler.SamplePose(ref ClipBlob, int targetIndex, float normalizedTime, in TargetRestPose, out TargetPose)`
  is the sampling entry (`ClipPreviewController.cs:1031`).
- `PreviewSkeletonMirror` (`:86-96`) is the depth-first hierarchy indexing pattern the node map copies.

---

## 4. The open question T0 answers

**How much of `ClipPreviewController`'s setup does a registry blob actually need?** A79-D5 commits to
`ClipSampler.SamplePose`, which needs a `ClipBlob` out of a `ClipRegistryBlob`, plus a `TargetRestPose`
per target. `ClipPreviewController` builds both (`RebuildRestPosesIfNeeded`, `ResolveRestPose`, and
whatever builds its `registry`). If that is a short, self-contained path, A79-D6 holds and §5.2 is a
~200-line poser. If it is entangled with the controller's selection or hierarchy state, the honest
answer is to host a `ClipPreviewController` in the bake preview after all — and the spec's §5.2 gets
rewritten before anyone builds it.

**Do not brief T1 until T0 has answered this in §7.**

---

## 5. Design

### 5.1 `Editor/ClipEditor/Shared/ToolkitIcons.cs` — two glyphs (A79-D3)

`public static Texture2D VatPartsGlyph` and `public static Texture2D CutoutPartsGlyph`, each a lazy
field + a `Build…Glyph()` beside `BuildGhostGlyph` (`:87-…`), reusing its distance-field helpers,
its grey, its edge softness and its `HideAndDontSave` flags. One comment total, on the pair, saying
why they are drawn rather than resolved.

### 5.2 New — `Editor/VatBaking/VatPreviewPartPoser.cs` *(shape, pending T0)*

Owns: a `ClipRegistryBlob` built for the previewed clip set, the rest-pose table, and a
`sourceNodePath` → `Transform` map into the source copy. Surface:

```csharp
public bool Rebuild(ClipSetAsset clipSet, RigAsset rig, GameObject sourceCopyRoot, out string failureMessage);
public bool HasNonVatParts { get; }          // drives A79-D8
public void PoseAt(ulong clipId, float normalizedTime);
public void RestoreRestPose();
public void Dispose();                        // the blob is Persistent — it must be disposed
```

Only targets that are **not** among the previewed set's baked parts are posed; a VAT part's transform
is the mesh's own and must not be written by the sampler as well. `Dispose` is not optional — a
`Persistent` blob leaked per rebuild is the trap the vault note names, and `VatPreviewElement`
already implements `IDisposable` for exactly this class of resource.

### 5.3 `Editor/VatBaking/VatPreviewElement.cs`

- Two `ToolbarToggle`s above the Ghost toggle in the same overlay column, same classes, same
  `SetToggleIcon` call, defaulting on (A79-D2), with the *Other parts* one disabled and tooltipped
  when `HasNonVatParts` is false (A79-D8).
- *VAT parts* off hides the baked mesh draw; *Other parts* off calls `RestoreRestPose` and stops
  posing. Neither touches `playback`, `isPlaying` or the frame readout (A79-D7).
- `Tick` calls `PoseAt` with the same clip id and normalized time it already resolves for the VAT
  frame, so the two halves cannot drift by a frame.
- `Show` rebuilds the poser whenever it rebuilds the source copy, and `Dispose` disposes it.

### 5.4 Docs

`Documentation~/rigged-characters.md`'s preview-limitation note and
`Documentation~/clip-editor.md`'s VAT Bake paragraph both currently say the bake preview shows only
the baked mesh. Update both. `CHANGELOG.md` gets a `## [0.27.0] — A79` paragraph; `package.json` →
`0.27.0`.

---

## 6. Tasks

### T0 — Grounding pass (orchestrator, before anything is briefed)

Read `ClipPreviewController.cs` around `RebuildRestPosesIfNeeded`, `ResolveRestPose` and whatever
builds its `registry` field. Answer §4 in §7, in ten lines or fewer: what a registry blob costs to
build outside that controller, what disposes it, and whether A79-D6 survives. If it does not, say so
and stop — §5.2 needs rewriting before T1 exists.

Then the usual: gate per HANDOFF §3, baseline totals in §7.

### T1 — The two glyphs [parallel-safe]
`Editor/ClipEditor/Shared/ToolkitIcons.cs` only. Read `:38-145` and §5.1. No fixture — a drawn
texture is judged by eye at T5.

### T2 — The poser
`Editor/VatBaking/VatPreviewPartPoser.cs` (new) + its fixture. Brief only after T0. One test that a
rebuild-then-dispose cycle leaks no blob, and one that a VAT-part target is not posed.

### T3 — The toggles
`Editor/VatBaking/VatPreviewElement.cs` only. Read it in full, T1's and T2's surfaces, and §5.3. No
fixture — UI.

### T4 — Gate, drive, docs (orchestrator)
Full gate. Drive A78's two-part tentacle sample: bake, then confirm each toggle state draws what it
should, and that toggling does not move the playhead. Capture the four states
(`EditorApplication.isFocused` first, scale by `pixelsPerPoint`). Docs, changelog, version, vault
note, HANDOFF §4.

### T5 — ⏸ owner checkpoint
The glyphs and the four states are his call. Stop with the captures and ask specifically about the
two drawn symbols — whether they read at 16px in the rail, and whether *Other parts* is the right
name for the half that is quads and flipbooks.

---

## 7. Build log

*(T0 fills in the §4 answer first; nothing else may be briefed until it has)*
