# Prompt — Rethink the 2D Ragdoll System

> **For:** Fable 5 (planning / architecture pass — no code yet, produce a recommendation).
> **Deliverable:** a design recommendation I can turn into a `dots-task-creator` plan.
> **Repo:** Stitch Punk (Unity 6.5, DOTS — Entities 6.5, Physics 6.5, Burst; URP 2.5D). Read `CLAUDE.md` and `_Vault/Memories/Code/RULES.md` first.

## Your role

Act as a senior DOTS / physics engineer. I want you to **evaluate my current ragdoll approach, weigh it against a "real" physics ragdoll, and recommend a direction** — including an honest verdict on whether real ragdoll is even worth it here. Do **not** write implementation code. Ask me follow-up questions until you are ~95% confident before committing to a recommendation (this is a project rule).

## The world & the characters (the core tension)

- The world is **fully 3D**. Units can be launched/thrown in **any 3D direction** (e.g. a death attack with knockback).
- But a character is a **stack of flat 2D planes (quads)** layered on top of each other — a Frankenstein/paper-doll rig. It reads as a 2D sprite that happens to live in a 3D world.
- So the requirement is: **the body can be thrown through 3D space, but the visual ragdoll should only articulate/react on a 2D plane** (the character's facing plane). A limb bending "into" the screen looks wrong because the parts are coplanar quads.
- It must **collide with the ground, and with certain other physical things in the world** (not everything — a curated set).

## What exists today (the current "fake ragdoll")

It is **not** physical — it's scripted motion that *looks* like a ragdoll. Files:

- **Components** — `_Scripts/Components/Units/Ragdoll2DComponents.cs`
  - `Ragdoll2D` (visual child, enableable): tilts body Z toward `±MAX_TILT_DEG (88°) + tiltOffset`, direction from `fallSideSign`.
  - `Ragdoll2DJoint` (joint-pivot empties, enableable): lerps `currentZAngle → targetAngle` at `settleSpeed`. `targetAngle` is picked randomly from a landing zone.
  - `Ragdoll2DConfig` (root, static): fall speed, forward/backward ground buffers + tilt offsets.
  - `Ragdoll2DLaunch` (root, enableable): scripted arc — `velocityX/Y`, gravity `20`, sideways drag `2.5`, lands at `groundY`.
- **Driver** — `_Scripts/Systems/HealthSystemGroup/Ragdoll2DSystem.cs` (runs in `LateSimulationSystemGroup`). Also `Ragdoll2DInitSystem`, `Ragdoll2DReviveSystem`, `Ragdoll2DSpawnInitSystem`.
- **Authoring** — folded into `_Scripts/Authoring/Units/BodyPartAuthoring.cs`: the `isRagdollJoint` bool, `settleSpeedOverride`, `groundBufferOverride`. `CharacterRigBakingSystem` (`PostBakingSystemGroup`) stamps `Ragdoll2D` / `Ragdoll2DJoint` from the rig's `BodyPart` buffer (`BodyPartFlags.RagdollJoint`); landing zones + settle speed come from the `PartLibrary` blob via `PartDefId`.
- **Manager** — `_Scripts/MonoBehaviours/Managers/RagDollManager.cs`.

## What I want to KEEP (these already feel good)

1. **Parts fall away from the direction the death attack came from** (`fallSideSign` today).
2. **Each attack can define its own ragdoll response** — different death attacks set different fake-physics parameters (launch force, tilt, etc.). I want per-attack authored control to survive whatever we do.

## What I'm unsure about / want your take on

1. **Real vs fake.** I'd genuinely prefer a *real* ragdoll if it's tractable, but I'm skeptical it is here. The blocker: **in DOTS, colliders on a single baked entity hierarchy tend to bake together into one compound collider** — so I can't easily get per-limb physical bodies + joints that collide independently the way a classic ragdoll needs. Is there a clean DOTS-native way (separate physics entities per joint, joint constraints, a physics world query, custom integration) — or is a real ragdoll a bad trade for a stacked-quad 2D character? Give me the real engineering cost.
2. **2D physics in a 3D physics world.** If we do go real (or semi-real), how do we constrain articulation to the character's 2D plane while still letting the whole body be thrown in 3D? (Planar joint constraints? Simulate ragdoll in a local 2D space and only place the root in 3D? A custom 2-bone/verlet solver per limb instead of Unity Physics?)
3. **Colliding with ground + a curated set of world objects** without dragging in full per-limb compound-collider complexity. What's the minimum viable collision here — a single body-capsule vs ground, plus layer-filtered queries?
4. **Where should tuning live?** Right now ragdoll authoring is folded into `BodyPartAuthoring`. Is that the right home, or should ragdoll config split out (dedicated `RagdollJointAuthoring` empty-object component per joint, a `RagdollConfigSO`, per-attack data on the `AttackSO`)? I'm fine keeping tuning in authoring *for now* if that's pragmatic — tell me the tradeoff.

## Options I want you to compare (add your own)

- **A — Keep it fake, but clean it up.** Split authoring out of `BodyPartAuthoring`, formalize per-attack ragdoll profiles, improve the look. Lowest risk.
- **B — Semi-real / procedural 2D.** A lightweight custom per-limb solver (verlet / 2-bone) constrained to the 2D plane, driven each frame in a system — real-ish secondary motion and ground collision without Unity Physics joints.
- **C — Full real ragdoll** via Unity Physics per-limb bodies + joints, with planar constraints. Highest cost; tell me if it's even viable given the collider-baking problem.

## Output I want from you

1. A short read-back of the problem so I know we're aligned.
2. Any follow-up questions you need answered before recommending (ask before deciding).
3. A comparison of options A/B/C (+ any you add) across: fidelity/look, engineering cost, DOTS-fit, how well each preserves the two "keep" features, and how it handles 3D-throw-but-2D-articulation + curated collision.
4. **A recommended direction** with reasoning, and a rough phase breakdown (what a first slice looks like).
5. A recommendation on **where ragdoll tuning should live** and how per-attack control is authored.

Once we've settled the direction, the next step will be to formalize it with the `dots-task-creator` skill into `_Vault/Tasks/Plans/`.
