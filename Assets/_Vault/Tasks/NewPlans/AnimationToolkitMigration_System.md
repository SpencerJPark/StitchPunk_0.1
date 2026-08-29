# Animation Toolkit Game Migration — Design Spec

> **Status:** 📝 spec drafted — decisions open, awaiting owner edit
> **Raw source:** [`../Claude/Systems_Gap_Audit_2026-08.md`](../Claude/Systems_Gap_Audit_2026-08.md) area 1
> **Prerequisites:** the toolkit's own queue remainder (`Docs/AnimationToolkit/HANDOFF.md` §4 — Clip Editor run, Samples~ compile check, visual passes) and ideally the verification-baseline play session (audit area 7), since three open checklists cover exactly the systems this plan deletes.

---

**Skills Needed:**
- `dots-system-scaffold` — the rewritten assignment system + the AnimEvent sound consumer (§6)
- `dots-authoring-baker` — `UnitSO`/`UnitLibraryBakingSystem` re-key, `CharacterRigAuthoring` slimming
- `dots-test` — characterize the command-seam translation before deleting the legacy path

---

## 1. Purpose & scope

Replace the game's legacy keyframe animation stack (9 systems in `Systems/AnimationSystemGroup/`) with the `com.dotsanimationtoolkit` runtime. Verified starting facts (2026-08-29):

- **Zero references to `DotsAnimationToolkit` exist in `Assets/_Scripts/`** — no asmdef ref, no `using`. This is a from-scratch integration.
- The game's `SetAnimation` buffer + `AnimationRequest` enableable is **structurally identical** to the toolkit's `AnimationCommand` buffer + `AnimationCommandPending` gate — the call-site rewrite is a shape-preserving swap, not a redesign.
- Only **12 files** touch the animation API; 2 of them are dead (`UnitFaceDirectionSystem.cs` is a fully commented-out corpse; `AnimationComponents.cs` carries retired comments).
- **~21 real legacy clips** exist (4 action, 2 base, 12 blinks, 1 mouth, 1 direction) — small enough to re-author by hand in the Clip Editor.
- Spencer has already begun toolkit authoring in-project: `NewRig.asset`, `NewClipSet.asset`, `NewClip*.asset` under `Assets/ScriptableObjects/Animations/`.
- The toolkit preserved the **`_ImageIndex`** shader property name verbatim for host compatibility (`SpriteSliceProperty`), and the game's three tint properties (`_BaseColor`/`_SecondaryColor`/`_TertiaryColor`) are game-owned components the toolkit never touches — the shader seam is already compatible by design.

**This is a migration, not a bridge.** End state deletes the legacy stack, `AnimationLibrarySO`/`AnimationClipSO`, the `AnimationType` enum, `Utils/AnimationUtils.cs`, and the custom `Editor/AnimationEditor/`. ← DECISION: confirm cut-over over a permanent bridge. *Recommendation: cut-over — a bridge doubles the maintenance surface for zero shipped value, and the call-site count (12 files) is small.*

**Out of scope:** event-driven AI timing (hitTime removal, `WaitForAnimEvent` commands) — that is [`AnimationEventTiming_System.md`](AnimationEventTiming_System.md), which builds on this plan's seam. The one event consumer built *here* is sound (§6), because `AnimationSoundMarkerSystem` dies with the legacy stack and cannot wait.

## 2. Architecture — where the toolkit slots into the frame

The toolkit runs as `AnimationToolkitSystemGroup` inside `SimulationSystemGroup` and deliberately declares **no ordering edges and no scene gating** — the host orders its own groups against it (exactly the movement-toolkit pattern already in `SystemGroups.cs`).

```
GameManager → Player → UtilityAI → MinionActionSelection → StateMachine → Item
  → Movement (package) → Buildings → Combat → Health → Design
  → AnimationSystemGroup (game, shrinks to assignment — issues AnimationCommands)
  → AnimationToolkitSystemGroup (package: binding → logic/events → presentation)
LateSimulation: Spawn → SpawnInit → Ragdoll (game Ragdoll2D, unchanged §5) → Sound → Despawn → Save
```

