# Animation Layers Content — Design Spec (AL)

> **Status:** ✅ spec written 2026-09-07, nothing built. Delegated decisions in §6.
> **Executor:** one fresh Claude Sonnet session with no prior context. `Cutscene_Roadmap.md` §4 is
> the execution protocol (read it first; substitute `AL-Tn:` for the commit prefix). Subagents may
> take only tasks marked **[parallel-safe]** and never touch `mcp__UnityMCP__*`.
> **Sibling spec:** [`RagdollTuning_System.md`](RagdollTuning_System.md) (RG). Independent — either
> can run first. Both edit `NewRig.asset`; if they run in the same day, run AL first and let RG
> re-read the rig, or expect a trivial merge on the rig's YAML.
> **Why it exists:** the migration spec decided a six-layer rig (`AnimationToolkitMigration_System.md`
> §4) and every game call site already casts `AnimationToolkitLayer.Action/Face/Eyes/Mouth` to a layer
> index — but `NewRig.asset` still declares **one** layer, so those commands target a slot that does
> not exist. Worse, the unit SOs' clip fields are stale integers from the deleted `AnimationType`
> enum, so the assignment system issues **no** clips at all and `MaleCitizen` walks in place forever
> off a starting-layer seed. This spec adds the five layers, the content that plays on them, and the
> SO wiring that lets the game drive Base and Action itself.

---

**Skills Needed:** `dots-test` (two PlayMode fixtures for T1), otherwise none — this is rig/clip/SO
authoring plus one small system fix.

---

## 1. Purpose & scope

Make `MaleCitizen` (and `Rotter`, which instances the same prefab) animate the way the code already
expects: an **idle** clip when standing, the existing **walk** when moving, a **punch** swing on the
Action layer when a melee behaviour fires (with the `Attack` event so damage lands on the swing, not
on the timeout), and a **blink** cycling on the Eyes layer. Face and Mouth get their slots declared so
the layer indices mean what `AnimationToolkitLayer` says; they get no clips (see §2 — the legacy
content on them was empty).

**In scope:** five new `LayerDefinition`s on `NewRig.asset`; four new rig targets for the face parts;
`Idle.asset`, `Attack.asset`, `Blink.asset`; two `DirectionSetAsset`s; `MaleCitizen.asset` and
`Rotter.asset` clip fields; the `startingLayers` seed on `MaleCitizen.prefab`; the assignment-job
fallback fix; re-pointing cutscene requests onto the Override layer.

**Out of scope:** `PlayerUnit`/`BaseUnit` (they instance a *different* copy of the body-part tree and
are not toolkit actors — `project_first_toolkit_actor` memory; the player's own swing therefore stays
invisible until a separate wiring pass); mouth/talk flaps (no art-driven trigger exists yet); per-eye-
style blink variants (§6 D6); sound event markers on clips (the `_AnimSoundEventMapping` table is a
separate, tiny follow-up); ragdoll bodies (RG).

---

## 2. What exists today (verified 2026-09-07 — re-verify before trusting)

