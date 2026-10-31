---
title: SmartHumanAI H5 — Guns, Wind-Up Telegraph, Line of Fire
status: spec ready
created: 2026-10-31
area: AI, Combat
parent: "[[SmartHumanAI_System]]"
---

# SmartHumanAI H5 — Guns, Wind-Up Telegraph, Line of Fire

> **Status:** ✅ spec ready 2026-10-31. Product calls come from `RangedCombat_System.md` §0 (RC-D1–D10) and the owner's 2026-10-31 notes (telegraph, aim around allies). H5-D1–D12 below are recorded architecture calls. One owner checkpoint is open: the telegraph look, judged from a capture (H5-D3).
> **Read first:** `Memories/Code/Systems_AI.md` (tiers, Keep Range, Throw), `RangedCombat_System.md` §0.
> **Comes before:** `DamageReaction_System.md` (fight/flee/help on any hit, friendly fire included). Until that lands, a friendly hit deals damage but makes no `ThreatEntry` (RC-D8 stays in force for H5).

## 1. Goal

Bandits use guns like they use rocks. Guns come in several kinds, each with its own stats. A bandit can start holding one or pick one up, and a gun counts as a weapon in the existing grab. Before any throw or shot, the unit **winds up visibly**: it swells and flashes white, and its aim locks. A player who steps aside dodges it. Shooters **try** not to hit allies: they check the line of fire and side-step, but discipline varies per person, and allies can walk into a locked shot during the wind-up. That is the opening the player exploits.

## 2. Skills Needed

`dots-system-scaffold` (new systems), `dots-authoring-baker` (gun projectile + starting weapon authoring), `shader-edit` (the `_FlashAmount` per-instance property), `dots-test`.

## 3. Decisions (recorded, H5-D1–D12)

