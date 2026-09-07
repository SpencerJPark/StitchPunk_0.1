# Ragdoll Bodies + Death Tuning — Design Spec (RG)

> **Status:** ✅ spec written 2026-09-07, nothing built. Delegated decisions in §6.
> **Executor:** one fresh Claude Sonnet session with no prior context. `Cutscene_Roadmap.md` §4 is
> the execution protocol (read it first; substitute `RG-Tn:` for the commit prefix). Subagents may
> take only tasks marked **[parallel-safe]** and never touch `mcp__UnityMCP__*`.
> **Sibling spec:** [`AnimationLayersContent_System.md`](AnimationLayersContent_System.md) (AL).
> Independent. Both edit `NewRig.asset` — if AL has landed, re-read the rig (it will have 20 targets
> and 6 layers); nothing here depends on that.
> **Why it exists:** the migration spec's Phase 5 (`AnimationToolkitMigration_System.md` §5) ported
> death onto the toolkit's `RagdollActor`/`RagdollLaunch` and is marked ✅ — but **no rig declares a
> single ragdoll body**, so `ActorBaker` never adds `RagdollActor` to any unit, `RagdollLaunchInitSystem`
> matches nothing, and a killed unit simply freezes. On top of that, nothing in the game adds the
> `RagdollLaunch` component the init system looks for, and the torque it writes is about an axis
> `Planar2D` freezes. This is authoring + two small fixes + a test-and-tune pass with the owner's eyes
> on the hinge limits and the launch.

---

**Skills Needed:** `dots-test` (one PlayMode fixture for T3), `dots-authoring-baker` (T1's one-line
baker addition). Everything else is rig authoring and Play-mode verification.

---

## 1. Purpose & scope

Give `NewRig.asset` a real ragdoll (eleven boxes on the trunk, head and limbs, `Planar2D`, no
self-collision in v1), make the death blow actually reach it, then watch deaths in `TestArea.unity`
and tune the hinge limits and launch numbers by eye until a kill reads as a fall.

**In scope:** `RagdollBodyDefinition`s on the rig; `RagdollLaunch` baked onto units; the torque-axis
and mass-scaling fixes in `RagdollLaunchInitSystem`; a Clip Editor preview pass; in-game drop /
sleep / revive / pool-reclaim verification; tuning the three attack SOs' launch values; correcting
the corpse-stacking claim in `CorpseCellSystem` and the vault.

**Out of scope:** `Spatial3D` (unfinished in the toolkit — `ragdoll.md`); a death *clip* (the ragdoll
is the death pose; legacy `MaleDeath` had no keys); corpse-on-corpse stacking (§2: it cannot happen
with this solver — record it, do not build it); wall collision (the physics probe casts along gravity
only, documented); `PlayerUnit` (not an actor); a `RagdollConfig` authoring component (the singleton's
defaults are fine — change one only if T5 proves it wrong, and say why in §7).

---

## 2. What exists today (verified 2026-09-07 — re-verify before trusting)

