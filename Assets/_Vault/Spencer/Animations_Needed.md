---
title: Animations Needed
status: running list
created: 2026-10-20
area: art
---

# Animations Needed — running list

Every animation the game asks for, grouped by who performs it. This is a running list: when a system starts asking for a new animation, add a row here in the same commit. When you finish a clip, tick it and write the date.

## How a clip gets picked up (read once)

- **The name is the binding.** The clip's name in the actor profile must match the name in the **Name** column exactly (`AnimationNameConvention`, G5 D1). A name that doesn't match bakes key 0, and the unit stays in idle. `UnitLibraryBakingSystem` prints **one warning per unit** listing every missing name, which makes it the fastest checklist.
- **Locomotion names:** `Idle`, `Walk`, plus `<Stance>Idle` / `<Stance>Walk` for the stances `Defensive` and `Running`. `RunningWalk` is the run cycle.
- **Action names** are the `ActionType` enum names, played by `PlayActionAnimation`, so one behaviour serves every unit (a zombie's `MeleeSingle` is a claw, a citizen's is a punch).
- **Event markers** (on the clip timeline) are what some behaviours wait for: `Attack` (the hit lands), `Damage`, `Sound`, `Dialogue`. A behaviour waiting on a missing event finishes on its timeout, so a missing marker means wrong timing, never a hang.
- **Right now no unit plays any of this.** MaleCitizen's actor profile was deleted in c07dfd75, so every unit is unanimated, and the existing clips below aren't bound to anyone. The first job is a profile for the human rig with Idle + Walk.

Priority: **P1** = the core loop reads wrong without it · **P2** = clearly better with it · **P3** = polish.
Loop: **L** = loops · **1** = plays once.

## Clips that already exist (`Assets/ScriptableObjects/Animations/`)

| Clip | Notes |
|---|---|
| `Idle`, `Idle_EastFacing` | Not bound to any unit profile yet |
| `Walk`, `Walk_EastFacing` | Same |
| `MeleeContinuous`, `MeleeContinuous_EastFacing` | Same |
| `Blink`, `DeathFace` | Face layer |
| `NewClip`, `NewClip 1` | Look like scratch clips; delete or rename |

---

## Humans: citizens + bandits (shared human rig)

### Locomotion
| ✓ | Name | P | Loop | Used by / notes |
|---|---|---|---|---|
| ☐ | `Idle` | P1 | L | Everything when standing |
| ☐ | `Walk` | P1 | L | Wander, Approach, patrol |
| ☐ | `RunningWalk` (run) | P1 | L | Flee, charging a target |
| ☐ | `RunningIdle` | P3 | L | Out-of-breath stop after a run |
| ☐ | `DefensiveIdle` | P2 | L | Combat stance, guards on alert |
| ☐ | `DefensiveWalk` | P2 | L | Strafing / closing in during a fight |

### Combat
| ✓ | Name | P | Loop | Used by / notes |
|---|---|---|---|---|
| ☐ | `MeleeSingle` | P1 | 1 | Punch (citizen), bandit swing. **Needs an `Attack` event** at the moment of impact |
| ☐ | `MeleeContinuous` | P1 | L | Sustained flurry; `Attack` event on each hit |
| ☐ | `Throw` | P1 | 1 | Rock throw (ThrowBehaviour). It waits 0.35 s for release today; a `Release` event would need registering in AnimEvents first |
| ☐ | `EquipWeapon` | P2 | 1 | Bend and grab an item off the ground (Pickup) |
| ☐ | `UseHealingItem` | P2 | 1 | Bandage / medkit on self |
| ☐ | `Flee` | P2 | L | Panicked run, arms up (a variant of the run is fine) |
| ☐ | Hit react (small) | P2 | 1 | Flinch on a non-lethal hit. **Needs a system hook**, nothing requests it yet |
| ☐ | `Death` | P3 | 1 | Death currently drops straight into the physics ragdoll; only needed if you want a pre-ragdoll beat |
| ☐ | `Shoot` / `ProjectileSingle` | P2 | 1 | Guns (SmartHumanAI H5, not built yet) |

### Camp life + needs
| ✓ | Name | P | Loop | Used by / notes |
|---|---|---|---|---|
| ☐ | `Sit` | P1 | L | SitBehaviour at chairs/campfire (sit-down + seated loop) |
| ☐ | `Talk` | P1 | L | Conversation gesturing; the emotes carry meaning, this sells it |
| ☐ | `EatFromSupplies` | P2 | L | Eating at the supply crate |
| ☐ | `Eat` | P2 | 1 | Eating a held food item (Bread) |
| ☐ | `Drink` | P2 | 1 | Water |
| ☐ | `ListenToMusic` | P2 | L | Idle sway by the gramophone |
| ☐ | `Patrol` | P3 | L | Alert walk for guards (falls back to Walk) |
| ☐ | `Sleep` | P3 | L | Declared action, no behaviour yet |
| ☐ | `Smoke` | P3 | L | Declared action, no behaviour yet; good ambient filler |
| ☐ | `Bathroom` | P3 | 1 | Declared; Bathroom interaction exists |
| ☐ | `Repair` / `Build` | P3 | L | Declared for humans; no behaviour yet |

