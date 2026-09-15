# Amendment A93 — Events tab: registry, payload, usage, routing table, consumer stubs

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.40.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2, first.
> **Predecessors:** A82 (catalog column, split view), A84 (usage index), A85 (payload schema),
> A87 (preview clip). Optional: A92 (Merge lands on the row menu if built).
> **Executor:** one orchestrator; `worker` subagents in **one wave of ten**, each ≤ 2 files; the
> orchestrator writes the shared types first and does the window wiring last.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A93 — Events tab** on the DOTS Animation Toolkit package (head
`0.39.0` or later; **A82, A84, A85 built**). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A93_EventsTab_Spec.md`. Read it, the roadmap §3 protocol,
then only what §3 here names. §2 decisions are settled — in particular: **the package ships no
event handler**; the routing table is data, the stub is a file written into the host's project.
T0 and T1 yours; one wave (T2–T11); one gate; T12–T15 yours. Stop at T16.

---

## 1. Goal

The event vocabulary is edited in a Project Settings page and a Quick Edit popup; its usage is
invisible; a host wiring events to sound, VFX, ragdoll or shader views writes the mapping asset
and the consuming system from scratch every time (the game's `AnimSoundEventMappingSO` +
`AnimEventSoundSystem` is the pattern). After this amendment one tab holds it all:

```
┌ Events ──────────────────────────────────────────────────────────────────────────────────────────────┐
│ ┌ Keys ──────────────┐ ┌ Footstep (16) ────────────────────────┐ ┌ Routes ───────────────────────┐ │
│ │ 🔍 search  [+][⟳]  │ │ Name  Footstep        Key 16  ■ mask  │ │ [+ route]                     │ │
│ │ ● Footstep      16 │ │ Description …                          │ │ ┌ Sound  ▸ id 0x2A  note … ┐ │ │
│ │ ● ApplyDamage   17 │ │ Payload  int: Foot [Left,Right]        │ │ └──────────────────────────┘ │ │
│ │ ○ Spawn         80 │ │          float: (unused)               │ │ ┌ Vfx    ▸ id 0x07          ┐ │ │
│ │   …                │ │ Preview clip  [footstep_01.wav]        │ │ └──────────────────────────┘ │ │
│ │ 64 maskable, 3 used│ │ Used by  3 clips · 1 cutscene · 0 prof │ │                               │ │
│ └────────────────────┘ │  ▸ Walk (2 markers)  ▸ Run  ▸ Intro    │ │ [Generate consumer stub…]     │ │
│                        └───────────────────────────────────────┘ └───────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Left: the registry as a catalog (● maskable, ○ pulse-only; the budget line). Middle: the entry's
fields (A85 payload, A87 preview clip) and A84's usage list, each row pinging its asset. Right: the
key's routes in a new `AnimEventRoutingAsset`, baked to a blob a host job reads; and a button that
writes a ready-to-edit `ISystem` file into the host's project.

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A93-D1 — `ClipEditorTab.Events`, placed after Cutscene Director.** Toggle `tab-events`, text
  "Events", cover pane `events-pane`.
- **A93-D2 — The Keys catalog is the registry, not assets.** `ToolkitCatalogColumn<TAsset>` is
  generic over `UnityEngine.Object`; registry entries are plain classes. So the Keys column is a new
  `EventKeyCatalogColumn` that **copies the column's row styling** (box, two lines, search, New,
  Refresh) but binds to `AnimEventKeyRegistry.entries`. If A82's column can take a non-Object
  `TItem` with a `Func<TItem,string> primaryName` cheaply, use it instead — T0 reads A82's result
  and decides; log the call.
- **A93-D3 — New mints the next free maskable key** via `IVocabularyRegistry`'s minting call, then
  `VocabularyRegistryProvider.Persist`. When all 64 maskable keys are used, New mints the next
  pulse-only key (≥ 80) and the budget line turns `Warning`.
