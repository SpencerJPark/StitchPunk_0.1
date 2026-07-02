# Player Attack System — Design Spec

> **Status:** 🔨 built — code landed, verify pending (see [`verify-player-attack.md`](verify-player-attack.md)). Compile gate NOT run this session (Unity MCP not connected); left to verification. Scoped to the player-side melee only — the retaliation/threat rule is deliberately left to the existing AI (dropped from this plan).
> **Raw source:** "update the player controls to the new systems — the player should be able to attack entities" (this session). Revives the dormant `PlayerAttackSystem.cs` against the current behavior/combat architecture.
> **v2 refresh (this pass):** the DamageEvent v2 rework renamed `AttackType → DamageSource` and moved damage onto a recycled `NativeQueue` bus. The player still writes an `AttackRequest` (unchanged entry point) — only field names change. **Retaliation stays an AI decision** (existing threat/awareness systems) — this plan does **not** touch the damage-side threat gate; it only gives the player a working swing.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — `PlayerCombatTargetingSystem`, `PlayerAttackCooldownSystem` (new `ISystem`s) + revived `PlayerAttackSystem` (§5)
- `dots-authoring-baker` — add `CombatTarget`, `AttackCooldown` to `PlayerControllerAuthoring.Baker` (§4, §8)

No blob-library work — reuses the existing `AttackLibrary` and `UnitDataLibrary` blobs. No new `ActionType`/`MotivationType` and no `dots-unit-ai` — **the player is directly controlled, not AI-driven**, so it never touches `UtilityActions`/`StateMachine`/`BehaviorExecutionSystem`.

---

## 1. Purpose & v1 scope

Give the player a working **melee attack**: press the attack button → the player swings at the nearest damageable entity in range, plays the swing animation, and deals damage through the existing combat pipeline.

The player is **already a full unit** — `PlayerUnit.prefab` is a **variant of `BaseUnit.prefab`**, so it inherits `UnitAuthoring` (`UnitData`, `Target`, `UnitAction`), `HealthAuthoring` (`Health`/`Dead`), `AnimatorAuthoring` (`SetAnimation`/`AnimationRequest`/`AnimationLayer`), plus its own `AttackAuthoring` (`AttackRequest`) and `PlayerControllerAuthoring` (`Faction = Player`, `PlayerSelectedAttack`). So this is a *revive + modernize* of the commented-out `PlayerAttackSystem.cs` — not a from-scratch build. The player writes an `AttackRequest` directly (the "request model"); `AttackRequestSystem` already consumes it, validates range, and `Enqueue`s a `DamageEvent` value onto the v2 `DamageBus`. No StateMachine, no behavior commands — the player is the one entity that bypasses the AI decision/execution split.

**v1 handles:**
- Attack input (`OnAttackPlayerInput`, already wired by `PlayerInputManager`) → melee swing.
- **Combat targeting separate from interaction targeting**, keyed on a "can take damage" filter (has `Health`, alive, not the player, not `PlayerImmune`).
- Attack vs interact resolved by **separate buttons** (attack button = combat; interact button = the existing interaction path).
- Snap-face the chosen target on swing.
- Per-swing cooldown gate so the attack can't be spammed.

**Out of v1 (deferred / explicitly out of scope):**
- **Retaliation / who-fights-back:** left entirely to the existing AI threat + awareness systems (`DamageEventSystem` threat gate, `SelfDefenceAwarenessSystem`, `MinionSelfDefenceSystem`). This plan does not add or change any damage-side guard — the AI decides what it attacks.
- Ranged attacks / aim-cone targeting (the player already has `AimDirection`/`AimPlayerInput` — reserved hook).
- Thrown equipped items (`AimPlayerInput` "throws instead of attacks" + `ThrownItemSystem`).
- Combo chains, charge attacks, weapon-driven attack-type swapping beyond `PlayerSelectedAttack`.
- Minion attack-order revival (`MinionAttackOrderSystem.cs` is also commented out — separate task; minions already attack via behaviors).

## 2. Architecture

Pure ECS, all inside `PlayerInputSystemGroup` (under `PlayerSystemGroup`), which runs **before** `CombatSystemGroup` in the simulation pipeline — so an `AttackRequest` written during player input is consumed the **same frame** by `AttackRequestSystem` in `CombatExecutionSystemGroup` (which Enqueues onto the `DamageBus`, drained the same frame by `DamageEventSystem`). No MonoBehaviour bridge needed (input is already bridged into ECS by `PlayerInputManager` via `OnAttackPlayerInput`).

Three small systems, all `[BurstCompile]` `ISystem`, gated `RequireForUpdate<Player>` (+ the libraries the attack system needs):

