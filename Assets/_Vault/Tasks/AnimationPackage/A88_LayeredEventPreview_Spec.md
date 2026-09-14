# Amendment A88 — Layered event preview in the Actor Editor

> **Status:** ✅ built 2026-09-13 as `0.38.0` (the specced `0.35.0` went to A89; see §7). T9 owner checkpoint open.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1.
> **Predecessors:** A70/A71 (profiles, Actor Editor), A86 (`EventLaneStyle`), A87 (the crossing
> resolver and preview player, reused here for the composited preview).
> **Executor:** one orchestrator; `worker` subagents in **one wave of four**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A88** on the DOTS Animation Toolkit package (head `0.34.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A88_LayeredEventPreview_Spec.md`. Read it, the roadmap
§3 protocol, then only what §3 here names. T0 and T1 yours; one wave (T2–T5); one gate; T6–T8
yours. Stop at T9.

---

## 1. Goal

The Actor Editor previews a profile with several layers playing at once, and nothing shows which
events will emit. The runtime rule (`EmitAnimationEventsJob`) is: **every active layer emits its
own clip's crossings**; an inactive layer emits nothing except its `ClipFinished` on the frame it
finishes; a crossfade *source* clip emits nothing. An Override layer does not silence Base's
events. Authors assume it does, and find out in play mode.

After this amendment a strip under the Actor Editor's preview shows one row per layer, each with
that layer's current clip's markers laid on a shared time axis, the row lit when the layer is
active and dimmed when not, the crossfade-source markers ghosted; the playhead sweeps all rows
together; a crossing flashes the pin and plays A87's preview clip.

---

## 2. Decisions (recorded — do not re-ask)

- **A88-D1 — Rows mirror `PlaybackLayer` state, not authoring order alone.** The strip reads
  `ActorPreviewComposer.LayerClip / LayerTime / LayerFlags / ClipDuration` each tick; a layer with
  `clipIndex == -1` shows an empty dimmed row with the layer's name.
- **A88-D2 — Time axis is per row, in each clip's own seconds**, with the row's playhead at
  `LayerTime(i)`; rows do not share a scale because clips differ in duration. A thin label at the
  row's right end shows `t / duration`.
- **A88-D3 — Emitting rule drawn, not explained:** active rows at full alpha; inactive rows at 35%;
  crossfade-source markers (the `previousClip` slot, when `blendDuration > 0`) drawn as hollow pins
  at 35% — visibly "will not emit". No tooltip essay; the docs page carries the one-paragraph rule.
- **A88-D4 — Reuse, do not re-implement:** pins via `EventLaneStyle.DrawPin`, crossings via
  `ScrubEventCrossingResolver.Resolve` per row with that row's previous/current time, sound via the
  pane's `EditorEventPreviewPlayer`.
- **A88-D5 — Placement:** a collapsible strip below the Preview column's viewport, default
  expanded, height 22 px per row, hidden when the profile has no layers. Its expanded state is an
  `EditorPrefs` bool.

---

## 3. Read first

- `Editor/ClipEditor/ActorEditor/ActorPreviewComposer.cs` lines 100–320 (`Tick`, the `Layer*`
  accessors).
- `Editor/ClipEditor/ActorEditor/ActorEditorPanel.cs` — grep `Preview` for the column's
  construction; `ActorEditorInspectorColumn.cs` lines 1–60 for the column-element idiom.
- `Runtime/Components/PlaybackLayer.cs` in full (short) — `flags`, `previousClip`, `blendDuration`.
- `Runtime/Systems/EventEmissionSystem.cs` lines 39–110 — the emission gate this strip mirrors.
- `Editor/ClipEditor/Shared/EventLaneStyle.cs` and `Editor/ClipEditor/Preview/
  ScrubEventCrossingResolver.cs` (A86/A87) in full.

---

## 4. Design

### 4.1 `Editor/ClipEditor/ActorEditor/LayerEventStripElement.cs` (T2)

```csharp
public sealed class LayerEventStripElement : VisualElement, IDisposable
{
    public void Bind(ActorProfileAsset profile, ActorPreviewComposer composer,
        AnimEventKeyRegistry registry, EditorEventPreviewPlayer player);
    public void Tick();          // called from the panel's tick after composer.Tick
    public void Dispose();
}
```

Rows are `LayerEventRowElement` (same file, private nested) using `generateVisualContent` for
pins and playhead; one `Label` for the name and one for D2's readout. Row rebuild only when the
layer's clip changes (compare `LayerClip(i)`), never per tick.

### 4.2 Pure row model — `Editor/ClipEditor/ActorEditor/LayerEventRowResolver.cs` (T3)

`public static LayerEventRowState Resolve(in PlaybackLayer layer, ClipAsset currentClip, ClipAsset
previousClip)` → `{ bool emits, bool ghostPrevious, float time, float duration }` — the D3 rule in
one testable function.

---

## 5. Tasks

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Confirm `ActorPreviewComposer` exposes
  what §3 lists; note where the panel calls `composer.Tick`. _(It did not; §7 drift 2.)_
- [x] **T1 — Nothing to pre-write;** proceed to the wave. _(Became `LayerCount` + `Layer(int)`; §7.)_
- [x] **T2 — Strip element [parallel-safe]** — Files: new `LayerEventStripElement.cs`. Read §4.1,
  the composer accessors, `EventLaneStyle`.
- [x] **T3 — Row resolver + fixture [parallel-safe]** _(three fixtures, not one; §7 drift 11)_ — Files: new `LayerEventRowResolver.cs`, new
  `Tests/EditMode/LayerEventRowResolverTests.cs`. Fixture:
  `InactiveLayer_DoesNotEmit_AndBlendingLayerGhostsPrevious` — flags without `Active` → `emits ==
  false`; `blendDuration 0.2, previousClipIndex 3` → `ghostPrevious == true`. Revert-to-fail:
  return `emits = true` unconditionally.
- [x] **T4 — Panel hosts the strip [parallel-safe]** _(five small ranges; §7 drift 9)_ — Files: `ActorEditorPanel.cs` (the Preview
  column construction and the tick call site only; two ranges). D5 placement and prefs.
- [x] **T5 — Docs + changelog [parallel-safe]** — Files: `Documentation~/actor-profiles.md`
  ("Which layers emit events" paragraph: the D3 rule in three sentences), `CHANGELOG.md`
  `## [0.35.0]`. _(Took `## [0.38.0]`.)_
- **Gate the wave.** `LayerEventRowResolverTests`, `ActorEditorPanelTests`. Commit `A88-T2..T5`.
- [x] **T6 — Orchestrator edits.** _(New "Layered event strip" traps section appended, not the dated one; conformance pin 0.38.0.)_ `package.json`; vault note "Actor Editor" section gains the strip
  and the "rebuild only on clip change" rule.
- [x] **T7 — Drive.** Full suites. Open a profile with Base + Override, play an Override animation
  over a looping Base: both rows lit, both pins flash on crossing; stop the Override: its row dims.
  Capture. _(Full suites run. No window drive and no capture: §7 drifts 12–13; rule proven by fixture,
  mount by a detached panel query.)_
- [x] **T8 — Close.** HANDOFF §4, roadmap checkbox. _(Box stays unticked until T9 is answered; to-do line added.)_
- [ ] **T9 — ⏸ owner checkpoint.** Message: "Actor Profiles ▸ pick MaleCitizen ▸ play Walk on
  Base and an Override animation. Under the preview: one row per layer with its event pins. Does
  the dimmed/hollow language read as 'will not fire'? Should the strip start collapsed?"

---

## 6. Deliberately out of scope

- Editing markers from the strip (read-only; edit in the Clip Editor).
- Direction variants: the strip shows the clip the composer resolved, not all six.

## 7. Build log

### T0 — 2026-09-13, head `386ccc7b` (0.37.0)

Baseline compile gate clean. Suite totals inherited, not re-run: EditMode 835 (one standing
Conformance_A failure, unrelated), PlayMode 285. Drift against this spec, verified by grep, and the
call made on each (escalated at T9, not silently re-specced):

1. **Version.** `0.35.0` went to A89; A88 takes `0.38.0`. A92's `0.39.0` stands.
2. **The composer exposed too little.** No layer count, and no `previousClip`, `previousClipIndex`,
   `blendDuration` or `loop`; §4.2's `in PlaybackLayer` had no source. T1 is no longer "nothing to
   pre-write": it adds `public int LayerCount` and `public PlaybackLayer Layer(int layerIndex)` (a
   copy; an invalid index returns `clipIndex -1`, `previousClipIndex -1`). Committed `A88-T1`.