### Living Humans (2026-10-20 spec, being built)
| ✓ | Name | P | Loop | Used by / notes |
|---|---|---|---|---|
| ☐ | `DrinkAlcohol` | P1 | 1 | Swig at the bar (DrinkAlcoholBehaviour) |
| ☐ | Tipsy idle / walk | P2 | L | Drunk sway. The code wobbles the path today; a drunk walk sells it more. Would bind as a stance (`DrunkIdle`/`DrunkWalk`) and **needs a `Drunk` stance added** |
| ☐ | `Brawl` | P1 | L | Sloppy drunk punches (BrawlBehaviour loops `PlayActionAnimation` + `RequestAttack`; `Attack` event per punch) |
| ☐ | `PassOut` | P1 | 1+L | Collapse, then lie on the ground. Today the unit stands still with a zzz |
| ☐ | Talk variants | P3 | 1 | Laugh, argue (angry pointing), shrug. Would need a small hook to pick a variant from the emote |

---

## Player — necro engineer

| ✓ | Name | P | Loop | Used by / notes |
|---|---|---|---|---|
| ☐ | `Idle`, `Walk`, `RunningWalk` | P1 | L | PlayerMoveSystem |
| ☐ | Dodge roll | P1 | 1 | PlayerRollStartSystem. Code-driven squash/spin stands in today (`PlayerRollVisualSystem`); a clip should last ~0.34s to match `rollDuration` |
| ☐ | Melee attack | P1 | 1 | PlayerAttackSystem (`Attack` event) |
| ☐ | `Throw` | P1 | 1 | Player item throw (ThrownItemSystemGroup) |
| ☐ | Aim idle / aim walk | P2 | L | PlayerAimSystem |
| ☐ | Revive / raise corpse | P1 | 1 | PlayerReviverSystem: the signature necromancy move |
| ☐ | Knocked out + get up | P2 | 1 | PlayerKnockoutSystem |
| ☐ | Mount / dismount workstation | P2 | 1 | WorkstationMountSystem |
| ☐ | Deploy / pack up workstation | P2 | 1 | WorkstationDeploySystem / PackUpSystem |
| ☐ | Craft / tinker (at workstation) | P2 | L | WorkstationCraftSystem, Repair |
| ☐ | Summon gesture | P2 | 1 | WorkstationSummonSystem |
| ☐ | Pick up / equip | P2 | 1 | PlayerEquipmentInputSystem |
| ☐ | Use healing item | P2 | 1 | HealRequestSystem |
| ☐ | Command point / order | P3 | 1 | Minion orders (OnFootCommandRing) |
| ☐ | Talk gestures | P3 | L | Dialogue / narrative events (`Dialogue` event marker) |

## Zombies: PlayerZombie, Runner, Brute, Rotter

Zombies are melee only and cheap, so a short list. Each variant can reuse the base zombie rig with its own timing.

| ✓ | Name | P | Loop | Used by / notes |
|---|---|---|---|---|
| ☐ | `Idle` | P1 | L | Shamble-in-place |
| ☐ | `Walk` | P1 | L | Shamble (Runner: a fast lurch; Brute: heavy stomp) |
| ☐ | `RunningWalk` | P2 | L | Runner sprint |
| ☐ | `MeleeSingle` | P1 | 1 | Claw / slam, with an `Attack` event |
| ☐ | `MeleeContinuous` | P2 | L | Gnawing / repeated claws |
| ☐ | `Resurrection` | P1 | 1 | Rising from a corpse (ZombifySystem, revive). Huge for feel |
| ☐ | `Spawn` | P2 | 1 | Summoned out of the workstation |
| ☐ | Follow / order acknowledge | P3 | 1 | Minion move orders |

## Props / buildings

| ✓ | Name | P | Loop | Used by / notes |
|---|---|---|---|---|
| ☐ | Workstation deploy / pack | P2 | 1 | WorkstationDeploySystem |
| ☐ | Furnace burning | P2 | L | WorkstationFurnaceSystem |
| ☐ | Summon chamber | P2 | 1 | WorkstationSummonSystem |
| ☐ | Workstation destroyed | P3 | 1 | WorkstationDestroyedSystem |
| ☐ | Gramophone spin | P3 | L | ListenToMusic spot |
| ☐ | Campfire flicker | P3 | L | Camp |

## Faces (face layer, every human)

| ✓ | Name | P | Loop | Notes |
|---|---|---|---|---|
| ☑ | `Blink` | — | L | Exists |
| ☑ | `DeathFace` | — | 1 | Exists |
| ☐ | Talk mouth | P2 | L | During `Talk` |
| ☐ | Angry / happy / scared faces | P3 | L | Could follow the emote above the head (Living Humans) |
| ☐ | Drunk face | P3 | L | Intoxication > 0.3 |

---

## Not animation, but on the same art track
- **Emote icons:** `Assets/Art/CrowdLife/EmoteAtlas.png` is a generated placeholder (4×4, 256 px cells, icon order = `EmoteIcon` enum: Happy, Laugh, Heart, Angry, Sad, Question, Exclaim, Idea, Sweat, Music, Sleep, Drunk, Drink). Keep the same layout when replacing it.
- **Ragdolls are physics, not clips.** Death and knockback come from the toolkit ragdoll, so they need colliders on the rig, not animation.
