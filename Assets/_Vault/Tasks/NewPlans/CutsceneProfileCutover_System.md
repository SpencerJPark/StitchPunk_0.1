# Cutscene Profile Cutover — Design Spec (G6)

> **Status:** ✅ spec written 2026-09-08, nothing built. Depends on toolkit **A73**
> (`Docs/AnimationToolkit/Amendment_A73_ProfileDrivenCutscenes_Spec.md`, both sessions).
> **Executor:** one fresh Claude Sonnet session; `Cutscene_Roadmap.md` §4 protocol, commit prefix
> `G6-Pn:`. Subagents only on **[parallel-safe]** tasks, never MCP.
> **Why it exists:** A73 removes `CutscenePlay.layerIndex` / `CutsceneApi.TopLayer` and re-shapes
> `CutsceneSlot` and `CutsceneClipBlock`, so the game does not compile and none of its seven cutscene
> assets play until they are re-pointed at `MaleCitizen.profile`. The same pass gates the game's own
> idle/walk assignment while a cutscene owns an actor, so the cutscene's auto locomotion is the only
> writer on Base.

---

**Skills Needed:** `dots-test` (one PlayMode fixture, one EditMode parity fixture).

---

## 1. Purpose & scope

End state: the game compiles against 0.19.0; `CutsceneRequest` carries `cutsceneKey` + `speed`
only; `UnitAnimationAssignmentJob` does nothing for a unit whose `CutsceneActor` is enabled;
`UnitFacingJob` and the toolkit agree on the `Direction` they both write into `ActorFacing`; all
seven `CutsceneAsset`s reference `MaleCitizen.profile` on their Actor slots with blocks by name and
locomotion defaulted; `MaleCitizenContentAuthoring` no longer writes a `directionSet`; the F9
acceptance cutscene shows minions *walking* (Walk cycling, turning) to their marks instead of sliding.

**Out of scope:** the player becoming a toolkit actor (memory `project_first_toolkit_actor`); new
cutscene content beyond re-pointing what exists; the RG ragdoll checkpoints.

## 2. What exists today (verified 2026-09-08 — re-verify; A73 will have moved names)

| Thing | State |
|---|---|
| `CutsceneRequest { cutsceneKey, layerIndex, speed }` (`Assets/_Scripts/Components/Cutscene/CutsceneComponents.cs` 13) | `layerIndex` written by `NarrativeEventManager.ExecutePlayCutsceneAsync` (~412, `CutsceneApi.TopLayer`) and `CutsceneDebugTrigger`; read by `CutsceneStartSystem` (72) into `CreatePlayRequestFromStage`. |
| Game tests naming `TopLayer` / `layerIndex` | `Tests/PlayMode/CutsceneSystemTests.cs` (81), `CutsceneDialogueCueTests.cs` (88), `CutsceneAcceptancePerfTests.cs` (60). |
| `UnitAnimationAssignmentJob` (`Systems/AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitAnimationAssignmentSystem.cs`) | Issues `PlayAnimation(idle/walk)` on `movement.isMoving` change, **not gated on `CutsceneActor`** — during a root-lane slide `isMoving` is false, so it would re-issue Idle against the cutscene's Walk every frame. |
| `UnitFacingJob` (`UnitFacingSystem.cs` 44–125) | Reads `CutsceneFacing.angleDegrees` → `ResolveMovementXY` → `desiredFacing` at the profile's `turnDirections` → writes `UnitFacing.current` and `ActorFacing.facing` (117–124). After A73 the toolkit writes `ActorFacing.facing` from the same angle; the two must snap identically. |
| Cutscene assets (7) | `Assets/ScriptableObjects/Animations/{G1,A63,A64,A65,G2}CheckpointCutscene.asset`, `NewCutscene.asset`, `Assets/ScriptableObjects/Cutscenes/RendezvousAndDepart.asset` (G3 acceptance). Every Actor slot pins `rig`/`clipSets`, blocks name `Walk.asset`'s clip id, A65's slot 0 has a `directionSet`. |
| `Assets/_Scripts/Editor/ContentAuthoring/MaleCitizenContentAuthoring.cs` | Line ~459–463 loads `A65CheckpointCutscene.asset` and writes `slots[0].directionSet = walkDirectionSet` — a field A73 deletes. Also still references `MaleCitizenWalkDirections` (G5 said re-author into the profile and delete the asset). |
| `MaleCitizen.profile.asset` | Base: `Idle`, `Walk` (directional); Action: `MeleeContinuous`, `Death`, `Resurrection`; Eyes: `Blink`, …; Face; Mouth; Override. The registry names `Idle` and `Walk` exist, so A73's "Defaults from profile" fills locomotion. |
| `CutsceneStartSystem` / `CutsceneEndSystem` | Enable/disable `CutsceneActor` on every bound unit (G1). The gate P2 adds keys off exactly this flag. |