- **A93-D4 — Routing is a project asset, not a registry field.** `AnimEventRoutingAsset :
  ScriptableObject` (Authoring/Assets) holds `List<AnimEventRoute>`; a project has one by default,
  auto-created under `Assets/Settings/DotsAnimationToolkit/` on first use (the owner's "I shouldn't
  have to manually assign any assets" rule — but it must live in `Assets/`, not `ProjectSettings/`,
  because it **is** baked). ⚠ Location.
- **A93-D5 — A route is `{ uint eventKey; AnimEventRouteKind kind; uint routeId; string note;
  UnityEngine.Object displayAsset }`** with `enum AnimEventRouteKind : byte { Sound, Vfx, Ragdoll,
  ShaderView, Custom }`. `routeId` is the host's own id (a `SoundType` cast, a prefab hash, a
  material view index); `displayAsset` is editor convenience and **not baked**. The package
  interprets none of it — `Ragdoll` is included because the host may prefer routing to
  `RagdollTrigger`'s built-in; the package does not act on it.
- **A93-D6 — Baked by `AnimEventRoutingBuilder` into `AnimEventRoutingBlob`** (sorted by key,
  `BlobArray<AnimEventRouteBlob>` + a per-key range table), attached as a singleton
  `AnimEventRouting : IComponentData { BlobAssetReference<AnimEventRoutingBlob> Value; }` by
  `AnimEventRoutingAuthoring` (a MonoBehaviour + Baker the host drops in its subscene). Read via
  `AnimEventRoutingApi.TryGetRoutes(in blob, uint key, out int start, out int count)` in
  `Runtime/Api/`.
- **A93-D7 — The stub generator writes host code, never package code.** `AnimEventConsumerStubBuilder`
  produces a `partial struct <Name>AnimEventSystem : ISystem` with an `IJobEntity` over
  `AnimEventOutput` gated by `AnimEventsPending`, a `switch` over `AnimEventRouteKind`, and a
  `// TODO` per case, into a folder the user picks (remembered in `EditorPrefs`). Uses `ConstantsGenerator`'s
  sanitising. It follows the roadmap's hard rules in the emitted code (no `var`, explicit types).
- **A93-D8 — Usage list is live from `AssetReferenceIndex.ReferencesToEventKey`**, grouped by
  `AssetReferenceKind`, each row a button that `EditorGUIUtility.PingObject`s the owner.