| Thing | State |
|---|---|
| `NewRig.asset` | `ragdollBodies: []`; `ragdollSettings { space Planar2D, gravityScale 1, defaultLinearDamping .05, defaultAngularDamping .05, jointStiffness 1, jointDamping .5, solverIterations 6, substepHz 120 }`. One billboard root (`Visual/MaleUnitVisual`, mode 4) — so `Planar2D` has a frame to fall in. 16 `Quad` targets; the eleven this spec bodies are in §3 with their stable ids. |
| `ActorBaker.AddRagdollBodies` (`Packages/com.dotsanimationtoolkit/Authoring/Baking/ActorBaker.cs`) | Resolves `rig.ragdollBodies` under the actor; **returns before adding anything when zero bodies resolve**. So today no unit has `RagdollActor`, `RagdollBody`, `RagdollRestPose` or `RagdollState`. |
| `RagdollLaunchInitSystem` (`Systems/HealthSystemGroup/`) | Queries `Dead` + `.WithPresent<RagdollActor>()`, builds `RagdollLaunch` from `Health.kill*`, enables `RagdollActor`. Two defects: (a) it only writes `RagdollLaunch` `if (ragdollLaunchLookup.HasComponent(entity))`, and **nothing in `Assets/_Scripts` ever adds `RagdollLaunch`** (`UnitBakingUtil`, `UnitAuthoring`, `SpawnStateInitSystem` all only *read* it) — every death is a plain collapse; (b) `worldTorque = (0, killSpin, 0)` is about world **up**, and `Planar2D` freezes every rotation axis except the billboard-plane normal, so `killSpin` does nothing. `worldImpulse` is `launchForce × ragdollForce` applied as an *impulse* to the root body (`RagdollSolver.ApplyLaunchImpulse`: `linearVelocity += impulse × invMass`), while `AttackSO`/`RagdollProfileSO` tooltips call the numbers "velocity (units/s)". |
| `RagdollCaptureSystem` (toolkit) | On the first frame `RagdollActor` is enabled: captures every body's pose, seeds the planar frame, and applies a pending `RagdollLaunch` **to body 0 (the root) only**, then disables the launch. It reads the launch by `HasComponent`, not by enabled state — a baked-disabled `RagdollLaunch` with zero fields applies a zero impulse, which is harmless. |
| `RagdollReviveSystem` | Disables `RagdollActor` once `Dead` is disabled; the toolkit restores the captured pose exactly. `SpawnStateInitSystem` disables both `RagdollActor` and `RagdollLaunch` on pool reclaim. |
| `CorpseCellSystem` (`Systems/GameManagerSystemGroup/`) | Rebuilds a per-cell registry of **sleeping** ragdoll roots each frame; no reader today. Its header says corpses "should stack correctly through actual body-vs-body collision" — **false**: ragdoll bodies are solved by the toolkit's own XPBD solver, are not `PhysicsCollider`s, and self-collision is per actor. Corpses pass through each other. Record, do not fix. |
| World collision | `DOTSTestScene.unity`'s `Ground` carries a legacy `BoxCollider` (y −0.53, scale 100×1×100) which Unity Physics bakes, and the movement cost map already depends on `PhysicsWorldSingleton`, so `RagdollPhysicsProbeSystem` (`Runtime.Physics`, `DOTS_ANIM_TOOLKIT_PHYSICS`) is the active contact provider: box-cast **along gravity**, `CollisionFilter.Default`. Units have no `PhysicsShape`, so a body cannot hit its own unit. `RagdollConfig.fallbackGroundHeight = 0` is only used if the probe is absent. |
| `RagdollConfig` singleton (toolkit `ConfigBootstrapSystem`) | `worldGravity (0,−9.81,0)`, `sleepLinearSpeed .05`, `sleepAngularSpeed .05`, `sleepDelaySeconds .5`, `maxSubstepsPerFrame 4`, `contactProbeRadius .02`. |
| Hinge semantics (`RagdollSolver.SolveLimitConstraintPlanar`) | Angle = the child's departure from `restRelativeRotation` (its orientation relative to its nearest ragdolled ancestor **in the authored rest pose**, measured by `RagdollBodyResolver` at bake) about the plane normal, clamped to `[limitMinDegrees, limitMaxDegrees]`. Defaults ±45°, "invented by an agent and never judged by eye" (HANDOFF §7). |
| Attack launch data | `Punch.asset`/`Claw.asset`: `ragdollForce 1.2, launchForceY 1, launchForceX 2`; `Swing.asset` same; `Slash.asset`: `ragdollForce 1, launchForceY 5, launchForceX 6`. `killSpin` comes from `RagdollProfileSO.spin` when a profile is assigned; none is. `DamageEventSystem` copies these into `Health.kill*` on the lethal hit. |
| Test scaffolding | No ragdoll fixture exists game-side. `Assets/_Scripts/Tests/PlayMode/BehaviorExecutionSystemTests.cs` is the World-fixture template. Toolkit fixtures `RagdollToggleTests`/`RagdollSleepTests`/`RagdollBakingTests` already pin capture/restore/sleep — do not duplicate them. |
| Ways to kill a unit in `TestArea.unity` | The player's melee (`PlayerAttackSystem`, attack input on a `CombatTarget`); a rotter converted from a citizen with the `DebugZombifyMenu` (`MonoBehaviours/`) punching a neighbour; or `execute_code` enqueueing a lethal `DamageEvent` on the `DamageBus` so `Health.kill*` is populated — **do not** just enable `Dead`, that skips the kill snapshot. |