```
PlayerInputSystemGroup
  ├── PlayerCombatTargetingSystem   → writes CombatTarget (nearest damageable)   [per frame]
  ├── PlayerAttackCooldownSystem    → ticks AttackCooldown down                    [per frame]
  └── PlayerAttackSystem            → reads OnAttackPlayerInput → AttackRequest    [on press]

CombatSystemGroup (UNCHANGED by this plan)
  ├── CombatExecutionSystemGroup ▸ AttackRequestSystem → DamageBus.raw.Enqueue
  └── CombatReactionSystemGroup  ▸ DamageEventSystem   → Health + ThreatEntry (AI-driven retaliation)
```

**RESOLVED:** combat targeting is a **new `PlayerCombatTargetingSystem`** (keeps combat and interaction targeting cleanly separate, mirrors the existing `PlayerTargetingSystem` structure) — not folded into `PlayerTargetingSystem`.

## 3. Entry points

- **Per-frame (persistent):** `PlayerCombatTargetingSystem` recomputes `CombatTarget` each frame and toggles its enabled state (enabled = a valid damageable target is in range). Mirrors how `PlayerTargetingSystem` maintains `Target`.
- **One-shot (request):** `OnAttackPlayerInput` (enableable, already on the player, enabled by `PlayerInputManager`) is the trigger. `PlayerAttackSystem` consumes it (`= false`), and — if not on cooldown and a `CombatTarget` is in range — writes a fresh `AttackRequest` (enableable) onto the player. `AttackRequestSystem` reads it, `Enqueue`s the `DamageEvent` after `AttackBlob.hitTime`, and disables it.

## 4. Data model

**New components** (add to `_Scripts/Components/Player/PlayerComponents.cs` — RESOLVED: these are player-only, so they live with the player components, not `UnitComponents.cs`):

```csharp
// Combat-specific target, distinct from the interaction `Target`.
// Enabled = a valid damageable entity is in range this frame.
public struct CombatTarget : IComponentData, IEnableableComponent
{
    public Entity entity;
}

// Per-swing cadence gate for the player melee (replaces the deleted ActionTimer).
// Enabled = on cooldown; ticks `remaining` to 0 then disables. Mirrors OnRollPlayerInput.
public struct AttackCooldown : IComponentData, IEnableableComponent
{
    public float remaining;
}
```

**Reused config (no new blobs):**
- `AttackLibrary` singleton → `AttackBlob` (indexed by `(int)DamageSource`): `range`, `cooldown`, `hitTime`, `damageAmount`, `ragdollForce`, `launchForce*`, `damageSource`, `damageBehaviour`.
- `UnitDataLibrary` singleton → `UnitDataBlob.actionAnimations` for the swing animation (via `AIUtils.GetAnimationByAction`), and `UnitDataBlob.attacks` (each entry has `.attack` = `DamageSource` + `.action` = `ActionType`) for the melee fallback + action resolution (`AIUtils.GetActionByAttack`).
- Per-entity: `AvailableAttack` buffer (`actionType` + `damageSource`), `PlayerSelectedAttack { DamageSource damageSource }` (primary attack choice, baked from `PlayerControllerAuthoring.defaultAttack = Slash`), `AttackRequest { targetEntity, damageSource, hitFired, elapsed }`, `SetAnimation`/`AnimationRequest`.