| Id | Decision |
|---|---|
| H5-D1 | **Wind-up is a blocking behaviour command, `WindUp`.** It takes `Duration` from the held item's `ItemBlob.aiWindUpSeconds`, with FloatParam as the fallback. On its first frame it **locks the aim point** to the target's position into a new `RangedAim { float3 aimPoint; float windUpTotal; float windUpRemaining; }` (enableable, on brain units). `RequestThrow` / `RequestShoot` fire at `RangedAim.aimPoint`, not at the target's live position, so stepping aside during the wind-up dodges the shot. |
| H5-D2 | **Wind-up presentation** is `WindUpPresentationSystem` (presentation, after StateMachine). While `RangedAim` is enabled it writes a new per-instance `BodyPartFlash [MaterialProperty("_FlashAmount")]` on the unit's body-part quads, ramping 0 → `WIND_UP_FLASH_MAX` (0.55). It also scales the unit's visual root by up to `WIND_UP_SCALE_MAX` (1.10). Both ease in over the wind-up and snap back on release. The body tint (`BodyPartTint`) is a *multiply* and cannot whiten, so the 2D body-part graphs gain an additive `lerp(color, flashColor, _FlashAmount)` (shader-edit). |
| H5-D3 | **Owner checkpoint:** the flash colour, strength and scale are tunables on a `WindUpPresentationConfig` singleton (authoring on the game manager). The owner judges them from a capture against the painterly ground. A short wind-up sound plays through the existing `PlaySound` command at the start of the wind-up, so the cue never depends on colour alone. Aim glint or line for guns waits for that checkpoint (only if bullets prove too fast to read). |
| H5-D4 | **One shared hit test.** It's a static `ProjectileHitUtils.FindFirstHitAlongSegment(previousPosition, currentPosition, radius, shooter, clearOrigin, ...)`: a swept XZ segment against every live unit's circle, returning the first hit **along the path**. It skips the shooter and anything within 1.2 m of the origin until the projectile clears it (the ThrownItem rule), and passes through `DodgeInvulnerable`. `ThrownItemHitSystem` switches to it, and bullets use it. |
| H5-D5 | **Friendly fire on for rocks and bullets (RC-D1).** The same-faction skip in `ThrownItemHitSystem` is removed. The victim takes the damage; `DamageEventSystem` still makes no ThreatEntry for a friendly hit until DamageReaction lands. |
| H5-D6 | **Bullets are `Projectile { float3 velocity; Entity shooter; float3 origin; DamageSource damageSource; float radius; }` entities** with `Lifetime` = 1.5 × maxRange / speed. They go through `ProjectileFlightSystem` (move, then shared hit test), hits enqueue into DamageBus and enable `Despawn`. Pooling: P0 probes whether `DespawnSystem` pools non-unit entities (`PoolOwner` is keyed by UnitType). If it doesn't, v1 instantiates bullets and destroys them on despawn (≈ 10 bullets/s worst case), and a bullet pool is a follow-up. |
| H5-D7 | **Guns are ItemSOs.** `ItemType.Pistol` and `ItemType.Rifle` are appended, category Weapon. New `ItemSO`/`ItemBlob` fields: `rangedKind` (None/Throw/Shoot), `projectileSpeed`, `rangedCooldownSeconds`, `aiWindUpSeconds`, `aiWeaponValue`, and the projectile prefab. Prefabs can't live in a blob, so the prefab is baked into an `ItemProjectilePrefab` buffer on the ItemLibrary entity, indexed by ItemType. Damage comes from the item's `weaponAttack` DamageSource (new `Pistol` / `Rifle` AttackSO entries). `aiThrowMinRange/MaxRange` are renamed `aiRangedMinRange/MaxRange` (`FormerlySerializedAs`), so rocks and guns share one range pair and Keep Range uses it unchanged. |
| H5-D8 | **v1 numbers (tune in play):** Pistol 25 dmg, 24 m/s, cooldown 1.6 s, range 4–14 m, wind-up 0.45 s, value 3. Rifle 40 dmg, 30 m/s, cooldown 2.8 s, range 6–20 m, wind-up 0.6 s, value 4. Rock is value 1 and gets wind-up 0.35 s (its existing pre-release wait). |
| H5-D9 | **One ranged awareness.** `ThrowAwarenessSystem` becomes `RangedAttackAwarenessSystem` (file renamed with its meta GUID kept). It emits `Throw` or `Shoot` by `rangedKind` at `Critical`, and also owns Keep Range and the line-of-fire move (H5-D11). A per-unit ranged cooldown, `TacticsCooldown.rangedReadyAtSeconds`, is set when the shot fires. That makes the gun's cooldown per weapon, and the behaviour only waits a 0.3 s recovery. `ShootBehaviour`: `PlaySound(cue) → WindUp → RequestShoot → WaitTime 0.3`. `ThrowBehaviour` changes the same way (its 0.35 s Wait becomes `WindUp`). |
| H5-D10 | **Getting guns:** (a) world pickups. ItemAwareness' weapon grab takes the highest `aiWeaponValue` in reach, with distance as tie-break; an armed unit doesn't swap in v1. (b) a per-instance **`StartingWeaponAuthoring`** (prefab field) on a unit GameObject. `StartingWeaponSpawnSystem` (SpawnInit, NewlySpawned) instantiates the weapon and equips it through the normal pickup path (EquipBy + AttachedTo + `PickupRequest`). It's per instance, not on UnitSO, so one bandit can be the gunman. CampLoop gets 1 Pistol + 1 Rifle placed in camp and 1 bandit starting with a Pistol. |
| H5-D11 | **Line of fire is a strategy, not a guarantee.** Before emitting Throw/Shoot, the awareness tests the segment shooter → target against live same-faction units (radius + 0.3 m). The unit's discipline is its `Resourceful` trait (0–1), −0.5 when Intoxication > 0.5. If the line is blocked and discipline ≥ 0.35, it **doesn't fire**; it emits a side-step instead: the KeepRange action to a point 2.5 m perpendicular, toward the side with fewer allies, sharing Keep Range's cooldown. If discipline is below 0.35 it fires anyway. The rule is deterministic (no dice), so the player can learn who is careless. The check uses positions at decision time, so an ally can still walk into the locked shot during the wind-up. |
| H5-D12 | **Out of H5:** ammo, reloads, arcs/piercing/homing, the player gun, and reactions to friendly hits (DamageReaction). |

