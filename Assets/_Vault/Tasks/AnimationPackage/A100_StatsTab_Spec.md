# Amendment A100 — Stats tab: what the toolkit is doing at run time

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.47.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2, last.
> **Predecessors:** none hard; A82 for the split view. Reads only runtime components that exist
> today (`PlaybackLayer`, `AnimEventsPending`, `AnimEventOutput`, `AnimLod`, `VatPartTextureBinding`,
> `RagdollActor`, `CutscenePlay`).
> **Executor:** one orchestrator; `worker` subagents in **one wave of six**, each ≤ 2 files.
> **T0 contains a platform probe** (D3: `ProfilerRecorder` on system markers).

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A100 — Stats tab** on the DOTS Animation Toolkit package (head
`0.46.0` or later). Spec: `Assets/_Vault/Tasks/AnimationPackage/A100_StatsTab_Spec.md`. Read it,
the roadmap §3 protocol, then only what §3 here names. **Run T0's probe before the wave.** T0 and
T1 yours; one wave (T2–T7); one gate; T8–T11 yours. Stop at T12.

---

## 1. Goal

Nothing shows what the toolkit costs or does while the game runs: how many actors, how many are at
each LOD level, how many events fired this frame, how much VAT texture memory is resident, how long
the animation group takes. The owner's stated interest is UI and performance, and performance has no
surface. After this amendment a Stats tab reads the default world four times a second in Play mode
and shows counts, a LOD histogram, an events-per-frame sparkline, VAT memory, and per-group timings,
with a Snapshot button that writes the same numbers as a Markdown table to the clipboard for a bug
report or a devlog.

