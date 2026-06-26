# Player Attack System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** "update the player controls to the new systems — the player should be able to attack entities" (this session). Revives the dormant `PlayerAttackSystem.cs` against the current behavior/combat architecture.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — `PlayerAttackSystem`, `PlayerCombatTargetingSystem`, `PlayerAttackCooldownSystem` (each an `ISystem`) (§5)
- `dots-authoring-baker` — add the new components (`CombatTarget`, `AttackCooldown`) to `PlayerControllerAuthoring.Baker`, and audit the player prefab's unit scaffolding (§4, §8)

No blob-library work — reuses the existing `AttackLibrary` and `UnitDataLibrary` blobs. No new `ActionType`/`MotivationType` and no `dots-unit-ai` — **the player is directly controlled, not AI-driven**, so it never touches `UtilityActions`/`StateMachine`/`BehaviorExecutionSystem`.

---

## 1. Purpose & v1 scope

Give the player a working **melee attack**: press the attack button → the player swings at the nearest damageable entity in range, plays the swing animation, and deals damage through the existing combat pipeline.

The player is **already a full unit** (bakes `UnitData`, `AvailableAttack`, `AttackRequest`, `SetAnimation`/`AnimationRequest`, `Health`), so this is a *revive + modernize* of the commented-out `PlayerAttackSystem.cs` — not a from-scratch build. The player writes an `AttackRequest` directly (the "request model"); the combat `AttackRequestSystem` already consumes it and applies damage. No StateMachine, no behavior commands — the player is the one entity that bypasses the AI decision/execution split.

**v1 handles:**
- Attack input (`OnAttackPlayerInput`, already wired by `PlayerInputManager`) → melee swing.
- **Combat targeting separate from interaction targeting**, keyed on a "can take damage" filter (has `Health`, alive, not `PlayerImmune`).
- Attack vs interact resolved by **separate buttons** (attack button = combat; interact button = the existing interaction path).
- Snap-face the chosen target on swing.
- Per-swing cooldown gate so the attack can't be spammed.

**Out of v1 (deferred):**
- Ranged attacks / aim-cone targeting (the player already has `AimDirection`/`AimPlayerInput` — reserved hook).
- Thrown equipped items (`AimPlayerInput` "throws instead of attacks" + `ThrownItemSystem`).
- Combo chains, charge attacks, weapon-driven attack-type swapping beyond `PlayerSelectedAttack`.
- Minion attack-order revival (`MinionAttackOrderSystem.cs` is also commented out — separate task; minions already attack via behaviors).

## 2. Architecture

Pure ECS, all inside `PlayerInputSystemGroup` (under `PlayerSystemGroup`), which runs **before** `CombatSystemGroup` in the simulation pipeline — so an `AttackRequest` written during player input is consumed the **same frame** by `AttackRequestSystem` in `CombatExecutionSystemGroup`. No MonoBehaviour bridge needed (input is already bridged into ECS by `PlayerInputManager` via `OnAttackPlayerInput`).

Three small systems, all `[BurstCompile]` `ISystem`, gated `RequireForUpdate<Player>` (+ the libraries the attack system needs):

```
PlayerInputSystemGroup
  ├── PlayerCombatTargetingSystem   → writes CombatTarget (nearest damageable)   [per frame]
  ├── PlayerAttackCooldownSystem    → ticks AttackCooldown down                    [per frame]
  └── PlayerAttackSystem            → reads OnAttackPlayerInput → AttackRequest    [on press]

CombatSystemGroup ▸ CombatExecutionSystemGroup
  └── AttackRequestSystem           → reads AttackRequest → Hurt buffer (UNCHANGED)
```

**← DECISION:** combat targeting as a **new `PlayerCombatTargetingSystem`** (recommended — keeps combat and interaction targeting cleanly separate, mirrors the existing `PlayerTargetingSystem` structure) vs. **folding it into the existing `PlayerTargetingSystem`** (one query pass, but couples interaction `Target` and combat `CombatTarget`). Spec below assumes the separate system.

## 3. Entry points

- **Per-frame (persistent):** `PlayerCombatTargetingSystem` recomputes `CombatTarget` each frame and toggles its enabled state (enabled = a valid damageable target is in range). Mirrors how `PlayerTargetingSystem` maintains `Target`.
- **One-shot (request):** `OnAttackPlayerInput` (enableable, already on the player, enabled by `PlayerInputManager`) is the trigger. `PlayerAttackSystem` consumes it (`= false`), and — if not on cooldown and a `CombatTarget` is in range — writes a fresh `AttackRequest` (enableable) onto the player. `AttackRequestSystem` reads it, applies damage after `AttackBlob.hitTime`, and disables it.

## 4. Data model