- **A93-D9 — Row context menu:** Rename (inline, `InlineRenameEditing`), Delete (confirm quoting
  usage; refuses when usage > 0 unless the user confirms a second time), Generate Constants (the
  registry's existing action), and — if A92 is built — Merge into…
- **A93-D10 — The tab does not join the shared selection** (nothing here is a clip set or rig).

---

## 3. Read first

- `Authoring/Assets/AnimEventKeyRegistry.cs` in full; `IVocabularyRegistry.cs` in full.
- `Editor/ClipUtilities/VocabularyRegistryProvider.cs` in full; `ConstantsGenerator.cs` lines 40–130.
- `Editor/ClipEditor/Shared/ToolkitCatalogColumn.cs` (A82) in full — D2's decision input.
- `Editor/ClipUtilities/AssetReferenceIndex.cs` (A84) — the `ReferencesToEventKey` surface.
- `Editor/ClipEditor/Components/EventPayloadFieldBuilder.cs` (A85) in full.
- `Authoring/Build/SocketRegistryBuilder.cs` in full — the smallest existing SO→blob builder to copy
  for D6; `Runtime/Api/ClipRegistryApi.cs` lines 1–60 — the `Api` idiom.
- `Editor/TexturePacker/TexturePackerPanel.cs` lines 1–80 — the most recent cover-pane panel's
  construction and `Bind` idiom; `TexturePackerSidebar.cs` for a segmented column.
- Game reference (read only, never referenced from the package — `Conformance_D`):
  `Assets/_Scripts/Systems/SoundSystemGroup/AnimEventSoundSystem.cs` — the consumer shape the stub
  reproduces generically.
- `Documentation~/animation-events.md` "Reading events from a system".

---

## 4. Design

### 4.1 Runtime + Authoring types (T1, orchestrator, before the wave)

`Runtime/Blobs/AnimEventRoutingBlob.cs`: `AnimEventRouteKind`, `AnimEventRouteBlob { uint
eventKey; AnimEventRouteKind kind; uint routeId; }`, `AnimEventRoutingBlob { BlobArray<AnimEventRouteBlob>
routes; BlobArray<int> keyStarts; BlobArray<uint> keys; }`.
`Runtime/Components/AnimEventRouting.cs`: the singleton component.
`Authoring/Assets/AnimEventRoutingAsset.cs`: the SO with `List<AnimEventRoute>` (D5).

### 4.2 `Runtime/Api/AnimEventRoutingApi.cs` (T2) — `TryGetRoutes` (binary search over `keys`),
`[BurstCompile]` static, `in` blob parameter (BC1064 rule).

### 4.3 `Authoring/Build/AnimEventRoutingBuilder.cs` (T3) — `Build(AnimEventRoutingAsset,
Allocator) → BlobAssetReference<AnimEventRoutingBlob>`; sorts by key; deterministic.

### 4.4 `Authoring/Baking/AnimEventRoutingAuthoring.cs` (T4) — MonoBehaviour with one
`AnimEventRoutingAsset` field + nested `Baker` calling the builder, `AddBlobAsset`, `AddComponent`.

### 4.5 `Editor/ClipUtilities/AnimEventRoutingAssetUtility.cs` (T5) — `GetOrCreateDefault()` (D4),
`AddRoute`, `RemoveRoute`, `Persist`.

### 4.6 `Editor/ClipUtilities/AnimEventConsumerStubBuilder.cs` (T6) — `BuildSource(string
systemName, IReadOnlyList<AnimEventRouteKind> kindsUsed) → string`; `WriteToFolder(...)`.

### 4.7 Editor columns (T7, T8, T9) — `Editor/Events/EventKeyCatalogColumn.cs`,
`EventKeyInspectorColumn.cs` (fields + usage), `EventRoutesColumn.cs` (routes list + stub button).

### 4.8 `Editor/Events/EventsPanel.cs` (T10) — three `CoverPaneSplitView`s (keys
`Events.Keys`, `Events.Inspector`), `Bind()`, subscribes `VocabularyRegistryProvider.RegistryChanged`
and `AssetReferenceIndex.Rebuilt`, `Dispose`.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Read A82's column and decide D2. Confirm
  `IVocabularyRegistry`'s minting method name. Record.
- [ ] **T1 — Shared types (orchestrator).** §4.1 by hand. Gate. Commit `A93-T1`.
- [ ] **T2 — Api + fixture [parallel-safe]** — Files: new `AnimEventRoutingApi.cs`, new
  `Tests/EditMode/AnimEventRoutingApiTests.cs` (build a blob by hand with `BlobBuilder`; keys
  16, 16, 20 → `TryGetRoutes(16)` gives start 0 count 2; `TryGetRoutes(17)` false; dispose via
  `BlobAssetReferenceScope`). Revert-to-fail: return the first index unconditionally.
- [ ] **T3 — Builder + determinism fixture [parallel-safe]** — Files: new
  `AnimEventRoutingBuilder.cs`, new `Tests/EditMode/AnimEventRoutingBuilderTests.cs` (two SOs with
  the same routes in different list order → identical `BlobSignature`; the `BlobSignature` helper
  exists under `Tests/EditMode/`). Revert-to-fail: drop the sort.
- [ ] **T4 — Authoring + Baker [parallel-safe]** — Files: new `AnimEventRoutingAuthoring.cs`.
  Read `Authoring/Baking/` for the nearest existing baker (grep `AddBlobAsset`). No `UnityEditor`.
- [ ] **T5 — Asset utility [parallel-safe]** — Files: new `AnimEventRoutingAssetUtility.cs`. Read
  `ActorProfileAssetUtility.cs`.
- [ ] **T6 — Stub builder + fixture [parallel-safe]** — Files: new `AnimEventConsumerStubBuilder.cs`,
  new `Tests/EditMode/AnimEventConsumerStubBuilderTests.cs` (`EmittedSource_HasNoVarAndOneCasePerKind`:
  output contains no `" var "` token and one `case AnimEventRouteKind.Sound:` when Sound is the
  only kind). Revert-to-fail: emit `var`.
- [ ] **T7 — Keys column [parallel-safe]** — Files: new `EventKeyCatalogColumn.cs`. D2, D3, D9.
- [ ] **T8 — Inspector column [parallel-safe]** — Files: new `EventKeyInspectorColumn.cs`. D8; A85
  fields via `EventPayloadFieldBuilder`; A87 preview clip field.
- [ ] **T9 — Routes column [parallel-safe]** — Files: new `EventRoutesColumn.cs`. D5 rows (kind
  dropdown, `routeId` hex field, note, display asset), stub button opening a folder picker.
- [ ] **T10 — Panel [parallel-safe]** — Files: new `EventsPanel.cs`, new
  `Tests/EditMode/EventsPanelTests.cs` (only if a non-trivial invariant exists — e.g. selecting a
  key updates the inspector column's bound entry; otherwise no fixture, say so).
- [ ] **T11 — Docs + changelog [parallel-safe]** — Files: new `Documentation~/events-tab.md`
  (the tab, the routing asset, the baker to drop in, the stub, and — one paragraph, no hedging —
  "the package never handles a route"), `CHANGELOG.md` `## [0.40.0]`. Also add the page to
  `index.md` — that is the orchestrator's (T12).
