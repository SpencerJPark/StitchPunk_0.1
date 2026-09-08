# Unified Clip Authoring — Design Spec (UA)

> **Status:** 🟡 **P0 built 2026-09-08, P1–P5 specced not built.** P0 (bone tracks pose without a
> baked registry) landed and is verified by capture — scrubbing a bone-only clip now moves the rig.
> Everything below it is written and unstarted.
> **Owner directive that created this spec (2026-09-08, verbatim):** *"I should be animating
> everything in the same window, vat bones, object transforms, flipbooks, etc…."*
> **Why it exists:** the Clip Editor's viewport is wired to one authoring model — cutout parts
> resolved through a **baked clip registry**. Every other track kind either rides along by accident
> or is invisible. The window already *stores* five kinds of track; it only *shows* some of them.

---

## 1. Purpose & scope

Make the Clip Editor the one window where every kind of animation in this toolkit is authored and,
crucially, **seen while it is authored**. A track kind that cannot be watched cannot be animated.

**In scope:** the preview path for every `ClipAsset` track kind, the timeline lanes that drive them,
and the status/validation copy that currently mislabels a legitimate rig as broken.

**Out of scope:** the runtime blob format, the VAT bake pipeline itself, and the Actor/Cutscene
editors' own timelines. This spec is about what the Clip Editor can show and edit.

---

## 2. What exists today (verified 2026-09-08 — re-verify before trusting)

Every row below was checked against the code, not assumed.

| Track kind | Field on `ClipAsset` | Timeline lanes? | Previewed in the viewport? |
|---|---|---|---|
| Cutout part transforms | `transformTracks` | yes | yes — **only** through the baked registry |
| Flipbook / sprite swaps | `spriteTracks` | yes | yes — same registry path (`TargetPose.sliceIndex`, `atlasRect`) |
| Billboards | `billboardTracks` | yes | yes — same registry path |
| Skinned bones | `boneTracks` | yes | **yes, as of P0** — registry-free |
| VAT parts | `vatTracks` | n/a — **not a keyed track** | **no** — no viewport preview in this window |
| Imported VAT clip | `vatSource.sourceClip` | **no** | **no** — an imported `AnimationClip` is invisible to the timeline |
| Events | `events` | yes | n/a |

### 2.1 What is already unified — do not rebuild this

The runtime side is not the gap. Verified 2026-09-08:

- **VAT is saved with the normal tracks.** The same per-clip `ClipBlob` that carries transform tracks
  and events also carries `vatFrameStart`, `vatFrameCount`, `vatFps` and `vatTargetRanges`;
  `ClipRegistryBlob` carries `vatSetKey` and `vatInfo`. One clip id resolves both.
- **VAT ticks alongside the normal animation path.** `VatMaterialSystem` is
  `[UpdateInGroup(AnimationToolkitPresentationSystemGroup)]` `[UpdateAfter(TransformSampleSystem)]` —
  the same presentation group as the transform sampling, immediately after it.
- **One bake holding many animations is designed for, not a gap.** A texture set is a shared strip;
  each clip names its own window into it (`vatFrameStart` + `vatFrameCount`), and a target-scoped part
  gets its own entry in `vatTargetRanges`, checked first by dense target index and falling back to the
  clip's untargeted range. Authoring side: `VatTextureSetAsset.clipRanges`, one `VatClipRange` per
  (clipId, targetId).
- **The baked set, not `vatTracks`, is the source of truth at bake time** — `vatTracks` is authoring
  intent, `clipRanges` is what was actually written (`ClipRegistryBuilder.FillVatTargetRanges`).

So the whole gap is editor-side visibility, which is what this spec is about.

### 2.2 The root cause

`ClipPreviewController.SamplePose(clipId, normalizedTime)` opened with:

```csharp
if (!registry.IsCreated) { return false; }
```

`registry` is the **baked `ClipRegistryBlob`**, built by `ClipRegistryBuilder.Build(rig, clipSets)`
from the rig's **targets**. A skinned rig that declares no cutout targets builds no registry at all —
measured on `VatSampleTentacleRig`: `registry.IsCreated = False`, `SamplePose(...) = False`.

So the whole preview, including the bone posing that sat at the *end* of that method, was gated on a
structure bone tracks never enter. `FindClipById`'s own comment says so: *"Bone tracks are
authoring-only data — they never reach the blob."* They were gated on the blob anyway.

This is the shape of the general problem: **preview is coupled to the cutout bake, not to the
authored data.** Each phase below decouples one more kind.

---

## 3. Phases

### P0 — Bone tracks pose without a registry ✅ built 2026-09-08

`SamplePose` now poses `boneTracks` **before** the registry guards and returns `posedBones` instead
of `false` when there is no registry or the clip is not in it. Socket markers moved after the bone
pose, which also fixes a one-frame lag they had (markers read the skeleton, and were updating before
it was posed).

**There were two registry gates, not one.** The second lived in the window's own render loop:

```csharp
else if (selectedClip != null && previewController.HasRegistry
    && !previewController.SamplePose(selectedClip.Id.Value, playheadTime))
```

`HasRegistry` short-circuited, so with a targetless rig `SamplePose` was **never called** and fixing
its interior changed nothing on screen. Removed; the "not in the built registry" message now only
appears when a registry actually exists and the clip is missing from it.

**Verification trap that hid this:** calling `SamplePose` directly proves the poser works and proves
nothing about the window. Verify by scrubbing through `SetPlayheadTime` (what the ruler calls) and
then reading the live previewed Transforms in a **separate** call — never in the one that scrubbed,
and never after calling the sampler yourself. Measured that way: at `t = 0.25` with
`HasRegistry = False`, Bone0 3.80°, Bone3 −0.47°, Bone6 −7.98°, Bone9 2.41°, Bone11 11.34°.

### P1 — A rig with no cutout targets is not a broken rig

The banner **"Rig 'X' declares no targets."** and its validation warning read as an error on a rig
that is legitimately skinned-only. Decide what a targetless rig means and say that instead.

← **DECISION:** is a targetless rig (a) always fine, (b) fine only when the clip set has bone or VAT
tracks, or (c) still worth a warning but worded as information? Recommend (b).

### P2 — Registry-free preview for every authored kind

Split `SamplePose` into per-kind posers, each guarded only on the data it needs, so one missing piece
never silences the others:

- `PoseBoneTracks` — done in P0.
- `PoseCutoutParts` — registry-dependent by nature (targets are the blob's identity). Keep the guard,
  but return "did nothing" rather than "failed".
- `PoseSpriteTracks` / `PoseBillboards` — currently reached only inside the registry loop. Establish
  whether a sprite track can be previewed against a rig target that exists but has no baked blob.

← **DECISION:** build a registry for a targetless rig (an empty-target blob so one code path serves
everything), or keep cutouts on the blob and let the other kinds bypass it? The first is less code at
the call sites and more code in the builder.

### P3 — Show the baked VAT in this window

**Correction to an earlier reading of this gap: `vatTracks` is not a keyframed track and does not
want a lane.** A `VatTrack` is `{targetId, sourceClip, sampleFps, loopSafe}` — a *binding* saying
"this rig target's VAT is baked from that AnimationClip", with no keys in it. Drawing diamonds for it
would be inventing data. The runtime side is already complete and already unified (see 2.1).

What is actually missing is two things:

1. **A binding row**, not a lane — a visible, editable statement in the clip's component stack that a
   target is VAT-driven, by which source, at what rate. Today this is hand-edited on the asset.
2. **Viewport preview of the baked result** — reuse `VatPreviewMaterial` and the runtime mesh the VAT
   Bake tab already draws, on the Clip Editor's shared playhead, so scrubbing moves the VAT part and
   the bones together and a drift between them is visible where the authoring happens.

Depends on the VAT Bake window's preview, which now works end to end and can be lifted.

### P4 — Imported clips as read-only reference lanes

An `AnimationClip` behind `vatSource.sourceClip` (or a `VatTrack.sourceClip`) shows nothing. Sample
its curves at the clip's frame rate and draw them as **read-only** lanes, clearly distinct from
authored keys, so a bake driven by an imported clip is still legible in the timeline.

← **DECISION:** read-only display only, or an "import to bone tracks" action that converts the curves
into authored keys the way the sample tentacle now ships? The second is more useful and more
dangerous — it doubles the source of truth if the imported clip stays referenced.

### P5 — One transport, one timeline, every kind

With P2–P4 landed, confirm the invariants that make it feel like one window rather than four modes
sharing a viewport: one playhead drives every kind; selecting a bone does not hide unrelated lanes
without saying so (today: *"focused on Bone0 (11 track(s) hidden — deselect to show all)"*); and the
transport's frame count is the clip's, not any one track's.

---

## 4. Verification

Per phase, and none of it is a unit test — this is a *seeing* feature:

1. Compile gate, then capture the window (`GUIView.GrabPixels`, scale by `pixelsPerPoint`).
2. Scrub to three times and confirm the viewport differs at each — the failure mode this spec exists
   to kill is a timeline that scrubs while nothing moves.
3. For each kind, one clip that drives **only** that kind, previewed against a rig that supports
   **only** that kind. A kind that works only alongside cutouts has not been decoupled.
4. `PackagingConformanceTests` stays at its current 11/12 (`Conformance_A` is a pre-existing asmdef
   drift, unrelated).

---

## 5. Open decisions for the owner

Recorded, not guessed. Nothing below should be decided by an executor session alone.

1. **P1** — what a targetless rig should say.
2. **P2** — empty-target registry, or per-kind bypass.
3. **P4** — read-only imported lanes, or convert-to-authored.
4. **Priority** — P3 (VAT in the window) is the largest single piece and the one the owner's phrase
   *"vat bones"* names first. P2 is the smaller unlock that makes flipbooks and transforms behave the
   way bones now do. Which goes first?
