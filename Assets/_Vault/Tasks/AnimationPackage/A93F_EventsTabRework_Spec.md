# Amendment A93F — Events tab rework: routing removed, usage column, buffer helper

> **Status:** 📝 specced 2026-09-14 from the owner's A93 T16 answer. Takes `0.43.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md), Phase 2 follow-up to A93.
> **Predecessors:** A93 (`0.40.0`), A94 (`AssetReferenceIndex.Dirtied`).
> **Executor:** one lead; `worker` subagents in **one wave of four**, each ≤ 2 files; the stage does the window
> wiring, the owner's stub conversion, the drive and the close.

---

## 0. Session prompt

You are running **A93F — Events tab rework** on the DOTS Animation Toolkit (head `0.42.0` or later). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A93F_EventsTabRework_Spec.md`. Read it, the roadmap §3 protocol, then only
what §3 names. §2 is settled by the owner (2026-09-14). T0 and T1 are the lead's; one wave (T2–T5); one gate;
T6–T10 belong to the stage orchestrator.

---

## 1. Goal

The owner's A93 checkpoint answer: a whole Routes panel that generates one routing `ISystem` makes no sense, and the
right panel is better used for the animations that use the event. After this amendment the Events tab is:

```
┌ Events ─────────────────────────────────────────────────────────────────────────────────────────┐
│ ┌ Keys ────────────┐ ┌ Attack (20) ──────────────────────┐ ┌ Used by ─────────────────────────┐ │
│ │ 🔍  [+][⟳]       │ │ Name, key, mask, description      │ │ Clips (2)                        │ │
│ │ ● Attack      20 │ │ Payload int / float               │ │ ┌ MeleeContinuous  @0.35 [↗] ┐ │ │
│ │ ● Footstep    16 │ │ Preview clip                      │ │ └ Walk  @0.10, 0.60      [↗] ┘ │ │
│ │ 64 maskable      │ │                                   │ │ Cutscenes (0) · Profiles (1)    │ │
│ └──────────────────┘ └───────────────────────────────────┘ └──────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Routing is gone from the package, and runtime delivery stays on the per-actor `AnimEventOutput` buffer. A small
Burst helper saves each consumer from writing the key-filter loop.

---

## 2. Decisions (owner, 2026-09-14 — do not re-ask)

- **F-D1 — Routing is removed, not hidden.** Delete `AnimEventRoutingAsset`, `AnimEventRoute`, `AnimEventRouteKind`,
  `AnimEventRoutingAuthoring` (+ baker), `AnimEventRoutingBuilder`, `AnimEventRoutingBlob`, `AnimEventRouteBlob`,
  `AnimEventRouting`, `AnimEventRoutingApi`, `AnimEventRoutingAssetUtility`, `AnimEventConsumerStubBuilder`,
  `EventRoutesColumn`, and their three fixtures (`AnimEventRoutingApiTests`, `AnimEventRoutingBuilderTests`,
  `AnimEventConsumerStubBuilderTests`), with every `.meta`. No migration: verified 2026-09-14, no routing asset exists
  and no scene or prefab carries `AnimEventRoutingAuthoring` (script guid `a869d988107c4b0e888fe402fffd246f`).
- **F-D2 — No event entities.** The owner chose the buffer after the trade-off: entity create/destroy per event is a
  structural change every frame. The standing "package ships no handler" call stays.
- **F-D3 — The right column is `EventUsageColumn`** (new, `Editor/Events/EventUsageColumn.cs`), replacing
  `EventRoutesColumn` in `EventsPanel`. It takes the usage list out of `EventKeyInspectorColumn` (`RefreshUsage` and
  `AddUsageGroup`, lines 194–295 at `ffdc6754`) and gives it room:
  - boxed groups **Clips**, **Cutscenes**, **Profiles**, each with its count;
  - one two-line row per owner asset: its name, then the marker times (`@0.35, 0.60` normalized for clips, seconds
    for cutscenes, `Layer ▸ animation` for profile ragdoll events), from `AssetReferenceIndex.ReferencesToEventKey`;
  - click pings the asset; the row's open button (`d_Linked` icon, tooltip "Open") raises
    `public event Action<UnityEngine.Object> OpenOwnerRequested`, which `EventsPanel` re-raises. The window wires it
    (T6); the column never references the window.
  - Refreshes on key selection and 500 ms after `AssetReferenceIndex.Dirtied` (the `HealthPanel` debounce pattern).