- **Gate the wave.** Compile; `AnimEventRoutingApiTests`, `AnimEventRoutingBuilderTests`,
  `AnimEventConsumerStubBuilderTests`. Commit `A93-T2..T11`.
- [ ] **T12 — Window wiring (orchestrator).** `ClipEditorTab.Events = 7`; UXML toggle + pane;
  `BindTab`; `ShowEventsTab`; `tabToggles` size; `ClipEditorLayoutTests` (the tab count assertion);
  `index.md`; `package.json`; `Conformance_G` allowlist (`AnimEventRoutingAssetUtility` is a
  `Utility` in `ClipUtilities/` — fine; `…StubBuilder` is a `Builder` — check the Builder rule's
  folder restriction, if any); `Conformance_A` if a new asmdef reference appeared (none expected).
  Gate; `ClipEditorLayoutTests`.
- [ ] **T13 — Drive.** Full suites. New key → appears with the next free number; give it a payload
  and a route; reload the registry and the routing asset from disk; add
  `AnimEventRoutingAuthoring` to `DOTSTestScene`'s subscene, enter Play, `execute_code` reads the
  singleton and `TryGetRoutes` for the key; generate a stub into `Assets/A93Scratch/`, confirm it
  compiles, delete it. Capture the tab.
- [ ] **T14 — Vault + HANDOFF.** Vault note "Events tab (A93)": D2's call, D4's location, the
  "never handles" rule restated. HANDOFF §4.
- [ ] **T15 — Close.** Roadmap checkbox.
- [ ] **T16 — ⏸ owner checkpoint.** Message: "Open the Events tab. Left: your keys with the
  64-key budget. Middle: fields and where each key is used — click a row to ping it. Right: routes;
  press Generate consumer stub and read the file it wrote. Two ⚠: the routing asset auto-creates
  under Assets/Settings/DotsAnimationToolkit/ — right place? And should the routes column exist at
  all, or is the stub alone enough?"

---

## 6. Deliberately out of scope

- Any handler in the package (sound, VFX, ragdoll-from-route, shader view).
- Multiple routing assets per project; per-actor overrides.
- A managed C# event bridge for MonoBehaviour consumers — a later sample if asked.

## 7. Build log

