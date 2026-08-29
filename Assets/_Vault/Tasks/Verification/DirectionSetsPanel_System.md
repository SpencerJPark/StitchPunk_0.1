# 2D Direction Sets Panel — editor rework + unit data layer

> **Status:** 🔨 **BUILT 2026-08-29** — all six phases. Every remaining `← DECISION` below went the recommended way; they are stamped in [`verify-directionsetspanel.md`](verify-directionsetspanel.md), which also records one deliberate out-of-spec runtime change (the per-set clip fold). Awaiting the owner's play-test.
> **Supersedes:** `DirectionFacing_System.md` §6a's *tool form* decision only ("standalone `EditorWindow`, launched from the Clip Editor"). The runtime facing model, the five-slot mirror-closed data shape, and derived coverage are all **kept** — this plan reworks the *tool* and moves the asset type, it does not reopen the facing architecture.
> **Depends on:** DirectionFacing phases 1–4 (built 2026-08-29). Phase 5 of that plan (owner art proof) should run **through this panel** once built.

---

**Skills Needed:**
- `dots-test` — toolkit fixture for the moved fill-pattern derivation; EditMode test for the `UnitBlob` health field (§5)
- `dots-authoring-baker` — `UnitSO`/`HealthAuthoring` changes (§5)
- `dots-blob-library` — `UnitBlob` extension conventions (existing library edit, not a new library)

---

## 1. Context — what the owner asked for, mapped to what exists

Four asks, stamped 2026-08-29:

1. **Rename** the tool "2D Direction Sets".
2. **In-window, not a popup:** the tool becomes a pane inside the toolkit's `ClipEditorWindow`, driven by a `ToolbarToggle` exactly like VAT Bake (`vat-bake-toggle` → `ShowVatBakeTab`). The standalone `DirectionSetEditorWindow` (`Assets/_Scripts/Editor/DirectionSetEditor/`) and the `OnDirectionSetsButtonClicked` static-event bridge are **deleted**.
3. **One viewer + queue, not six panes:** a single preview viewport playing the selected logical animation, a **direction slider** that rotates the previewed facing through the resolver (six authored+mirrored members → the character visibly turns; one member → the clip plays regardless of slider), and a **clip queue** — a list where clips are added and their direction(s) assigned, which is the authoring surface for the set.
4. **Unit data layer:** one place to author a unit's rig, clip set, direction sets, max health, attack damage, etc.

**Stamped decisions (owner Q&A 2026-08-29 — do not reopen):**
- [x] **`DirectionSetSO` is promoted into the toolkit** as `DirectionSetAsset` (`Packages/com.dotsanimationtoolkit/Authoring/Assets/`). The panel is then fully toolkit-native and direction sets become a sellable toolkit feature — `Direction`, `AnimationDirections`, `FacingResolver`, `PartFacing` already live there, and the asset only ever referenced toolkit types. No `.asset` files exist yet, so this is a code-only re-point.
- [x] **The "enum" is the game's existing `ActionType`/stance mappings**, surfaced through a host-context seam (§4) — no new `Animations` enum, no new enum→set library. Picking "Zombie · Moving" or "Zombie · MeleeSingle" previews exactly what `UnitAnimationAssignmentSystem` will resolve.
- [x] **The queue is UI over the existing five east-side slots.** Data shape unchanged: five slots, coverage derived by `TryGetEffectiveDirections`, west always a free mirror. No arbitrary (clip, direction-flags) list — that would reopen the stamped mirror-closure decision.
- [x] **Unit data consolidates onto the existing `UnitSO`** (bakes into `UnitLibrary` by `UnitType` — the enum→blob pointer pattern). No new `UnitDefinitionSO`.

## 2. Data model

### 2a. `DirectionSetAsset` (moved, not redesigned)

- `Assets/_Scripts/Data/SOs/DirectionSetSO.cs` → `Packages/com.dotsanimationtoolkit/Authoring/Assets/DirectionSetAsset.cs`, namespace `DotsAnimationToolkit.Authoring`, beside `ClipSetAsset`. Fields, tooltips, `GetSlot`, and `TryGetEffectiveDirections` move verbatim (including the shared-derivation comment — `DirectionSetBakeUtil` still calls it).
- `CreateAssetMenu` moves to the toolkit's menu path (match whatever `ClipSetAsset` uses). ← DECISION: keep a game-side `Units/Direction Set` menu alias, or toolkit menu only? (recommend: toolkit only — one create path)
- Game-side re-points (type name only, logic untouched): `UnitSO` (all `DirectionSetSO` fields + `ActionAnimationMapping`/`StanceAnimationMapping`), `DirectionSetBakeUtil`, `UnitLibraryBakingSystem`, `DirectionSetBlob` stays game-side (it stores game `ClipId`s into `UnitBlob`).
- The fill-pattern → effective-count fixture moves from the game's `FacingSpaceTests` into the **toolkit's own test suite** — the logic now ships in the package, so the package pins it (sellability). The game keeps only the world→facing-space mapping tests.

