# Animation Toolkit Game Migration — Design Spec

> **Status:** 🔨 building (2026-08-29) — phases 3–5 landed (vocabulary, command-seam cutover, seams + ragdoll adoption), compiling clean incl. Burst. Remaining: phase 2 (clip/rig authoring, owner) and phase 6 (delete the now-inert legacy presentation stack + docs truth pass). No play-test possible yet — no rig/clips/ragdoll bodies are authored.
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

**This is a migration, not a bridge.** End state deletes the legacy stack, `AnimationLibrarySO`/`AnimationClipSO`, the `AnimationType` enum, `Utils/AnimationUtils.cs`, and the custom `Editor/AnimationEditor/`. **DECIDED: cut-over**, not a permanent bridge — a bridge doubles the maintenance surface for zero shipped value, and the call-site count (12 files) is small.

**Out of scope:** event-driven AI timing (hitTime removal, `WaitForAnimEvent` commands) — that is [`AnimationEventTiming_System.md`](AnimationEventTiming_System.md), which builds on this plan's seam. The one event consumer built *here* is sound (§6), because `AnimationSoundMarkerSystem` dies with the legacy stack and cannot wait.

## 2. Architecture — where the toolkit slots into the frame

The toolkit runs as `AnimationToolkitSystemGroup` inside `SimulationSystemGroup` and deliberately declares **no ordering edges and no scene gating** — the host orders its own groups against it (exactly the movement-toolkit pattern already in `SystemGroups.cs`).

```
GameManager → Player → UtilityAI → MinionActionSelection → StateMachine → Item
  → Movement (package) → Buildings → Combat → Health → Design
  → AnimationSystemGroup (game, shrinks to assignment — issues AnimationCommands)
  → AnimationToolkitSystemGroup (package, inside SimulationSystemGroup: binding → logic/events →
      presentation, ragdoll included — AnimationToolkitRagdollSystemGroup nests inside
      AnimationToolkitPresentationSystemGroup, after BillboardResolveSystem, before SocketResolveSystem)
LateSimulation: Spawn → SpawnInit → Sound → Despawn → Save (no game-side Ragdoll group any more — §5)
```

**Correction (verified against `AnimationToolkitSystemGroups.cs` while building this):** `AnimationToolkitSystemGroup` — and therefore the toolkit's ragdoll — lives entirely inside `SimulationSystemGroup`, not `LateSimulationSystemGroup`. The original draft assumed the ragdoll ran in the game's old `LateSimulation` ragdoll slot; it doesn't. The game's `RagdollSystemGroup` is now deleted (§5) rather than repointed — nothing plugs into it, since the toolkit self-orders inside its own `SimulationSystemGroup` pipeline. The "apply-pose stomps, ragdoll re-writes after" contract still holds, just entirely within one `AnimationToolkitPresentationSystemGroup` pass rather than spanning Simulation→LateSimulation.