**New components** (add to `_Scripts/Components/Units/UnitComponents.cs` or `_Scripts/Components/Player/PlayerComponents.cs` — ← DECISION on file):

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
- `AttackLibrary` singleton → `AttackBlob` (indexed by `(int)AttackType`): `range`, `cooldown`, `hitTime`, `damageAmount`, `ragdollForce`, `launchForce*`.
- `UnitDataLibrary` singleton → `UnitDataBlob.actionAnimations` for the swing animation (via `AIUtils.GetAnimationByAction`).
- Per-entity: `AvailableAttack` buffer (attack fallback), `PlayerSelectedAttack` (primary attack choice), `AttackRequest`, `SetAnimation`/`AnimationRequest`.

**Damageable filter** (the "can take damage" tag, per decision *any faction*): `WithAll<Health>` + alive (`Dead` present-but-disabled) + `WithNone<Player>` + not `PlayerImmune` (enabled). `Faction` is **not** filtered (friendly fire on) — `PlayerImmune` is the per-entity opt-out for allies/vehicles that shouldn't take player damage.

No new enums.

## 5. Systems

### `PlayerCombatTargetingSystem` (`PlayerInputSystemGroup`)
- Query: `Player` + `RefRO<LocalTransform>` + `RefRW<CombatTarget>` + `EnabledRefRW<CombatTarget>` (`.WithPresent<CombatTarget>()`).
- Inner scan: nearest entity with `Health`, alive (`!Dead`), `WithNone<Player>`, `PlayerImmune` disabled, within `COMBAT_TARGET_RANGE`. Writes `CombatTarget.entity` + enables; disables when nothing in range. Structurally a copy of `PlayerTargetingSystem` with the damageable filter swapped in for `PlayerInteractable`.
- **← DECISION:** `COMBAT_TARGET_RANGE` const (start ~5f, matching `PlayerTargetingSystem.TARGET_RANGE`). Could later derive from the selected `AttackBlob.range` instead of a flat const.

### `PlayerAttackCooldownSystem` (`PlayerInputSystemGroup`)
- Ticks `AttackCooldown.remaining -= deltaTime`; at `<= 0` clamps to 0 and disables. Direct copy of `PlayerRollInputSystem`'s pattern (which ticks `OnRollPlayerInput.rollTime`). Could be folded into the top of `PlayerAttackSystem` — ← DECISION (separate system is cleaner / matches the roll precedent).

