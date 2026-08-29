---
tags: [task, claude, code, audit, roadmap, animation, ai]
related: "[[Code_Audit_2026-07]], [[Structural_Review_2026-07]], [[Memories/Code/Systems]], [[Memories/Code/Systems_AI]], [[Memories/Code/Systems_Animation]], [[Tasks/Plans/README]]"
created: 2026-08-29
status: active
---

# Systems Gap Audit — August 2026

Post-extraction pass over `Assets/_Scripts/Systems/`, written right after Movement moved into
`com.dotsmovementtoolkit` and the Animation Toolkit went gate-green (Phase F + A53–A56). Question
asked: **which systems need building out or restructuring, what benefits from packaging, and where
are the gaps** — each numbered area is a candidate for its own deep-dive/plan session later.

Facts verified against the tree this pass (not carried over from docs):

- **Zero references to `DotsAnimationToolkit` anywhere in `Assets/_Scripts/`** — no asmdef ref, no
  `using`. The game and the toolkit have never touched.
- `Assets/AnimationToolkitMigration/` (named as active in `Assets/CLAUDE.md`) **does not exist**.
- The legacy animation stack (9 systems in `AnimationSystemGroup/`) is still what the game runs.
- `AttackBlob.hitTime` is the only combat-timing source; nothing reads animation data for timing.
- Movement seam is clean: game owns only 3 files under `Systems/MovementSystemGroup/`
  (`MovementStuckBridgeSystem`, `LocomotionStanceSystem`, `PlayerFollowerSystem`) + `UnitSpeedBakingSystem`.

Predecessor docs still live: [[Code_Audit_2026-07]] items **2, 3 (partially), 6–13** and
[[Structural_Review_2026-07]] item 4 (feature-tag wiring) and item 7 (World tests) remain open.
This doc does not restate them except where the animation work changes their priority.

---

## 1. Animation Toolkit → in-game implementation (top of the list) — `L`, multi-session

The toolkit is a complete parallel animation runtime that nothing consumes. The game runs the
legacy SO→blob stack. Nearly every legacy system has a toolkit successor, so this is a
**migration, not an integration** — the end state deletes most of `AnimationSystemGroup/`.

| Legacy (game) | Toolkit successor | Note |
|---|---|---|
| `AnimationTimeSystem` | `PlaybackTimeSystem` | |
| `AnimationSamplingSystem` | `TransformSampleSystem` | |
| `ApplyAnimatedPoseSystem` | `TransformApplySystem` | ragdoll-stomp interaction must be re-decided (see 1c) |
| `UpdateImageIndexSystem` | `SpriteMaterialSystem` + `SpriteIndexResolver` | Design system writes tints/indices — seam decision (1d) |
| `BillboardSystem` | `BillboardResolveSystem` | toolkit's screen-aligned mode needs a host `_ToolkitCameraForward` writer — nothing writes it since the demo folder was deleted |
| `CameraVisibilitySystem` gating | `AnimLodDistanceSystem` / `AnimationLodPolicy` | keep game system for BodyPart propagation? or adopt LOD policy |
| `AnimationSoundMarkerSystem` | `EventEmissionSystem` → `AnimEventOutput` consumer | first real event consumer (see area 2) |
| `AnimationRequestSystem` + `SetAnimation` buffer + `AnimationUtils.SetLayer` | `AnimationCommandUtil` → `AnimationCommand` buffer | every play-animation call site changes |
| `Ragdoll2DSystem` + init/revive/spawn-init systems | `RagdollCapture/Solve/Apply/Release/ProbeFallback` | biggest keep-or-replace decision (1c) |
| `CharacterRigBakingSystem` / `BodyPart` registry | `RigBindingSystem` + `PartComponents` / `TargetId` tags | rig model overlap with the Design system (1d) |
| `UnitFaceDirectionSystem` + Direction layer | `FacingResolver` + `DirectionEnums` | collides with the Direction specs (area 3) |

Sub-decisions to work through in the deep-dive (each is a session topic):

- **1a. Bridge vs cut-over.** Recommend cut-over per rig archetype: pick one unit prefab, author it
  as a toolkit actor, run both stacks side by side in the sandbox scene, then convert the roster.
  A permanent bridge layer would double the maintenance surface for no shipped value.
- **1b. Command seam.** Call sites that push animations today: `BehaviorExecutionSystem`
  (PlayAnimation/PlayActionAnimation/StopAnimation arms), `UnitAnimationAssignmentSystem` (Base
  layer from locomotion), `PlayerAttackSystem`, death/revive paths. All route through
  `AIUtils.GetAnimationByAction` / `AnimationUtils.SetLayer`. Target: one thin game-side utility
  mapping `ActionType` → `ClipId` (generated `TargetTags`/clip constants — names, never numbers)
  that calls `AnimationCommandUtil`. Layer-model mapping (7 game layers → toolkit `PlaybackLayer`)
  is decided here.