Ordering edges added in `SystemGroups.cs` (on **game** types, per the toolkit's contract):
- `AnimationSystemGroup` gains `[UpdateBefore(typeof(AnimationToolkitSystemGroup))]` — commands issued this frame apply this frame. ✅ done.
- `DesignSystemGroup` already precedes `AnimationSystemGroup` — its ordering intent ("before image index push") transfers unchanged.
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
| `Ragdoll2DSystem`, `Ragdoll2DInitSystem`, `Ragdoll2DReviveSystem`, `Ragdoll2DComponents.cs` | **Delete** — replaced by the toolkit's `RagdollActor`/`RagdollLaunch`; corpse-cell stacking and `Health.kill*` seeding port onto the toolkit API (§5) |
| `AnimationLibraryBakingSystem` (PostBaking) | **Delete** with `AnimationLibrarySO`/`AnimationClipSO`/`_AnimationLibrary.asset` |
| `Editor/AnimationEditor/` + `EditorAnimationSystem` | **Delete** once clip content is re-authored — the Clip Editor is its replacement |

## 3. Clip vocabulary — `AnimationType` enum → `ClipId`

Every clip reference today is the `AnimationType : ushort` enum (~100 values, most unused). The toolkit's identity is `ClipId` (stable ulong, minted per asset; generated `const` file via "Generate Clip Id Constants" — names, never numbers, per standing directive).

**DECIDED — direct `ClipAsset` references (option A).** `UnitSO.idleAnimation` etc. become `ClipAsset` object fields; `UnitLibraryBakingSystem` bakes `clipAsset.StableId` into the blob as `ClipId`. Designer picks assets, no enum to maintain, renames free. `UnitDataBlob` fields become `ulong`. Applies everywhere data-driven (UnitSO, BehaviorSO, NarrativeEventSO); the generated constants file covers the handful of hard-coded game-code sites (death/revival clips).

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

Layer model: rig layers are ordered slots, index = priority (`RigAsset.MaxLayerCount = 8`).

**DECIDED — v1 rig declares six layers: `0 Base / 1 Action / 2 Override / 3 Face / 4 Eyes / 5 Mouth`.** The legacy Direction layer stays dropped (its system is a corpse, nothing assigns it). Face/Eyes/Mouth get real slots now, because the ~12 blink clips and 1 mouth clip already exist and Spencer wants them reusable across rigs (heads authored once, shared by many bodies), not deferred behind a not-yet-built assigning system.

**Reuse mechanism — target tags, not per-rig duplication.** Per `sharing-clips.md`, clips/clip sets don't belong to a rig; an `Actor` pairs a `RigAsset` with a list of `ClipSetAsset`s, and a track binds to a **target tag** (e.g. `EyeL`, `EyeR`, `Jaw`) rather than a rig-specific target id. One `FaceExpressions` clip set — blinks, mouth — authored once, tags its parts via the Clip Editor's tag picker (or `RigAsset` inspector's Target Tags section), and plays on any rig that declares matching tagged targets; a rig missing a tag just skips those tracks with a warning, never an error. This is what makes "heads authored separately, reused across many rigs" work without a second actor/entity per unit.

**Known limitation to hold as a project convention, not a toolkit guarantee** (the toolkit's own docs flag this as unsolved): layer *index* identity is per-`RigAsset`, not tag-based — nothing enforces that layer 3 means "Face" on every rig. Every rig this migration touches must declare the same six-layer list in the same order, by convention, for a shared Face/Eyes/Mouth clip set's starting-layer references to mean the same thing everywhere. Document this in the rig-authoring note so a future rig doesn't drift.

The Action-layer handback contract transfers cleanly: legacy relied on `looping=false` clips deactivating so `UnitAnimationAssignmentSystem` regains the layer — the toolkit deactivates a `LoopMode.Once` layer on completion and `PlaybackQuery.IsPlaying` answers false, which is the same contract with better queries.

## 5. Ragdoll — adopt the toolkit's ragdoll

**DECIDED — adopt the toolkit ragdoll, replace Ragdoll2D.** This is a larger scope addition than the spec originally recommended (which was to keep Ragdoll2D untouched); it needs its own care package within this migration:

- **VAT check passes.** The toolkit's ragdoll cannot animate VAT/skinned parts at runtime (limitation 1 in `ragdoll.md`) — only cutout and transform-track parts. Verified: this game's body parts are per-quad cutout (`BodyPartFlags.HasQuad`, per-quad `ImageIndex`/pose), not VAT-skinned meshes, so the toolkit ragdoll fully applies to every part.
- **Mode: `Planar2D`.** Matches the 2.5D billboard game; falls "down the screen" in the resolved billboard frame regardless of which billboard mode §7 lands on (spherical, upright, or screen-aligned all resolve a frame the ragdoll reads the same way). Requires the rig to declare a billboard root — verify `NewRig.asset` has one.
- **Port, don't discard, the verified gameplay:** corpse-cell stacking (`Systems/GameManagerSystemGroup/CorpseCellSystem.cs`) and `Health.kill*` seeding (`Systems/HealthSystemGroup/DeathSystem.cs`) currently key off Ragdoll2D's launch/settle state — re-wire them onto `RagdollLaunch`/`RagdollActor` (enable `RagdollActor` + optional `RagdollLaunch` to drop and throw; disable to restore the captured pose) and whatever settled/sleeping signal the toolkit exposes (`ragdoll.md` §Sleeping — bodies stop simulating but keep writing pose once quiet for `sleepDelaySeconds`).
- **Revive.** `Ragdoll2DReviveSystem`'s job — restore the pre-ragdoll pose — is now `SetComponentEnabled<RagdollActor>(entity, false)`, which the toolkit itself guarantees restores the pose captured on enable exactly. Simplifies this call site.
- **Delete:** `Ragdoll2DSystem`, `Ragdoll2DInitSystem`, `Ragdoll2DReviveSystem`, `Ragdoll2DComponents.cs`, `RagdollSimConfigAuthoring.cs`, `RagdollConfigSO.cs` — replaced by the toolkit's `RagdollConfig` singleton and per-body authoring on the rig (mass/limits/hinge range authored in the Clip Editor, not a game SO).
- Ensure toolkit actors carry ragdoll bodies only where authored (opt-in via the rig, not implicit) — verify the rig's ragdoll boxes before relying on them.

## 6. Design, sound, and part-entity seams

- **Design → restSliceIndex.** `DesignApplyUtil.ApplyDesign` currently writes `AnimationTargetRestPose.baseImageIndex` + `ImageIndex`; it changes to writing the toolkit's per-part rest slice (`restSliceIndex`) — sprite tracks in `RelativeToRest` slice space then retarget every variant automatically (this is exactly the toolkit feature built for design systems). The three `BodyPart*Tint` components and `DesignSlot`/`PersistedDesign`/palette pipeline are untouched.
- **Sound.** `AnimationSoundMarkerSystem` (normalized-time markers on legacy clips) is replaced by the first real AnimEvent consumer: a small game system reading `AnimEventOutput` (gated on `AnimEventsPending`) and mapping event keys → `SoundUtil.PlayOn`. Sound marker data moves into clip event keys (`AnimEvents.Footstep` etc., auto-registered vocabulary). Consumer placement: `SoundSystemGroup` (LateSimulation) — it runs *after* the toolkit's emission the same frame, so no added latency vs today. This system is deliberately written as the template the events plan (area 2) will follow.
- **Part entities.** `BodyPart` buffer/`BodyPartInfo`/`BaseParent` (design + ragdoll registry) stay. Parts additionally get `RigTargetAuthoring` (binds by transform name — part GO names must match rig target names; audit the prefab once). `AnimationTargetPose`/`AnimationTargetRestPose`/`ImageIndex`/`ImageIndexOverride` components and their bakers are deleted; `CharacterRigAuthoring` stops baking them.
- **Spawn/pool.** The toolkit's `RigBindingSystem` (OrderFirst binding group) self-heals part references on ECB-instantiate — that's the exact bug class `BodyPartInitSystem` exists for, handled package-side. Pool-reclaim check for `SpawnStateInitSystem`: reset playback layers on reclaim (stop all layers or re-issue starting clips) — verify what state a reclaimed actor carries; **do not assume the toolkit resets it.**

## 7. Visibility & billboard

- **Visibility — DECIDED: extend `CameraVisibilitySystem`.** Toolkit presentation gates on its `AnimVisible` enableable; logic/events never gate (timers advance off-screen — same design as today's ungated `AnimationTimeSystem`). `CameraVisibilitySystem` keeps its `CameraView` + hysteresis logic and additionally flips `AnimVisible` on actor roots — one visibility authority feeding both the game's own `CameraVisible` (kept for the game's own presentation gates) and the toolkit's flag. The toolkit's `AnimLodDistanceSystem` is skipped in v1 — running it alongside would give the game two independent, possibly-disagreeing answers to "is this visible."
- **Billboard — DECIDED: `BillboardResolveSystem`, Y-axis upright (mode 2).** Matches the current 2.5D look with no new code. Screen-aligned mode requires a host `_ToolkitCameraForward` writer that no longer exists anywhere (the old `ToolkitCameraBinder` died with the demo folder) — not needed for this decision, but noted here in case a future look change wants it: a ~20-line MonoBehaviour in `MonoBehaviours/` writing the global from the main camera.
- **Shaders:** the 2D graphs already read `_ImageIndex`; diff them against `Docs/AnimationToolkit/shader-contract.md` before Play-mode surprises (atlas mode adds `_AtlasFrame`, unused if we stay on texture arrays).

## 8. Proposed file manifest

**New:** `Systems/SoundSystemGroup/AnimEventSoundSystem.cs` · generated clip-constants file (toolkit action) · generated target-tag constants file (toolkit action, §4) · optional `MonoBehaviours/ToolkitCameraForwardWriter.cs` (§7, not built this pass)
**Rewritten:** `UnitAnimationAssignmentSystem.cs` · `DesignApplyUtil.cs` (+`DesignApplySystem`/`DesignChangeSystem` write targets) · `Systems/GameManagerSystemGroup/CorpseCellSystem.cs` (§5, re-wire onto `RagdollActor`/`RagdollState.Sleeping`) · `CharacterRigBakingSystem.cs` (drops its ragdoll-stamp half) · `BodyPartAuthoring.cs` (drops the `RagdollJoint` flag). **`DeathSystem.cs` needed no changes** — `Health.kill*` seeding onto the toolkit ragdoll landed as a new system (`RagdollLaunchInitSystem`, same `HealthSystemGroup` slot Ragdoll2DInitSystem held), not an edit to DeathSystem.
**Edited:** `SystemGroups.cs` (edges) · `BehaviorExecutionSystem.cs` · `BehaviorInterruptSystem.cs` · `PlayerAttackSystem.cs` · `NarrativeEventManager.cs` · `CameraVisibilitySystem.cs` · `CharacterRigAuthoring.cs` · `SpawnStateInitSystem.cs` · `UnitSO/UnitBlob/UnitLibraryBakingSystem` · `BehaviorSO/BehaviorBlobs/BehaviorLibraryBakingSystem` · `AttackSO/AttackBlobs/AttackLibraryBakingSystem` · `NarrativeEventSO.cs` · `StitchPunk.Systems.asmdef` + `Components`/`MonoBehaviours` asmdefs (+`DotsAnimationToolkit` refs)
**Deleted (final phase):** the 9 legacy `AnimationSystemGroup` systems · `AnimationLibraryBakingSystem.cs` · `AnimationComponents.cs` (all but the three tint components + `BaseParent`, which move next to `BodyPartComponents`) · `AnimationUtils.cs` · `AnimationEnums.cs` (`AnimationType`) · `AnimationClipSO/AnimationLibrarySO` + the 21 legacy clip assets + `_AnimationLibrary.asset` · `Editor/AnimationEditor/` · `Core/Unused/` legacy animation files (8) · legacy entries in `SpawnInit`/bakers · `Ragdoll2DSystem.cs` · `Ragdoll2DInitSystem.cs` · `Ragdoll2DReviveSystem.cs` · `Ragdoll2DComponents.cs` · `RagdollSimConfigAuthoring.cs` · `RagdollConfigSO.cs` (§5)
**Assets:** rig finalized from `NewRig.asset`, six layers (§4) · clip set + ~21 re-authored clips, blinks/mouth split into a tag-bound `FaceExpressions` set (§4) · ragdoll bodies authored on the rig (mass/limits/hinge, §5) · re-pointed actor prefabs (Phase F shipped no migration — Rig/Clip Sets re-assigned by hand)

## 9. Build phases

0. **Prereqs** (separate sessions): HANDOFF §4 queue + baseline play pass. **Waived 2026-08-29 by owner call** — proceeding to Phase 1 without the Clip Editor persistence check / Samples~ compile-check / A55 visual pass having been run. If Phase 1's pilot actor surfaces a Clip Editor or toolkit-authoring bug, that's this waiver's risk landing; circle back to HANDOFF §4 before pushing further.
1. **Pilot actor, both stacks alive.** Asmdef refs + ordering edges; author one unit prefab variant as a toolkit actor (rig targets on parts, `ActorAuthoring`, starting Base clip); verify it idles/walks in `DOTSTestScene` while normal units still run legacy. No game system changes yet — starting layers + a throwaway play system prove the pipeline.
   - ✅ Done (2026-08-29): `using DotsAnimationToolkit;` + `[UpdateBefore(typeof(AnimationToolkitSystemGroup))]` on `AnimationSystemGroup` in `SystemGroups.cs`; `DotsAnimationToolkit.Runtime` added to `StitchPunk.Systems.asmdef`. Compile gate clean, zero console errors.
   - ⏸ Blocked on owner: `NewRig.asset` currently has **zero targets and zero layers** (`targets: []`, `layers: []`), while the two clips already keyed against it (`NewClip.asset`, `NewClip 1.asset`) reference target ids (`1369813100`/`495986569`/`473712160`/`1535265300`) the rig doesn't declare — they'll warn-and-skip (rule T6) until the rig's targets exist. Mapping real body-part GameObjects to rig targets (name, `TargetKind`, half-extents) is a visual Clip Editor task, not something to blind-author via asset patches. Rig targets + the six-layer list (§4) need an owner pass before the prefab variant / starting-Base-clip / throwaway play system can mean anything.
2. **Clip content.** Re-author the legacy clips in the Clip Editor against the real rig, including tagging the shared `FaceExpressions` set's parts (§4). Blinks are 3-key clips — hand re-authoring first; fall back to a converter only if the count grows past what an hour of hand work covers. Sound markers become event keys as clips are re-authored. Author ragdoll bodies (mass/limits, `Planar2D`) on the rig here too, while eyes are already on the timeline.
   - ⏸ Owner's asset-authoring task — not started. Blocks nothing below at the code level; phases 3–6 landed against the toolkit's real API without waiting on it (per owner call 2026-08-29: "leave the mapping to me, I want the code environment ready to test against once real assets exist").
3. **Vocabulary.** `UnitSO`/`BehaviorSO`/`AttackSO`/`NarrativeEventSO` re-key to `ClipAsset` refs (§3); bakers bake `ClipId`. Legacy enum fields stay one commit longer so both paths compile.
   - ✅ Done (2026-08-29), full cutover rather than a staged dual-path — no live legacy clip content exists to protect, so `UnitSO`/`BehaviorSO`/`NarrativeEventSO`'s `AnimationType` fields were replaced outright with `ClipAsset` (not kept alongside). `AttackSO` needed no change (swing clips resolve through `UnitSO.actionAnimations`, not `AttackBlob`).
4. **Command seam cut-over.** Rewrite the five call-site groups (§4) + `UnitAnimationAssignmentSystem`; delete `AnimationRequestSystem`/`SetAnimation`. This is the flag-day commit — after it, legacy `AnimationLayer` buffers receive nothing.
   - ✅ Done (2026-08-29). `AnimationRequestSystem`, `SetAnimation`, `AnimationRequest` deleted. The 6 read-side legacy presentation systems (`AnimationTimeSystem`, `AnimationSamplingSystem`, `ApplyAnimatedPoseSystem`, `UpdateImageIndexSystem`, `BillboardSystem`, `AnimationSoundMarkerSystem`) and `AnimationLibrarySO`/`AnimationClipSO`/`AnimationUtils.cs`/`AnimationEnums.cs`/`Editor/AnimationEditor/` are **deliberately left for phase 6** — nothing writes `AnimationLayer` any more, so they're now permanently-empty-query no-ops, not a correctness risk, just not yet deleted.
5. **Seams.** Design `restSliceIndex` write; `AnimEventSoundSystem`; `AnimVisible` in `CameraVisibilitySystem`; billboard mode; pool-reclaim reset check; ragdoll port — re-wire `CorpseCellSystem`/`DeathSystem` onto `RagdollActor`/`RagdollLaunch`, delete the six Ragdoll2D files (§5).
   - ✅ Done (2026-08-29) — all of it, including the ragdoll port. Billboard mode needs no code (`ActorAuthoring.billboardMode`, an authoring-time field). Ragdoll port went wider than the original 6-file estimate once traced: also touched `CharacterRigBakingSystem.cs` (dropped its ragdoll-stamp half), `BodyPartAuthoring.cs`/`BodyPartFlags` (dropped the `RagdollJoint` flag), and deleted `RagdollJointAuthoring.cs`/`RagdollJointSO.cs`/`Ragdoll2DSpawnInitSystem.cs` — the legacy per-joint landing-zone authoring has no toolkit equivalent (hinge ranges are authored once on the rig body, not rolled per kill). The corpse-stacking landing-height hack (`corpseStackOffset`/`corpseStackMax`) was dropped rather than ported — see `CorpseCellSystem.cs`'s header comment for why. `RagdollLaunchInitSystem`'s `worldPoint`/`worldTorque` are a documented approximation (no precise hit-location tracking exists) — tune visually once real physics play-testing is possible.
6. **Delete + docs truth pass.** Everything in §8's delete list; rewrite `Systems_Animation.md` as the game↔toolkit seam note; purge `Gotchas.md` animation entries; fix `Assets/CLAUDE.md`; update `Contracts.md` (`AnimationCommand`/`AnimEventOutput` rows replace `SetAnimation`).
7. **Verify** (below) then retire this doc to `Verification/`.

**DECIDED — branch strategy.** Phases 1–3 are safe on `main` (both stacks alive, additive). Phases 4–6 land on `animation-toolkit-cutover`, one commit per phase, squashed into a single commit on merge to `main` once phase 7 passes — keeps the flag-day change as one clean, revertable unit in `main`'s history.

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

## Decisions (locked 2026-08-29)

- [x] §1 — Cut-over, not a bridge.
- [x] §3 — Clip references: direct `ClipAsset` fields (A).
- [x] §4 — v1 rig: six layers, `Base/Action/Override/Face/Eyes/Mouth`; Face/Eyes/Mouth reused across rigs via target-tag-bound `ClipSetAsset`s, not per-rig duplication or a second head actor. Layer-index-means-the-same-thing-everywhere is a project convention to hold, not a toolkit guarantee — see §4.
- [x] §5 — Ragdoll: adopt the toolkit's ragdoll (not Ragdoll2D). Corpse-cell stacking and `Health.kill*` seeding port onto `RagdollActor`/`RagdollLaunch`; `Planar2D` mode; VAT-runtime limitation verified not to apply (parts are cutout quads).
- [x] §7 — Visibility: `CameraVisibilitySystem` extended to flip `AnimVisible`; toolkit's `AnimLodDistanceSystem` not used.
- [x] §7 — Billboard: Y-axis upright (mode 2); no camera-forward writer needed this pass.
- [x] §9 — Branch: `animation-toolkit-cutover` for phases 4–6, squashed on merge.
- Not a decision, self-resolving: §9 phase-2 clip converter is built only if hand re-authoring the ~21 clips proves too slow in practice.
