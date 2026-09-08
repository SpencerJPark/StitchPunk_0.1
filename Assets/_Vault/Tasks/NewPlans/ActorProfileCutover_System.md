# Actor Profile Cutover — Design Spec (G5)

> **Status:** 🔨 P1–P9 built and gated 2026-09-07 (EditMode 823 / PlayMode 291, one known `Conformance_A` drift). **⏸ P10 owner checkpoint open.** Depends on toolkit **A70** and **A71**
> (`ActorEditor_Roadmap.md` §3). Absorbs the content tasks of the superseded
> [`AnimationLayersContent_System.md`](AnimationLayersContent_System.md).
> **Executor:** one fresh Claude Sonnet session; `Cutscene_Roadmap.md` §4 protocol, commit prefix
> `G5-Pn:`. Subagents only on **[parallel-safe]** tasks, never MCP.
> **Why it exists:** A70 removes `ActorAuthoring.rig/clipSets/startingLayers`, `RigAsset.layers`
> and `DirectionSetAsset`'s five fields, so the game does not compile until its unit data, its
> assignment system and its facing writer move onto the profile. The same pass authors the first
> real profile for `MaleCitizen` and the clips it needs, which is where this morning's AL content
> work lands.

---

**Skills Needed:** `dots-test` (two PlayMode fixtures), `dots-authoring-baker` (`UnitSO` re-key +
`UnitLibraryBakingSystem`), `dots-blob-library` (the `UnitBlob` field removals).

---

## 1. Purpose & scope