- **F-D4 — The inspector column keeps** the entry fields, payload schema and preview clip; its usage section and the
  "Used by" headline are removed.
- **F-D5 — `AnimEventBufferApi`** (new, `Runtime/Api/AnimEventBufferApi.cs`), static, Burst-compatible, no
  `[BurstCompile]` on the class:
  - `public static bool ContainsEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey)`
  - `public static bool TryFindEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey, out AnimEventOutput foundEvent)`: the first match.
  - `public static bool TryFindNextEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey, ref int searchIndex, out AnimEventOutput foundEvent)`:
    scans from `searchIndex`; on a match it returns true with `searchIndex` one past the match, so
    `while (TryFindNextEvent(...))` visits every same-key event in a frame (two layers can emit one key).
- **F-D6 — The owner's generated `Assets/_Scripts/Systems/CombatSystemGroup/DamageEventSystemAnimEventSystem.cs` is
  converted by the stage (T7)**, not deleted. It becomes a job over `AnimEventOutput` gated by `AnimEventsPending` that
  calls `AnimEventBufferApi.TryFindNextEvent` for the project's damage event key, with a `// TODO: apply damage`
  body. It is game code and untracked; it stays untracked.

---

## 3. Read first

- `Editor/Events/EventsPanel.cs` (96 lines, whole).
- `Editor/Events/EventKeyInspectorColumn.cs` lines 20–60 and 190–300.
- `Editor/ClipUtilities/AssetReferenceIndex.cs` lines 1–40 (the `AssetReference` struct) and 367–446
  (`ReferencesToEventKey`).
- `Editor/Health/HealthPanel.cs` lines 250–285 (the `Dirtied` debounce to copy).
- `Runtime/Components/AnimEventOutput.cs` (whole).
- `Documentation~/events-tab.md` and the "Reading events" part of `Documentation~/animation-events.md`.

---

## 4. Design notes

- `EventsPanel` keeps its two `CoverPaneSplitView`s (`Events.Keys` 260, `Events.Inspector` 420); `Routes` becomes
  `Usage` (`public EventUsageColumn Usage { get; }`), and `OpenOwnerRequested` is forwarded.
- `EventUsageColumn.Bind(uint eventKey)` and `Dispose()` (unhook `Dirtied` and `EditorApplication.update`).
- Row element names: `event-usage-column`, `event-usage-group`, `event-usage-row`, `event-usage-open-button`.
- The helper takes the buffer `in` (a handle, safe), never a copied `NativeArray`.

---

## 5. Tasks

- [ ] **T0 — Grounding (lead).** Grep every F-D1 name across `Packages/` and `Assets/_Scripts/`: the only game hit
  should be the owner's stub (F-D6, stage's). Confirm where `AnimEventRouteKind` and `AnimEventRouteBlob` live.
  Confirm `AssetReferenceIndex.Dirtied`'s exact signature. Confirm an EditMode fixture can create a `World`
  (grep `new World(` under `Tests/EditMode/`); if none does, the helper fixture uses `World` creation copied from
  `Tests/PlayMode/` into EditMode and says so in §7. Log drift in §7.