### 2b. `UnitSO` extension (the unit data layer)

- `int maxHealth` (new, `[Header("Combat")]`) → baked into `UnitBlob`. At spawn-init (existing LateSim `SpawnInit` pass that already stamps per-unit blob data onto new units), write `Health { healthAmount = max, healthAmountMax = max }`.
  ← DECISION: does a non-zero per-prefab `HealthAuthoring` value override the blob, or does the blob always win and `HealthAuthoring`'s serialized values retire? (recommend: blob always wins; `HealthAuthoring` keeps only the component-add so pre-placed units still get the component, values zeroed)
- **Attack damage stays on `AttackSO`** (keyed by `DamageSource`, mapped via `UnitSO.attacks`) — no duplication. The unit inspector/panel shows the resolved damage read-only.
- `RigAsset rig` + `ClipSetAsset clipSet` (new, `[Header("Animations")]`) — **validate-only in v1**: an editor validation (OnValidate + a bake warning) checks they match what the prefab's `ActorAuthoring` actually carries, so a mismatched unit surfaces in the editor instead of failing silently at runtime. The prefab remains the runtime source of truth.
  ← DECISION: confirm validate-only, or should baking push `UnitSO.rig`/`clipSet` onto the spawned entity, demoting the prefab's copy? (recommend: validate-only v1 — pushing is a bigger spawn-pipeline change with its own spec)

## 3. The panel — 2D Direction Sets

Lives in `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/DirectionSets/` (new folder). All UI Toolkit, following `VatBakePanel`'s conventions.

### 3a. Shell + toggle

- `ClipEditorWindow.uxml`: the `direction-sets-button` `ToolbarButton` becomes `ToolbarToggle name="direction-sets-toggle" text="2D Direction Sets"`, same slot (immediately before VAT Bake).
- `Show2DDirectionSetsTab(bool)` mirrors `ShowVatBakeTab`: pane covers the dock (the `TwoPaneSplitView` zero-layout trap in the USS comment applies here too), panel built lazily on first show, nothing torn down on hide. Mutually exclusive with the VAT pane the same way VAT and New Rig already coexist. ← DECISION at build time: check whether VAT/New Rig toggles auto-close each other and match that behavior.
- `OnDirectionSetsButtonClicked` static event, its `[InitializeOnLoad]` subscriber, and the whole `Assets/_Scripts/Editor/DirectionSetEditor/` folder are deleted.
- Double-clicking a `DirectionSetAsset` opens the Clip Editor with the toggle on and the set loaded — mirror `FocusWithVatBakeTab` (drive the toggle, never the pane directly). Menu item: none of its own; the pane is reached through the Clip Editor. ← DECISION: also keep a `Window/…/2D Direction Sets` menu shortcut? (recommend: no — one entry path, less to document)

### 3b. Layout

```
┌ toolbar: [Direction Set (ObjectField)] [Unit Context ▾] [New Set]        ┐
├──────────────────────────────┬────────────────────────────────────────────┤
│  Directions: [Six ▾] (1/2/4/6/8)   Coverage: Four — missing: S, N         │
│  Clip Queue                  │   Viewer (single ClipPreviewController)    │
│  ┌ row: [ClipAsset] [Slot ▾] │                                            │
│  │      serves: SE + SW(mirror) [Open in Clip Editor] [×]                 │
│  ├ row: …                    │   (mirrorX rendered as Image scale(-1,1))  │
│  └ [+ Add Clip]              │                                            │
├──────────────────────────────┴────────────────────────────────────────────┤
│  Direction:  ◄──────●──────►  0–360°   → "NorthEast (mirrored → NW)"      │
│  [▶/❚❚]  scrub ────────────────────────  time                             │
└───────────────────────────────────────────────────────────────────────────┘
```

### 3b-i. Directions dropdown (owner-requested, added 2026-08-29)

An `AnimationDirections` dropdown (One / Two / Four / Six / Eight) above the queue — the set's **target directions**. It cannot *declare* effective coverage (stamped: coverage is derived from filled slots, never declared — the bake keeps using `TryGetEffectiveDirections`), so it does three things instead:

- **Scaffolds the queue:** picking Six shows the four required slots (SE, NE, S, N) as rows immediately — filled ones with their clip, unfilled ones as empty placeholder rows — so "what do I still need to author" is visible at a glance. "+ Add Clip" fills the next empty required slot first.
- **Gap readout:** while fill ≠ target, the coverage label reads e.g. `Coverage: Four — missing: S, N`; when fill matches, plain `Coverage: Six`. Invalid patterns keep the shared bake-warning text.
- **Drives the slider quantize:** the direction slider quantizes at the target count (overridden by an active unit context's `animationDirections`), so you can preview how a partially-filled set folds — a Six target with only SE filled visibly degrades to left/right mirroring, exactly what runtime does.

Opening a set initializes the dropdown to its derived coverage. ← DECISION: keep the target panel-local (resets per session), or persist it on `DirectionSetAsset` as an editor-only `targetDirections` field so the *bake* can also warn "authored below target"? (recommend: persist on the asset — it's authoring intent, not runtime data; the bake warning is the whole value)

### 3c. Clip queue (authoring surface over the five slots)

- One row per **authored** clip: `ClipAsset` field + a dropdown of the five east-side slots (SouthEast / NorthEast / South / North / East) + a read-only "serves" readout listing the slot **and its free mirror** (SE → "SE + SW (mirror)"; South → "S only") + per-row "Open in Clip Editor" + remove.
- "+ Add Clip" adds a row targeting the next unfilled slot in promotion order (SE → NE → S → N → E); the dropdown can re-slot it. Two rows on the same slot: last write wins with an inline warning.
- The coverage label re-derives live via `TryGetEffectiveDirections` and shows the identical warning text `DirectionSetBakeUtil` will log for invalid patterns — the panel and the bake can never disagree because they share the method.
- All slot writes go through `Undo.RecordObject` + `SetDirty`, as the old window already did.

### 3d. Viewer + direction slider

- **One** `ClipPreviewController` (not one per direction). Rig comes from the unit context (§4) when set, else a manual `RigAsset` field with the existing "assign a rig to pose" hint.
- **Direction slider:** 0–360°, with a label showing the quantized `Direction` and whether the shown clip is a mirror. Pipeline per change — exactly the runtime path:
  1. angle → facing-space vector `(cos, sin)`;
  2. `FacingResolver.FromMovement(vector, actorDirections, currentFacing)` where `actorDirections` = the unit context's `animationDirections` when a context is active, else the **Directions dropdown** (§3b-i);
  3. snap into the set's coverage + `ToAuthoredSide` → east-side slot + `mirrorX`;
  4. bind that slot's clip to the controller; `mirrorX` renders as `Image` `scale(-1,1)` (same trick as the old window — identical to runtime `PartFacing.mirrorX`, no second pipeline).
- A Six set therefore visibly turns through all six members as the slider sweeps; a South-only (One) set plays head-on at every angle — both owner-stated acceptance cases.
- **Playback:** play/pause toggle looping the clip (tick on `EditorApplication.update`, advance normalized time by `deltaTime / clipDuration` so differing clip lengths play at true speed — sliding direction mid-play is the mismatched-foot-phase check), plus the scrub slider for frame-stepping. Facing changes mid-play keep the playhead (normalized), mirroring how runtime swap-on-facing-change behaves.
- The old per-direction pane grid is **not** kept in v1 — the slider replaces it. ← DECISION: if side-by-side comparison is missed during the art pass, a "grid" view toggle can return as a follow-up; confirm cut for v1.

### 3e. Viewer mechanics (gap round 2, stamped 2026-08-29)

- **One registry, all clips.** The registry rebuild (`SetClipSet` → `ClipRegistryBuilder.Build`) is the expensive step, so the viewer builds **one** synthetic `ClipSetAsset` containing *all* authored clips of the open direction set. A direction change is then just a different `clipId` into `SamplePose` — no rebuild, no hitch mid-turn. (The old window's one-controller-one-clip-per-pane pattern does not carry over.) Rebuild only when the queue's clip membership or the rig changes.
- **Fixed front-on camera** — stamped (owner Q&A): `BillboardPreviewEnabled = true`, `FrameRig()` once on load/rig change, no orbit/zoom input. Direction comes from the slider, not the camera — the view matches what the game camera sees.
- **Playback controls: play/pause + scrub only** — stamped (owner Q&A): looping play/pause at true clip speed (`ClipAsset.duration` seconds) plus the scrub slider. No speed dropdown, no loop toggle in v1.
- **External-edit refresh:** `Undo.undoRedoPerformed` + asset-modification on the open set refresh the queue and (when membership changed) the registry — inspector edits and undo can't leave the panel showing stale slots.
- **Mismatched-rig clip:** a queued clip that fails registry validation against the preview rig gets a per-row inline warning naming the clip; the other clips keep previewing. The whole viewport never goes silently dead over one bad row.
- **New Set:** save-file dialog → creates the asset → loads it with the Directions dropdown at Six (roster default), empty required-slot rows scaffolded.
- **Unit-context robustness:** the provider skips units with no prefab / no `ActorAuthoring` (one consolidated console warning), and lists mappings whose set is null as disabled "unassigned" entries. Write-back ("pick a set to wire it here") is a follow-up, not v1.
- **Out of scope for the viewer:** design/palette variants (the rig's source prefab renders with its authored default look) and preview scenery selection — both stay whatever the Clip Editor's preview substrate defaults to.

## 4. Host unit-context seam (toolkit ↔ game, no dependency)

The toolkit cannot know `UnitSO`/`ActionType`, so the "set the enum, see the character run" flow crosses via a provider interface — the same one-way seam philosophy as the deleted static event, but data-shaped:

- **Toolkit:** `public interface IDirectionSetContextProvider { IReadOnlyList<DirectionSetContextEntry> GetEntries(); }` with `DirectionSetContextEntry { string label; DirectionSetAsset set; RigAsset previewRig; AnimationDirections actorDirections; }`, registered via `DirectionSetsPanel.SetContextProvider(...)` (static, null = the Unit Context dropdown hides — the panel works standalone in a buyer's project).
- **Game:** `Assets/_Scripts/Editor/DirectionSetContext/UnitDirectionSetContextProvider.cs` (`[InitializeOnLoad]` registration): enumerates `UnitSO` assets and flattens their mappings into labels — `"<Unit> · Idle"`, `"<Unit> · Moving"`, `"<Unit> · <Stance> Idle/Moving"`, `"<Unit> · <ActionType>"` — each carrying the mapped set, the rig resolved from the unit's prefab (`ActorAuthoring`), and the unit's `animationDirections`.
- Picking an entry loads set + rig + actor direction count in one click; the set field still works bare for sets not yet wired to any unit.
- `PackagingConformanceTests` must stay green: the toolkit gains the interface, the game implements it — dependency points the allowed direction only.

## 5. Systems (runtime, small)

- **No new system group.** The only runtime change is health-from-blob: extend whichever existing `SpawnInit`-group system already stamps per-unit blob data at spawn to also write `Health` from `UnitBlob.maxHealth` (per the §2b DECISION). If none fits cleanly, a minimal `UnitHealthInitSystem` in the existing SpawnInit group — `dots-system-scaffold`, group-level gating only.
- `UnitLibraryBakingSystem` bakes the new `maxHealth` field and emits the rig/clip-set mismatch warning (§2b).

## 6. File manifest

**New (toolkit):** `Authoring/Assets/DirectionSetAsset.cs` (moved) · `Editor/ClipEditor/DirectionSets/DirectionSetsPanel.cs` · `DirectionSetClipQueueView.cs` · `DirectionSetContext.cs` (interface + entry struct) · toolkit test fixture for fill-pattern derivation
**Edited (toolkit):** `ClipEditorWindow.uxml`/`.uss` (button → toggle, pane element) · `ClipEditorWindow.cs` (toggle wiring, `Show2DDirectionSetsTab`, `FocusDirectionSetsTab`, delete `OnDirectionSetsButtonClicked`) · `Docs/AnimationToolkit/HANDOFF.md`
**New (game):** `Editor/DirectionSetContext/UnitDirectionSetContextProvider.cs`
**Edited (game):** `Data/SOs/UnitSO.cs` (+`maxHealth`, +`rig`, +`clipSet`; `DirectionSetSO`→`DirectionSetAsset` re-types) · `Data/Structs/UnitBlob.cs` · `Utils/DirectionSetBakeUtil.cs` · `Systems/PostBakingSystemGroup/UnitLibraryBakingSystem.cs` · `Authoring/Units/HealthAuthoring.cs` · the SpawnInit-group health stamp (§5) · `Tests/FacingSpaceTests.cs` (fill-pattern fixture moves out)
**Deleted (game):** `Data/SOs/DirectionSetSO.cs` · `Editor/DirectionSetEditor/` (whole folder)
**Docs:** `_Vault/Memories/Code/Editor.md`, `Data.md`, `Systems_Animation.md` updates; `DirectionFacing_System.md` §6a forward-pointer.

## 7. Build phases

1. **Asset promotion.** Move `DirectionSetSO` → toolkit `DirectionSetAsset`; re-point every game reference; move the fill-pattern fixture into the toolkit suite. Compile gate + touched fixtures. Game behavior identical.
2. **Panel shell.** Toggle replaces button; pane + `Show2DDirectionSetsTab`; standalone window + static event deleted; double-click-asset focuses the tab. The pane can temporarily host the old six-pane content to keep the step small — or go straight to 3 if the diff stays reviewable.
3. **Queue + viewer.** Clip queue over the five slots, single viewport, direction slider pipeline (§3d), play/pause + scrub, live coverage/warning readout, per-row Open in Clip Editor.
4. **Unit context seam.** Toolkit provider interface + dropdown; game-side `UnitDirectionSetContextProvider`; picking an entry loads set/rig/count.
5. **Unit data layer.** `UnitSO.maxHealth` → blob → spawn-init stamp; `rig`/`clipSet` validate-only fields + bake warning; `HealthAuthoring` per-prefab values retired per the DECISION. EditMode test for the blob field; compile + rebake.
6. **Docs + retire.** HANDOFF.md, vault memories, `PackagingConformanceTests` green, README links, retire per the verification section.

## 8. Verification (→ `verify-directionsetspanel.md` at retire time)

- Clip Editor toolbar shows **2D Direction Sets** as a toggle; on toggles the pane over the dock, off restores the editor exactly (dock geometry untouched); VAT Bake still toggles independently.
- Double-clicking a `DirectionSetAsset` opens the Clip Editor with the pane up and that set loaded.
- Queue: adding a clip to SouthEast shows "SE + SW (mirror)" and Coverage: Two; adding NorthEast flips coverage to Four live; an invalid pattern (e.g. North only) shows the same warning text the bake logs.
- Directions dropdown: picking Six on an SE-only set shows three empty placeholder rows (NE, S, N) and `Coverage: Two — missing: NE, S, N`; the slider still sweeps six stops but the preview only mirrors left/right; filling the slots clears the gap readout and the sweep turns through all six members.
- Viewer with a Six-coverage set + rig: press play, sweep the direction slider — the character turns through all six members, west-side angles visibly mirrored, playhead continuous across the swap **with no hitch** (single registry, §3e). With a South-only set the same sweep changes nothing.
- Edit a slot in the Inspector while the panel is open, then undo — the queue and viewer track both changes without reopening the set.
- Queue a clip from a different rig — that row warns inline naming the clip; the other rows keep previewing.
- Unit Context: pick "<Unit> · Moving" — set, rig, and turn granularity load in one click and match what Play mode shows for that unit walking.
- `UnitSO.maxHealth` change → rebake → spawned unit's `Health` matches the blob value, not the old prefab number; a `UnitSO.rig` that disagrees with the prefab's `ActorAuthoring` warns at bake naming the unit.
- Toolkit `PackagingConformanceTests` green; toolkit fill-pattern fixture green in the package suite.

## Open decisions (collected)

- [x] Package home: **promote into toolkit as `DirectionSetAsset`** — stamped 2026-08-29.
- [x] Enum picker: **existing `ActionType`/stance mappings via host-context seam** — stamped 2026-08-29.
- [x] Queue: **UI over the five east-side slots; data shape unchanged** — stamped 2026-08-29.
- [x] Unit data layer: **extend `UnitSO`** — stamped 2026-08-29.
- [x] Viewer camera: **fixed front-on, billboard mode, no orbit/zoom** — stamped 2026-08-29 (gap round 2).
- [x] Playback controls: **play/pause + scrub only** (no speed/loop toggles in v1) — stamped 2026-08-29 (gap round 2).
- [ ] CreateAssetMenu path: toolkit-only, or keep a game-side alias? (recommend toolkit-only)
- [ ] Health override: blob always wins vs. non-zero `HealthAuthoring` overrides? (recommend blob always wins)
- [ ] `UnitSO.rig`/`clipSet`: validate-only v1 confirmed? (recommend yes)
- [ ] Directions-dropdown target: persist as editor-only `targetDirections` on `DirectionSetAsset` (bake warns "authored below target"), or panel-local only? (recommend persist)
- [ ] Per-direction grid view cut for v1? (recommend yes — slider replaces it)
- [ ] Extra `Window/…` menu entry for the pane? (recommend no)