**Damageable filter** (the "can take damage" set): `WithAll<Health>` + alive (`WithNone<Dead>`, i.e. `Dead` present-but-disabled excluded) + `WithNone<Player>` (never target self) + `WithNone<PlayerImmune>` (per-entity opt-out for allies/vehicles/story NPCs that shouldn't take player damage). `Faction` is **not** filtered — the player can hit anything with `Health`. Whatever the target does in response is decided by the existing AI (out of scope here).

> Note: `.WithNone<PlayerImmune>` matches on the component being **present** — since `PlayerImmune` is enableable and baked (if at all) as an opt-out tag, the simplest v1 uses presence. If `PlayerImmune` is instead baked present-but-disabled on everything, switch to an explicit `IsComponentEnabled<PlayerImmune>` check. Confirm the bake pattern in Phase 0 (mirror how the old commented system used `.WithNone<Player, PlayerImmune, Dead>()`).

No new enums (the `AttackType → DamageSource` rename already landed in the v2 pass).

## 5. Systems

### `PlayerCombatTargetingSystem` (`PlayerInputSystemGroup`) — new
- Query: `Player` + `RefRO<LocalTransform>` + `RefRW<CombatTarget>` + `EnabledRefRW<CombatTarget>` (`.WithPresent<CombatTarget>()`).
- Inner scan: nearest entity with `Health`, `.WithNone<Player, PlayerImmune, Dead>()`, within `COMBAT_TARGET_RANGE` (XZ distance). Writes `CombatTarget.entity` + enables; disables when nothing in range. Structurally a copy of `PlayerTargetingSystem` with the damageable filter swapped in for `PlayerInteractable`.
- **RESOLVED:** `COMBAT_TARGET_RANGE` = flat `5f` const (matches `PlayerTargetingSystem.TARGET_RANGE`; tunable in Phase 4). Acquisition range is intentionally wider than a given `AttackBlob.range` so the player locks on then steps in.

### `PlayerAttackCooldownSystem` (`PlayerInputSystemGroup`) — new
- Ticks `AttackCooldown.remaining -= deltaTime`; at `<= 0` clamps to 0 and disables. Direct copy of `PlayerRollInputSystem`'s pattern (ticks `OnRollPlayerInput.rollTime`). **RESOLVED:** kept as its own system (matches the `PlayerRollInputSystem` precedent), not folded into `PlayerAttackSystem`.

### `PlayerAttackSystem` (`PlayerInputSystemGroup`) — *revive `PlayerAttackSystem.cs`*
Per player entity:
1. Action-map gate: `PlayerActionMap.activeActionMap == ActionMaps.Player` (skip when in `ControlUnits` etc.).
2. Read `OnAttackPlayerInput`; if disabled skip; else consume (`= false`).
3. Cooldown gate: if `AttackCooldown` enabled → return.
4. Resolve attack type: `PlayerSelectedAttack.damageSource` if `!= DamageSource.None`, else first `UnitDataBlob.attacks[0].attack` (melee). Skip if none.
5. Resolve `ActionType` via `AIUtils.GetActionByAttack(ref unitBlob, damageSource)`; resolve `AttackBlob` from `AttackLibrary[(int)damageSource]` (guard `attackIndex > 0 && < attacks.Length`).
6. Target: read `CombatTarget`; require enabled + alive (`Dead` present-but-disabled) + within `AttackBlob.range` (XZ). (No target → no swing. **RESOLVED:** v1 has no auto-step-in; the player must be in range.)
7. **Snap-face** the target: set the player's `LocalTransform.Rotation` to look at the target on the XZ plane.
8. Fire: write `AttackRequest { targetEntity, damageSource, hitFired = false, elapsed = 0 }`, enable it.
9. Animation: push `SetAnimation { layer = Action, animation = AIUtils.GetAnimationByAction(ref unitBlob, actionType), speed = 1, looping = false }`, enable `AnimationRequest`.
10. Start cooldown: `AttackCooldown.remaining = max(AttackBlob.cooldown, AttackBlob.hitTime + 0.05f)`, enable it. (Guarantees the hit lands before the next swing — preserves the commented system's intent now that `ActionTimer` is gone.)

> ⚠ The revived system drops the stale `ActionTimer` references (component deleted) and uses `AttackCooldown`. Uses the v2 field names (`AttackRequest.damageSource`, `PlayerSelectedAttack.damageSource`). `UnitDataLibrary`/`AttackLibrary`/`PlayerImmune` names confirmed current.

## 6. MonoBehaviour bridge
None. Input is already bridged: `PlayerInputManager` enables `OnAttackPlayerInput` on the player entity. No new managed objects.

## 7. Integration points
- **Combat (read path, unchanged):** `AttackRequestSystem` (`CombatExecutionSystemGroup`) consumes `AttackRequest`, validates range/alive, and `Enqueue`s a `DamageEvent` value onto `DamageBus.raw` → `DamageResolutionSystem` → `DamageEventSystem` → `Health`/`Dead`/ragdoll. Player benefits from all of it for free. **This plan does not modify the combat/damage systems.**
- **Animation:** `SetAnimation` (Action layer) + `AnimationRequest` → `AnimationRequestSystem`. Player bakes the `SetAnimation` buffer + `AnimationRequest` via the inherited `AnimatorAuthoring` (confirmed — §1).
- **Interaction (sibling, untouched):** existing `PlayerTargetingSystem` → `Target` → interact path stays as-is. Attack uses the separate `CombatTarget`; interact uses `Target`. Separate buttons, no conflict.
- **Retaliation (AI-owned, untouched):** whether a hit unit fights back is decided by the existing `DamageEventSystem` threat gate + `SelfDefenceAwarenessSystem` / `MinionSelfDefenceSystem`. Out of scope for this plan.
- **Equipment (future hook):** `PlayerSelectedAttack.damageSource` is already updatable; a later weapon-equip pass can write it so the swung attack follows the equipped weapon.

## 8. Proposed file manifest
**New:**
- `Assets/_Scripts/Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerCombatTargetingSystem.cs`
- `Assets/_Scripts/Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerAttackCooldownSystem.cs`

**Edited:**
- `Assets/_Scripts/Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerAttackSystem.cs` — uncomment + modernize (drop `ActionTimer` → `AttackCooldown`, read `CombatTarget`, add snap-face, v2 field names, melee-only v1).
- `Assets/_Scripts/Components/Player/PlayerComponents.cs` — add `CombatTarget`, `AttackCooldown`.
- `Assets/_Scripts/Authoring/Player/PlayerControllerAuthoring.cs` — bake `CombatTarget` (disabled) + `AttackCooldown` (disabled).
- `Assets/_Vault/Memories/Code/Systems.md` + `Systems_AI.md` — note the player is the one combatant outside the AI decision/execution split; document `CombatTarget`/`AttackCooldown`.
- `Assets/_Vault/Tasks/Plans/README.md` — flip status.

**Assets:** none (reuses existing `AttackSO`/`_AttackLibrary` and `UnitSO`/`_UnitLibrary`). Confirm the player's `UnitDataBlob.attacks` + `actionAnimations` include the `Slash` entry with a swing animation.

## 9. Build phases
1. **Phase 0 — audit (DONE):** confirmed `PlayerUnit.prefab` is a `BaseUnit.prefab` variant → already bakes `UnitData`, `Target`, `Health`/`Dead`, `SetAnimation`/`AnimationRequest`, `AttackRequest`, `Faction = Player`, `PlayerSelectedAttack`. Only `CombatTarget`/`AttackCooldown` are missing. *Remaining check: confirm `PlayerImmune` bake pattern (present-disabled vs absent) so the targeting filter matches (§4).*
2. **Phase 1 — components + baking:** add `CombatTarget`, `AttackCooldown` to `PlayerComponents.cs`; bake them disabled on the player. *Gate: components present (disabled) on the player entity; clean console.*
3. **Phase 2 — combat targeting:** `PlayerCombatTargetingSystem`. *Gate: walking near a damageable entity enables `CombatTarget` with the right `entity`; leaving range disables it; a `PlayerImmune` unit is never targeted.*
4. **Phase 3 — attack + cooldown:** revive `PlayerAttackSystem` + add `PlayerAttackCooldownSystem`. *Gate: pressing attack near an enemy writes `AttackRequest`, the swing animation plays, the target's `Health` drops, rapid presses gated by `AttackCooldown`.*
5. **Phase 4 — polish:** snap-face on swing; tune `COMBAT_TARGET_RANGE` and cooldown.

## 10. Verification
Play `Assets/Scenes/TestArea/DOTSTestScene.unity` (or `Game.unity`) with a damageable test unit near the player:
- **Targeting:** approach a unit with `Health` → inspect the player entity, `CombatTarget` enabled and pointing at it; walk away → disabled. A `PlayerImmune` unit is never targeted.
- **Attack:** press the attack input in `ActionMaps.Player` → swing animation on the Action layer, `Health.healthAmount` decreases on the target; kill it → death/ragdoll fires via the existing pipeline. Combat/Health logs show the hit.
- **Cooldown:** hold/mash attack → swings fire no faster than `max(cooldown, hitTime + 0.05)`.
- **Retaliation (observe, not built here):** whatever the AI already does when hit — unchanged by this plan.
- **Separation:** the interact button still drives the existing interaction `Target` path; attack never triggers an interaction and vice-versa.
- **Console gate:** `Unity_GetConsoleLogs` (`logTypes: "Error"`) clean — no `CS####`/`BC####` — after each phase.
- *Spencer-only (Editor):* whether the swing animation reads well, range/cooldown feel, confirming `PlayerImmune` bake pattern.

## Open decisions (collected) — RESOLVED
- [x] §2/§5 — combat targeting as a **new `PlayerCombatTargetingSystem`** (not folded into `PlayerTargetingSystem`).
- [x] §4/§8 — `CombatTarget`/`AttackCooldown` live in **`PlayerComponents.cs`** (player-only).
- [x] §5 — `PlayerAttackCooldownSystem` is its **own system** (matches `PlayerRollInputSystem`).
- [x] §5 — `COMBAT_TARGET_RANGE` = **flat `5f`** (tunable Phase 4).
- [x] §5 — v1 has **no auto-step-in**: out-of-range press = no swing.
- [x] §4 — player can hit **anything with `Health`** (not self, not `PlayerImmune`); `Faction` not filtered.
- [x] §4 — **retaliation left to the existing AI** (no damage-side guard added; `DamageEventSystem`/`AttackFaction` untouched).