| Thing | State |
|---|---|
| `Assets/ScriptableObjects/Animations/NewRig.asset` | 16 `Quad` targets (G0-T1's list), **`layers: [ Base ]` only**, 1 socket (`RightHand`), 1 billboard root, `ragdollBodies: []`. `sourcePrefab = MaleCitizen.prefab`. |
| `Assets/_Scripts/Data/Enums/AnimationToolkitLayer.cs` | `Base=0 Action=1 Override=2 Face=3 Eyes=4 Mouth=5`. Every game write site casts this to `byte`: `UnitAnimationAssignmentJob` (Base + Action), `Utils/BehaviorCommands/AnimationCommands.cs` (Action), `PlayerAttackSystem` (Action), `AttackRequestSystem` (reads `AnimEventOutput.layerIndex == Action`). |
| `Assets/Prefabs/Units/MaleCitizen.prefab` | `ActorAuthoring { rig = NewRig, clipSets = [ NewClipSet ], startingLayers = [ { layerIndex 0, clip Walk, loop 0 } ] }`. This seed is the **only** reason anything animates. Parts `Eyes`, `Mouth`, `LeftEyeBrow`, `RightEyeBrow` exist under `Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead/` but carry no `RigTargetAuthoring` (G0 decision D3). |
| `Assets/ScriptableObjects/Units/MaleCitizen.asset`, `Rotter.asset` | **Stale.** `idleAnimation: 1`, `movingAnimation: 2`, `actionAnimations: [ {Wander, 1}, {Interact, 2}, {MeleeContinuous, 37} ]`, `stanceAnimations: [ {Normal, 0, 0} ]`. Those integers are `AnimationType` ordinals from before 2026-08-29; the fields are now `DirectionSetAsset` object references and deserialize as **null**, so `UnitLibraryBakingSystem` bakes invalid `ClipId`s and every `PlaybackApi.Play` site checks `IsValid` and does nothing. `Player.asset` is stale the same way (irrelevant here — not an actor). |
| `Assets/ScriptableObjects/Animations/MaleCitizenWalkDirections.asset` | A `DirectionSetAsset` with `southEast = Walk`, `targetDirections = Two`. Referenced **only** by `A65CheckpointCutscene.asset`. This is the walk set the unit SO should point at. |
| `Walk.asset` | The G0 walk: 1.0 s, `Loop`, 16 tag-bound `Override` tracks, keys are **offsets from rest** in degrees on `rotation.z`. Read G0's §7 T5 entry for the three authoring facts about this rig before writing a single key. |
| `UnitAnimationAssignmentJob` (`Systems/AnimationSystemGroup/AnimationAssignmentSystemGroup/`) | Base branch is correct. Action branch: when `unitAction.current != Idle` and the Action layer is inactive, `GetAnimationForAction` **falls back to the moving/idle clip** when no mapping exists and plays it on Action as `Once` — so it re-issues the base clip on the Action layer every time it finishes. `UnitAction.current` is written **only** by `DeathSystem` (→ `Death`) and `ReviveRequestSystem` (→ `Idle`), so in practice this branch fires only for corpses; but with a real Idle clip wired it will visibly restart the idle every cycle on a dead unit under the ragdoll. |
| NPC attack animation path | `MeleeContinuousBehaviour.asset` = `Approach → PlayActionAnimation → RequestAttack → WaitTime → LoopUntil → StopAnimation`. `AnimationCommands.RunPlayActionAnimation` resolves `actionAnimations[stateMachine.action]` (via `AIUtils.GetAnimationByAction`, which already returns `default` for an unmapped action) and plays it on **Action**, `Once`. `AttackRequestSystem` lands damage the frame `AnimEvents.Attack` (`0x12`, generated in `Assets/Generated/DotsAnimationToolkit/AnimEvents.cs`) is emitted on the Action layer, else at `AttackBlob.hitTime` (Punch: 0.3 s) with a Combat-category warning. |
| Cutscene clip-block layer | Per request: `CutsceneRequest.layerIndex` (`Components/Cutscene/CutsceneComponents.cs`), written by `NarrativeEventManager` from `PlayCutsceneAction.layer` and by `CutsceneDebugTrigger`. On a one-layer rig these necessarily say `0`. Cutscene blocks and Base assignment must not share a layer once Base has real content (§6 D4). |
| Legacy clips (git, `43530db7^:Assets/ScriptableObjects/Animations/…`) | `Action/Punch`, `Action/MaleDeath`, `Mouth/Nuteral` had **no pose keys** — nothing to port. `Base/Idle`: 6 s loop of small sways (body ±4°, arms 1–2°, hands ~11–19°). `Eyes/*Blink*` ×12: 5 s loops; eye sprite frames stepped at 0/.02/.04/.06/.08 and mirrored at .94–1 (Human: `53→11→9→7→1`, Zombie: `47→47→45→43→37`), plus a ±0.01 y-bob on both eyebrows. The closing frames are **not** a fixed offset from the open frame across styles. |
| Eye art | `Assets/Textures/Units/Arrays/EyeArray.png` — `Texture2DArray`, 8×8 flipbook = 64 slices, on `Materials/Units/MaleEyes.mat`. `MouthArray.png` likewise. |
| Sprite-track semantics (`ClipAsset.cs`) | `SpriteSliceSpace.Absolute`: keys name the slice, **`-1` = leave the current frame alone**. `RelativeToRest`: offsets from `TargetRestPose.restSliceIndex` (which `DesignApplyUtil` writes per design roll). |

---

## 3. The target

```
NewRig.asset      layers: [ Base(active) | Action | Override | Face | Eyes(active) | Mouth ]
                  targets: 16 body targets + Eyes, Mouth, LeftEyeBrow, RightEyeBrow (tags likewise)
                                  |
MaleCitizen.prefab   RigTargetAuthoring on the 4 face parts
                     ActorAuthoring.startingLayers = [ 0 → Idle (Loop), 4 → Blink (Loop) ]
                                  |
NewClipSet.asset  clips: [ NewClip, NewClip 1, Walk, Idle, Attack, Blink ]
                                  |
MaleCitizen.asset / Rotter.asset
                  idleAnimation   = MaleCitizenIdleDirections   { southEast = Idle }
                  movingAnimation = MaleCitizenWalkDirections   { southEast = Walk }   (exists)
                  actionAnimations = [ { MeleeContinuous, MaleCitizenAttackDirections { southEast = Attack } } ]
                  stanceAnimations = []
                                  |
runtime           UnitAnimationAssignmentJob: Base ← Idle/Walk on Movement.isMoving
                  MeleeContinuousBehaviour → PlayActionAnimation → Attack on layer 1, Once,
                      emits AnimEvents.Attack @ ~0.2 s → AttackRequestSystem lands damage
                  Blink loops on layer 4 from the seed; cutscenes play on layer 2
```

**Acceptance:** in `TestArea.unity` (Play), a standing citizen sways on Idle and blinks every ~5 s; a
walking citizen plays Walk with the blink continuing on top; a rotter converted with the
`DebugZombifyMenu` punches a citizen with its arms while its legs keep whatever Base is doing, and
the Console shows **no** "Attack event never arrived" warning; a cutscene checkpoint (F9 in
`CutsceneG1Checkpoint.unity`) still plays its walk blocks.

---

## 4. Read first (in this order, and only these)

1. Repo root `CLAUDE.md`; `Assets/_Vault/Memories/Code/RULES.md`; `Gotchas.md` §"The cutscene
   layer must exist on the rig" and §"DOTS Animation Toolkit — authoring a clip from code".
2. `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6. Skip §4.
3. `Assets/_Vault/Tasks/NewPlans/ActorContentRebuild_System.md` **§7 only** — the T3/T5 build-log
   entries are the recipe for editing this rig and authoring a clip against it from `execute_code`
   (fully-qualified names, no `using`, `TargetKind` lives in `DotsAnimationToolkit` not
   `.Authoring`, keys are rest offsets, `rotation` not `rotationZ`, Z is the only swing axis).
4. `Assets/_Vault/Memories/Code/Systems_Animation.md` — the game↔toolkit seam and the layer convention.
5. `Packages/com.dotsanimationtoolkit/Authoring/Assets/RigAsset.cs` — `LayerDefinition`
   (`displayName`, `defaultActive`), `RigTargetDefinition`, `EnsureStableIds`.
6. `Packages/com.dotsanimationtoolkit/Authoring/Assets/ClipAsset.cs` — `TransformTrack`
   (`tagId`, `channels`, `blendOp`, `keys`), `TransformKey`, `SpriteTrack` (`tagId`, `sliceSpace`,
   `baseIndex`, `keys`), `SpriteKey` (`sliceIndex`, `indexMode`), `EventMarker` (`normalizedTime`,
   `eventKey`, `windowSeconds`).
7. `Packages/com.dotsanimationtoolkit/Authoring/Assets/DirectionSetAsset.cs` — `SetSlot`,
   `targetDirections`, `TryGetEffectiveDirections`.
8. `Assets/_Scripts/Systems/AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitAnimationAssignmentSystem.cs`,
   `Assets/_Scripts/Utils/BehaviorCommands/AnimationCommands.cs`,
   `Assets/_Scripts/Systems/CombatSystemGroup/CombatExecutionSystemGroup/AttackRequestSystem.cs` lines 100–150.
9. `Assets/_Scripts/Tests/PlayMode/BehaviorExecutionSystemTests.cs` — the World-fixture template
   for T1's tests.

---

## 5. Tasks

Work in order. After each: save → compile gate → run only the fixtures the task names → tick the
box → commit that task alone (`AL-Tn: <what>`, stage paths explicitly, never `git add -A`).
Asset-only tasks still get a compile gate (a rebake happens on Play) and a `git status` check.

- [ ] **T0 — Re-verify §2.** Open the rig, the prefab's `ActorAuthoring`, and `MaleCitizen.asset` in
  the inspector (the SO's four clip fields must read **None**). Enter Play in `TestArea.unity` and
  read a citizen's `PlaybackLayer` buffer length (expect 1) and layer 0's clip (expect Walk, even
  while it stands still). Record in §7. *Gate: the recorded numbers.*

- [ ] **T1 — Assignment never replays the base clip on the Action layer, and Action is untouched
  for an unmapped action.** In `UnitAnimationAssignmentJob`, make `GetAnimationForAction` return
  `default` when no `actionAnimations` mapping matches (delete the moving/idle fallback; mirror
  `AIUtils.GetAnimationByAction`). Keep the Base branch exactly as it is.
  *Fixture:* `Assets/_Scripts/Tests/PlayMode/UnitAnimationAssignmentSystemTests.cs`, one World, one
  entity with `AnimationCommand` buffer, `AnimationCommandPending` (disabled), `PlaybackLayer` ×2
  (both inactive, `clipIndex = -1`), `UnitData`, `Movement { isMoving = false }`, `UnitAction
  { current = ActionType.Death }`, `LocomotionStance { Normal }`, `UnitFacing`, and a
  `UnitDataLibrary` blob whose one unit has an `idleAnimation` set (build it the way
  `DirectionSetBlobFoldTests` builds a `DirectionSetBlob`) and **no** action mappings. Run the system
  once. Assert exactly one `AnimationCommand` was issued and its `layerIndex == 0` — the old code
  issues a second command on layer 1. Second test: same world, with a `MeleeContinuous` mapping and
  `unitAction.current = MeleeContinuous` → a command on layer 1 with `loop == LoopMode.Once`. Revert
  the fix and watch the first test fail before keeping it (roadmap §4 rule 4).
  *Gate:* the two tests + `StitchPunk.Tests.PlayMode` group.

- [ ] **T2 — Declare the six layers on `NewRig.asset`.** Append `LayerDefinition`s in this exact
  order after `Base`: `Action`, `Override`, `Face`, `Eyes`, `Mouth`. `defaultActive`: `Base` and
  `Eyes` true (both get a seed in T7), the other four false. Use the rig inspector's **Layers**
  list (`RigAssetEditor`), or `execute_code` appending to `rig.layers` + `EditorUtility.SetDirty` +
  `AssetDatabase.SaveAssets`. Layer identity is **list position** — never reorder later.
  *Gate:* reload from disk: `layers.Count == 6`, names in order; Play → a citizen's `PlaybackLayer`
  buffer has length 6. Then `read_console`: the "clip block silently dropped / layer does not exist"
  class of warning must be gone from the G1 checkpoint (see T3).

- [ ] **T3 — Cutscenes play on the Override layer.** Find every producer of
  `CutsceneRequest.layerIndex` (grep `layerIndex` under `Assets/_Scripts/MonoBehaviours/` and
  `Assets/_Scripts/Systems/CutsceneSystemGroup/`, plus the `PlayCutsceneAction.layer` field on the
  narrative SOs under `Assets/ScriptableObjects/Narrative/`) and make the default
  `AnimationToolkitLayer.Override`; re-point any asset that stores `0`. Reason: T7 puts Idle/Walk on
  Base through assignment, which keeps running for a cutscene actor because mark-walking NPCs need
  Base = Walk; a clip block on layer 0 would then be fought every frame. Override composites above
  Base and wins every channel it keys (`LayerCompositionTests.CompositeLayers_UpperOverride_WinsContestedChannelsBottomUp`).
  *Gate:* fire the G1 checkpoint from `execute_code` at `speed = 0.05` (G0-T6's recipe) and assert
  the bound minion's `PlaybackLayer[2]` holds the Walk clip while `PlaybackLayer[0]` holds whatever
  assignment chose. No new fixture — this is data + a default.

- [ ] **T4 — Add the four face targets to the rig and the prefab.** [parallel-safe with T5/T6 —
  **but T5/T6 author against tags this task mints, so mint the tags first and hand the four
  (name, tagId) pairs to the subagents in their prompt**.] Add `RigTargetDefinition`s for
  `Visual/MaleUnitVisual/Pelvis/Torso/Neck/BaseHead/{Eyes, Mouth, LeftEyeBrow, RightEyeBrow}`
  (`kind = Quad`, `boundsExtents` from `|mesh.center| + mesh.extents` about the pivot exactly as G0-T5
  did, `facesDirection` false), tags `Eyes`, `Mouth`, `LeftEyebrow`, `RightEyebrow` minted through
  `VocabularyRegistryProvider` (the registry already spells the existing rows `{Upper|Lower}{Side}{Limb}`;
  these four are new rows), `EnsureStableIds()` **after** the list is populated, regenerate
  `Assets/Generated/DotsAnimationToolkit/TargetTags.cs` the way G0-T3 did. Add a `RigTargetAuthoring`
  to each of the four parts on `MaleCitizen.prefab` (`targetStableId` set, `rig` left null,
  `restSliceIndex` = the slice the part's material currently shows — read `_ImageIndex` off the
  renderer's material in Play, or 0 if the design system overwrites it anyway; note which in §7).
  Add the two eyebrow targets to a mirror pair.
  *Gate:* `ClipValidation.ValidateRig` empty; Play → `RigPartRef` buffer length **20**; no
  `ActorBakeFailed`-class error (G0-T4's note: assert the `ClipRegistry` + `RigPartRef` pair, the tag
  itself is `[BakingType]`).

- [ ] **T5 — Author `Idle.asset`.** [parallel-safe] Create through `ClipAssetUtility.CreateClipInSet(NewClipSet)`
  + `RenameClip`, then fill: `duration` 4.0 s, `defaultLoop = Loop`, `frameRate` 30, `defaultBlendIn`
  0.25 s, `defaultBlendOut` 0.25 s. Tag-bound `Override` tracks, `channels = Rotation` unless noted,
  keys at 0 / .5 / 1 (first repeated last), `EaseInOut`: `Torso` ±2°, `Neck` ∓1°, `Head` ±1.5°
  (counter-rotating so the head stays level), `UpperLeftArm`/`UpperRightArm` ±2° in phase with the
  torso, `LowerLeftArm`/`LowerRightArm` ±3°, `Pelvis` a 1 cm dip on `PositionXY` at t=.5. Legs and
  feet get **no** tracks (they fall through to rest). Create
  `Assets/ScriptableObjects/Animations/MaleCitizenIdleDirections.asset` (`DirectionSetAsset`,
  `SetSlot(SouthEast, Idle)`, `targetDirections = Two` to match the walk set).
  *Gate:* `ValidateClip(Idle)` clean; a scratch Play sample two frames apart shows `Torso` rotation
  changing. Delete nothing — the clip is the deliverable.

- [ ] **T6 — Author `Attack.asset` with its `Attack` event.** [parallel-safe] Same creation path.
  `duration` 0.5 s, `defaultLoop = Once`, `frameRate` 30, `defaultBlendIn` 0.05, `defaultBlendOut`
  0.1. Tracks only on `UpperRightArm`, `LowerRightArm`, `RightHand`, `Torso`, `UpperLeftArm`
  (the counter-arm) — legs, pelvis, neck, head untouched so Base keeps them. Shape, in rest-offset
  degrees on `rotation.z` (signs verified in the Clip Editor: the walk's leg swing shows which sign
  swings forward on this rig): wind-up to t=.2 (upper arm −40°, lower arm −60° "cocked", torso −8°),
  strike at t=.35 (upper arm +70°, lower arm +10°, torso +10°), recover to 0 at t=1. `Linear` into
  the strike, `EaseOut` after. One `EventMarker { normalizedTime = 0.35, eventKey = AnimEvents.Attack
  (0x12), windowSeconds = 0 }` — read the key from the generated constants, never type the number.
  Create `MaleCitizenAttackDirections.asset` (`SetSlot(SouthEast, Attack)`, `Two`).
  *Gate:* `ValidateClip(Attack)` clean; the event is at 0.175 s < `Punch.asset`'s `hitTime` 0.3 s,
  so the event decides and the timeout never fires.

- [ ] **T7 — Author `Blink.asset` and seed the Eyes layer.** Depends on T4. `duration` 5.0 s,
  `defaultLoop = Loop`, `frameRate` 30. One `SpriteTrack` on tag `Eyes`, `sliceSpace = Absolute`,
  keys `indexMode = Absolute`: `-1` at 0, then the closing frames at .02/.04/.06/.08, the open frames
  back at .94/.96/.98 and `-1` at 1 — `-1` means "leave the current frame", so the open eye is
  whatever the design rolled and the clip works for every eye style that shares closing frames.
  **Pick the closing slices by eye:** open the clip in the Clip Editor on `MaleCitizen`, read the
  Eyes part's live `_ImageIndex` (the open frame), then scrub candidate indices in the sprite key
  field until a plausible half-closed → closed sequence shows (`EyeArray.png` is an 8×8 grid; the
  legacy Human set used `11, 9, 7, 1` and Zombie `45, 43, 37` — start there). Record the chosen
  indices in §7. Add two `Override` `PositionXY` tracks on `LeftEyebrow`/`RightEyebrow`: y −0.005 at
  0, +0.01 at .5, −0.005 at 1 (the legacy bob). Then on `MaleCitizen.prefab`'s `ActorAuthoring`:
  `startingLayers = [ { 0, Idle, loop Loop }, { 4, Blink, loop Loop } ]` — Walk is **removed** from
  the seed; assignment owns Base from T8 on.
  *Gate:* Play → `PlaybackLayer[4].clip == Blink` and, sampled twice ~0.1 s apart around the loop
  seam, the Eyes part's `_ImageIndex` differs; `PlaybackLayer[0].clip == Idle` at frame 1.

- [ ] **T8 — Wire the unit SOs.** On `MaleCitizen.asset` **and** `Rotter.asset`: `idleAnimation =
  MaleCitizenIdleDirections`, `movingAnimation = MaleCitizenWalkDirections`, `actionAnimations =
  [ { MeleeContinuous, MaleCitizenAttackDirections } ]` (delete the stale `Wander`/`Interact` rows
  — those actions have no swing and, after T1, an unmapped action leaves the Action layer alone),
  `stanceAnimations = []`, `animationDirections` left as is. Optionally set the validate-only `rig`
  / `clipSet` fields so `UnitSO.ValidateRigAndClipSet` can warn. Re-bake (`_UnitLibrary` is a
  PostBaking blob — re-enter Play).
  *Gate:* Play in `TestArea.unity`: a standing citizen's `PlaybackLayer[0].clip == Idle`; order it to
  move (click-move a selected minion, or wait for Wander) → `== Walk` while `Movement.isMoving`, back
  to Idle when it stops. `read_console` shows no `DirectionSetBakeUtil` warning for these two SOs.

- [ ] **T9 — Prove the punch.** Play `TestArea.unity`, use `DebugZombifyMenu` to convert one citizen
  into a rotter next to another citizen, and watch the Console + world: `PlaybackLayer[1].clip ==
  Attack` during the swing, `AnimEventOutput` on the attacker carries `AnimEvents.Attack` with
  `layerIndex 1` on the strike frame, the victim's `Health.healthAmount` drops, and **no**
  "Attack event never arrived … falling back to hitTime" line appears. Then run both full game
  suites (`StitchPunk.Tests`, `StitchPunk.Tests.PlayMode`) and the toolkit EditMode suite once;
  discovered totals must not drop (HANDOFF §3).

- [ ] **⏸ T10 — Owner checkpoint.** Stop and hand over. The owner opens `TestArea.unity`, presses
  Play, and looks for: (1) idle sway on standing citizens, (2) blinks every ~5 s that keep going
  while walking, (3) walk when moving with no hitch at the idle↔walk crossfade, (4) a rotter's punch
  swinging the right arm with the legs untouched, (5) F9 in `CutsceneG1Checkpoint.unity` still walks
  the minions. Anything that reads wrong is tuning feedback on the keys, not a re-decision of §6.

---

## 6. Decisions

**Delegated, already made — do not re-litigate:**

- **D1. Six layers in `AnimationToolkitLayer` order, added in place on `NewRig.asset`.** The enum
  and every call site already assume it; the rig catches up. `defaultActive` true only where a seed
  exists (Base, Eyes).
- **D2. Assignment owns Base; the seed is only frame-1 insurance.** `startingLayers` seeds Idle on 0
  so a freshly spawned actor is never in rest pose, and `UnitAnimationAssignmentJob` switches to
  Walk/Idle from `Movement.isMoving`. Walk leaves the seed.
- **D3. Unmapped action → the Action layer is left alone.** Replaying the base clip on Action was
  never intended; it only survived because no clip content existed to show it.
- **D4. Cutscene clip blocks play on Override (2), and assignment is *not* gated on
  `CutsceneActor`.** A64 marks walk NPCs through `MovementAPI`, so they need Base = Walk from
  assignment while the cutscene runs; the cutscene's keyed channels win through compositing
  instead of through a gate.
- **D5. The Attack clip keys arms + torso only**, so it composites over whatever Base is doing.
  That is the whole reason the Action layer exists; a full-body swing would just be a second Base.
- **D6. One style-agnostic blink using Absolute keys with the `-1` sentinel for the open frame.**
  The legacy 12-clip set was per eye style × mood; closing frames are not a constant offset from the
  open frame, so `RelativeToRest` cannot express them, and per-style clips would need design-driven
  clip selection the game does not have. If the chosen closing frames read wrong on a non-Human eye
  style, that is the follow-up (per-style blink sets + a `PartDefinitionSO`-keyed pick), not this spec.
- **D7. Face and Mouth get slots and no clips.** Legacy `Mouth/Nuteral` and `Face/BlinkNormal`
  carried no keys. Mouth flaps belong to a dialogue-driven trigger that does not exist yet.
- **D8. Stale `Wander`/`Interact` action rows are deleted, not re-pointed.** They mapped to the base
  idle/walk enum values — a leftover of the legacy fallback, never real content.

**Owner calls — ask, do not assume:**

- Nothing blocks. The idle/attack/blink *feel* is T10 feedback.

---

## 7. Open questions / build log

- *(fill per task; the T0 numbers, the four tag ids from T4, the blink slice indices from T7, and
  every `CutsceneRequest.layerIndex` producer found in T3 go here.)*
- **Known follow-ups this spec deliberately leaves:** `PlayerUnit`/`BaseUnit` are not actors, so the
  player's own swing (`PlayerAttackSystem`) stays invisible; `_AnimSoundEventMapping.asset` still
  maps a stale `eventKey: 1` — a `Sound` marker on the swing and a footstep marker on the walk are
  a ten-minute follow-up once this lands; `NewClip.asset`/`NewClip 1.asset` are still stubs logging
  rule-T6 warnings per bind (G0 §7).