---

## 3. The target

```
NewRig.asset.ragdollBodies (RigTarget addresses, buffer order = depth):
   Pelvis 1140299929 (root)
     ├─ Torso 264798881
     │    ├─ BaseHead 3088210805          (Neck rides the torso; nearest-ragdolled-ancestor rule)
     │    ├─ LeftUpperArm 1173584893 ─ LeftLowerArm 1630943582      (LeftHand rides)
     │    └─ RightUpperArm 1726161722 ─ RightLowerArm 2128489924    (RightHand rides)
     ├─ LeftUpperLeg 3452626627 ─ LeftLowerLeg 1711980931           (LeftFoot rides)
     └─ RightUpperLeg 2973879041 ─ RightLowerLeg 2283231638         (RightFoot rides)

MaleCitizen.prefab  ActorBaker → RagdollActor(disabled) + RagdollBody×11 + RagdollRestPose×11 + RagdollState
UnitBakingUtil      + RagdollLaunch (disabled)
death               DamageEventSystem → Health.kill* → DeathSystem → RagdollLaunchInitSystem
                    (impulse = velocity × rootMass, torque about the actor's plane normal)
                    → RagdollCaptureSystem → solve → sleep → CorpseCells
revive              ReviveRequestSystem → RagdollReviveSystem → pose restored
```

**Acceptance:** in `TestArea.unity`, a punched citizen drops as a jointed figure onto the ground,
knees and elbows bending one way only, comes to rest within about a second without jitter, keeps
that pose (no snap-back) until revived, and a reviving corpse stands back into the pose it fell
from. A `Slash` kill visibly throws the body away from the attacker.

---

## 4. Read first (in this order, and only these)

1. Repo root `CLAUDE.md`; `Assets/_Vault/Memories/Code/RULES.md`; `Gotchas.md` §"Ragdoll — Gotchas"
   and §"DOTS Animation Toolkit — ragdoll".