- [ ] **T1 — Removal + stubs (lead).** `git rm` every F-D1 file and `.meta`. Remove the `Routes` member from
  `EventsPanel.cs` (lead edit, minimal, so the removal compiles). Add committed stubs: `EventUsageColumn` with
  `Bind(uint)`, `Dispose()`, `OpenOwnerRequested`; `AnimEventBufferApi` with the three signatures returning false.
  Stage the owner's stub conflict honestly: the removal breaks `DamageEventSystemAnimEventSystem.cs` on the stage,
  not in the worktree (untracked files don't exist there). Gate `PackagingConformanceTests` (compile). Commit
  `A93F-T1`.
- [ ] **T2 — Buffer helper + fixture [parallel-safe]** — Files: `Runtime/Api/AnimEventBufferApi.cs`, new
  `Tests/EditMode/AnimEventBufferApiTests.cs`:
  - `TryFindNextEvent_VisitsEverySameKeyEventInOrder`: a buffer of keys 20, 21, 20 (distinct `intParam`s); the loop
    yields two events, `intParam` in buffer order; key 22 yields none; `ContainsEvent(21)` true.
  - Revert-to-fail: do not advance `searchIndex` past the match (the loop must be bounded by a test-side guard of
    10 iterations and fail, never hang).
- [ ] **T3 — Usage column [parallel-safe]** — Files: `Editor/Events/EventUsageColumn.cs`. F-D3 in full; the grouping
  logic moves from the inspector column (copy, then T4 deletes the original).
- [ ] **T4 — Inspector trim + panel swap [parallel-safe]** — Files: `Editor/Events/EventKeyInspectorColumn.cs`
  (delete `usageSection`, `RefreshUsage`, `AddUsageGroup` and their call sites; add NO `<summary>` blocks),
  `Editor/Events/EventsPanel.cs` (construct `Usage`, bind it on key selection, forward `OpenOwnerRequested`).
- [ ] **T5 — Docs [parallel-safe]** — Files: `Documentation~/events-tab.md` (rewrite: Keys, fields, Used by; no
  routing, no stub generator; one paragraph "reading events in your systems" pointing at `AnimEventBufferApi`),
  `Documentation~/animation-events.md` (a short "Finding one key" example with `TryFindNextEvent` inside an
  `IJobEntity` gated by `AnimEventsPending`).
- **Gate the wave.** `DotsAnimationToolkit.Tests.EditMode.AnimEventBufferApiTests`,
  `DotsAnimationToolkit.Tests.EditMode.PackagingConformanceTests` (Conformance_A the only expected failure). Revert-
  to-fail. Commit `A93F-T2..T5`. Section 7 `### For integration`: CHANGELOG `## [0.43.0]` with a **Removed** list,
  wiring, traps, HANDOFF draft. Then `worktree.py status a93f ready`.
- [ ] **T6 — Window wiring (stage).** New `public static void FocusClip(ClipAsset clip)` in `ClipEditorWindow.cs`:
  focus the Clip Editor tab, load a clip set that lists the clip (`AssetReferenceIndex.ReferencesToClip`, kind
  `ClipSetClip`), then the private `SelectClip`. In `ShowEventsTab`, subscribe `eventsPanel.OpenOwnerRequested` to
  route `ClipAsset` → `FocusClip`, `CutsceneAsset` → `FocusCutsceneTab`, `ActorProfileAsset` →
  `FocusWithActorEditorTab`; unsubscribe in teardown. CHANGELOG, `package.json`, conformance pin `0.43.0`.
- [ ] **T7 — Owner stub conversion (stage).** F-D6. Read `Assets/Generated/DotsAnimationToolkit/AnimEvents.cs` for
  the damage key constant (ask nothing: if there is no damage-named key, use the name `Attack` and say so). Compile
  gate; the file stays untracked.
- [ ] **T8 — Drive (stage).** Full suites. Detached `EventsPanel` bound to a `CreateInstance` registry copy: select
  `Attack`; the usage column lists `MeleeContinuous` with its marker time; invoking a row's open button raises
  `OpenOwnerRequested` with that clip. Confirm no routing type remains in any loaded assembly.
- [ ] **T9 — Vault + HANDOFF + close (stage).** Vault "Events tab rework (A93F, 0.43.0)"; HANDOFF §4; roadmap.
- [ ] **T10 — ⏸ owner checkpoint.** "Events: pick Attack; the right column lists MeleeContinuous @0.35 — press its
  open button and the Clip Editor opens on it. Routing and the stub button are gone; your damage system now reads the
  buffer through AnimEventBufferApi. Does the usage column show what you need?"

---

## 6. Out of scope

- Event entities (F-D2). Any package-side handler.
- Rewriting the game's existing buffer readers (`WaitForAnimEventCommand`, `AnimEventSoundSystem`, …) to the helper;
  a later game-side task if wanted.

## 7. Build log

- **2026-09-14 — Phase 0 (stage, parallel batch A93F-A95F).** Head `eb60b150`, package `0.42.0`, CHANGELOG top section `## [0.42.0]`. Baseline: compile clean; EditMode 850 (Conformance_A the one standing failure), PlayMode 285. Registry sha256: AnimEventKeyRegistry `3bdb420d…d14701`, TargetTagRegistry `dbec3d5f…eb4f`. Preflight: broker alive, hooks installed, stage blockers only the owner's five uncommitted files. Lead opus, worker sonnet.
  The owner's untracked `DamageEventSystemAnimEventSystem.cs` exists on the stage (2,813 bytes); T7 converts it at integration.