## 4. Files

| Change | File |
|---|---|
| New | `Components/AI/RangedAimComponents.cs` (`RangedAim`, `AiShootRequest`), `Components/Combat/ProjectileComponents.cs` (`Projectile`), `Components/Units/BodyPartComponents.cs` (+`BodyPartFlash`) |
| New | `Utils/ProjectileHitUtils.cs`; `Utils/BehaviorCommands/` `WindUp` arm (+ catalog, blocking) and `RequestShoot` arm |
| New systems | `ProjectileFlightSystem` (CombatExecution, DamageBus producer: copy AttackRequestSystem's handle registration), `AiShootSystem` (ItemEquip group, beside AiThrowItemSystem), `StartingWeaponSpawnSystem` (SpawnInit), `WindUpPresentationSystem` |
| Edited | `ThrownItemHitSystem` (shared test, no faction skip), `ThrowAwarenessSystem` → `RangedAttackAwarenessSystem`, `ItemAwarenessSystem` (weapon value), `AiThrowItemSystem` (aim point), `ItemSO`/`ItemBlob`/ItemLibrary baking, `ThinkComponents` (`rangedReadyAtSeconds`), body-part 2D shader graphs |
| Assets | Pistol/Rifle ItemSO + AttackSO + prefabs + bullet prefab, `ShootBehaviour`, `ShootAction`, `ShootModule` (BanditBrain), `WindUpPresentationConfig`, CampLoop_Sub placements |

## 5. Build phases (worktree `spec/smart-ai-h5`, merges agreed with peers)

0. **Probes** (one `execute_code` each, memory "probe the platform first"): Does `DespawnSystem` pool a non-unit entity? Which entity is a unit's visual root for scaling (VisualBillboard child)? Do the body-part 2D graphs take one more Hybrid Per Instance float without breaking batching?
1. **Shared hit test + friendly fire on rocks.** EditMode fixture for the segment math: first hit along the path, shooter skip, clear-origin rule, tunnelling at 30 m/s.
2. **Wind-up + aim lock on rocks**, with the flash and scale presentation. **⏸ Owner capture checkpoint** (H5-D3) before guns.
3. **Gun data + bullets:** ItemSO fields, the range rename, the prefab buffer, `AiShootSystem`, `ProjectileFlightSystem`, despawn.
4. **Shooting AI:** RangedAttackAwareness, ShootBehaviour/Action/Module, ranged cooldown, Keep Range on gun ranges, weapon-value grab, starting weapon, CampLoop placements.
5. **Line of fire + discipline side-step.**
6. **Tests + docs:** PlayMode fixtures proven red with Burst off; `Systems_AI.md`; the `verify-smart-ai.md` H5 section.

## 6. Tests (PlayMode unless noted)

- Segment hit math (EditMode): first unit along the path wins; the shooter is skipped; a 30 m/s bullet can't tunnel through a 0.4 m unit.
- A rock now damages a same-faction unit in its path.
- WindUp locks the aim: a target that moves after the wind-up starts is missed. The flash is > 0 during the wind-up and 0 after.
- Shoot is offered only in [min, max] and only after `rangedReadyAtSeconds`. A bullet spawns with its velocity toward the aim point and despawns on hit or TTL.
- Weapon grab prefers the Rifle over a nearer Rock when both are in reach.
- An ally in the line of fire: a disciplined shooter offers a side-step instead of Shoot; an undisciplined one shoots.

## 7. Verification (owner, `verify-smart-ai.md` § H5)

- [ ] Every throw and shot is preceded by a visible swell + white flash and a sound; side-stepping during it dodges.
- [ ] A bandit picks up the camp rifle over a rock; the starting gunman shoots from range and backs off when you close in.
- [ ] Stand so a bandit is between you and a shooter: a careful shooter side-steps, a careless one shoots its friend.
- [ ] A rock or bullet that hits a fellow bandit hurts it (no fight breaks out yet; that comes with DamageReaction).