Make the game play animations **by name** through the actor's profile and delete every game-side
copy of the knowledge the profile now holds. End state: `UnitSO` names a profile (validate-only,
the prefab's `ActorAuthoring.profile` is the truth); `ActionType`/`StanceType` bind to animation
names by convention at bake; `UnitAnimationAssignmentJob` issues `PlayAnimation(AnimNames.Idle /
Walk / <Stance>Idle / <Stance>Walk)` on change; behaviour commands and the player swing issue
`PlayAnimation(<action name>)`; `UnitFacingSystem` also writes `ActorFacing`; cutscenes request
`CutsceneApi.TopLayer`; `DirectionSetBlob`, `DirectionSetBakeUtil`, `AnimationToolkitLayer` and the
`UnitBlob` animation fields are gone. `MaleCitizen` gets a profile with Idle, Walk, Attack, Death
(ragdoll Start), Resurrection (ragdoll Stop), DeathFace and Blink.

**Out of scope:** `PlayerUnit`/`BaseUnit` becoming actors (separate visual tree — memory
`project_first_toolkit_actor`); per-eye-style blink variants; mouth flaps; the RG ragdoll bodies
(without RG, Death/Revive triggers are no-ops with a P6 warning — that is fine).

## 2. What exists today (verified 2026-09-07 — re-verify; A70/A71 will have moved names)

| Thing | State |
|---|---|
| Game call sites of `PlaybackApi` | `UnitAnimationAssignmentSystem.cs` (4), `Utils/BehaviorCommands/AnimationCommands.cs` (3: `RunPlayAnimation`, `RunPlayActionAnimation`, `RunStopAnimation`), `PlayerAttackSystem.cs` (1), `SpawnStateInitSystem.cs` (1 — the pool-reclaim reset). `NarrativeEventManager.cs` lines 304–331 write an `AnimationCommand` by hand for `PlayAnimationAction.animationClip`. |
| `AnimationToolkitLayer` (`Data/Enums/`) | `Base0 Action1 Override2 Face3 Eyes4 Mouth5`. Readers: the four write sites above, `AttackRequestSystem` (filters `AnimEventOutput.layerIndex == Action`), `NarrativeEventManager` (`PlayCutsceneAction.layer`, `PlayAnimationAction.layer`), `CutsceneDebugTrigger`. |
| `UnitSO` animation fields | `animationDirections`, `actionAnimations[] { ActionType, DirectionSetAsset }`, `stanceAnimations[] { StanceType, idle, moving }`, `idleAnimation`, `movingAnimation`, validate-only `rig`, `clipSet`; `DescribeRigMismatch()` compares against the prefab's `ActorAuthoring`. All four unit `.asset`s hold stale integers in these fields (AL §2). |
| `UnitBlob` | `animationDirections`, `actionAnimations`, `stanceAnimations`, `idleAnimation`, `movingAnimation` (`DirectionSetBlob`), baked by `UnitLibraryBakingSystem` through `DirectionSetBakeUtil.Bake`. `AIUtils.GetAnimationByAction` and `GetActionByAttack` read them. |
| `UnitFacingSystem` / `UnitFacingJob` | derives `UnitFacing.current` (movement, aim, `CutsceneFacing`) and pushes `PartFacing` onto every `BodyPart` part with `PartLibraryBlob` view offsets. Stays; gains one write. |
| `UnitAnimationAssignmentJob` | Base branch picks idle/walk per stance and facing and `Play`s on change; Action branch keys off `UnitAction.current`, which only `DeathSystem`/`ReviveRequestSystem` write. |
| Tests | `DirectionSetBlobFoldTests` (moves to the package in A70-T4, delete here), `FacingSpaceTests` (keep), `AIUtilsTests` (keep), `BehaviorExecutionSystemTests` (the World-fixture template), `SystemGroupOrderTests`/`SystemPlacementConformanceTests` (keep green). |
| Editor seam | `Assets/_Scripts/Editor/DirectionSetContext/UnitDirectionSetContextProvider.cs` implements the toolkit's `IDirectionSetContextProvider`; A71 deletes the interface — delete the provider here. |
| Content | `NewRig.asset` 16 targets (+4 face targets if AL-T4 ran; else add them here, §5 P5), `Walk.asset`, `NewClipSet.asset`, `MaleCitizenWalkDirections.asset` (re-author as `DirectionSlots` inside the profile; delete the asset), `MaleCitizen.prefab` `ActorAuthoring` (A70 re-shaped it: `profile` only). Legacy clip facts and the blink recipe: AL §2 and AL-T5/T6/T7. |
| Ragdoll on death | `RagdollLaunchInitSystem` enables `RagdollActor` from `Dead`; with the profile's Death entry also carrying `ragdollTrigger = Start`, both would enable it — harmless (idempotent), but the launch must still be written before capture: keep `RagdollLaunchInitSystem` as the impulse writer and let it **also** stay the enabler (decision D5). |

## 3. The target

```
MaleCitizen.profile.asset (ActorProfileAsset)      rig = NewRig, clipSets = [ NewClipSet ], turnDirections = Six
  Base      { starting = Idle }   Idle (dir: SE)         Walk (dir: SE)
  Action                          MeleeContinuous (dir: SE, Once)  Death (clip: none/empty, ragdoll Start at play)
                                  Resurrection (ragdoll Stop at play)
  Face                            DeathFace (clip, Loop)
  Eyes      { starting = Blink }  Blink (clip, Loop)
  Mouth                           —
  Override                        —
UnitSO.MaleCitizen / Rotter        actorProfile = MaleCitizen.profile (validate-only)
bake                               ActionType/StanceType → AnimNames by their own names: MeleeContinuous,
                                   Death, Resurrection, plus Idle/Walk and "<Stance>Idle"/"<Stance>Walk"
runtime                            assignment: PlayAnimation(Idle|Walk) on change · behaviours: PlayAnimation(action)
                                   facing: UnitFacingSystem → ActorFacing.facing (toolkit re-picks slots)
                                   death: DeathSystem → RagdollLaunchInitSystem (impulse + enable) and profile Death entry
```

**Acceptance:** in `TestArea.unity`, citizens idle, walk, blink; a rotter's punch plays Attack on
the Action layer with legs untouched and no "Attack event never arrived" warning; a killed citizen
plays DeathFace and (with RG) ragdolls; reviving it plays Resurrection and restores; F9 cutscenes still
play on the top layer. All four suites green with no dropped counts.

## 4. Read first

1. Repo `CLAUDE.md`, `RULES.md`, `Gotchas.md` §"cutscene layer must exist" (now closed by A70) and
   §"DOTS Animation Toolkit — authoring a clip from code".
2. `Docs/AnimationToolkit/Amendment_A70_ActorProfile_Spec.md` §3 (the API you are cutting over
   to) and its §7 build log (the game fixtures it left red); `Documentation~/actor-profiles.md`.
3. `Systems_Animation.md`, `Contracts.md` rows for `AnimationCommand`, `AnimEventOutput`,
   `CutsceneFacing`.
4. The files in §2, plus `Assets/_Scripts/Data/Enums/AiEnums.cs` (`ActionType`, `StanceType`),
   `Assets/_Scripts/Utils/UnitBakingUtil.cs`, `Assets/_Scripts/Systems/PostBakingSystemGroup/UnitLibraryBakingSystem.cs`.
5. AL §2 (legacy clip facts, eye array, sprite-key semantics) and AL-T5/T6/T7 (the clip recipes)
   — those three tasks are reproduced below as P5–P7 and are the only part of AL that survives.

## 5. Tasks

After each: compile gate → the task's fixtures → tick → commit `G5-Pn: <what>`.

- [x] **P1 — Compile again.** Re-point everything A70 broke, mechanically, no behaviour change yet:
  `UnitSO` drops the seven animation fields and gains `ActorProfileAsset actorProfile`
  (`DescribeRigMismatch` → `DescribeProfileMismatch`, comparing against the prefab's
  `ActorAuthoring.profile`); `UnitBlob` drops its five animation fields; `UnitLibraryBakingSystem`
  and `DirectionSetBakeUtil` shrink accordingly (delete the util); `DirectionSetBlob.cs` and
  `DirectionSetBlobFoldTests.cs` deleted (the package owns them now); `UnitDirectionSetContextProvider.cs`
  deleted; every `AnimationToolkitLayer` reader stubbed to compile (real cutover in P2–P4). *Gate:*
  both game assemblies compile; `StitchPunk.Tests` discovered count equals the pre-A70 count
  minus the deleted fixture's tests.

- [x] **P2 — Name-convention binding at bake.** `UnitLibraryBakingSystem` resolves, through
  `VocabularyRegistryProvider.AnimationNames` (editor/baking assembly), `"Idle"`, `"Walk"`, every
  `StanceType` as `"<Stance>Idle"`/`"<Stance>Walk"`, and every `ActionType` by its enum name, into
  a new `UnitBlob.animationKeys` table (`BlobArray<ActionAnimationKey { ActionType action; uint key }>`
  + `idleKey`, `walkKey`, `BlobArray<StanceAnimationKeys>`); an unresolved name bakes `0` and is
  listed in **one** consolidated bake warning per unit. `AIUtils.GetAnimationByAction` returns the
  key (`uint`, 0 = none). *Fixture:* EditMode `UnitAnimationKeyBindingTests` over a pure
  `ResolveConventionName(ActionType)` — `MeleeContinuous → "MeleeContinuous"`, `Death → "Death"`,
  and the stance composition — it exists so the convention has one home; keep it to two tests.

- [x] **P3 — Assignment issues named commands.** `UnitAnimationAssignmentJob`: Base branch becomes
  `key = isMoving ? walkKey : idleKey` (stance-aware as today) and `if (!PlaybackApi.IsAnimationPlaying(layers, key)) PlaybackApi.PlayAnimation(...)`.
  **Delete the Action branch entirely** — action animations are behaviour-command driven
  (`MeleeContinuousBehaviour`'s `PlayActionAnimation`) and `UnitAction.current` only ever says
  Idle or Death; Death is the profile's trigger now. The job no longer needs `UnitFacing`
  (the toolkit re-picks). *Fixture:* PlayMode `UnitAnimationAssignmentSystemTests` — a standing
  unit whose Base layer is inactive gets exactly one `PlayAnimation` command with `idleKey`; a
  unit already playing it gets none (fails on a job that re-issues every frame).

- [x] **P4 — Every other write site.** `AnimationCommands.RunPlayActionAnimation` →
  `PlayAnimation(AIUtils.GetAnimationByAction(...))`; `RunPlayAnimation`'s `cmd.AnimationClip`
  becomes an animation key on `BehaviorCommandAuthoring` (picked through the toolkit's
  `VocabularyPicker` in the behaviour inspector — extend the existing custom inspector, or a
  `PropertyDrawer` on a `[AnimationName] uint` field; never a raw int field); `RunStopAnimation` →
  `StopAnimation(key)` where the interrupt cleanup knows the key it started, else stop the Action
  layer by index resolved from the profile blob (`ActorProfileApi.TryResolve` gives `layerIndex`).
  `PlayerAttackSystem` → `PlayAnimation(key)`. `NarrativeEventManager.PlayAnimationAction` →
  `animationName` (key) + `PlaybackApi.PlayAnimation` instead of the hand-built command; its
  `IsLayerInactive` wait → `!IsAnimationPlaying(key)`; `PlayCutsceneAction.layer` and
  `CutsceneDebugTrigger` → `CutsceneApi.TopLayer`. `AttackRequestSystem`: the event filter becomes
  `attackerEvents[i].animationKey == attackKey` (the attack's own key from `UnitBlob`), no layer
  index. Delete `AnimationToolkitLayer.cs`. `SpawnStateInitSystem`'s reclaim reset: `Stop` every
  layer by index is still legal (raw layer API stays). *Gate:* `BehaviorExecutionSystemTests`,
  `CutsceneSystemTests`, `CutsceneDialogueCueTests` green; grep proves zero `AnimationToolkitLayer`
  references remain.

- [x] **P5 — Facing.** `UnitFacingJob` writes `ActorFacing.facing = unitFacing.current` on the
  root (lookup, `HasComponent`-guarded) in the same pass that pushes `PartFacing`. No fixture
  beyond `FacingSpaceTests` — the write is one line and the toolkit's `ActorFacingRepickTests`
  cover the consequence. *Gate:* Play → a turning unit's `ActorFacing.facing` follows
  `UnitFacing.current`. If AL-T4 did not run: add the four face targets (`Eyes`, `Mouth`,
  `LeftEyeBrow`, `RightEyeBrow`) to `NewRig.asset` and `RigTargetAuthoring` to the parts now,
  exactly as AL-T4 specified (tags `Eyes`, `Mouth`, `LeftEyebrow`, `RightEyebrow`, extents about
  the pivot, `EnsureStableIds()` after, regenerate `TargetTags.cs`).

- [x] **P6 — Clips.** [parallel-safe, three subagents, one each] Author against `NewRig` into
  `NewClipSet` exactly as AL-T5 (`Idle.asset`), AL-T6 (`Attack.asset` with the `AnimEvents.Attack`
  marker at 0.35 and arms+torso tracks only) and AL-T7 (`Blink.asset`, Absolute sprite keys with the
  `-1` sentinel, eyebrow bob) describe — copy those task texts verbatim into the subagent prompts.
  Plus one new clip: `DeathFace.asset` — 0.2 s, `Loop`, a sprite track on `Eyes` holding the
  closed frame (Blink's last closing slice) and a small `Mouth` sprite change if the mouth array
  has an open-mouth slice that reads as slack (pick by eye; record the index). *Gate:*
  `ClipValidation.ValidateClip` clean on all four; each one's sampled pose changes between two
  frames (Idle, Attack) or its sprite slice differs from rest (Blink, DeathFace).

- [x] **P7 — The profile, the registry names, the SOs.** Mint `Idle`, `Walk`, `MeleeContinuous`,
  `Death`, `Resurrection`, `DeathFace`, `Blink` in the Animation Names registry (Project Settings page or the
  picker's Create row; regenerate `Assets/Generated/DotsAnimationToolkit/AnimNames.cs`). Create
  `Assets/ScriptableObjects/Animations/MaleCitizen.profile.asset` per §3 — through the Actor Editor
  tab (A71) so the panel gets its first real use; `execute_code` only if the tab cannot do a step,
  and then record why in §7. `Idle`/`Walk`/`MeleeContinuous` are directional with `southEast` filled and
  `targetDirections = Two`; `Death` has no clip (`clip = null` is legal for a trigger-only entry
  — if P4 of A70 rejects it, give it a one-frame empty clip and log the drift), `ragdollTrigger =
  Start`; `Resurrection` likewise with `Stop`. Point `MaleCitizen.prefab`'s `ActorAuthoring.profile` at
  it; set `UnitSO.MaleCitizen.actorProfile` and `Rotter.actorProfile`; delete
  `MaleCitizenWalkDirections.asset` after re-pointing `A65CheckpointCutscene.asset`'s slot
  `directionSet` at a fresh `DirectionSetAsset` whose `slots.southEast = Walk` (cutscene blocks
  still use the asset). *Gate:* Play → a citizen's `PlaybackLayer` buffer has 6 entries, `[0]`
  holds Walk or Idle by `Movement.isMoving`, `[4]` holds Blink; `read_console` has no P-rule error
  and no unresolved-name bake warning for these two units.

- [x] **P8 — Prove the punch and the death.** As AL-T9: `DebugZombifyMenu` converts a citizen,
  it punches a neighbour: `IsAnimationPlaying(attackKey)` true during the swing, `AnimEventOutput`
  carries `Attack` with the attack's `animationKey`, victim health drops, no fallback warning. On
  the victim: DeathFace playing on layer 2 (Face) after death; with RG landed, `RagdollActor`
  enabled; `ReviveRequest` → Resurrection entry → `RagdollActor` disabled and Base resumes Idle. Then the
  four full suites, counts not dropped.

- [x] **P9 — Docs truth pass.** `Systems_Animation.md` rewritten around the profile (what the
  game still owns: `ActorFacing` write, name-convention binding, the two `PlaybackApi` call
  shapes); `Contracts.md` rows; `Gotchas.md`: remove the "cutscene layer must exist on the rig"
  trap, add "an animation name that is not in the registry bakes to key 0 and the unit silently
  never plays it — read the consolidated bake warning". Mark `AnimationLayersContent_System.md`
  superseded at its top (it already is; confirm), update `Tasks/Plans/README.md` rows, memory
  note `project_animation_layers_and_ragdoll_specs`.

- [ ] **⏸ P10 — Owner checkpoint.** `TestArea.unity`, Play: idle sway, blink, walk, a rotter's
  punch, a death with the death face (and the ragdoll if RG landed), a resurrection; F9 in
  `CutsceneG1Checkpoint.unity` still walks the minions. Then open the Actor Editor on
  `MaleCitizen.profile.asset` and confirm the same mix previews there — that is A71's checkpoint
  seen through real content.

## 6. Decisions

- **D1 Name-convention binding, no per-unit mapping table.** The animation name **equals the enum
  name**: actions bind to `"MeleeContinuous"`, `"Death"`, `"Resurrection"`, …; stances to
  `"<Stance>Idle"` / `"<Stance>Walk"`; locomotion to `"Idle"` / `"Walk"`. The registry rows P7
  mints are therefore `Idle`, `Walk`, `MeleeContinuous`, `Death`, `Resurrection`, `DeathFace`,
  `Blink` (§3 and the tasks above use these names). The owner's standing directive ("I shouldn't
  have to manually assign any assets") decides this; a per-unit table is the thing it forbids.
  Revert: add `UnitSO.actionAnimationNames[]` with the picker.
- **D2 The assignment job loses its Action branch.** Behaviour commands own action animations;
  death is a trigger. Nothing else ever wrote `UnitAction.current`.
- **D3 `AttackRequestSystem` matches events by animation key, not layer index**, so an attack
  authored on any layer still lands damage.
- **D4 `PartFacing` stays host-owned.** The game keeps its view-offset art data; the toolkit only
  learns the facing.
- **D5 `RagdollLaunchInitSystem` keeps enabling `RagdollActor`.** The profile's Death trigger
  enables it too; both are idempotent, and the launch impulse must be written by the game before
  the toolkit's capture regardless. If the double-enable ever matters, drop the game's enable and
  keep the impulse write.
- **D6 Cutscene slots keep their own `DirectionSetAsset`.** One asset survives for that purpose.

## 7. Open questions / build log

- *(per task: the pre-/post-A70 test counts, the registry names as minted, DeathFace slice
  indices, anything the Actor Editor could not do that `execute_code` had to.)*
- **(P1–P4, 2026-09-07)** Built by three Sonnet agents; drift: `RunStopAnimation` has no stored key at
  either call site, so it stops every non-Base layer whose `animationKey != 0`; `BehaviorSOEditor` /
  `NarrativeEventSOEditor` pick the name through an IMGUI popup over the registry (game inspectors are
  IMGUI already). **A pre-existing bug surfaced by the new fixture:** `UnitAnimationAssignmentJob`
  took `EnabledRefRW<AnimationCommandPending>` without `[WithPresent]`, so no idle unit ever matched
  — every unit's Base layer was only ever driven by the starting seed. Fixed.
- **(P5–P7, 2026-09-07)** Content authored by `Assets/_Scripts/Editor/ContentAuthoring/MaleCitizenContentAuthoring.cs`
  (one method per step, each idempotent): 4 face targets (rig now 20 targets, 20 tags), `Idle`,
  `MeleeContinuous`, `Blink`, `DeathFace`, `MaleCitizen.profile.asset` (6 layers), `MaleCitizen.asset`
  and `Rotter.asset` re-keyed, `MaleCitizenWalkDirectionSet.asset` for the A65 cutscene slot. Blink
  slices are the legacy Human sequence (11, 9, 7, 1) — pick by eye at P10. A70 gained a rule change on
  the way: a trigger-only entry (Death/Resurrection with no clip) is a request, not a P4 error.
- **(P8, machine, 2026-09-07)** Play in `TestArea`: both actors bake clean (6 layers, 20 parts, 11
  bodies, `RagdollLaunch`, `ActorFacing`), Base switches Idle↔Walk with `Movement.isMoving`, the Eyes
  layer cycles Blink (slice 9 at the expected phase), a killed citizen ragdolls. Two gaps closed after
  the sample: the baker now stamps `animationKey` on a seeded layer, and `UnitFacingSystem` syncs
  `ActorFacing` against the actor's own value (it was SouthEast while `UnitFacing` said South).
  **Nothing issued `Death`/`Resurrection`/`DeathFace` — there is no Death behaviour asset.** Follow-up
  built the same day: `DeathSystem` plays the `Death` key and the `<Action>Face` key (`DeathFace`),
  `ReviveRequestSystem` plays `Resurrection` and stops the face clip; `faceAnimationKeys` bind by the
  `<Action>Face` convention. The punch itself (a rotter converted with `DebugZombifyMenu`) is P10's.
- **(P8, second sample, 2026-09-07)** With the death seam in: a killed citizen goes `action = Death`,
  `DeathFace` plays, the ragdoll engages. The Eyes layer composites *above* Face, so a death face on
  Face was overridden by the blink — `DeathFace` now lives on the **Eyes** layer (replacing Blink)
  and a `ResurrectionFace` entry (the Blink clip) restarts the blink on revive; `ReviveRequestSystem`
  plays `<Action>Face` for `Resurrection`. Face stays an empty slot. Two Editor restarts were needed
  during this pass for the in-session Burst cache corruption (memory: `project_burst_jit_cache_corruption`).