- **2026-09-14 — stage Phase 0 (parallel batch A93–A95, stage orchestrator).** Baseline at `bdd439b9`: compile clean; EditMode 840 (standing `Conformance_A` failure only), PlayMode 285. CHANGELOG top is `## [0.39.0]`. A88 T9 and A92 T10 were unanswered at batch start. Registry sha256: event keys `3bdb420d…d14701`, tags `dbec3d5f…d1eb4f`. T0 (no-Unity part), T1, the wave and the fixtures run under a `spec-lead` in its own worktree; window wiring, CHANGELOG, `package.json`, conformance pin, drive, vault, HANDOFF and close stay with the stage orchestrator.
- **2026-09-14 — T0 (spec-lead, worktree `spec/a93`, grep only).** Drifts, each settled here:
  1. **Minting call is `IVocabularyRegistry.CreateVocabularyEntry(string name)`**, not a `Mint*` name, and it already falls back to the lowest free pulse-only key above 79. D3 is that one call plus `VocabularyRegistryProvider.Persist`. The registry editor names new rows `"NewEvent"`; the column does the same.
  2. **D2 → new `EventKeyCatalogColumn`.** `ToolkitCatalogColumn<TAsset>` is constrained to `UnityEngine.Object` (`SelectedAsset`, `Select(TAsset)`, `AssetSelected`). Widening it would edit A82's shared column mid-batch while A94/A95 consume it, so the Events column copies its styling instead.
  3. **4.2's `in` blob parameter is `ref`,** matching `ClipRegistryApi`. Reading a `BlobArray` through an `in` reference makes defensive copies whose relative offsets point at garbage. `ref` still satisfies BC1064.
  4. **Blob layout:** `keyStarts` holds `keys.Length + 1` entries, ending with `routes.Length`, so count = `keyStarts[i+1] - keyStarts[i]`. Routes sort by `(eventKey, kind, routeId)`. Sorting by key alone would leave same-key order list-dependent and fail T3's determinism fixture.
  5. **T3:** `BlobSignature.Describe` only takes `ClipRegistryBlob`. The fixture compares the two routing blobs field by field inside its own file; the shared helper is not edited.
  6. **T2:** `BlobAssetReferenceScope` is `ClipRegistryBlob`-only. The fixture disposes in `try/finally`.
  7. **T5:** `ActorProfileAssetUtility` calls `AssetDatabase.SaveAssets`. The routing utility uses `SetDirty` + `SaveAssetIfDirty` instead, because `SaveAssets` flushes the owner's unsaved editor state (A84/A92 trap).
  8. **D4 surface grew:**
     - `FindDefault()` finds an existing `AnimEventRoutingAsset` anywhere by `FindAssets` and never creates one, so opening the tab writes nothing.
     - `GetOrCreateDefault()` runs only on the first `+ route`.
     - Added `RoutesForKey`, and `RoutingChanged` raised by `Persist`.
  9. **4.8 uses two `CoverPaneSplitView`s, not three:** outer `Events.Keys` (keys | rest), inner `Events.Inspector` (inspector | routes). Three columns need two dividers.
  10. **D9 "Generate Constants" is not a standalone action** in the registry editor. It is `VocabularyConstantsSection.RegenerateIfConfigured()`. The column builds a detached section with the editor's own arguments (`registry, registry, "AnimEvents", "Event", "Event", persist`).
  11. **T6 surface grew:** `SystemTypeName`, `CollectKindsUsed` and `FolderPrefsKey` added. `WriteToFolder(folder, systemName, kinds)` returns the written path and writes through `ConstantsGenerator.WriteGeneratedFile`.
  12. **`EventPayloadFieldBuilder` builds a marker's value field, not a schema editor.** The inspector edits the schema with plain fields and shows a marker preview built by `BuildIntField`/`BuildFloatField`.
  13. **T10 gets no fixture.** Selection-to-inspector is plain event forwarding, and `Bind()` reads the project registry, which fixtures may not touch.
  14. **Package `.meta` files are tracked.** The lead writes a meta for every new file and for `Editor/Events/`.
  15. **Batch contract:** T11 writes only `Documentation~/events-tab.md`; the CHANGELOG text lives in For integration.