## 3. Read first

1. Repo `CLAUDE.md`, `RULES.md`, `Contracts.md` rows `CutsceneRequest`, `CutsceneMoveToMark`, `CutsceneFacing`.
2. `Docs/AnimationToolkit/Amendment_A73_ProfileDrivenCutscenes_Spec.md` §3.1–§3.3 and its §7 build
   log; `Documentation~/cutscenes.md` "Recipes" (written by A73-T8).
3. The files in §2.

## 4. Tasks

After each: compile gate → the task's fixtures → tick → commit `G6-Pn: <what>`.

- [x] **P1 — Compile again.** Delete `CutsceneRequest.layerIndex`; `NarrativeEventManager`,
  `CutsceneDebugTrigger`, `CutsceneStartSystem` and the three tests drop the argument
  (`CreatePlayRequestFromStage(entityManager, stageEntity, request.speed)`). Remove the
  `directionSet` write and every `MaleCitizenWalkDirections` reference from
  `MaleCitizenContentAuthoring.cs`; delete that `.asset` if it still exists and nothing else
  references it (grep `.asset` files for its guid first). *Gate:* both game assemblies compile;
  `StitchPunk.Tests` / `.PlayMode` discovered counts unchanged from the G5 close.
  **Done 2026-09-08:** the argument-dropping (`NarrativeEventManager`, `CutsceneDebugTrigger`
  [never constructed the field directly — routes through `NarrativeEventManager`],
  `CutsceneStartSystem`, the three PlayMode tests) was already committed by A73's own closing
  session (`9095bf91`) as an unplanned compile prerequisite — verified by grep, not redone. This
  session's actual work: deleted `CutsceneRequest.layerIndex` (`CutsceneComponents.cs`, now
  `{ cutsceneKey; speed; }` only); deleted the `WalkDirectionSetPath`/`OldWalkDirectionSetPath`
  consts and the whole direction-set-authoring block (old lines ~443–470, including the
  `cutsceneAsset.slots[0].directionSet` write and the now-orphaned `CutscenePath` const) from
  `MaleCitizenContentAuthoring.cs`; deleted `MaleCitizenWalkDirectionSet.asset` via
  `AssetDatabase.DeleteAsset` (its only other referrer was `A65CheckpointCutscene.asset`'s own
  `directionSet` field, unread by the builder per A73's T1 deviation note and about to be
  re-pointed by P4 anyway — confirmed via guid grep before deleting;
  `MaleCitizenWalkDirections.asset`, the *old* path, no longer existed — an earlier run of this
  same authoring script had already deleted it). *Gate:* clean compile
  (`refresh_unity` → `read_console` zero errors); `StitchPunk.Tests` 57/57, `.PlayMode` 15/15 —
  both counts match the A73 baseline exactly.

- [x] **P2 — Gate assignment on `CutsceneActor`.** `UnitAnimationAssignmentJob` adds
  `[WithPresent(typeof(CutsceneActor))]` and an `EnabledRefRO<CutsceneActor> cutsceneActorEnabled`
  parameter; return early when enabled. The cutscene's own locomotion (A73 §3.3) is Base's single
  writer while it runs; `CutsceneEndSystem` disabling the flag hands Base back, and the job's
  existing `IsAnimationPlaying` check means no pop on hand-back. *Fixture (PlayMode,
  `UnitAnimationAssignmentSystemTests`):* `CutsceneActor_Enabled_IssuesNoCommand` — a standing unit
  with an inactive Base and `CutsceneActor` enabled gets zero commands (fails on the ungated job).
  **Done 2026-09-08:** built exactly as specced. Fixture proven — reverted the early-return, watched
  it fail (`Expected: 0 But was: 1`), restored it. *Gate:* clean compile;
  `StitchPunk.Tests.PlayMode` 16/16 (15 baseline + 1 new), all green.

