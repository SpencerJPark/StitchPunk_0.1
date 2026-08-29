---
title: Verify — 2D Direction Sets Panel
status: active
created: 2026-08-29
area: code
---

## Goal

Confirm the reworked direction-set authoring tool: `DirectionSetSO` promoted into the toolkit as
`DirectionSetAsset`, the standalone `DirectionSetEditorWindow` replaced by a toggle pane inside the
Clip Editor, and `UnitSO` grown into the unit data layer. Spec:
[`DirectionSetsPanel_System.md`](DirectionSetsPanel_System.md). Built and compile-gated this
session; everything below marked `[ ]` needs the Editor and the owner's eyes, which this session
could not supply.

## What was stamped at build time

The spec left six DECISIONs open. All six went the recommended way:

- **CreateAssetMenu:** toolkit-only (`DOTS Animation Toolkit ▸ Direction Set Asset`). No game-side alias.
- **Health override:** the blob always wins. `UnitHealthInitSystem` stamps `Health` from
  `UnitDataBlob.maxHealth` at spawn-init. `HealthAuthoring`'s numbers were **kept, not zeroed** —
  zeroing would have spawned any scene-placed unit (one that never runs the spawn-init pass) on 0 HP.
  They are documented as a scene-placed fallback. A `maxHealth` of 0 is skipped for the same reason.
- **`UnitSO.rig`/`clipSet`:** validate-only. `UnitSO.DescribeRigMismatch()` is shared by `OnValidate`
  (reported once per domain load, not per keystroke) and `UnitLibraryBakingSystem`.
- **Directions target:** persisted on the asset as `DirectionSetAsset.targetDirections`, and
  `DirectionSetBakeUtil` now warns "authored below target" for a valid-but-unfinished set.
- **Per-direction grid view:** cut for v1. The slider replaces it.
- **Extra `Window/…` menu entry:** none. The pane is reached through the Clip Editor toggle or by
  double-clicking a `DirectionSetAsset`.
- **§3a's build-time check:** VAT Bake and New Rig do *not* auto-close each other — all three panes
  are independent absolute covers. 2D Direction Sets matches.

## Out-of-spec change, deliberate — read this one

**The runtime clip pick was folding once where it needed to fold twice.**
`UnitAnimationAssignmentSystem` and `AIUtils.GetAnimationByAction` quantized the facing at the
*actor's* `animationDirections` and then called `DirectionSetBlob.GetSlot` directly — so a set
covering fewer directions than the actor turns through returned an **empty `ClipId`** for every
facing it had not authored. `effectiveDirections` was being baked for exactly this fold and nothing
read it; the code comment at the call site already claimed the fold happened.

The spec's §3d pipeline and its §8 acceptance ("a Six target with only SE filled visibly degrades to
left/right mirroring, **exactly what runtime does**") are only true once it does. So
`DirectionSetBlob.ResolveSlot` was added and every pick routed through it, pinned by
`DirectionSetBlobFoldTests`. No content depends on the old behaviour — no `.asset` direction sets
exist yet — but this is a real runtime behaviour change outside §5's "the only runtime change is
health-from-blob", and the owner should know it happened.

## Steps

### Compile + tests (done this session)

- [x] Full recompile via `refresh_unity` — console clean, no `error CS####`. New types confirmed
  loaded in the live domain via `unity_reflect` (`DirectionSetAsset`, `DirectionSetsPanel`,
  `DirectionSetClipQueueView`, `IDirectionSetContextProvider`, `UnitDirectionSetContextProvider`).
- [x] EditMode, touched fixtures only — 29/29 green: `DirectionSetCoverageTests` (moved into the
  package, plus a new required-slots ↔ derived-coverage round trip), `ClipEditorLayoutTests` (two new
  cases: the `direction-sets-toggle` and `direction-sets-pane` names, and that the toggle clones as a
  `ToolbarToggle` rather than the `ToolbarButton` it used to be), `PackagingConformanceTests`,
  `DirectionSetBlobFoldTests` (new), `FacingSpaceMappingTests` (trimmed).
- [ ] Full toolkit suites at commit time — EditMode `DotsAnimationToolkit.Tests.EditMode`, PlayMode
  `DotsAnimationToolkit.Tests.PlayMode`. Check the discovered totals, not just pass/fail.

### Panel shell (owner, needs the Editor)

- [ ] Clip Editor toolbar shows **2D Direction Sets** as a toggle, immediately before VAT Bake. On
  covers the dock; off restores the editor exactly — playhead, selection and all three split
  positions untouched.