Ordering edges to add in `SystemGroups.cs` (on **game** types, per the toolkit's contract):
- `AnimationSystemGroup` gains `[UpdateBefore(typeof(AnimationToolkitSystemGroup))]` — commands issued this frame apply this frame.
- `DesignSystemGroup` already precedes `AnimationSystemGroup` — its ordering intent ("before image index push") transfers unchanged.
- `Ragdoll2DSystem` (LateSimulation) already runs after all of `SimulationSystemGroup`, so the "apply-pose stomps, ragdoll re-writes after" contract survives with **no change** — `TransformApplySystem` simply replaces `ApplyAnimatedPoseSystem` as the stomper.
- Scene gating: the toolkit self-gates on its queries (empty world costs nothing); do **not** wrap it in `GameSceneSystemGroup`. Use `ToolkitWorldControl.SetEnabled` if a scene ever needs it hard-off.

### System disposition table

| Legacy system | Fate |
|---|---|
| `AnimationTimeSystem`, `AnimationSamplingSystem`, `ApplyAnimatedPoseSystem` | **Delete** — `PlaybackTimeSystem`/`TransformSampleSystem`/`TransformApplySystem` |
| `UpdateImageIndexSystem` | **Delete** — `SpriteMaterialSystem` writes `_ImageIndex` |
| `BillboardSystem` | **Delete** — `BillboardResolveSystem` (§7 decision on mode) |
| `AnimationSoundMarkerSystem` | **Delete** — replaced by the AnimEvent sound consumer (§6) |
| `AnimationRequestSystem` | **Delete** — `CommandApplySystem` drains `AnimationCommand` directly |
| `UnitFaceDirectionSystem` | **Delete now** (already a fully commented corpse — RULES.md says corpses never stay in `Systems/`) |
| `UnitAnimationAssignmentSystem` | **Rewrite in place** — same job shape, issues `AnimationCommandUtil.Play` with `ClipId`s (§4) |
| `CameraVisibilitySystem` (GameManager) | **Keep + extend** — also flips the toolkit's `AnimVisible` (§7) |
| `Ragdoll2DSystem` + init/revive/spawn-init | **Keep unchanged** (§5) |
| `AnimationLibraryBakingSystem` (PostBaking) | **Delete** with `AnimationLibrarySO`/`AnimationClipSO`/`_AnimationLibrary.asset` |
| `Editor/AnimationEditor/` + `EditorAnimationSystem` | **Delete** once clip content is re-authored — the Clip Editor is its replacement |

## 3. Clip vocabulary — `AnimationType` enum → `ClipId`

Every clip reference today is the `AnimationType : ushort` enum (~100 values, most unused). The toolkit's identity is `ClipId` (stable ulong, minted per asset; generated `const` file via "Generate Clip Id Constants" — names, never numbers, per standing directive).

**← DECISION — how clip references are authored on SOs.** Two options:
- **(A, recommended) Direct `ClipAsset` references.** `UnitSO.idleAnimation` etc. become `ClipAsset` object fields; `UnitLibraryBakingSystem` bakes `clipAsset.StableId` into the blob as `ClipId`. Designer picks assets, no enum to maintain, renames free. `UnitDataBlob` fields become `ulong`.
- **(B) Keep a game enum mapping to ClipIds.** Preserves `[SearchableEnum]` UX but recreates the two-vocabulary drift the toolkit's whole identity system exists to kill.

*Recommendation: A everywhere data-driven (UnitSO, BehaviorSO, NarrativeEventSO); the generated constants file covers the handful of hard-coded game-code sites (death/revival clips).*

Affected data files: `UnitSO.cs` (+`ActionAnimationMapping`/`StanceAnimationMapping`), `UnitBlob.cs`, `UnitLibraryBakingSystem.cs`, `BehaviorSO`/`BehaviorBlobs.cs` (the `PlayAnimation` command's `IntParam` currently casts to `AnimationType` — becomes a `ClipAsset` field on the SO command, baked to `ulong` in `BehaviorCommandBlob`), `NarrativeEventSO.cs` (`animationType` field), `AttackSO`/`AttackBlobs` (attack swing clip, currently resolved via `actionAnimations`).

## 4. The command seam — call-site rewrite

Call sites and their translation (all keep their current logic; only the write changes):

| Site | Today | After |
|---|---|---|
| `BehaviorExecutionSystem` `PlayAnimation`/`PlayActionAnimation`/`StopAnimation` arms | `SetAnimation` buffer add + enable `AnimationRequest` via lookups | `AnimationCommandUtil.Play`/`Stop` via `BufferLookup<AnimationCommand>` + `ComponentLookup<AnimationCommandPending>` |
| `BehaviorInterruptSystem` cleanup | same pattern | same swap |
| `PlayerAttackSystem` swing push | `AIUtils.GetAnimationByAction` → SetAnimation | attack clip `ClipId` from `AttackBlob` → `Play` on the Action layer |
| `UnitAnimationAssignmentSystem` | writes `AnimationLayer` buffer directly via `AnimationUtils.SetLayer` | compares via `PlaybackQuery.IsPlaying`, issues `Play` only on change (never every frame — commands are requests, not state) |
| `NarrativeEventManager` (managed) | SetAnimation via `EntityManager` | same via `EntityManager.GetBuffer<AnimationCommand>` + `SetComponentEnabled<AnimationCommandPending>` |

Layer model: rig layers are ordered slots, index = priority. **← DECISION — the rig's layer list.** *Recommendation: v1 rig declares `0 Base / 1 Action / 2 Override` only. The legacy Direction layer is dormant (its system is a corpse), and Face/Eyes/Mouth have clips but no assigning system — re-add those layers when a blink/expression system actually exists. Fewer layers = cheaper per-actor sampling.*

The Action-layer handback contract transfers cleanly: legacy relied on `looping=false` clips deactivating so `UnitAnimationAssignmentSystem` regains the layer — the toolkit deactivates a `LoopMode.Once` layer on completion and `PlaybackQuery.IsPlaying` answers false, which is the same contract with better queries.

## 5. Ragdoll — keep Ragdoll2D (recommended)

**← DECISION.** The toolkit ships a full ragdoll (capture→probe→solve→apply→release, its own presentation sub-group). The game's Ragdoll2D is *verified gameplay* (launch trajectories, corpse-cell stacking, revive reset); the toolkit's is test-clean but never judged by eye and its ±45° hinge defaults are known-suspect. *Recommendation: keep Ragdoll2D unchanged in v1 — it already tolerates a pose-stomping applier, and nothing about it reads legacy animation components. Evaluate the toolkit ragdoll later as its own task (it would also need corpse cells and `Health.kill*` seeding ported). Do not run both.* Ensure toolkit actors do **not** bake the toolkit's ragdoll components (they're opt-in via authoring — verify, don't assume).

## 6. Design, sound, and part-entity seams

- **Design → restSliceIndex.** `DesignApplyUtil.ApplyDesign` currently writes `AnimationTargetRestPose.baseImageIndex` + `ImageIndex`; it changes to writing the toolkit's per-part rest slice (`restSliceIndex`) — sprite tracks in `RelativeToRest` slice space then retarget every variant automatically (this is exactly the toolkit feature built for design systems). The three `BodyPart*Tint` components and `DesignSlot`/`PersistedDesign`/palette pipeline are untouched.
- **Sound.** `AnimationSoundMarkerSystem` (normalized-time markers on legacy clips) is replaced by the first real AnimEvent consumer: a small game system reading `AnimEventOutput` (gated on `AnimEventsPending`) and mapping event keys → `SoundUtil.PlayOn`. Sound marker data moves into clip event keys (`AnimEvents.Footstep` etc., auto-registered vocabulary). Consumer placement: `SoundSystemGroup` (LateSimulation) — it runs *after* the toolkit's emission the same frame, so no added latency vs today. This system is deliberately written as the template the events plan (area 2) will follow.
- **Part entities.** `BodyPart` buffer/`BodyPartInfo`/`BaseParent` (design + ragdoll registry) stay. Parts additionally get `RigTargetAuthoring` (binds by transform name — part GO names must match rig target names; audit the prefab once). `AnimationTargetPose`/`AnimationTargetRestPose`/`ImageIndex`/`ImageIndexOverride` components and their bakers are deleted; `CharacterRigAuthoring` stops baking them.
- **Spawn/pool.** The toolkit's `RigBindingSystem` (OrderFirst binding group) self-heals part references on ECB-instantiate — that's the exact bug class `BodyPartInitSystem` exists for, handled package-side. Pool-reclaim check for `SpawnStateInitSystem`: reset playback layers on reclaim (stop all layers or re-issue starting clips) — verify what state a reclaimed actor carries; **do not assume the toolkit resets it.**

## 7. Visibility & billboard

- **Visibility:** toolkit presentation gates on its `AnimVisible` enableable; logic/events never gate (timers advance off-screen — same design as today's ungated `AnimationTimeSystem`). *Recommendation:* `CameraVisibilitySystem` keeps its `CameraView` + hysteresis logic and additionally flips `AnimVisible` on actor roots; skip the toolkit's `AnimLodDistanceSystem` in v1 (two visibility authorities is one too many). `CameraVisible` itself stays for the game's own presentation gates. ← DECISION.
- **Billboard:** adopt `BillboardResolveSystem`. Mode choice: ← DECISION — Y-axis upright (mode 2) likely matches the current 2.5D look; **screen-aligned mode requires a host `_ToolkitCameraForward` writer that no longer exists anywhere** (the old `ToolkitCameraBinder` died with the demo folder). If screen-aligned is wanted, a ~20-line MonoBehaviour in `MonoBehaviours/` writing the global from the main camera is part of this plan; otherwise it degrades silently to spherical — write the mode down either way.
- **Shaders:** the 2D graphs already read `_ImageIndex`; diff them against `Docs/AnimationToolkit/shader-contract.md` before Play-mode surprises (atlas mode adds `_AtlasFrame`, unused if we stay on texture arrays).

## 8. Proposed file manifest

**New:** `Systems/SoundSystemGroup/AnimEventSoundSystem.cs` · generated clip-constants file (toolkit action) · optional `MonoBehaviours/ToolkitCameraForwardWriter.cs` (§7)
**Rewritten:** `UnitAnimationAssignmentSystem.cs` · `DesignApplyUtil.cs` (+`DesignApplySystem`/`DesignChangeSystem` write targets)
**Edited:** `SystemGroups.cs` (edges) · `BehaviorExecutionSystem.cs` · `BehaviorInterruptSystem.cs` · `PlayerAttackSystem.cs` · `NarrativeEventManager.cs` · `CameraVisibilitySystem.cs` · `CharacterRigAuthoring.cs` · `SpawnStateInitSystem.cs` · `UnitSO/UnitBlob/UnitLibraryBakingSystem` · `BehaviorSO/BehaviorBlobs/BehaviorLibraryBakingSystem` · `AttackSO/AttackBlobs/AttackLibraryBakingSystem` · `NarrativeEventSO.cs` · `StitchPunk.Systems.asmdef` + `Components`/`MonoBehaviours` asmdefs (+`DotsAnimationToolkit` refs)
**Deleted (final phase):** the 9 legacy `AnimationSystemGroup` systems · `AnimationLibraryBakingSystem.cs` · `AnimationComponents.cs` (all but the three tint components + `BaseParent`, which move next to `BodyPartComponents`) · `AnimationUtils.cs` · `AnimationEnums.cs` (`AnimationType`) · `AnimationClipSO/AnimationLibrarySO` + the 21 legacy clip assets + `_AnimationLibrary.asset` · `Editor/AnimationEditor/` · `Core/Unused/` legacy animation files (8) · legacy entries in `SpawnInit`/bakers
**Assets:** rig finalized from `NewRig.asset` · clip set + ~21 re-authored clips · re-pointed actor prefabs (Phase F shipped no migration — Rig/Clip Sets re-assigned by hand)

## 9. Build phases

0. **Prereqs** (separate sessions): HANDOFF §4 queue + baseline play pass. Nothing below starts until the Clip Editor has been driven for real.
1. **Pilot actor, both stacks alive.** Asmdef refs + ordering edges; author one unit prefab variant as a toolkit actor (rig targets on parts, `ActorAuthoring`, starting Base clip); verify it idles/walks in `DOTSTestScene` while normal units still run legacy. No game system changes yet — starting layers + a throwaway play system prove the pipeline.
2. **Clip content.** Re-author the legacy clips in the Clip Editor against the real rig (blinks are 3-key clips — an hour, not a converter project ← DECISION if the count grows). Sound markers become event keys as clips are re-authored.
3. **Vocabulary.** `UnitSO`/`BehaviorSO`/`AttackSO`/`NarrativeEventSO` re-key to `ClipAsset` refs (§3); bakers bake `ClipId`. Legacy enum fields stay one commit longer so both paths compile.
4. **Command seam cut-over.** Rewrite the five call-site groups (§4) + `UnitAnimationAssignmentSystem`; delete `AnimationRequestSystem`/`SetAnimation`. This is the flag-day commit — after it, legacy `AnimationLayer` buffers receive nothing.
5. **Seams.** Design `restSliceIndex` write; `AnimEventSoundSystem`; `AnimVisible` in `CameraVisibilitySystem`; billboard mode; pool-reclaim reset check.
6. **Delete + docs truth pass.** Everything in §8's delete list; rewrite `Systems_Animation.md` as the game↔toolkit seam note; purge `Gotchas.md` animation entries; fix `Assets/CLAUDE.md`; update `Contracts.md` (`AnimationCommand`/`AnimEventOutput` rows replace `SetAnimation`).
7. **Verify** (below) then retire this doc to `Verification/`.

Each phase is a separate commit; phases 1–3 are safe on `main`, phase 4 onward on a branch until 7 passes. ← DECISION: branch name / whether Spencer wants 4–6 squashed.

## 10. Verification (→ `verify-animationtoolkitmigration.md` at retire time)

All in `DOTSTestScene` + `Game.unity`, owner's eyes required:
- Idle↔walk transition on citizen and rotter; stance (sneak) clip if authored.
- Attack swing plays on Action layer and hands back to Base when done (behavior + player attack).
- Death clip → ragdoll launch → settle → revive reset → design intact (the full HealthSystemGroup pipeline over the new applier).
- Zombify/design change re-skins parts (restSliceIndex path) with tints correct.
- Spawn 50 units, pool-reclaim a batch, confirm reclaimed actors animate from frame 1.
- Off-screen: walk a unit out of view, wait a clip's length, walk back — pose is current, not stale.
- Footstep/swing SFX still fire (AnimEvent path).
- Frame time comparison vs the pre-migration baseline (the area-7 pass) at the same unit count.

## Open decisions

- [ ] §1 cut-over confirmed (vs bridge)
- [ ] §3 clip references: ClipAsset fields (A) vs game enum map (B)
- [ ] §4 v1 rig layer list (recommend Base/Action/Override)
- [ ] §5 ragdoll: keep Ragdoll2D v1 (recommended) vs adopt toolkit ragdoll
- [ ] §7 visibility: CameraVisibilitySystem writes AnimVisible (recommended) vs AnimLodDistanceSystem
- [ ] §7 billboard mode (upright vs screen-aligned + host writer)
- [ ] §9 phase-2 converter only if hand re-authoring proves too slow
- [ ] §9 branch strategy for the flag-day phases