- [x] **P3 — Facing parity.** [parallel-safe with P2] The toolkit now snaps
  `CutsceneFacing.angleDegrees` with `FacingResolver.FromMovement((cos θ, sin θ), turnDirections, current)`;
  `UnitFacingJob` snaps the same angle through `ResolveMovementXY` + its own fold. *Fixture (EditMode,
  `FacingSpaceTests`):* `CutsceneAngle_SnapsToTheSameDirection_AsTheToolkit` — for θ in
  {0, 45, 90, 135, 180, 225, 270, 315} and `Six`/`Four`/`Two`, the game's pure snap equals
  `FacingResolver.FromMovement`. If they disagree anywhere, the **game** changes to call the
  toolkit's resolver directly (one writer's fold, not two), and the fixture stays as the guard.
  Then delete the game's `ActorFacing` write at `UnitFacingSystem.cs` 117–124 **only if** the
  fixture proves the values always agree *and* every unit that turns is always cutscene-driven — it
  is not (aim/movement facing outside cutscenes), so expect to keep it; record the reasoning in §6.
  **Done 2026-09-08:** found `UnitFacingJob` already calls `FacingResolver.FromMovement` directly
  (no separate game-side fold to reconcile — this landed in an earlier pass, predating A73) and
  already writes `ActorFacing.facing` at lines 117–124 using that same `desiredFacing` value, so
  G6-D2's write already existed; P3's job was to add the parity guard, not build the write. Added
  `CutsceneAngle_SnapsToTheSameDirection_AsTheToolkit` to `FacingSpaceTests.cs`: for each θ it
  independently reconstructs the toolkit's `(cos θ, sin θ)` vector (does **not** call through
  `UnitFacingJob.CutsceneAngleToFacingSpace` for the "toolkit" side — routing both sides through the
  same helper would pass even if that helper's own convention drifted) and asserts
  `FacingResolver.FromMovement` snaps it identically to what `ResolveMovementXY` feeds it, across
  Six/Four/Two. First draft of the fixture routed both sides through the same helper and stayed
  green even after deliberately swapping cos/sin in `CutsceneAngleToFacingSpace` — caught before
  committing by the "prove it can fail" step, not after; rewrote the toolkit side to compute its
  vector independently, reproved the swap now fails (6 of 8 angles wrong under `Six`), restored the
  correct convention, reconfirmed green. Not deleting the `ActorFacing` write — the fixture proves
  the two folds already agree, but aim-facing and non-cutscene movement facing are real callers with
  no cutscene involved, so the second condition for deletion is false, exactly as anticipated. *Gate:*
  clean compile; `StitchPunk.Tests` 65/65 (57 baseline + 8 new `TestCase` angles), all green.

