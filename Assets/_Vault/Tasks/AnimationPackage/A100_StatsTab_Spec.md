# Amendment A100 — Stats tab: what the toolkit is doing at run time

> **Status:** ✅ built 2026-09-15 as `0.53.0` on trunk (`ClipEditorTab.Stats = 14`, after Ragdoll, before Health). T12 closed under the owner's standing rule; its question is kept in HANDOFF §4.
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

- [x] **T0 — Baseline + probes (orchestrator).** Gate; totals. In Play mode on `DOTSTestScene`
  with `execute_code`: (a) D4 — does a `ProfilerRecorder` on `AnimationToolkitSystemGroup`'s name
  return samples? (b) D3 — time `ToComponentDataArray<AnimLod>` over all actors. (c) `ToolkitWorldApi`
  accessor name. Record all three; brief T2 with them.
- [x] **T1 — Sample struct (orchestrator).** §4.1. Gate. Commit `A100-T1`.
- [x] **T2 — Collector [parallel-safe]** — Files: new `ToolkitStatsCollector.cs`. Brief carries T0's
  three findings verbatim. No fixture (needs a live world; the drive is the proof).
- [x] **T3 — Sparkline [parallel-safe]** — Files: new `SparklineElement.cs`.
- [x] **T4 — Snapshot formatting + fixture [parallel-safe]** — Files: new
  `StatsSnapshotFormatting.cs`, new `Tests/EditMode/StatsSnapshotFormattingTests.cs`
  (`ToMarkdown_MarksSampledLod`: `lodSampled = true` → the LOD row ends in "(sampled)"; false →
  it does not). Revert-to-fail: drop the suffix.
- [x] **T5 — Panel [parallel-safe]** — Files: new `StatsPanel.cs`.
- [x] **T6 — Docs [parallel-safe]** — Files: new `Documentation~/stats-tab.md` (what each number
  is, how it is measured, what "sampled" means), `README.md` (one bullet).
- [x] **T7 — Changelog [parallel-safe]** — Files: `CHANGELOG.md` `## [0.47.0]`.
- **Gate the wave.** `StatsSnapshotFormattingTests`. Commit `A100-T2..T7`.
- [x] **T8 — Window wiring (orchestrator).** Tab; `index.md`; `package.json`; `Conformance_G`
  allowlist (`ToolkitStatsCollector`, `StatsSnapshotFormatting`). Gate.
- [x] **T9 — Drive.** Full suites. Enter Play on `DOTSTestScene`; the panel's actor count equals
  an `execute_code` `CalculateEntityCount` over `PlaybackLayer`; trigger events (walk actors) and
  watch the sparkline move; Snapshot → paste the clipboard into §7. Exit Play → dashes return, no
  exceptions in the console (disposed queries on a dead world is the trap to watch). Capture.
- [x] **T10 — Vault + HANDOFF.** Vault note "Stats tab (A100)": the D4 probe outcome, the world
  accessor, the dead-world disposal order.
- [x] **T11 — Close.** Roadmap checkbox — and the roadmap's Phase 2 is complete; say so in
  HANDOFF §4.
- [x] **T12 — ⏸ owner checkpoint.** Message: "Enter Play and open Stats. The numbers are real —
  the snapshot from my run is in §7. Which of these do you actually want to watch, and which are
  noise? ⚠ Should Snapshot also save a file under Library/?"

---

## 6. Deliberately out of scope

- Any runtime instrumentation (D8).
- Per-actor drill-down, entity inspection (the Entities window does that).
- Historical logging across sessions.

## 7. Build log

### T0 — baseline and probes (2026-09-15, head `94dc6b4e`, 0.52.1)

- **Baseline.** Compile clean. EditMode 865 (864 passed, the standing `Conformance_A` only); PlayMode 285/285. Registry sha256s: AnimEventKey `3bdb420d…d14701`, TargetTag `dbec3d5f…ebd1eb4f`.
- **Concurrency note.** A peer session (`stitch-punk-ae`) was mid-way through the 0.52.1 Sprite Sheets → Flipbooks rename on trunk when T0 began; my first PlayMode baseline died on a stale Bee graph naming `SpriteSheetAsset.cs` (CS2001). Held all writes until it committed `94dc6b4e`, then re-ran both baselines clean.
- **(a) D4 timings — works, by a different marker name.** `ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "AnimationToolkitSystemGroup")` (and the namespaced name) is `Valid == false`. `ProfilerRecorderHandle.GetAvailable` shows system markers are named **`"<World.Name> <Type.FullName>"`**, category Scripts, unit nanoseconds — e.g. `Default World DotsAnimationToolkit.AnimationToolkitSystemGroup`. Started with that name, all five groups are valid and sample without the Profiler window open (count 5 after a few frames on an empty scene: toolkit 0.026 ms, binding 0.003, logic 0.007, presentation 0.014, ragdoll 0.005). The recorder name therefore depends on the world, so recorders are recreated on a world change. The spec's mock-up names (Playback/Sampling/Events) do not exist: the real child groups are **Binding, Logic, Presentation, Ragdoll** (Ragdoll nests inside Presentation).
- **(b) D3 LOD histogram — no sampling needed.** `DOTSTestScene` has **no toolkit actors** (16 entities, 0 with `PlaybackLayer`). Probed instead by creating 1,000 temporary `AnimLod` entities in the Play world, then destroying them: `ToComponentDataArray<AnimLod>(Temp)` plus bucketing took 0.005 ms warm, 0.026 ms cold — far under 1 ms. The collector buckets every actor; `lodSampled` stays in the struct and the snapshot for a cap of 100,000 actors, above which it samples the first 1,024.
- **(c) World accessor.** `ToolkitWorldApi` has only `SetEnabled`/`IsEnabled`; no accessor. D2 falls back to `World.DefaultGameObjectInjectionWorld` ("Default World"; the Play world also carries five Streaming loading worlds, which are ignored).
- **Drift.** "Windows open" has no counter component; it is read as the count of actors with `AnimEventMask` enabled (`EventWindowSystem` enables it while any window is open), labelled "Actors with windows open". Because `DOTSTestScene` has no actors, T9's actor-count check compares against probe entities the drive creates in the Play world (never saved).