```
┌ Stats ───────────────────────────────────────────────────────────────────────────────── ● playing ┐
│ ┌ Actors ───────────────────────┐ ┌ Events / frame ─────────────────┐ ┌ Timing (ms) ───────────┐ │
│ │ Actors        412             │ │  ▁▂▁▃▂▁▁▅▂▁  now 3   peak 11    │ │ AnimationToolkit  0.42 │ │
│ │ Layers        498             │ │ Pending actors  3               │ │  Playback         0.08 │ │
│ │ Ragdolling    2               │ │ Windows open    7               │ │  Sampling         0.21 │ │
│ │ In cutscene   0               │ └─────────────────────────────────┘ │  Events           0.02 │ │
│ │ LOD  0 ▮▮▮▮▮▮ 180            │ ┌ VAT ────────────────────────────┐ │  Ragdoll          0.06 │ │
│ │      1 ▮▮▮▮ 120              │ │ Sets bound  2   Parts  9         │ └────────────────────────┘ │
│ │      2 ▮▮ 80   3 ▮ 32        │ │ Texture memory  14.2 MB          │                [Snapshot] │
│ └───────────────────────────────┘ └─────────────────────────────────┘                             │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A100-D1 — `ClipEditorTab.Stats`, after Ragdoll.** Toggle `tab-stats`, text "Stats", pane
  `stats-pane`. Not on the shared selection.
- **A100-D2 — Reads the world the runtime uses: `ToolkitWorldApi`'s world** (T0 names the accessor;
  fall back to `World.DefaultGameObjectInjectionWorld`). Edit mode shows the panel with dashes and
  "Enter Play mode". Polling at 4 Hz via `EditorApplication.update` with a stopwatch gate; never
  per editor frame.
- **A100-D3 — Counts come from `EntityQuery.CalculateEntityCount()`**, never from iterating
  entities on the main thread — except events-per-frame, which sums `AnimEventOutput` buffer
  lengths over the `AnimEventsPending`-enabled query (small by construction: only actors with events
  this frame). LOD histogram: four queries with `SharedComponent`? No — `AnimLod.level` is a plain
  byte; use one `ToComponentDataArray<AnimLod>(Allocator.Temp)` on the 4 Hz tick and bucket it. T0
  measures this on the DOTS test scene; if it exceeds 1 ms at 1k actors, the histogram is sampled
  from at most 1,024 entities and says "sampled".
- **A100-D4 — Timings are `ProfilerRecorder`s on the system groups' markers**
  (`AnimationToolkitSystemGroup` and its child groups — T0 lists them from
  `AnimationToolkitSystemGroups.cs`). **Probed in T0:** system group markers are named by type; if
  `ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "<GroupName>")` yields no samples, fall back
  to `ProfilerRecorderHandle` enumeration via `ProfilerRecorderHandle.GetAvailable` filtering by
  name; if that also fails, the Timing box shows "needs Profiler window open" and T3 is scoped to
  that message. Record the outcome.
- **A100-D5 — VAT memory sums `Texture.GetRuntimeMemorySizeLong`-equivalent** via
  `UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong` over the distinct textures referenced by
  `VatPartTextureBinding` components (a `ToComponentDataArray` on the tick; textures deduplicated by
  instance id). Editor-only API is fine here — this is an Editor assembly.
- **A100-D6 — The events sparkline keeps 60 samples** (15 s at 4 Hz) as a fixed ring buffer drawn
  with `generateVisualContent`; peak is over the ring.
- **A100-D7 — Snapshot writes Markdown to `EditorGUIUtility.systemCopyBuffer`**: one table, the
  same rows as the panel, with a timestamp and package version line. ⚠ Whether it should also write
  a file under `Library/` — the checkpoint asks.
- **A100-D8 — Nothing runtime changes.** No new components, no counters written by systems; the
  tab only reads.

---

## 3. Read first

- `Runtime/Api/ToolkitWorldApi.cs` in full — the world accessor.
- `Runtime/Systems/AnimationToolkitSystemGroups.cs` in full — group names for D4.
- `Runtime/Components/ActorStateComponents.cs` lines 60–80 (`AnimLod`, `AnimSampleState`);
  `AnimEventOutput.cs`, `PlaybackLayer.cs` (short); `VatPartTextureBinding.cs`;
  `RagdollComponents.cs` lines 10–25 (`RagdollActor`); `CutsceneComponents.cs` — grep `CutscenePlay`.
- `Editor/TexturePacker/TexturePackerPanel.cs` lines 1–80 — panel idiom.
- `Editor/ClipEditor/Shared/ToolkitPalette.cs` — the tokens for bars and the sparkline.
- Memory note `reference_editor_background_verification_limits.md` (the vault index names it):
  UI Toolkit is drivable from a session; what the drive can assert.

---

## 4. Design

### 4.1 `Editor/Stats/ToolkitStatsSample.cs` (T1, orchestrator) — plain struct: every number the
panel shows, plus `bool worldAvailable`, `bool lodSampled`, `bool timingsAvailable`.

### 4.2 `Editor/Stats/ToolkitStatsSampler.cs` (T2) — `Sample(World world, ref ToolkitStatsSample
output)`; owns the `EntityQuery`s (created lazily per world, disposed on world change) and the
`ProfilerRecorder`s. `Sampler` suffix is "pure blob+time→pose" in the vocabulary — **not** this;
name it `ToolkitStatsCollector` (plain noun, allowlist) to avoid a `Conformance_G` argument.

### 4.3 `Editor/Stats/SparklineElement.cs` (T3) — ring buffer + `generateVisualContent`.
### 4.4 `Editor/Stats/StatsSnapshotFormatting.cs` (T4) — `ToMarkdown(in ToolkitStatsSample, string
version, DateTime now) → string`; pure; plain noun allowlist (or `…Math`? no — `Formatting` is
honest; allowlist it).
### 4.5 `Editor/Stats/StatsPanel.cs` (T5) — three boxes, tick, Snapshot, edit-mode placeholder,
`Dispose` (stops recorders, disposes queries).

---

## 5. Tasks

- [ ] **T0 — Baseline + probes (orchestrator).** Gate; totals. In Play mode on `DOTSTestScene`
  with `execute_code`: (a) D4 — does a `ProfilerRecorder` on `AnimationToolkitSystemGroup`'s name
  return samples? (b) D3 — time `ToComponentDataArray<AnimLod>` over all actors. (c) `ToolkitWorldApi`
  accessor name. Record all three; brief T2 with them.
- [ ] **T1 — Sample struct (orchestrator).** §4.1. Gate. Commit `A100-T1`.
- [ ] **T2 — Collector [parallel-safe]** — Files: new `ToolkitStatsCollector.cs`. Brief carries T0's
  three findings verbatim. No fixture (needs a live world; the drive is the proof).
- [ ] **T3 — Sparkline [parallel-safe]** — Files: new `SparklineElement.cs`.
- [ ] **T4 — Snapshot formatting + fixture [parallel-safe]** — Files: new
  `StatsSnapshotFormatting.cs`, new `Tests/EditMode/StatsSnapshotFormattingTests.cs`
  (`ToMarkdown_MarksSampledLod`: `lodSampled = true` → the LOD row ends in "(sampled)"; false →
  it does not). Revert-to-fail: drop the suffix.
- [ ] **T5 — Panel [parallel-safe]** — Files: new `StatsPanel.cs`.
- [ ] **T6 — Docs [parallel-safe]** — Files: new `Documentation~/stats-tab.md` (what each number
  is, how it is measured, what "sampled" means), `README.md` (one bullet).
- [ ] **T7 — Changelog [parallel-safe]** — Files: `CHANGELOG.md` `## [0.47.0]`.
- **Gate the wave.** `StatsSnapshotFormattingTests`. Commit `A100-T2..T7`.
- [ ] **T8 — Window wiring (orchestrator).** Tab; `index.md`; `package.json`; `Conformance_G`
  allowlist (`ToolkitStatsCollector`, `StatsSnapshotFormatting`). Gate.
- [ ] **T9 — Drive.** Full suites. Enter Play on `DOTSTestScene`; the panel's actor count equals
  an `execute_code` `CalculateEntityCount` over `PlaybackLayer`; trigger events (walk actors) and
  watch the sparkline move; Snapshot → paste the clipboard into §7. Exit Play → dashes return, no
  exceptions in the console (disposed queries on a dead world is the trap to watch). Capture.
- [ ] **T10 — Vault + HANDOFF.** Vault note "Stats tab (A100)": the D4 probe outcome, the world
  accessor, the dead-world disposal order.
- [ ] **T11 — Close.** Roadmap checkbox — and the roadmap's Phase 2 is complete; say so in
  HANDOFF §4.
- [ ] **T12 — ⏸ owner checkpoint.** Message: "Enter Play and open Stats. The numbers are real —
  the snapshot from my run is in §7. Which of these do you actually want to watch, and which are
  noise? ⚠ Should Snapshot also save a file under Library/?"

---

## 6. Deliberately out of scope

- Any runtime instrumentation (D8).
- Per-actor drill-down, entity inspection (the Entities window does that).
- Historical logging across sessions.

## 7. Build log

_(empty — must hold the three T0 probe results before the wave, and the T9 snapshot)_