3. **No `ClipId → ClipAsset` lookup exists in Editor/.** Markers are authoring data
   (`ClipAsset.events`); the preview registry is sorted and deduped, so its index is not clip-set
   order. Settled: `LayerEventRowResolver.FindClipAsset(profile, clipId)` scans
   `profile.clipSets[*].clips` for `clip.Id == clipId`; the strip calls it only when a row's clip or
   previous clip changes.
4. **Time is not normalized.** `PlaybackLayer.time` is un-wrapped seconds, speed may be negative,
   and `loop` may be `UseClipDefault` (→ `ClipAsset.defaultLoop`). Settled, in the resolver so the
   fixture covers it: `resolvedLoop = ClipSampler.ResolveLoopMode(layer.loop, currentClip.defaultLoop)`,
   `normalizedTime = ClipSampler.MapTimeNormalized(layer.time, currentClip.duration, resolvedLoop)`
   (Once clamps, Loop wraps, PingPong reflects: the runtime's own mapping), `time = normalizedTime *
   duration`. Row playhead, readout and `ScrubEventCrossingResolver` input all use it.
5. **No preview player in the Actor Editor.** D4's "the pane's player" does not exist (TimelinePane
   creates its own lazily). Settled: the strip owns a lazily created `EditorEventPreviewPlayer`, reads
   the registry from `ClipInspectorPane.ResolveEventKeyRegistry()`, disposes the player in `Dispose`.
   `Bind` shrinks to `Bind(ActorProfileAsset profile, ActorPreviewComposer composer)`.
6. **Pin colour** is `ToolkitPalette.ColorForEventKey(marker.eventKey)` (TimelinePane :576). The
   flash copies TrackLaneElement's private 120 ms / 3 px values; no new token.
7. **Emission rule in §1/D3 was slightly wrong** (`EmitAnimationEventsJob`). A layer emits when
   `Active` **or** `FinishedThisFrame` is set, and on its finishing frame it emits its crossings as
   well as `ClipFinished`, not only `ClipFinished`. The crossfade source emits nothing. Settled:
   `emits = currentClip != null && clipIndex >= 0 && (flags & (Active | FinishedThisFrame)) != 0`;
   `ghostPrevious = (flags & Blending) != 0 && previousClipIndex >= 0 && previousClip != null`,
   keyed on the flag as the runtime is, not on `blendDuration > 0`.
8. **Two tick sites.** The panel ticks the composer in `Step(int)` (paused frame step) and in
   `Tick()`. Settled: `Tick(bool isComposerPlaying)`, not `Tick()` — the crossing resolver needs it.
   `Tick()` passes `isPlaying`; `Step` passes `true`, because a step is one frame of playback and a
   stepped loop wrap must still fire. Paused scrubs go through `SetLayerTime` and reach the strip on
   the next zero-elapsed tick with `isPlaying false`, so a big jump stays silent (A87 D1).
9. **Placement.** The transport row is also in `viewport-column`, after `viewport-frame`. Settled:
   the strip goes after the transport row, so the viewport keeps its transport and the strip sits
   where the Clip Editor puts its timeline. T4 touches five small ranges of `ActorEditorPanel.cs`,
   not two: field, construction (strip after the transport, `Bind` beside the columns' initial
   `Bind`), `Step`, the `Profile` setter's `Bind`, `Dispose`, `Tick`.
10. **Expanded pref key** `DotsAnimationToolkit.ActorEditor.LayerEventStripExpanded`, matching
    `DotsAnimationToolkit.ActorProfiles.FailPlayerBuildsOnNameErrors`.
11. **Fixture split.** One test asserting both fields lets the first failure mask the second
    mutation, and Unity's NUnit has no `Assert.Multiple`. Settled: three tests, each asserting one
    rule, so one compile proves three mutations (emits forced true; ghost keyed on `blendDuration`;
    loop left unresolved).
12. **The drive cannot use an Override animation.** MaleCitizen's layers: Base (Idle start, Walk),
    Action (MeleeContinuous, Death, Resurrection), Eyes (Blink start, DeathFace, ResurrectionFace);
    Face, Mouth, Override have none. Only `MeleeContinuous_EastFacing` carries a marker (`Attack` at
    0.35, no preview clip, so it flashes silently). Idle, Walk, Blink carry none.
13. **Drive.** `ClipEditorWindow` is single-instance and docked by the owner, so no window drive;
    the rule is proven by the fixture and the mount by a detached `new ActorEditorPanel()` query.

### Wave and close — 2026-09-13

- **T1** `edd627c9`: accessors, compile gate clean.
- **T2–T5** `851de0b6`: four parallel workers (54–79k tokens each); T2 came in at 331 lines, over
  the ~260 guidance, reviewed and kept. Review found no stray `<summary>`, `var`, single-letter names or
  per-tick rebuild. One compile gate clean; `LayerEventRowResolverTests` + `ActorEditorPanelTests` 10/10.
- **Revert-to-fail**, one compile, three mutations: `emits = true` → `InactiveLayer_…` fails (True ≠
  False); ghost on `blendDuration > 0f` → `BlendingLayer_…` fails (True ≠ False); `resolvedLoop =
  layer.loop` → `UseClipDefaultLoop_…` fails (1.0 ≠ 0.25). Restored from backup, sha256 matched, recompiled.
- **T6–T8**: `package.json` and the conformance pin at `0.38.0`; vault note section "Layered event
  strip (A88, 0.38.0)"; HANDOFF §4; roadmap status line, box version and a T9 to-do line.
- **T7 drive**: EditMode 838 (835 + 3; only the standing Conformance_A failure), PlayMode 285/285.
  A detached `new ActorEditorPanel()` via `execute_code` shows `layer-event-strip`
  (`LayerEventStripElement`, holding a `Foldout`) as the `viewport-column` child directly after
  `actor-editor-transport-row`, hidden with no profile. No window drive and no capture (docked
  single-instance window; a detached panel has no preview controller to tick).
- **Known limit, not fixed:** ⏮ pressed *while playing* can jump a looping layer's playhead backwards
  across the wrap. The next playing tick then reads it as a loop wrap and may flash markers once.
  The strip's cached time is not reset from `JumpToStart`, which would be a sixth panel range.

**Questions for the owner (T9):** the drifts above are settled as logged; do any need a different call?
Especially drift 9 (strip below the transport, not between viewport and transport) and drift 8
(a paused step counts as playing for crossings).