### T2–T8 — one wave of six workers, then the wiring (2026-09-15)

- Six sonnet `worker`s in parallel (collector, sparkline, formatting + fixture, panel, docs, changelog), all complete, no caps, no respawns. T8 (tab enum, window, UXML, the three layout name lists, the plain-noun allowlist, index.md) was applied by the orchestrator while the panel worker ran, since no file overlapped, and gated with the wave.
- **One compile gate, clean first time.** Touched fixtures: 22 run, 21 passed, the standing `Conformance_A` only (Conformance_G, Conformance_I, `ClipEditorLayoutTests`, `StatsSnapshotFormattingTests`).
- **Revert-to-fail:** `FormatLodCounts` with the " (sampled)" suffix replaced by "" made `ToMarkdown_MarksSampledLod` red ("Expected: True But was: False"); restored, green. Kept.
- Committed `ce1dfc51` (`A100-T2..T8`).
- **Deviation from D3:** `layerCount` sums `PlaybackLayer` buffer lengths per chunk (a `BufferAccessor` walk), not a pure query count; the only other main-thread walk is the events sum over pending actors, as D3 allows.

### T9 — full suites and the drive (2026-09-15)

- **Full suites:** EditMode 866 (865 passed, standing `Conformance_A` only; +1 for `StatsSnapshotFormattingTests`), PlayMode 285/285.
- **Drive, Play mode on `DOTSTestScene`, a detached `StatsPanel` (the owner's docked window untouched).** The scene has no toolkit actors, so the drive created 12 actors (two `PlaybackLayer`s each, `AnimLod` 6/3/2/1) and 4 event carriers (`AnimEventsPending` enabled, three `AnimEventOutput` each) in the Play world only; nothing saved.
  - First poll: world "Default World", actors 12, layers 24, LOD 6/3/2/1 not sampled, events 12, pending 4, `timingsAvailable` false (recorders created on this poll, no samples yet).
  - Second poll a few frames later: **panel actors 12 = world `CalculateEntityCount` over `PlaybackLayer` 12**; actors label "12", "playing"; timings available: toolkit group 0.059 ms, Logic 0.033, Presentation 0.015; sparkline count 3, peak 12, latest 0 (the toolkit cleared the hand-made carriers after a frame, so the sparkline moved; real walking actors were not available in this scene).
  - Snapshot to `EditorGUIUtility.systemCopyBuffer`, read back (564 chars; the version line read 0.52.1 because the manifest bump is a close step):

```
## DOTS Animation Toolkit stats

Version 0.52.1 · captured 2026-09-15 05:58:55 · world Default World

| Stat | Value |
| --- | --- |
| Actors | 12 |
| Layers | 24 |
| Ragdolling | 0 |
| In cutscene | 0 |
| LOD 0 / 1 / 2 / 3 | 6 / 3 / 2 / 1 |
| Events this frame | 0 |
| Actors with pending events | 0 |
| Actors with windows open | 0 |
| VAT parts bound | 0 |
| VAT textures | 0 |
| VAT texture memory | 0.0 MB |
| AnimationToolkit group | 0.06 ms |
| Binding group | 0.00 ms |
| Logic group | 0.03 ms |
| Presentation group | 0.01 ms |
| Ragdoll group | 0.00 ms |
```

  - **Exit Play, same panel:** `RefreshNow` against the destroyed world did not throw (the collector drops, rather than disposes, queries whose world is gone); actors "—", "not playing", snapshot "world — · not playing"; `Dispose` twice without throwing. Console: 0 errors, 0 warnings. `DOTSTestScene` not dirty; `TestArea` (the scene the owner had open) reloaded afterwards.
  - **Drive-found fix:** in Edit mode the five timing cells read "unavailable" instead of the dash; now "unavailable" only for a Play world whose recorders have no samples, the dash otherwise.
- Registry sha256s unchanged at close.

### Not seen (owner away; visual checks skipped by instruction)

- The tab was never captured or looked at: box layout, split widths, the LOD bars' fill and scale, the sparkline's stroke, the playing dot's colour, and whether `d_SaveAs` (reused from Flipbooks) reads as "Snapshot".
- VAT memory and the VAT counts on real baked actors (`DOTSTestScene` has none; every VAT number was 0), non-zero ragdoll and cutscene counts, events from actors actually walking, and the `(sampled)` path above 100,000 actors.
- The tab inside the docked window (only a detached panel was driven); the 4 Hz `EditorApplication.update` gate firing on its own (the drive called `RefreshNow`).

### Close

- `package.json` and the `PackagingConformanceTests` pin at `0.53.0`; CHANGELOG `## [0.53.0]`; `stats-tab.md`, `index.md`, README bullet; HANDOFF header and §4 paragraph; AnimationToolkit.md "Stats tab (A100, 0.53.0)"; roadmap box. Phase 2 of the roadmap is complete.
- **T12 closed under the standing rule.** Question kept in HANDOFF §4: which numbers are worth watching and which are noise, and should Snapshot also save a file under `Library/`?
