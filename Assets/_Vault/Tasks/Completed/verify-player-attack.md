---
title: Verify Player Attack (melee swing → AttackRequest; separate CombatTarget; per-swing AttackCooldown)
status: active
created: 2026-07-01
area: code
---

## Goal

Confirm the player can melee-attack in `Assets/Scenes/TestArea/DOTSTestScene.unity` (or `Game.unity`). All **code** is committed: two new player components (`CombatTarget`, `AttackCooldown` in `PlayerComponents.cs`, baked disabled by `PlayerControllerAuthoring`), two new systems (`PlayerCombatTargetingSystem`, `PlayerAttackCooldownSystem`), and the revived `PlayerAttackSystem` — all in `PlayerSystemGroup/PlayerInputSystemGroup/`. The player writes an `AttackRequest` directly; the existing v2 combat pipeline (`AttackRequestSystem` → `DamageBus` → `DamageEventSystem`) applies the damage unchanged.

**Scope note:** this build is the *player-side melee only*. Who fights back is left entirely to the existing AI threat/awareness systems — no damage-side guard was added. Spec: [`PlayerAttack_System.md`](PlayerAttack_System.md).

The **compile gate was NOT run this session** (Unity MCP not connected) — the first step below is the real compile check.

## Steps

### Compile + import (first — gate everything on this)
- [ ] Re-enter the Unity Editor; confirm **no compile errors** (`error CS####`) and **no Burst errors** (`BC####`).
- [ ] Confirm **no duplicate-GUID warnings** — the new `.cs` `.meta` GUIDs were hand-generated (`PlayerCombatTargetingSystem.cs.meta` = `6418a83aa8654b80a06af6bcf6b38d72`, `PlayerAttackCooldownSystem.cs.meta` = `97d4ee8799b04bf7856020549e2e7600`, this doc). If a collision is reported, delete that `.meta` and let Unity regenerate, then re-commit.
- [ ] Systems window: `PlayerCombatTargetingSystem`, `PlayerAttackCooldownSystem`, `PlayerAttackSystem` all present in `PlayerInputSystemGroup`.

### Baking (re-bake the player prefab / subscene)
- [ ] Inspect the player entity in the Entities window → it now has `CombatTarget` (disabled) and `AttackCooldown` (disabled), alongside the inherited `UnitData`, `Health`/`Dead`, `AttackRequest`, `SetAnimation`/`AnimationRequest`, `Faction = Player`, `PlayerSelectedAttack`.
- [ ] Confirm the player's baked `UnitDataBlob.attacks` includes the `Slash` entry (matches `PlayerControllerAuthoring.defaultAttack`) and `actionAnimations` has a swing animation for the resolved `ActionType`.
- [ ] Confirm the `PlayerImmune` bake pattern in the scene matches the targeting filter: the query uses `WithNone<PlayerImmune>` (excludes when the component is **present-and-enabled**). If your allies/vehicles carry `PlayerImmune` present-but-disabled, they will still be targetable — enable it on anything that must not take player damage.

### Combat targeting (`PlayerCombatTargetingSystem`)
- [ ] Walk the player near a unit with `Health` → the player's `CombatTarget` becomes **enabled** and `entity` points at the nearest damageable unit. Walk away (> ~5f) → it **disables**.
- [ ] A unit with `PlayerImmune` **enabled** is never selected as `CombatTarget`. A `Dead` (enabled) unit is never selected.

### Attack + cooldown (`PlayerAttackSystem` / `PlayerAttackCooldownSystem`)
- [ ] Ensure the action map is `ActionMaps.Player` (on-foot). Press the attack input near a damageable unit → the swing animation plays on the **Action** layer, `AttackRequest` enables, and after `AttackBlob.hitTime` the target's `Health.healthAmount` drops (Combat/Health logs show the hit).
- [ ] The player **snap-faces** the target on the swing (rotation turns to look at it on the XZ plane).
- [ ] Kill a unit → death + ragdoll fire via the existing pipeline (nothing player-specific needed).
- [ ] **Cooldown:** mash/hold attack → swings fire no faster than `max(cooldown, hitTime + 0.05)`. `AttackCooldown` shows enabled during the gap and disables when it elapses.
- [ ] **Out of range:** press attack with no unit inside `AttackBlob.range` → **no swing** (v1 has no auto-step-in). Targeting acquires at 5f but the swing only fires inside the attack's own range.

### Separation from interaction
- [ ] The **interact** button still drives the existing interaction `Target` path (revive/pickup). Attacking never triggers an interaction and vice-versa — they use `CombatTarget` vs `Target` independently.

### Retaliation (observe only — not built here)
- [ ] Whatever the existing AI already does when the player hits it (fight back / flee / ignore) is **unchanged** by this build. If the retaliation behavior needs tuning, that is a separate AI-decision task, not part of Player Attack.

## Notes

Code files (this build):
- **New:** `Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerCombatTargetingSystem.cs`, `.../PlayerAttackCooldownSystem.cs` (+ hand-generated `.meta`).
- **Edited:** `.../PlayerAttackSystem.cs` (revived from fully-commented; drops deleted `ActionTimer` → `AttackCooldown`; v2 field names `AttackRequest.damageSource` / `PlayerSelectedAttack.damageSource`; reads `CombatTarget`; melee-only). `Components/Player/PlayerComponents.cs` (+`CombatTarget`, `AttackCooldown`). `Authoring/Player/PlayerControllerAuthoring.cs` (bakes both, disabled).
- **Docs:** `Memories/Code/Systems.md`, `Components.md`; `Tasks/Plans/README.md`.

Gotchas to watch:
- **Enableable query semantics:** `WithNone<Dead>` / `WithNone<PlayerImmune>` match entities where the component is absent **or present-but-disabled** — so alive units (Dead present, disabled) are correctly included, and only enabled `Dead`/`PlayerImmune` are excluded. If everything or nothing is being targeted, check whether these tags are baked enabled vs disabled.
- The three player systems run **main-thread** (no jobs), matching the sibling `PlayerTargetingSystem` / `PlayerRollInputSystem`. That is intentional (single player entity) and not a `.Run()` violation.
- Stale docs referenced a `CombatTarget`/`AttackCooldown` from a deleted `CombatAI.cs` / `UnitComponents.cs`; those structs no longer existed in code. The only definitions now live in `PlayerComponents.cs`. `EnemyAwarenessSystem` mentions `CombatTarget` only in a `///` comment; `MinionAttackOrderSystem` is fully commented out — neither conflicts.

When everything passes: move this file to `Assets/_Vault/Tasks/Done/` and flip the spec status to ✔️ done.