- [x] **P4 — Re-author the seven cutscene assets by script.** [parallel-safe with P2/P3 — writes
  assets only] New re-runnable `Assets/_Scripts/Editor/ContentAuthoring/CutsceneProfileReauthoring.cs`
  (menu `Stitch Punk/Content/Re-point Cutscenes at MaleCitizen Profile`): for each asset, every
  Actor slot gets `profile = MaleCitizen.profile`, locomotion defaulted (`Idle`/`Walk` keys through
  `VocabularyRegistryProvider.AnimationNames`, `enabled = true`), and each block's old clip id is
  mapped to the profile entry that names that clip (walk clip → `Walk`; an unmappable id is logged
  with the asset name and left as key 0 so the Unresolved row shows it). `RendezvousAndDepart`'s
  **Player slot gets `locomotion.enabled = false`** — the player is not a toolkit actor and walks by
  hand (G2 §3.4). `EnsureStableIds()`, `SetDirty`, save. Run it; commit the assets. *Gate:* opening
  each asset in the Cutscene Editor shows layer rows and named blocks, no Unresolved row except
  where the log said so.
  **Done 2026-09-08:** before writing the script, grepped every asset's raw YAML for the literal
  `clipId:` line (still present on disk pre-migration, since Unity's deserializer already drops it
  once a type-changed field is loaded into memory — confirmed A64CheckpointCutscene.asset had
  already lost it to `animationKey: 0` from an earlier compile-gate touch this session). Every clip
  block in every one of the seven assets carried the identical id `17929205651740358465`, which
  belongs to `Walk.asset` (grepped its owning stableId) — there was only ever one clip authored
  anywhere in this content, so the "mapping" is one entry, not a table. Wrote the script against
  that finding rather than trying to read the dead field at runtime (impossible — the C# type no
  longer has it).
  `RendezvousAndDepart`'s and `G2CheckpointCutscene`'s `Player` slots are already authored as
  **Prop**, not Actor (`kind: 1`) — consistent with "the player is not a toolkit actor," but meaning
  the spec's literal "Player slot gets `locomotion.enabled = false`" line is a no-op today
  (locomotion is documented "Ignored for Prop slots"). Applied it anyway, gated only on slot name +
  asset path, so it costs nothing now and holds if the slot's kind ever changes.
  `NewCutscene.asset` has zero Actor slots (`Prop 1`/`Prop 2` only) — correctly left untouched
  (no dirty, no resave) rather than forced into the diff.
  Ran the menu item, then re-ran it directly via `execute_code` to confirm idempotency (`slots
  repointed=9, blocks resolved=0` the second time — the first run had already resolved every
  block). *Gate:* clean compile; `StitchPunk.Tests` 65/65 unchanged; built
  `CutsceneBlobBuilder.Build` for all seven assets via `execute_code` — zero animation-key/profile
  warnings on any of them (the only warnings present, on `G2CheckpointCutscene`/`RendezvousAndDepart`,
  are the pre-existing, unrelated "mark walks through a rendezvous hold mid-editor-rehearsal" notes
  that the builder itself says "plays correctly" at runtime).

- [ ] **P5 — `CutsceneMoveToMarkSystem` and locomotion agree.** The mark system paths NPCs through
  `MovementAPI` (isMoving true) while the cutscene measures displacement; no code change expected —
  verify in Play that a marked minion shows Walk cycling and turns toward its mark, then Idle
  latched to the mark's arrival facing. If the minion arrives but keeps playing Walk, the threshold
  (0.05 m/s) is being crossed by settle jitter: raise the slot's threshold in the asset, not the
  default in the package.

- [ ] **P6 — Vault + docs.** `Contracts.md`: `ActorFacing` row gains its second writer
  ("`CutsceneTimelineSystem`, for bound Actor slots while a cutscene runs — same fold as
  `UnitFacingJob`, values agree by the P3 fixture"); `CutsceneRequest` row drops `layerIndex`;
  `Systems_Animation.md` (or the note that owns `UnitAnimationAssignmentSystem`) records the
  `CutsceneActor` gate; `Gotchas.md` gains "a cutscene's locomotion owns Base until
  `CutsceneEndSystem` clears `CutsceneActor`". `Tasks/Plans/README.md` status line.

- [ ] **⏸ P7 — Owner checkpoint.** `TestArea.unity`, Play, F9: the two minions **walk** to their
  marks beside the cart — Walk cycling, facing the direction of travel, turning through the
  profile's six directions — stand in Idle facing the cart, board, the dialogue holds, the cart
  drives off, everyone detaches on the ground and AI resumes with the citizens idling. Then
  `DOTSTestScene` (F9) for G2's beat. Two things to judge by eye that no test can: whether the
  turn-while-walking pops (a re-pick swaps clips in place with no crossfade by A70 §3.4) and whether
  the arrival latch faces the way the mark's disc tick points.

## 5. Decisions

- **G6-D1 The assignment job is gated, not deleted.** Outside cutscenes it is the only idle/walk
  writer; inside, the cutscene is. One flag already exists for "this actor is puppeted".
- **G6-D2 The game keeps its `ActorFacing` write** beside the toolkit's, guarded by a parity fixture.
  Removing it would leave non-cutscene turning unwritten.
- **G6-D3 Assets are re-pointed by a script, not by hand**, so the pass is repeatable when A73's
  field names drift and so the mapping (clip id → animation name) is recorded in code.

## 6. Build log

(empty)