- **T1 (spec-lead).** Wrote the three shared types (4.1, singleton field `Value` as `ClipRegistry`). Committed stubs for every cross-worker surface: `AnimEventRoutingBuilder`, `AnimEventRoutingAssetUtility`, `AnimEventConsumerStubBuilder`, and the three `Editor/Events/` columns.
- **Wave T2–T11 (spec-lead, ten parallel sonnet workers, one file pair each).**
  - Lead fix after review: T7's Generate Constants passed `null` for the constants row callbacks, which would regenerate `AnimEvents` without payload lines and value-name constants. It now passes the inspector's two callbacks (`AnimEventKeyRegistryEditor.DescribePayloadForConstants` made `internal`). New, Rename and Delete regenerate constants, as the inspector does.
  - Drift 16: the baker is the sibling class `AnimEventRoutingBaker` in the same file, the package idiom from `CutsceneStageAuthoring`, not a nested `Baker`.
  - Drift 17: the stub emits a `// Place after EventEmissionSystem` comment instead of an `[UpdateAfter]` attribute, because `EventEmissionSystem` sits in `AnimationToolkitLogicSystemGroup` and the host picks its own group.
  - Gate 1 (wave commit `b9e49dff`): compile-errors, `EventsPanel.cs` missing `using DotsAnimationToolkit.Authoring;` (CS0246 ×3). Fixed by a fresh worker.
  - Stage trap (message 2026-09-14): gate fixture names must be namespace-qualified (`DotsAnimationToolkit.Tests.EditMode.<Fixture>`). A bare name matches zero tests and still reports pass. The T1 gate was compile-only, so nothing needed re-gating.

### For integration

**CHANGELOG `## [0.40.0]` text**

```
## [0.40.0]
### Added
- Events tab in the Clip Editor (after Cutscene Director), three columns:
  - Keys: the project event registry as a searchable catalog with a 64-key maskable budget line (amber when full). New mints the next free maskable key, then pulse-only. Right-click for Rename, Delete (confirms quoting usage, asks twice when used), Generate Constants and Merge into….
  - Middle: the event's fields, payload schema with a marker preview, preview clip, and "Used by" rows that ping each clip, cutscene or profile.
  - Routes: the key's routes (kind, route id in hex, note, display asset), plus Generate consumer stub….
- `AnimEventRoutingAsset` (Authoring) and `AnimEventRoute`: project routing data, created at `Assets/Settings/DotsAnimationToolkit/AnimEventRouting.asset` on the first added route; opening the tab never creates it.
- `AnimEventRoutingAuthoring` + `AnimEventRoutingBaker` ("DOTS Animation Toolkit/Anim Event Routing"), baking through `AnimEventRoutingBuilder` into the `AnimEventRouting` singleton (`AnimEventRoutingBlob`: routes sorted by key, kind and route id, plus `keys` and `keyStarts`).
- `AnimEventRoutingApi.TryGetRoutes(ref AnimEventRoutingBlob, uint, out int, out int)`: Burst binary search.
- `AnimEventConsumerStubBuilder`: writes a host `partial struct <Name>AnimEventSystem : ISystem` with an IJobEntity over `AnimEventOutput` gated by `AnimEventsPending` and one `switch` case per used route kind.
- `AnimEventRoutingAssetUtility` (FindDefault, GetOrCreateDefault, RoutesForKey, AddRoute, RemoveRoute, Persist, RoutingChanged).
- Documentation: `events-tab.md`.
### Notes
- The package never handles a route. Routes are data; the stub is host code.
```

**`Conformance_G` allowlist: none needed.** Every new static class carries a role suffix: `AnimEventRoutingApi` (Runtime/Api), `AnimEventRoutingBuilder`, `AnimEventConsumerStubBuilder`, and `AnimEventRoutingAssetUtility` (in `Editor/ClipUtilities/`). The three columns and the panel are instance classes. There are no new asmdef references.

