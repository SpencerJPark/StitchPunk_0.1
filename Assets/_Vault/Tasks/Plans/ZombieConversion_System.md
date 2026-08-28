# Zombie Conversion System — Design Spec

> **Status:** ✅ spec ready — and **both halves it composes now exist**: `SwapBrainRequest` (`SwapBrainSystem`, from Brain Control Split) and `ChangeDesignRequest` (from the CharacterRig/ColorPalette work — `CharacterRigAuthoring.cs:114`). Only the `ZombifyRequest` composer is missing, so this is now a much smaller build than the doc implies.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #5 — "the vertical-slice payoff of everything just built, and it's nearly free now"

---

**Skills Needed:**
- `dots-system-scaffold` — `ZombifySystem` (§5)
- `dots-unit-ai` — if the trigger is an awareness/behavior (bite attack) rather than narrative-only (§3)

---

## 1. Purpose & v1 scope

Convert a living human into a zombie at runtime by composing two mechanisms that already exist: `SwapBrainRequest` (re-keys `UtilityBrain.unitType`, `Faction`, `AttackFaction`/`AvailableAttack`/`Motivation` buffers — built for Minion Revival, explicitly designed as a "generic brain-swap hook (revive, future feral turn, debug)") and `ChangeDesignRequest` (re-skin — the rig commit made zombification "a palette shift": Skin → Zombie palette column). This plan adds the thin composition layer plus the trigger.

**v1 handles:** one conversion path (human `unitType` → zombie `unitType`), palette shift, brain swap, conversion animation beat, contract row in [[Contracts]].
**Out of v1:** infection timers/spread mechanics, partial states, cure. Reserve via `ZombifyRequest.delaySeconds` defaulting to 0.

## 2. Architecture

New enableable request + one consuming system, per the request model:

```
trigger (narrative / bite / debug key)
   └─ enables ZombifyRequest { targetUnitType }
        └─ ZombifySystem (HealthSystemGroup, after SwapBrainSystem)
             ├─ stamps + enables SwapBrainRequest { newUnit = targetUnitType }
             ├─ stamps + enables ChangeDesignRequest { paletteChanges.skin = zombieColumn }
             ├─ fires ActionInterruptRequest (tears down live behavior via BehaviorInterruptSystem)
             └─ disables itself
```

Ordering: `ZombifySystem` runs in `HealthSystemGroup` **after** `SwapBrainSystem` so a same-frame revive-then-zombify never interleaves; `ChangeDesignRequest` is consumed downstream the same frame by `DesignChangeSystem` (DesignSystemGroup runs after Health — the ordering comment in `SystemGroups.cs` exists for exactly this).

**← DECISION:** does conversion play through a behavior (a `ZombifyBehaviour.asset` with a transformation animation + `WaitTime`, giving a visible beat) or apply instantly? *Recommendation: instant in phase 1 (prove the pipeline), behavior-wrapped in phase 3 — the interpreter already supports everything needed (`PlayAnimation`, `WaitTime`, `ModifyMotivation`).*

## 3. Entry points

- **`ZombifyRequest : IComponentData, IEnableableComponent`** `{ UnitType targetUnitType; float delaySeconds; }` — baked disabled on convertible units by `UnitBakingUtil` (same block as `SwapBrainRequest`).
- **Triggers (pick for v1):** ← DECISION:
  - Narrative action — extend `NarrativeEventSO`'s action types with a Zombify action (the EnableComponent action type may already suffice — check before adding a new one).
  - Zombie bite — on-kill hook in `DamageEventSystem` (killing blow from a zombie faction source → enable on victim instead of normal death) — the demo-defining version.
  - Debug key in `DebugSaveMenu`-style OnGUI for testing regardless.
  *Recommendation: debug key + narrative action in v1; bite-conversion as the phase-4 flourish since it touches the death path.*

## 4. Data model

None new beyond the request struct. Zombie palette column + non-randomizable Zombie tag ranges must exist in the part SOs (`_PartLibrary`) — an **asset task, not code**; verify `CharacterPalette` group capacity note in the audit before adding a group.

## 5. Systems

- **New:** `HealthSystemGroup/ZombifySystem.cs` — query enabled `ZombifyRequest`, compose the three requests above, disable. `IJobEntity.ScheduleParallel`; lookups: `SwapBrainRequest`, `ChangeDesignRequest`, `ActionInterruptRequest` (all `[NativeDisableParallelForRestriction]`, each unit owns its own).
- **Edited:** `UnitBakingUtil.cs` (bake the request), `Contracts.md` (+row), `DamageEventSystem.cs` (phase 4 only).

## 8. Proposed file manifest

**New:** `Components/Units/ZombifyRequest.cs` (or alongside `SwapBrainRequest`'s file), `Systems/HealthSystemGroup/ZombifySystem.cs`
**Edited:** `Utils/UnitBakingUtil.cs`, `_Vault/Memories/Code/Contracts.md`
**Assets:** Zombie palette column entries in part SOs; optional `ZombifyBehaviour.asset` (phase 3)

## 9. Build phases

1. Request component + `ZombifySystem` + debug trigger → instant conversion works.
2. Palette/tag assets: Zombie skin column authored, `ChangeDesignRequest` path verified visually.
3. Behavior-wrapped conversion beat (animation + delay) — optional, ← DECISION above.
4. Bite-conversion hook in `DamageEventSystem` (zombie-faction killing blow converts instead of kills). ← DECISION: convert-on-death vs convert-on-threshold-damage.

## 10. Verification

DOTSTestScene: debug-key a citizen → same frame: skin swaps to zombie column (visual), `UtilityBrain.unitType` re-keyed (Entities window), `AttackFaction` now targets humans, behavior interrupted then re-decided (unit aggresses). Save/load after conversion → design persists (`PersistedDesign` path already covered by MinionRestore).

## Open decisions (collected)

- [ ] §2 — instant vs behavior-wrapped conversion beat (recommend instant first, wrap in phase 3).
- [ ] §3 — v1 trigger set (recommend debug key + narrative; bite in phase 4).
- [ ] §9.4 — bite converts on death vs on damage threshold.
