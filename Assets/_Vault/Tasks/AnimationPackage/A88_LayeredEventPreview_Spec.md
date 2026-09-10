# Amendment A88 — Layered event preview in the Actor Editor

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.35.0`.
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

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Confirm `ActorPreviewComposer` exposes
  what §3 lists; note where the panel calls `composer.Tick`.
- [ ] **T1 — Nothing to pre-write;** proceed to the wave.
- [ ] **T2 — Strip element [parallel-safe]** — Files: new `LayerEventStripElement.cs`. Read §4.1,
  the composer accessors, `EventLaneStyle`.
- [ ] **T3 — Row resolver + fixture [parallel-safe]** — Files: new `LayerEventRowResolver.cs`, new
  `Tests/EditMode/LayerEventRowResolverTests.cs`. Fixture:
  `InactiveLayer_DoesNotEmit_AndBlendingLayerGhostsPrevious` — flags without `Active` → `emits ==
  false`; `blendDuration 0.2, previousClipIndex 3` → `ghostPrevious == true`. Revert-to-fail:
  return `emits = true` unconditionally.
- [ ] **T4 — Panel hosts the strip [parallel-safe]** — Files: `ActorEditorPanel.cs` (the Preview
  column construction and the tick call site only; two ranges). D5 placement and prefs.
- [ ] **T5 — Docs + changelog [parallel-safe]** — Files: `Documentation~/actor-profiles.md`
  ("Which layers emit events" paragraph: the D3 rule in three sentences), `CHANGELOG.md`
  `## [0.35.0]`.
- **Gate the wave.** `LayerEventRowResolverTests`, `ActorEditorPanelTests`. Commit `A88-T2..T5`.
- [ ] **T6 — Orchestrator edits.** `package.json`; vault note "Actor Editor" section gains the strip
  and the "rebuild only on clip change" rule.
- [ ] **T7 — Drive.** Full suites. Open a profile with Base + Override, play an Override animation
  over a looping Base: both rows lit, both pins flash on crossing; stop the Override: its row dims.
  Capture.
- [ ] **T8 — Close.** HANDOFF §4, roadmap checkbox.
- [ ] **T9 — ⏸ owner checkpoint.** Message: "Actor Profiles ▸ pick MaleCitizen ▸ play Walk on
  Base and an Override animation. Under the preview: one row per layer with its event pins. Does
  the dimmed/hollow language read as 'will not fire'? Should the strip start collapsed?"

---

## 6. Deliberately out of scope

- Editing markers from the strip (read-only; edit in the Clip Editor).
- Direction variants: the strip shows the clip the composer resolved, not all six.

## 7. Build log

_(empty)_