- [ ] VAT Bake and New Rig still toggle independently of it, and of each other.
- [ ] Double-clicking a `DirectionSetAsset` opens the Clip Editor with the pane up and that set
  loaded. (Create one via `Assets ▸ Create ▸ DOTS Animation Toolkit ▸ Direction Set Asset`, or the
  pane's own **New Set**.)
- [ ] `Window ▸ Stitch Punk ▸ Direction Set Editor` is **gone**, and nothing in the project links to
  it.

### Queue + coverage

- [ ] Assign a clip to SouthEast: the row reads `serves: SE + SW (mirror)` and the header reads
  `Coverage: Two`.
- [ ] Add NorthEast: coverage flips to `Four` live, with no reopen.
- [ ] An invalid pattern (North only) shows the same wording `DirectionSetBakeUtil` logs at bake —
  compare the two strings directly.
- [ ] Directions dropdown set to Six on an SE-only set: three empty placeholder rows (NE, S, N) and
  `Coverage: Two — missing: NE, S, N`. Filling them clears the gap readout.
- [ ] **+ Add Clip** adds a row for the next slot with no row yet (e.g. East on a Six target), and
  disables itself once all five are showing.
- [ ] Re-slotting a row via its dropdown moves the clip and clears the old slot; dropping it onto an
  occupied slot replaces it and logs the "…has been replaced" warning.
- [ ] Edit a slot in the Inspector while the pane is open, then Ctrl+Z — the queue and viewer track
  both, without reopening the set. (This is polled per tick rather than event-driven; if it lags,
  that is where to look.)

### Viewer

- [ ] Six-coverage set + rig: press Play, sweep the direction slider — the character turns through
  all six members, west-side angles visibly mirrored, and **the playhead is continuous across each
  swap with no hitch**. A hitch means the registry is rebuilding when it should not — check
  `RebuildRegistryIfClipsChanged`.
- [ ] South-only (One) set: the same sweep changes nothing; it plays head-on at every angle.
- [ ] Two clips of different lengths in one set play at their true speeds (this is the
  mismatched-foot-phase check the viewer exists for).
- [ ] Camera is fixed front-on — no orbit, no zoom, and the framing survives a rig change.
- [ ] Queue a clip authored against a *different* rig: that row warns inline naming the clip, and
  every other row keeps previewing. The viewport must not go dead.

### Unit context

- [ ] The **Unit Context** dropdown is populated (it hides entirely if
  `UnitDirectionSetContextProvider` failed to register — the `[InitializeOnLoad]` trap).
- [ ] Picking `<Unit> · Moving` loads the set, the rig and the actor's turn granularity in one
  click, and the resulting turn matches what Play mode shows for that unit walking.
- [ ] A unit whose mapping has no set listed shows as `… (unassigned)` and says so rather than
  leaving the previous set on screen.
- [ ] Units with no prefab / no `ActorAuthoring` produce **one** consolidated console warning, not
  one per unit.

### Unit data layer (needs a rebake + Play)

- [ ] Change `UnitSO.maxHealth` → reopen the subscene or re-enter Play → a spawned unit's `Health`
  matches the blob value, not the prefab's old `HealthAuthoring` number.
- [ ] A restored/loaded minion still comes back with its **saved** health, not the blob's —
  `UnitHealthInitSystem` runs before `MinionRestoreApplySystem` precisely for this.
- [ ] Set `UnitSO.rig` to something the prefab's `ActorAuthoring` disagrees with: a warning naming
  the unit appears at bake, and once in the console on the inspector edit.

## Files

**New (toolkit):** `Authoring/Assets/DirectionSetAsset.cs` ·
`Editor/ClipEditor/DirectionSets/DirectionSetsPanel.cs` · `DirectionSetClipQueueView.cs` ·
`DirectionSetContext.cs` · `DirectionSetAssetOpener.cs` · `Tests/EditMode/DirectionSetCoverageTests.cs`
**Edited (toolkit):** `ClipEditorWindow.uxml` / `.uss` / `.cs` · `Tests/EditMode/ClipEditorLayoutTests.cs` ·
`Docs/AnimationToolkit/HANDOFF.md`
**New (game):** `Editor/DirectionSetContext/UnitDirectionSetContextProvider.cs` ·
`Systems/SpawnInitSystemGroup/UnitHealthInitSystem.cs` · `Tests/DirectionSetBlobFoldTests.cs`
**Edited (game):** `Data/SOs/UnitSO.cs` · `Data/Structs/UnitBlob.cs` · `Data/Structs/DirectionSetBlob.cs` ·
`Utils/DirectionSetBakeUtil.cs` · `Utils/AIUtils.cs` ·
`Systems/AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitAnimationAssignmentSystem.cs` ·
`Systems/PostBakingSystemGroup/UnitLibraryBakingSystem.cs` · `Authoring/Units/HealthAuthoring.cs` ·
`Tests/FacingSpaceTests.cs`
**Deleted (game):** `Data/SOs/DirectionSetSO.cs` · `Editor/DirectionSetEditor/` (whole folder)

## Follow-ups deliberately not built

- Write-back from the Unit Context dropdown ("pick a set to wire it here") — v1 reads only.
- A side-by-side per-direction grid view, if the slider turns out to be missed during the art pass.
- Design/palette variants and preview scenery selection in the viewer — both stay whatever the Clip
  Editor's preview substrate defaults to.
- Pushing `UnitSO.rig`/`clipSet` onto the spawned entity. That demotes the prefab and is its own
  spec.
