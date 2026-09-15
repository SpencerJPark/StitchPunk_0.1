# Amendment A93F — Events tab rework: routing removed, usage column, buffer helper

> **Status:** ✅ built 2026-09-14 as `0.43.0` in the A93F–A95F parallel worktree batch (merged `6b515793`, integrated `f67b47e3`); ⏸ T10 owner checkpoint open. Specced the same day from the owner's A93 T16 answer.
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

- [x] **T0 — Grounding (lead).** Grep every F-D1 name across `Packages/` and `Assets/_Scripts/`: the only game hit
  should be the owner's stub (F-D6, stage's). Confirm where `AnimEventRouteKind` and `AnimEventRouteBlob` live.
  Confirm `AssetReferenceIndex.Dirtied`'s exact signature. Confirm an EditMode fixture can create a `World`
  (grep `new World(` under `Tests/EditMode/`); if none does, the helper fixture uses `World` creation copied from
  `Tests/PlayMode/` into EditMode and says so in §7. Log drift in §7.
- [x] **T1 — Removal + stubs (lead).** `git rm` every F-D1 file and `.meta`. Remove the `Routes` member from
  `EventsPanel.cs` (lead edit, minimal, so the removal compiles). Add committed stubs: `EventUsageColumn` with
  `Bind(uint)`, `Dispose()`, `OpenOwnerRequested`; `AnimEventBufferApi` with the three signatures returning false.
  Stage the owner's stub conflict honestly: the removal breaks `DamageEventSystemAnimEventSystem.cs` on the stage,
  not in the worktree (untracked files don't exist there). Gate `PackagingConformanceTests` (compile). Commit
  `A93F-T1`.
- [x] **T2 — Buffer helper + fixture [parallel-safe]** — Files: `Runtime/Api/AnimEventBufferApi.cs`, new
  `Tests/EditMode/AnimEventBufferApiTests.cs`:
  - `TryFindNextEvent_VisitsEverySameKeyEventInOrder`: a buffer of keys 20, 21, 20 (distinct `intParam`s); the loop
    yields two events, `intParam` in buffer order; key 22 yields none; `ContainsEvent(21)` true.
  - Revert-to-fail: do not advance `searchIndex` past the match (the loop must be bounded by a test-side guard of
    10 iterations and fail, never hang).
- [x] **T3 — Usage column [parallel-safe]** — Files: `Editor/Events/EventUsageColumn.cs`. F-D3 in full; the grouping
  logic moves from the inspector column (copy, then T4 deletes the original).
- [x] **T4 — Inspector trim + panel swap [parallel-safe]** — Files: `Editor/Events/EventKeyInspectorColumn.cs`
  (delete `usageSection`, `RefreshUsage`, `AddUsageGroup` and their call sites; add NO `<summary>` blocks),
  `Editor/Events/EventsPanel.cs` (construct `Usage`, bind it on key selection, forward `OpenOwnerRequested`).
- [x] **T5 — Docs [parallel-safe]** — Files: `Documentation~/events-tab.md` (rewrite: Keys, fields, Used by; no
  routing, no stub generator; one paragraph "reading events in your systems" pointing at `AnimEventBufferApi`),
  `Documentation~/animation-events.md` (a short "Finding one key" example with `TryFindNextEvent` inside an
  `IJobEntity` gated by `AnimEventsPending`).
- **Gate the wave.** `DotsAnimationToolkit.Tests.EditMode.AnimEventBufferApiTests`,
  `DotsAnimationToolkit.Tests.EditMode.PackagingConformanceTests` (Conformance_A the only expected failure). Revert-
  to-fail. Commit `A93F-T2..T5`. Section 7 `### For integration`: CHANGELOG `## [0.43.0]` with a **Removed** list,
  wiring, traps, HANDOFF draft. Then `worktree.py status a93f ready`.
- [x] **T6 — Window wiring (stage).** New `public static void FocusClip(ClipAsset clip)` in `ClipEditorWindow.cs`:
  focus the Clip Editor tab, load a clip set that lists the clip (`AssetReferenceIndex.ReferencesToClip`, kind
  `ClipSetClip`), then the private `SelectClip`. In `ShowEventsTab`, subscribe `eventsPanel.OpenOwnerRequested` to
  route `ClipAsset` → `FocusClip`, `CutsceneAsset` → `FocusCutsceneTab`, `ActorProfileAsset` →
  `FocusWithActorEditorTab`; unsubscribe in teardown. CHANGELOG, `package.json`, conformance pin `0.43.0`.
- [x] **T7 — Owner stub conversion (stage).** F-D6. Read `Assets/Generated/DotsAnimationToolkit/AnimEvents.cs` for
  the damage key constant (ask nothing: if there is no damage-named key, use the name `Attack` and say so). Compile
  gate; the file stays untracked.
- [x] **T8 — Drive (stage).** Full suites. Detached `EventsPanel` bound to a `CreateInstance` registry copy: select
  `Attack`; the usage column lists `MeleeContinuous` with its marker time; invoking a row's open button raises
  `OpenOwnerRequested` with that clip. Confirm no routing type remains in any loaded assembly.
- [x] **T9 — Vault + HANDOFF + close (stage).** Vault "Events tab rework (A93F, 0.43.0)"; HANDOFF §4; roadmap.
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
- **2026-09-14 — T0 grounding (lead, worktree `spec/a93f` from `2fe3038a`).** Grep of every F-D1 name over `Packages/`, `Assets/_Scripts/` and `Docs/`: the 9 source files + 3 fixtures holding the F-D1 types, plus `CHANGELOG.md` (0.40.0 history, stays), `Documentation~/events-tab.md` (T5) and `Docs/AnimationToolkit/HANDOFF.md` (stage). No game hit in the worktree (the owner's stub is untracked). No hit in `PackagingConformanceTests` allowlists, `Samples~`, asmdefs or USS. `AnimEventRouteKind` and `AnimEventRouteBlob` both live in `Runtime/Blobs/AnimEventRoutingBlob.cs`; `AnimEventRoute` in `Authoring/Assets/AnimEventRoutingAsset.cs`. `AssetReferenceIndex.Dirtied` is `public static event Action Dirtied;` (line 51). An EditMode fixture already creates a `World` (`ActorPreviewParityTests.cs:71`), so the helper fixture stays in EditMode.
  Drift and lead decisions (6):
  1. `Documentation~/index.md` lines 159–161 still describe the routing asset and stub generator; the file is stage-owned (see For integration).
  2. `ReferencesToEventKey` details are per marker (`marker @0.35`, `marker @1.20s`, `Layer X ▸ anim`), so `EventUsageColumn` strips `marker ` and joins to `@0.35, 0.60`; `AssetReferenceIndex` is unchanged.
  3. The lead wired `EventsPanel` fully in T1 (construct `Usage`, bind on selection, forward `OpenOwnerRequested`, dispose), so T4 became the inspector trim only.
  4. `EventsPanel` no longer subscribes `AssetReferenceIndex.Rebuilt` (its only use was `Inspector.RefreshUsage`); the usage column refreshes on `Dirtied` (debounced) and never on `Rebuilt`, because its own query can fire `Rebuilt`.
  5. `EventUsageColumn` gains `public void RequestOpenOwner(Object)` and `public void Refresh()` beyond F-D3, and each open button carries its owner in `userData`, so T8 can drive it detached.
  6. The spec said the T1 removal cannot break worktree gates. It does: gates compile on the stage, where the untracked stub lives. See the T1 entry.
- **2026-09-14 — T1 (lead).** `8c303d41`: 24 files `git rm`'d (9 sources + 3 fixtures, each with its `.meta`), stubs `EventUsageColumn` and `AnimEventBufferApi`, `EventsPanel` swapped. Gate `PackagingConformanceTests`: **compile-errors**, the one error `DamageEventSystemAnimEventSystem.cs(37,42) CS0246 AnimEventRoutingBlob` in the owner's untracked stage stub (F-D6). The lead may not touch `Assets/_Scripts`, so the stage was messaged.
  7. Stage converted the stub early (plain loop) so a93f gates compile; T7 swaps in the helper. The stub is now a plain `AnimEventOutput` loop comparing against `AnimEvents.Damage`, still untracked.
- **2026-09-14 — Wave T2–T5 (four sonnet workers, one file pair each).** `052030c7`. Gate `AnimEventBufferApiTests` + `PackagingConformanceTests`: **12 passed, 1 failed of 13 named** (1 helper test + 12 conformance). The one failure is the standing Conformance_A (Editor asmdef's extra `Unity.RenderPipelines.Universal.Runtime`), and there were no compile errors. This gate also covers the T1 re-gate after the stage's early stub conversion. Revert-to-fail: mutation `e0980acb` (`searchIndex = index`) gated **11 passed, 2 failed**, `TryFindNextEvent_VisitsEverySameKeyEventInOrder` failing with "did not advance searchIndex past its match" (the guard fired, no hang). Then `git reset --hard HEAD~1`, and `AnimEventBufferApi.cs` sha256 `a4c2ceb8…96d2ec4e` matches the committed file. T5 note: the "Finding one key" example names the key through `GameEventKeys`, the class that doc's existing examples already use.
  Unverified in the worktree (static review only): `EventUsageColumn` layout and behaviour (no drive; T8), the row-click/open-button split (the row ignores a `ClickEvent` whose target is the open button), and the `.toolkit-box` USS classes it reuses from `ClipEditorWindow.uss`, which may not be loaded when the panel is used detached.

### For integration

**CHANGELOG** (top, above `## [0.42.0]`):

```
## [0.43.0] — A93F — Events tab rework

### Changed
- Events tab right column is now **Used by** (`EventUsageColumn`): boxed Clips, Cutscenes and Profiles groups with counts, one two-line row per asset (name, then `@0.35, 0.60` for clips, seconds for cutscenes, `Layer ▸ animation` for profile ragdoll events). Click pings; the open button raises `EventsPanel.OpenOwnerRequested`, which the window routes to the Clip Editor, Cutscene tab or Actor Editor. Refreshes on key selection and 500 ms after `AssetReferenceIndex.Dirtied`.
- `EventKeyInspectorColumn` keeps the entry fields, payload schema and preview clip; its usage list and `RefreshUsage` are gone.
- `EventsPanel.Routes` is replaced by `EventsPanel.Usage`; the panel no longer listens to `AssetReferenceIndex.Rebuilt`.

### Added
- `AnimEventBufferApi` (Runtime, Burst-compatible): `ContainsEvent`, `TryFindEvent`, `TryFindNextEvent(in DynamicBuffer<AnimEventOutput>, uint, ref int, out AnimEventOutput)`; a `while` loop over `TryFindNextEvent` visits every same-key event in a frame.
- `ClipEditorWindow.FocusClip(ClipAsset)`.

### Removed
- Event routing: `AnimEventRoutingAsset`, `AnimEventRoute`, `AnimEventRouteKind`, `AnimEventRoutingAuthoring` and `AnimEventRoutingBaker`, `AnimEventRoutingBuilder`, `AnimEventRoutingBlob`, `AnimEventRouteBlob`, `AnimEventRouting`, `AnimEventRoutingApi`, `AnimEventRoutingAssetUtility`, `AnimEventConsumerStubBuilder`, `EventRoutesColumn`, and the fixtures `AnimEventRoutingApiTests`, `AnimEventRoutingBuilderTests`, `AnimEventConsumerStubBuilderTests`. No migration: no routing asset or `AnimEventRoutingAuthoring` existed in the project. Events stay on the per-actor `AnimEventOutput` buffer; the package ships no handler.
```

**Conformance_G allowlist:** none needed. `AnimEventBufferApi` ends in `Api`; `EventUsageColumn` is not static. Removed names that were never on the allowlist need no deletion (grep of `PackagingConformanceTests.cs` for routing names: zero hits).

**Wiring (T6):**
- No new enum member, UXML toggle or pane: the Events tab and its pane already exist.
- In `ShowEventsTab`: after `new EventsPanel()` and `Bind()`, add `eventsPanel.OpenOwnerRequested += OnEventsPanelOpenOwnerRequested;`. The handler routes `ClipAsset` to `FocusClip`, `CutsceneAsset` to `FocusCutsceneTab`, and `ActorProfileAsset` to `FocusWithActorEditorTab`.
- In teardown: unsubscribe first, then `eventsPanel.Dispose()`. Dispose now also disposes `Usage`, which unhooks `Dirtied` and `EditorApplication.update`.
- If the window references `eventsPanel.Routes` or `AnimEventRoutingAssetUtility` anywhere, delete those lines. The lead's grep found none outside `EventsPanel.cs`.
- Drive handles (T8): `Usage.Refresh()`, `Usage.RequestOpenOwner(Object)`, and each `event-usage-open-button` carries its owner in `userData`. Element names: `event-usage-column`, `event-usage-group`, `event-usage-row`, `event-usage-open-button`.
- `Documentation~/index.md` lines 159–161 still describe "the routing asset a host bakes and reads, and the consumer-stub generator … never handles a route". Replace them with: "catalog with its 64-key budget, each key's fields and payload, and a Used by column that opens the clips, cutscenes and profiles using the event."

**Vault-note traps:**
- Gates compile on the stage, so an untracked stage file that names a removed type breaks every worktree gate. The spec wrongly assumed otherwise; the stage converted the owner stub early.
- `EventUsageColumn` must never listen to `AssetReferenceIndex.Rebuilt`: its own `ReferencesToEventKey` query can fire it (a refresh loop). Listen to `Dirtied`, debounced.
- `ReferencesToEventKey` emits one reference per marker with a `marker @` prefix. The column groups by owner and reformats; other callers still see the raw per-marker details.
- The helper takes `in DynamicBuffer` (a handle). Do not add `[BurstCompile]` to the class; it is called from inside the consumer's Burst job.

**HANDOFF draft (section 4):** A93F (0.43.0) removes event routing from the package. The routing asset, authoring and baker, blob, API, asset utility, consumer-stub generator, Routes column and their three fixtures are gone, with no migration because no routing data existed. The Events tab's right column is now Used by (`EventUsageColumn`): boxed Clips, Cutscenes and Profiles groups whose rows ping on click and open the owner through `EventsPanel.OpenOwnerRequested`, wired by the window to `FocusClip`, `FocusCutsceneTab` and `FocusWithActorEditorTab`. The inspector column lost its usage list. Runtime delivery stays on the per-actor `AnimEventOutput` buffer. The new Burst-compatible `AnimEventBufferApi` (`ContainsEvent`, `TryFindEvent`, `TryFindNextEvent`) saves consumers the key-filter loop, and the owner's damage system reads through it after T7. Open: T10 owner checkpoint on whether the usage column shows what he needs.

### Close (stage, 2026-09-14)

- **Merge:** fast-forward to `6b515793`, pushed. `worktree.py remove` dropped git's record but failed WinError 32 on the
  empty worktree folder (the finished lead's process still held it); `spec/a93f` was deleted after
  `git merge-base --is-ancestor spec/a93f main` (a plain `branch -d` refused because the stage HEAD was detached for
  another lead's gate).
- **T6 (integration `f67b47e3`):** `ClipEditorWindow.FocusClip(ClipAsset)` and `OnEventsPanelOpenOwnerRequested`
  (clip → `FocusClip`, `CutsceneAsset` → `FocusCutsceneTab`, `ActorProfileAsset` → `FocusWithActorEditorTab`),
  subscribed in `ShowEventsTab` and unsubscribed before `Dispose`. Drift: the clip is selected through
  `clipListPane.SelectClipRow`, the path `RestoreView` uses, instead of the private `SelectClip`, so the row is
  highlighted as well as loaded. The open set is kept when it already lists the clip, and a clip that no set lists is
  pinged. `index.md` lines 158–161 no longer describe routing. Compile clean; not driven (the docked window is the
  owner's).
- **T7:** the owner's stub was converted in two steps. The T1 gate compiled on the stage, where the untracked file
  lives, and failed on `AnimEventRoutingBlob`, so the stage first rewrote it as a plain `AnimEventOutput` loop
  (compiles on trunk and every branch). At integration it moved to
  `AnimEventBufferApi.TryFindNextEvent(events, AnimEvents.Damage, ref searchIndex, out damageEvent)` inside a
  `while`, with `// TODO: apply damage`. A `Damage` key exists (`0x11`), so the `Attack` fallback was not needed. The
  routing `RequireForUpdate` and job fields are gone; the owner's "Place after EventEmissionSystem" note stays. Still
  untracked; the original is backed up in the session scratchpad. C# compile clean. One Burst hash error in
  `StitchPunk.Systems` sits before the integration reload in the Editor log and lists the whole assembly's systems:
  the standing Burst cache corruption, not this file.
- **Gates:** integration fixtures 32 passed of 33 (Conformance_A only); full EditMode 850 of 851 (Conformance_A only;
  851 = 850 − the three routing fixtures + the three batches' new fixtures); PlayMode 285 of 285.
- **T8 drive:** detached `EventsPanel` bound to an `EditorJsonUtility` copy of the registry. `Attack` is `0x12`. Used
  by: **Clips (3)** `MeleeContinuous @0.35`, `MeleeContinuous_EastFacing @0.35`, `NewClip @0.27`; Cutscenes (0);
  Profiles (0). Three open buttons, tooltip "Open", each carrying its `ClipAsset` in `userData`. Invoking
  `MeleeContinuous`'s button's own `clicked` delegate raised `OpenOwnerRequested(MeleeContinuous)`. Routing types
  loaded in any assembly: 0. Registry sha256s unchanged.
- **Not seen by eye:** the column's layout and its `.toolkit-box` styles, and `FocusClip` end to end in the real window.