2. `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6, §7 (the ragdoll bullets), §8. Skip §4.
3. `Packages/com.dotsanimationtoolkit/Documentation~/ragdoll.md` — all of it; it is short and it
   is the contract. Then `Documentation~/clip-editor.md` §Ragdoll.
4. `Docs/AnimationToolkit/Phase_D_Ragdoll_Spec.md` **§3 and §9 only** (data model, traps G1/G2/G12).
5. `Packages/com.dotsanimationtoolkit/Authoring/Assets/RigAsset.cs` — `RagdollBodyDefinition`
   (`address`, `boxCenter`, `boxSize`, `boxEulerAngles`, `mass`, `linearDamping`, `angularDamping`,
   `restitution`, `friction`, `limitMinDegrees`, `limitMaxDegrees`, `selfGroup`, `selfCollidesWith`,
   `collidesWithWorld`), `RagdollRigSettings`, `RigNodeAddress { kind, targetId }`, `EnsureStableIds`.
6. `Packages/com.dotsanimationtoolkit/Runtime/Components/RagdollComponents.cs`;
   `Authoring/Baking/ActorBaker.cs` `AddRagdollBodies` + `BuildBodyParams`;
   `Runtime/Systems/RagdollCaptureSystem.cs` lines 140–170; `Runtime/Sampling/RagdollSolver.cs`
   `ApplyLaunchImpulse` and `SolveLimitConstraintPlanar`.
7. `Assets/_Scripts/Systems/HealthSystemGroup/RagdollLaunchInitSystem.cs`, `RagdollReviveSystem.cs`,
   `DeathSystem.cs`; `Assets/_Scripts/Utils/UnitBakingUtil.cs`;
   `Assets/_Scripts/Systems/GameManagerSystemGroup/CorpseCellSystem.cs`;
   `Assets/_Scripts/Systems/SpawnInitSystemGroup/SpawnStateInitSystem.cs` (the ragdoll lines).
8. `Assets/_Vault/Tasks/NewPlans/ActorContentRebuild_System.md` **§7 T3/T5 entries** — how to edit
   this rig from `execute_code` (CodeDom, fully-qualified names, `EnsureStableIds()` *after*
   populating) and the measured fact that every part is a flat quad pivoted at its joint with the art
   hanging below (`center.y ≈ −0.19` on a limb) — which is exactly where a ragdoll box must sit.

---

## 5. Tasks

Work in order. After each: save → compile gate → run only the fixtures the task names → tick the
box → commit that task alone (`RG-Tn: <what>`, stage paths explicitly, never `git add -A`).

- [ ] **T0 — Re-verify §2 in the live world.** Play `TestArea.unity`; from `execute_code` count
  entities with `RagdollActor` (expect 0) and with `RagdollLaunch` (expect 0), read
  `PhysicsWorldSingleton.PhysicsWorld.NumBodies` (expect > 0 — the ground), and record the ground's
  top surface world Y (box-cast down from a unit, or read the collider bounds). Confirm
  `DebugZombifyMenu` is in the scene. Record in §7. *Gate: the numbers.*

- [ ] **T1 — Bake `RagdollLaunch` onto every unit.** In `UnitBakingUtil` (the shared unit baker, next
  to the `CutsceneActor` add): `baker.AddComponent<RagdollLaunch>(entity);
  baker.SetComponentEnabled<RagdollLaunch>(entity, false);`. `ragdoll.md` says "not baked — add
  immediately before enabling"; that is advice for hosts without a pooled-unit archetype, and
  `SpawnStateInitSystem` already assumes the component is present to disable it. No fixture — baker
  wiring; *Gate:* Play → every `Unit` entity has `RagdollLaunch`, disabled.

- [ ] **T2 — Author the eleven bodies on `NewRig.asset`.** Prefer the Clip Editor: open the rig on
  `MaleCitizen`, select each §3 node, **Add Component → Ragdoll** (the window sizes the box from the
  node's renderer), then adjust in the component block. Fallback: `execute_code` building
  `RagdollBodyDefinition`s with `address.kind = RigTarget`, `address.targetId` from §3,
  `boxCenter = mesh.bounds.center` (the art hangs below the pivot — a box centred on the pivot
  covers half the limb), `boxSize = mesh.bounds.size` with **`z` clamped to 0.05** (a quad's z size
  is 0 and rule V-R4 rejects it), then `rig.EnsureStableIds()`. Per-body values (decisions D1–D3):

  | Body | mass | limitMin/Max (deg, from rest) | selfCollidesWith |
  |---|---|---|---|
  | Pelvis | 3.0 | root, ignored | 0 |
  | Torso | 4.0 | −20 / 20 | 0 |
  | BaseHead | 1.5 | −25 / 25 | 0 |
  | Upper arms | 1.0 | −120 / 120 | 0 |
  | Lower arms | 0.7 | one-way, ~120° on the elbow's bend side, 5° on the other (sign from T4) | 0 |
  | Upper legs | 1.5 | −70 / 70 | 0 |
  | Lower legs | 1.0 | one-way, ~110° on the knee's bend side, 5° on the other (sign from T4) | 0 |

  Damping left at −1 (inherit), `restitution 0`, `friction 0.5`, `collidesWithWorld true`,
  `ragdollSettings` unchanged.
  *Gate:* `ClipValidation.ValidateRig(rig)` returns no V-R rule; reload from disk and assert 11
  bodies with non-zero ids; Play → a citizen has `RagdollActor` (disabled), a `RagdollBody` buffer of
  length 11 whose element 0 is the Pelvis with `parentBodyIndex == −1` and whose lower-arm entries
  parent to their upper arm; `read_console` has no "matches no node under actor" error.

- [ ] **T3 — Make the launch reach the ragdoll the way the SOs describe.** In
  `RagdollLaunchInitSystem`: (a) torque about the actor's plane normal, not world up —
  `math.mul(transform.ValueRO.Rotation, math.forward())` (the root's +Z under Y-upright
  billboarding is the plane normal up to sign; the sign is a `killSpin` sign judged in T7); (b)
  scale the impulse by the root body's mass so `launchForceX/Y` really are velocities: read
  `RagdollBody[0].parameters.invMass` through a `BufferLookup<RagdollBody>` (`[ReadOnly]`) and use
  `worldImpulse = velocity / invMass` when `invMass > 0`. Keep `worldPoint = unitPosition`
  (the feet — the lever arm below the pelvis is what makes a horizontal blow tip the body; that is
  the tumble the legacy `spin` faked).
  *Fixture:* `Assets/_Scripts/Tests/PlayMode/RagdollLaunchInitSystemTests.cs` — one World, one
  entity with `Dead` (enabled), `Health { killSpin 1, killRagdollForce 1, killLaunchForceX 1,
  killLaunchForceY 0, killSourcePosition (−1,0,0) }`, `LocalTransform` rotated 90° about Y at the
  origin, `RagdollActor` (disabled), `RagdollLaunch` (disabled), a `RagdollBody` buffer with one
  element whose `parameters.invMass = 0.5`. Run the system once. Test 1: `RagdollLaunch.worldTorque`
  is parallel to `(1,0,0)` (the rotated forward), not `(0,1,0)`. Test 2: `worldImpulse.x == 2` (mass
  2 × velocity 1), not 1. Both fail on the old code; revert and watch before keeping.
  *Gate:* the two tests + `StitchPunk.Tests.PlayMode`.

- [ ] **T4 — Preview pass in the Clip Editor.** Open `MaleCitizen` on `NewRig`, put the playhead on
  a neutral pose (t = 0 of Idle if AL has landed, else the rest pose with no clip), toggle **Ragdoll**
  on, watch the drop on the preview ground; toggle off, confirm the pose restores. Learn the hinge
  sign here: bend a lower-arm limit to `[0, 120]` vs `[−120, 0]` and see which one lets the elbow
  fold the way the art does (G0's walk keys say knees and elbows "only ever bend one way" — the
  `Walk.asset` `LowerLeftLeg` key signs tell you which). Write the final one-way limits back into
  the rig. Iterate until the preview drop reads as a body, not a chain of boards. Record every
  limit you changed and why in §7. *Gate: the recorded table; no code, no fixture.*

- [ ] **T5 — In-game drop, settle, sleep.** Play `TestArea.unity`. Convert one citizen with
  `DebugZombifyMenu`, let it punch a neighbour dead. Through `execute_code`, across three samples
  ~0.5 s apart: `RagdollActor` enabled on the corpse; `RagdollLaunch` disabled again (consumed);
  root `RagdollBody.state.position.y` decreasing then constant; `RagdollState.flags` gains
  `Sleeping` within ~2 s of the last movement; the lowest body's world Y sits on T0's ground height
  (± half its box); a limb part's `LocalTransform` is **unchanged** between the last two samples
  (no snap-back — Gotcha G1). `CorpseCells.map.Count()` equals the number of settled corpses. Then
  kill a second citizen with the **player's** melee and confirm the same. Record the numbers.
  If the body falls through the floor: the probe is not running — check `PhysicsWorldSingleton`
  and the `DOTS_ANIM_TOOLKIT_PHYSICS` define before touching `fallbackGroundHeight`.

- [ ] **T6 — Revive and reclaim.** Enable `ReviveRequest` on a settled corpse from `execute_code`
  (`MaleCitizen` has `becomesUnitType = PlayerZombie`, so it is revivable). Assert `RagdollActor`
  disabled next frame and each ragdolled part's `LocalTransform` equal (float-exact) to the value
  sampled the frame before it died — the toolkit's own guarantee, checked once end-to-end through
  the game's systems. Then let a corpse despawn/pool (or force it through `SpawnStateInitSystem`'s
  path) and assert a re-spawned unit has `RagdollActor` disabled and the launch cleared.

- [ ] **T7 — Tune the launch numbers.** [parallel-safe for the SO edits only] With T3's mass
  scaling, `launchForceX/Y` are metres per second on the pelvis. Starting values to judge from:
  `Punch`/`Claw`/`Swing` `launchForceX 2.5, launchForceY 2, ragdollForce 1`; `Slash`
  `launchForceX 5, launchForceY 3.5, ragdollForce 1`. Add one `RagdollProfileSO` (`Units/Ragdoll
  Profile`, e.g. `RagdollProfile_HeavySwing`, `spin 180`) on `Slash.asset` so `killSpin` has a
  non-zero path to test the torque sign from T3. Re-bake (`_AttackLibrary` is a PostBaking blob).
  Watch each kind once. Record what each value looked like; leave the best guess in the assets for
  the owner's checkpoint.

- [ ] **T8 — Truth pass on docs.** [parallel-safe] Rewrite `CorpseCellSystem`'s header: the
  registry is a position map; the toolkit ragdoll has no cross-actor collision and corpses pass
  through each other, so any stacking needs a future reader of this map. Same correction to
  `Gotchas.md` §"Corpse-stacking landing-height hack was dropped" and to `Systems_Animation.md`'s
  Ragdoll bullet + "What's still pending" paragraph (bodies now exist). Mark
  `Tasks/Verification/verify-ragdoll2d.md` superseded by this spec at its top. Leave HANDOFF §7/§8's
  "never judged by eye" lines for the session that closes T9 — they are true until then.

- [ ] **T9 — Full suites**, once: `StitchPunk.Tests`, `StitchPunk.Tests.PlayMode`,
  `DotsAnimationToolkit.Tests.EditMode`, `.PlayMode`. Discovered totals must not drop.

- [ ] **⏸ T10 — Owner checkpoint.** Stop and hand over. The owner opens `TestArea.unity`, presses
  Play, converts a citizen with the debug menu and lets it kill a neighbour, then slashes one
  themselves. Look for: (1) the drop reads as a **fall**, not a collapse or a chain of planks;
  (2) knees and elbows bend one way and stop at a believable angle — hyperextension or a limb
  folding through the torso is a limit to change; (3) a `Slash` kill throws the corpse away from the
  attacker and tumbles it in the picture plane; (4) it settles in about a second with no growing
  bounce or jitter; (5) the corpse stays put while the camera orbits; (6) a revived corpse stands
  back into its old pose and walks off. Every "looks wrong" is a number in `NewRig.asset`'s
  `ragdollBodies` or the three attack SOs — write the owner's verdict per joint into §7, retune, and
  hand over again. When the owner signs off, update HANDOFF §7 (drop the ±45° line) and §8 (the
  ragdoll drop has now been judged), and retire this spec into `Tasks/Verification/verify-ragdoll.md`.

---

## 6. Decisions

**Delegated, already made — do not re-litigate:**

- **D1. Eleven bodies, not sixteen.** Hands and feet ride their lower limb, the neck rides the torso.
  Fewer joints settle faster and read cleaner on a cutout; a flopping hand box is the first thing
  that looks wrong on a paper doll. Add them later only if the owner asks for it at T10.
- **D2. Self-collision off in v1 (`selfCollidesWith = 0` everywhere).** Cutout limbs overlap the
  torso and each other by construction; every non-parent/child overlapping pair is "boxes fly apart
  on the first frame" (`ragdoll.md` §Common failures). Enabling left-vs-right limb groups is a T10
  follow-up if limbs visibly pass through each other while settling.
- **D3. One-way elbows and knees, wide hips and shoulders, tight spine and neck.** The table in T2
  is a starting point; T4 fixes the signs, T10 fixes the feel.
- **D4. `RagdollLaunch` is baked disabled on every unit.** The alternative — adding it on death
  through an ECB — is a structural change per kill and a second thing for pool reclaim to remove.
- **D5. `launchForceX/Y` are pelvis velocities**, so the SO tooltips become true and mass tuning on
  the rig does not silently retune every attack. Recorded as a reversal of Phase 5's raw-impulse
  reading; revert by deleting the `invMass` division in T3.
- **D6. Torque about the actor's forward, judged by sign at T7.** `Planar2D` only turns about the
  plane normal; a torque about world up is thrown away.
- **D7. No death clip.** The ragdoll is the death pose; AL-T1 makes the assignment system leave the
  Action layer alone for `Death`.
- **D8. Corpse stacking is recorded as impossible with this solver, not re-implemented.** The
  `CorpseCells` map stays for a future landing-height reader.

**Owner calls — ask, do not assume:**

- Whether the corpse should stay a solid obstacle to the living (it does not today and never did
  with Ragdoll2D either). Not blocking; note the owner's answer in §7 if it comes up at T10.

---

## 7. Open questions / build log

- *(fill per task: T0 counts and ground height; T2 box centres/sizes as authored; T4's per-joint
  limit table with the sign finding; T5's sample table; T7's launch values and how each looked;
  T10 owner verdicts per joint.)*