- **1c. Ragdoll.** The game's Ragdoll2D (pendulum flail + corpse cells + kill-launch) is verified
  gameplay; the toolkit ragdoll (Phase D) is test-clean but never judged by eye, and its ±45°
  default hinge limits are known-suspect. Decide: adopt toolkit ragdoll (and port corpse-cell
  stacking + `Health.kill*` seeding into game-side glue), or keep Ragdoll2D and only migrate pose
  playback. Do not run both against the same transforms.
- **1d. Design-system seam.** `DesignApplySystem`/`DesignChangeSystem` write `ImageIndex` + 3 tint
  components consumed by the legacy MPB path. Toolkit has its own `MaterialPropertyComponents`.
  The design pipeline (PartLibrary/ColorPalette blobs, zombify palette swap) stays game code — it
  just needs to write whatever the toolkit's sprite material system reads.
- **1e. Prerequisites from the toolkit queue** (HANDOFF §4): re-point actors' Rig/Clip Sets by hand
  (Phase F shipped no migration), run the Clip Editor + rig domain-reload check, Samples~ compile
  check, owner visual passes. These come first — don't migrate the game onto an editor surface
  that hasn't been driven once.
- **1f. Doc cleanup at the end:** delete the `Core/Unused/` legacy animation files (8 of them),
  retire `Systems_Animation.md` content into a new note describing the game↔toolkit seam, fix the
  `Assets/CLAUDE.md` line pointing at the nonexistent migration folder.

## 2. StateMachine/AI — event-driven timing (the simplification you named) — `M`–`L`

Today every duration in the AI is a **hand-authored timer that must agree with an animation by
coincidence**: `AttackBlob.hitTime` (0.3s default), `PlayerAttackSystem`'s
`cooldown = max(cooldown, hitTime + 0.05)`, and every `WaitTime` command in a `BehaviorSO`
(Pickup's `WaitTime 1s`, Sit's `WaitTime 8s`) that exists only to cover a clip's length. The
toolkit replaces all of that with authored data: `AnimEventOutput` (typed key + int/float payload,
`AnimEventsPending` gate) and the reserved `ClipFinished` event.

Deep-dive topics:

- **New blocking commands** in the interpreter: `WaitForAnimEvent(eventKey)` and
  `WaitForClipFinished(layer)` — these replace most `WaitTime` uses and make behaviors
  self-synchronizing. `RequestAttack`'s windup timer in `AttackRequestSystem` becomes "swing clip
  plays → Hit event → enqueue DamageEvent"; `hitTime` leaves `AttackBlob` (or stays as a fallback
  for units with no clip).
- **Frame-order contract.** `AnimationSystemGroup` (and toolkit emission) runs *after*
  StateMachine/Combat in the sim order, so consumers see events **one frame late** — the toolkit
  documents this as its latency contract. Accept it (a 1-frame delay on a hit at 60fps is
  invisible) rather than reordering groups; write the decision into `Systems_AI.md` so nobody
  "fixes" it later.
- **Sequencing:** do `BehaviorCommandSplit_System.md` (spec ready — extract the interpreter's
  switch arms into `Utils/BehaviorCommands/`) **before** adding event arms. July audit #9 said
  "before ranged inflates it"; events inflate it the same way.
- **Downstream event consumers** the HANDOFF says the toolkit itself must not build — they are
  game-side systems in this area: sound (retires `AnimationSoundMarkerSystem`), damage/hit
  windows (`EventWindowSystem` already exists toolkit-side), ragdoll triggers, dialogue cues.
- **`UnitAnimationAssignmentSystem` shrinks or dies:** with the state machine issuing commands and
  clips reporting completion, per-frame inference of "which clip should be playing" is only needed
  for locomotion (idle/walk from velocity). Everything action-shaped moves into behavior commands.

## 3. Direction: two spec-ready plans now collide with the toolkit — decision `S`, do soon

`Direction_System.md` and `DirectionalTexturePacking_System.md` (both ✅ spec ready, unbuilt)
were written before the toolkit owned `DirectionEnums`, `FacingResolver`, and
`SpriteIndexResolver`. Re-audit both specs against the toolkit **before** authoring any more part
SOs or building either plan — the July audit already flagged direction as the decision that
re-authors every part asset if it lands late, and the toolkit may have already made half of it.

## 4. Further package candidates — recommendation: hold until area 1 lands

Judged by the two criteria that made Movement/Animation work (self-contained seam, reusable in any
DOTS game), plus churn risk:

| Candidate | Verdict | Why |
|---|---|---|
| **Utility AI + behavior interpreter** | Best third package — but **after** area 2 | Blob-baked considerations + SO-authored command sequences is genuinely sellable and mostly decoupled already (requests are the API). Extracting mid-rework would freeze the wrong API. |
| **Save (`IPersist` serializer)** | Good small candidate, any time | `PersistRegistry`/`SaveSerialization` are game-agnostic by construction; tiny surface, low churn. Could wait for the remap/multi-entity phases so the package ships complete. |
| **Sound (ECS-decides/Mono-plays)** | Plausible, not yet | Voice scoring + mood/music-state is reusable, but it's about to gain animation-event input (area 2). Same freeze risk. |
| **World services** (spatial hash, floating origin, registries) | No — fold pieces into existing packages if needed | Too entangled with game components (`Faction`, `NavigationWaypoint`); floating origin arguably belongs to the movement toolkit if anything. |
| **Ragdoll2D** | Not a package — it's decision 1c | Either it's replaced by the animation toolkit's ragdoll or it becomes a toolkit feature; a third standalone ragdoll package would compete with your own product. |

The general rule this table applies: **extract after the API stops moving, and Stitch Punk churns
its AI/sound APIs the moment animation events land** — so the only extraction safe to start today
is Save.

## 5. Restructuring / hygiene inside `Systems/` — `S` each, fill-in work

- **Feature-tag gating never got wired** (Structural Review item 4, still ◐): `FeatureTags.cs` +
  `FeatureConfigAuthoring` exist but no group does `RequireForUpdate<XFeature>`, and the
  per-system `GameSceneTag` boilerplate slated for stripping is still in place (e.g. the
  brand-new `AnimationRequestSystem` carries one). `FeatureIsolation_System.md` is spec-ready.
- **`BuildingsSystemGroup` is an empty declared group** — fine while the factory loop is parked,
  but it's the one group whose folder holds zero systems; the `FactoryMinimalLoop_System.md` spec
  is the un-parking plan when the slice needs it.
- **CleanupBatch 2026-07 rows 1/2/5/6 still open**: Thirst `NeedType`, EffectLibrary enum-index
  collision (two SOs silently overwrite one slot — the same bug class the bake validators were
  built for), `groundBufferOverride` unconsumed, `#region` rule decision.
- **Cutscene plan is half-stale**: `Cutscene_System.md`'s editor half is superseded by the toolkit
  Clip Editor; the `CutsceneSO` runtime (Phase 4) should be re-scoped as a toolkit *consumer*
  (Override-layer commands + events driving dialogue/camera) rather than built as spec'd.
- **Vault truth pass after area 1**: `Systems.md`'s animation section, `Systems_Animation.md`,
  `Gotchas.md` animation entries, and the `Assets/CLAUDE.md` "where the work is" lines all
  describe the legacy stack.

## 6. Build-queue gaps (spec ready, zero code) — unchanged priorities from July, re-sequenced

Animation migration (1) and events (2) jump the whole queue because everything below either
consumes them or is content-blocked by them. After that, July's order still holds:

1. **Despawn System** — prerequisite for ranged projectile pooling.
2. **Minion Order Robustness** — hardcoded `ActionType.MeleeSingle` breaks silently on the first
   ranged minion; prerequisite for ordering them.
3. **Zombie Conversion** — cheapest demo-defining win; both composed requests already exist.
4. **Ranged/Projectile Combat** — last unbuilt behavior-plan phase; lands *after* the interpreter
   split + events so `SpawnEntity` and hit timing are built once, the new way.
5. **Player Resource + Health UI**, then **Factory Minimal Loop**, **Schedules + Waypoints**,
   **Crowd-Scale Awareness** — as in [[Code_Audit_2026-07]] #10–13.

## 7. Verification debt — one consolidated play session clears most of it

Ten `verify-*.md` checklists sit open in `Tasks/Verification/`, the behavior-recreation phases
(P0–P3b) still await their single compile+rebake+play pass, and the movement toolkit's play-test
checklist (death/revive/pool-reclaim especially) is pending. Worth doing **before** area 1 starts
tearing into animation: it establishes the "known good" baseline the migration will be diffed
against, and several checklists (ragdoll, billboarding, camera visibility) cover exactly the
systems the migration replaces — last chance to verify them cheaply.

---

## Suggested deep-dive order

1. Verification baseline pass (area 7 — one Editor session, owner driving)
2. Toolkit queue remainder (1e) → then build area 1's spec: **drafted 2026-08-29 →
   [`../NewPlans/AnimationToolkitMigration_System.md`](../NewPlans/AnimationToolkitMigration_System.md)**
3. Area 2 spec: **drafted 2026-08-29 →
   [`../NewPlans/AnimationEventTiming_System.md`](../NewPlans/AnimationEventTiming_System.md)** — built after 2 + BehaviorCommandSplit
4. Direction re-audit (area 3): **done 2026-08-29 →
   [`../Plans/DirectionFacing_System.md`](../Plans/DirectionFacing_System.md)** — toolkit facing model
   adopted (slices for direction, `Six` default); both pre-toolkit specs deleted as superseded,
   channel packing re-scoped to intra-slice state variants (follow-up spec)
5. Everything else per areas 5/6
