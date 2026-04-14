---
title: Test Combat AI System End-to-End
status: active
created: 2026-04-13
area: code
---

## Goal

Validate the full combat AI pipeline after the modular Behavior SO system was built. Confirm feral zombie detects, pursues, and attacks hostile targets — and that threat scoring correctly biases re-targeting toward attackers.

## Steps

### Asset setup
- [ ] Create a `ChaseBehaviorSO` asset (`AI/Behaviors/Chase`) — set `detectionRadius=10`, `retargetIntervalSeconds=1.5`, `hostileFactions=[Player, Human]`
- [ ] Create a `MeleeAttackBehaviorSO` asset (`AI/Behaviors/MeleeAttack`) — set `attackRange=1.5`, `attackCooldownSeconds=1`, `damageAmount=10`, `attackType=Claw`
- [ ] Create a `FeralZombieConfig` `BrainConfigSO` asset (`AI/BrainConfig`) — set `behaviors=[ChaseBehavior, MeleeAttackBehavior]`

### Scene setup
- [ ] Place a feral zombie prefab in a test scene
  - Add `FactionAuthoring` → `FactionType.Undead`
  - Add `BrainConfigAuthoring` → `FeralZombieConfig`
  - Confirm it has standard body components: `Health`, `Hurt` buffer, `PathRequest`, `PathfindingAgent`
- [ ] Place the player — add `FactionAuthoring` → `FactionType.Player`
- [ ] Place a human NPC — add `FactionAuthoring` → `FactionType.Human`
- [ ] Confirm both player and NPC also have `Hurt` buffer (added automatically by `FactionAuthoring`)

### Detection test
- [ ] Play the scene — zombie should detect and begin chasing the nearest target within 10 units
- [ ] Move out of range — zombie should stop chasing (CombatTarget disables on retarget timer expiry)
- [ ] Move back in range — zombie should resume chase

### Attack test
- [ ] Walk into attack range (1.5u) — zombie should attack and the target should take damage
- [ ] Confirm `DamageApplicationSystem` still applies damage correctly (Health decreases)
- [ ] Confirm the attack respects the cooldown (no instant-repeat hits)

### Threat / retargeting test
- [ ] Have the player deal damage to the zombie while it's chasing the NPC
- [ ] Zombie should switch priority to the player (threat score accumulates)
- [ ] Kill the player — zombie should retarget the NPC

### Regression check
- [ ] Existing citizen NPCs in the same scene are unaffected — no `ChaseConfig`, so new systems skip them entirely
- [ ] No errors from `ThreatUpdateSystem` interfering with `DamageApplicationSystem`

## Notes

Key files if something breaks:
- `ChaseTargetingSystem.cs` — checks `FactionRegistry` + threat scoring; ensure `FactionRegistrySystem` ran first (same frame, earlier group)
- `MeleeAttackSystem.cs` — ECB `AppendToBuffer` writes `Hurt`; check ECB playback isn't double-applying
- `ThreatUpdateSystem.cs` — must run `[UpdateBefore(typeof(DamageApplicationSystem))]` in `CombatReactionSystemGroup`
- `FactionRegistrySystem.cs` — skips entities where `Dead` is enabled; check if test entities have `Dead` baked (if not, they're always included)

`FactionType` bitmask: `None=bit0, Player=bit1, Human=bit2, Undead=bit3, Neutral=bit4`. A mask of `6` = hostile to Player + Human.