**Wiring, as the `TexturePacker` pattern**

- `ClipEditorTab.cs`: `Events = 7`.
- `ClipEditorWindow.uxml`: after the Cutscene Director toggle, `<uie:ToolbarToggle name="tab-events" text="Events" class="clip-editor__tab"/>`. Beside the other cover panes, `<ui:VisualElement name="events-pane" class="clip-editor__cover-pane clip-editor--hidden"/>`.
- `ClipEditorWindow*.cs`:
  - fields `private VisualElement eventsPane; private EventsPanel eventsPanel;`;
  - `eventsPane = rootVisualElement.Q<VisualElement>("events-pane");`;
  - `BindTab(ClipEditorTab.Events, "tab-events", "<tooltip>")`;
  - resize `tabToggles`;
  - `ShowEventsTab(bool isShown)`: when shown and `eventsPanel == null`, do `eventsPanel = new EventsPanel(); eventsPanel.Bind(); eventsPane.Add(eventsPanel);` then `eventsPane.EnableInClassList(HiddenUssClassName, !isShown)`;
  - in the window's OnDisable/teardown, `eventsPanel?.Dispose(); eventsPanel = null;`.
- `Bind()` is re-bindable (it unsubscribes first) and binds `VocabularyRegistryProvider.AnimEventKeys`. `Bind(AnimEventKeyRegistry)` exists for a detached drive. D10: no shared selection.
- `ClipEditorLayoutTests`: tab count + 1.
- `index.md`: link `events-tab.md`.
- Split prefs keys: `DotsAnimationToolkit.Split.Events.Keys` (default 260) and `DotsAnimationToolkit.Split.Events.Inspector` (default 420).

**Vault-note traps**

- **Blob parameters:** `TryGetRoutes` takes the blob by `ref`, never `in`. An `in` blob makes defensive `BlobArray` copies whose relative offsets read garbage.
- **Sort order:** routes sort by `(eventKey, kind, routeId)`. Sorting by key alone keeps same-key order list-dependent and breaks bake determinism.
- **Inspector edits:** `VocabularyRegistryProvider.Persist` raises `RegistryChanged` on every inspector edit. The fields are `isDelayed`, and the panel rebinds the inspector only when the selected entry object changes; rebinding the same entry rebuilds the fields under the cursor.
- **Routing asset:** `FindDefault` never creates, so opening the tab writes nothing. The asset appears on the first `+ route`. `Persist` uses `SaveAssetIfDirty`, never `SaveAssets`.
- **Generate Constants** is a detached `VocabularyConstantsSection(...).RegenerateIfConfigured()` with the registry inspector's arguments. There is no standalone registry action.
- **Merge into…** always acts on the project registry (A92).
- **Stub generator:** it refuses folders outside `Application.dataPath`, remembers the folder in `DotsAnimationToolkit.Events.StubFolder` and the name in `DotsAnimationToolkit.Events.StubName`, and writes through `ConstantsGenerator.WriteGeneratedFile` (deferred `AssetDatabase.Refresh`). The emitted system carries a `// Place after EventEmissionSystem` comment, not an ordering attribute.

**HANDOFF draft**

A93 (0.40.0) adds the Clip Editor's Events tab. Keys (left) is the project event registry as a catalog with the 64-key maskable budget, New, and a row menu: Rename, Delete, Generate Constants, Merge into…. The middle column edits the entry, its payload schema and preview clip, and lists every clip, cutscene and profile using the key, each row pinging its asset. Routes (right) edits the key's rows in the project `AnimEventRoutingAsset`, which is auto-created under `Assets/Settings/DotsAnimationToolkit/` on the first route. `AnimEventRoutingAuthoring` bakes that asset into the `AnimEventRouting` singleton, read by `AnimEventRoutingApi.TryGetRoutes`. Generate consumer stub… writes a host `ISystem` switching over route kinds. The package never handles a route. Owner checkpoint (T16) asks whether that asset location is right and whether the routes column should exist at all.