### `PlayerAttackSystem` (`PlayerInputSystemGroup`) — *revive `PlayerAttackSystem.cs`*
Per player entity:
1. Action-map gate: `PlayerActionMap.activeActionMap == ActionMaps.Player` (skip when in `ControlUnits` etc.).
2. Read `OnAttackPlayerInput`; if disabled skip; else consume (`= false`).
3. Cooldown gate: if `AttackCooldown` enabled → return.
4. Resolve attack type: `PlayerSelectedAttack.attackType` if `!= None`, else first `AvailableAttack` entry (melee). Skip if none.
5. Resolve `ActionType` via `AIUtils.GetActionByAttack(ref unitBlob, attackType)`; resolve `AttackBlob` from `AttackLibrary[(int)attackType]`.
6. Target: read `CombatTarget`; require enabled + alive + within `AttackBlob.range`. (No target → no swing. v1 has no auto-step-in; the player must be in range.)
7. **Snap-face** the target: set the player's `LocalTransform.Rotation` to look at the target on the XZ plane.
8. Fire: write `AttackRequest { targetEntity, attackType, hitFired=false, elapsed=0 }`, enable it.
9. Animation: push `SetAnimation { layer = Action, animation = AIUtils.GetAnimationByAction(...), looping=false }`, enable `AnimationRequest`.
10. Start cooldown: `AttackCooldown.remaining = max(AttackBlob.cooldown, AttackBlob.hitTime + 0.05f)`, enable it. (Guarantees the hit lands before the next swing — preserves the commented system's intent now that `ActionTimer` is gone.)

> ⚠ The revived system must drop the stale `ActionTimer` references and use `AttackCooldown`. Re-verify `UnitDataLibrary`/`AttackLibrary` singleton names (confirmed current) and `PlayerImmune` (confirmed current) while uncommenting.

## 6. MonoBehaviour bridge
None. Input is already bridged: `PlayerInputManager` enables `OnAttackPlayerInput` on the player entity. No new managed objects.

## 7. Integration points
- **Combat (read path, unchanged):** `AttackRequestSystem` (`CombatExecutionSystemGroup`) consumes `AttackRequest` → `Hurt` buffer → `DamageApplicationSystem` → `Health`/`Dead`/ragdoll. Player benefits from all of it for free.
- **Animation:** `SetAnimation` (Action layer) + `AnimationRequest` → `AnimationRequestSystem`. Requires the player to bake the `SetAnimation` buffer + `AnimationRequest` (part of the "full unit" assumption — verify in Phase 0).
- **Interaction (sibling, untouched):** existing `PlayerTargetingSystem` → `Target` → interact path stays as-is. Attack uses the separate `CombatTarget`; interact uses `Target`. Separate buttons, no conflict.
- **Equipment (future hook):** `PlayerSelectedAttack` is already updatable; a later weapon-equip pass can write it so the swung attack follows the equipped weapon.

## 8. Proposed file manifest
**New:**
- `Assets/_Scripts/Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerCombatTargetingSystem.cs`
- `Assets/_Scripts/Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerAttackCooldownSystem.cs`

**Edited:**
- `Assets/_Scripts/Systems/PlayerSystemGroup/PlayerInputSystemGroup/PlayerAttackSystem.cs` — uncomment + modernize (drop `ActionTimer` → `AttackCooldown`, read `CombatTarget`, add snap-face).
- `Assets/_Scripts/Components/Units/UnitComponents.cs` *(or `Player/PlayerComponents.cs` — ← DECISION)* — add `CombatTarget`, `AttackCooldown`.
- `Assets/_Scripts/Authoring/Player/PlayerControllerAuthoring.cs` — bake `CombatTarget` (disabled) + `AttackCooldown` (disabled). **Phase 0:** confirm the player prefab also bakes `UnitData`, `AvailableAttack`, `AttackRequest`, `SetAnimation`, `AnimationRequest`, `Health` (the "full unit" assumption); if any are missing, add them here or via `UnitBakingUtil`.
- `Assets/_Vault/Memories/Code/Systems.md` + `Systems_AI.md` — note the player is the one combatant outside the AI decision/execution split; document `CombatTarget`/`AttackCooldown`.
- `Assets/_Vault/Tasks/Plans/README.md` — register this spec.

**Assets:** none (reuses existing `AttackSO`/`_AttackLibrary` and `UnitSO`/`_UnitLibrary`). Confirm the player's `UnitDataBlob.attacks` + `actionAnimations` include a melee entry with a swing animation.

## 9. Build phases
1. **Phase 0 — audit:** confirm the player prefab bakes the full-unit scaffolding (§8). Capture findings; add any missing components. *Gate: clean console + player entity shows `AttackRequest`/`AvailableAttack`/`Health` in the Entities window.*
2. **Phase 1 — components + baking:** add `CombatTarget`, `AttackCooldown`; bake them on the player. *Gate: components present (disabled) on the player entity.*
3. **Phase 2 — combat targeting:** `PlayerCombatTargetingSystem`. *Gate: walking near a damageable entity enables `CombatTarget` with the right `entity`; leaving range disables it.*
4. **Phase 3 — attack + cooldown:** revive `PlayerAttackSystem` + add `PlayerAttackCooldownSystem`. *Gate: pressing attack near an enemy writes `AttackRequest`, the swing animation plays, the target's `Health` drops, and rapid presses are gated by `AttackCooldown`.*
5. **Phase 4 — polish:** snap-face on swing; tune `COMBAT_TARGET_RANGE` and cooldown.

## 10. Verification
Play `Assets/Scenes/TestArea/DOTSTestScene.unity` (or `Game.unity`) with a damageable test unit near the player:
- **Targeting:** approach a unit with `Health` → inspect the player entity, `CombatTarget` enabled and pointing at it; walk away → disabled. A `PlayerImmune`-enabled unit is never targeted.
- **Attack:** press the attack input in `ActionMaps.Player` → swing animation on the Action layer, `Hurt` entry on the target, `Health.healthAmount` decreases; kill it → death/ragdoll fires via the existing pipeline. Combat log (`LogCategory.Combat`) shows the hit.
- **Cooldown:** hold/mash attack → swings fire no faster than `max(cooldown, hitTime+0.05)`.
- **Separation:** the interact button still drives the existing interaction `Target` path; attack never triggers an interaction and vice-versa.
- **Console gate:** `Unity_GetConsoleLogs` (`logTypes: "Error"`) clean — no `CS####`/`BC####` — after each phase.
- *Spencer-only (Editor):* whether the swing animation reads well, range/cooldown feel, and confirming the player prefab's authoring after Phase 0.

## Open decisions (collected)
- [ ] §2/§5 — `PlayerCombatTargetingSystem` as a new system *(recommended)* vs. fold combat targeting into `PlayerTargetingSystem`.
- [ ] §4/§8 — put `CombatTarget`/`AttackCooldown` in `UnitComponents.cs` vs. `PlayerComponents.cs`.
- [ ] §5 — `PlayerAttackCooldownSystem` as its own system *(recommended, matches `PlayerRollInputSystem`)* vs. tick inside `PlayerAttackSystem`.
- [ ] §5 — `COMBAT_TARGET_RANGE` value (start ~5f) and whether to derive it from `AttackBlob.range` later.
- [ ] §5 — v1 has **no auto-step-in**: out-of-range press = no swing. Confirm that's acceptable (vs. queueing an approach).
- [ ] §4 — confirm "any faction" friendly fire is intended; `PlayerImmune` is the only opt-out. Tag allies/vehicles/important NPCs with `PlayerImmune` accordingly.
