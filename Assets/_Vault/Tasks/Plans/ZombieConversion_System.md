# Zombie Conversion System — Design Spec

> **Status:** 🔨 **phase 1 built 2026-09-04** — `ZombifyRequest` + `ZombifySystem` + both v1 triggers (debug menu, narrative `ZombifyAction`) landed. **C# compiles green** — the Editor (busy on another session) recompiled all six touched assemblies at 17:44 on 2026-09-04 with no `error CS` from these files, read out of `Logs/Editor.log`. One Burst error was found and fixed there (`BC1016`, string → `FixedString` inside the job — see [[Gotchas]]), and the next recompile came back clean. ⚠ **NOT rebaked, NOT play-tested.** The gate + play pass is the checklist at [`../Verification/verify-zombieconversion.md`](../Verification/verify-zombieconversion.md). Phase 2 (zombie palette/tag assets) is an owner art task; phases 3–4 deferred, see §11.
> Both halves it composes already existed: `SwapBrainRequest` (`SwapBrainSystem`, from Brain Control Split) and `ChangeDesignRequest` (from the CharacterRig/ColorPalette work — `CharacterRigAuthoring.cs:114`).
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

**DECIDED 2026-09-04 — instant, with a delay knob.** Conversion applies in the frame the request resolves; `ZombifyRequest.delaySeconds` counts down inside the request so a trigger can stage a beat without a behavior asset. Behavior-wrapped conversion stays phase 3 (the interpreter already supports `PlayAnimation` / `WaitTime` / `ModifyMotivation`).

## 3. Entry points

- **`ZombifyRequest : IComponentData, IEnableableComponent`** `{ UnitType targetUnitType; float delaySeconds; }` — baked disabled on convertible units by `UnitBakingUtil` (same block as `SwapBrainRequest`).
- **Triggers — DECIDED 2026-09-04: debug menu + narrative action, both built.**
  - `MonoBehaviours/DebugZombifyMenu.cs` — throwaway OnGUI (the `DebugSaveMenu` precedent): "Nearest to mouse" / "All within Nm", plus a delay stepper.
  - `ZombifyAction : NarrativeActionBase` (`Data/SOs/NarrativeEventSO.cs`) executed by `NarrativeEventManager`, drawn by `NarrativeEventSOEditor`. `EnableComponentAction` was checked first and does **not** suffice — it toggles a component but cannot carry `targetUnitType`/`delaySeconds`, and `NarrativeToggleType` is a closed enum of two provider components. `waitForConversion` holds the narrative group until the request is consumed.
  - Zombie bite stays phase 4 — see §11.

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
4. Bite-conversion hook in `DamageEventSystem` (zombie-faction killing blow converts instead of kills) — **deferred, see §11**.

## 10. Verification

DOTSTestScene: debug-key a citizen → same frame: skin swaps to zombie column (visual), `UtilityBrain.unitType` re-keyed (Entities window), `AttackFaction` now targets humans, behavior interrupted then re-decided (unit aggresses). Save/load after conversion → design persists (`PersistedDesign` path already covered by MinionRestore).

## Open decisions (collected)

- [x] §2 — instant conversion + a `delaySeconds` knob on the request; behavior-wrapped beat stays phase 3.
- [x] §3 — v1 triggers: debug menu + narrative `ZombifyAction` (both built).
- [ ] §9.4 — bite converts on death vs on damage threshold. **Owner call, see §11** — it collides with the corpse-revive path.

---

## 11. Build log — phase 1 (2026-09-04)

**Files**

- **New:** `Components/Units/UnitComponents.cs` → `ZombifyRequest` (next to `Undead`/`ReviveRequest`, which is what it is about); `Systems/HealthSystemGroup/ZombifySystem.cs`; `MonoBehaviours/DebugZombifyMenu.cs`.
- **Edited:** `Utils/UnitBakingUtil.cs` (bakes the request disabled on every brain unit — **archetype change, needs a rebake**), `Systems/SpawnInitSystemGroup/SpawnStateInitSystem.cs` (spawn/pool-reclaim default off — [[Gotchas]] "enableable bits are not reliably copied by ECB.Instantiate"), `Data/SOs/NarrativeEventSO.cs`, `MonoBehaviours/NarrativeEventManager.cs`, `Editor/NarrativeEditor/NarrativeEventSOEditor.cs`, `_Vault/Memories/Code/{Contracts,Components,Systems}.md`.

**Design calls made while building** (beyond the three markers above)

1. **Ordering is `[UpdateAfter(ReviveRequestSystem)]` + `[UpdateBefore(SwapBrainSystem)]`, not "after SwapBrainSystem" as §2 sketched.** Running *before* the swap is what makes the conversion land in the requesting frame (`DesignSystemGroup` runs after Health, so the re-skin lands the same frame too). Running *after* it would be actively wrong: `SwapBrainSystem` consumes the request through an `EndSimulation` ECB, so a swap stamped after it can be disabled again before it is ever read.
2. **In-flight-swap guard instead of a "never interleave" ordering rule.** If a `SwapBrainRequest` is already enabled when `ZombifySystem` runs (a revive stamped one this frame), the job returns with `ZombifyRequest` still enabled and retries next frame, rather than clobbering the revive's swap.
3. **No `ActionInterruptRequest` fired here** — `SwapBrainSystem` already fires it as part of every swap, so the behavior is torn down and re-decided with the new brain. A second fire would be redundant.
4. **`targetUnitType = None` resolves through `UnitDataBlob.becomesUnitType`**, the same authored field the revive path converts through, so no trigger has to know the human→zombie mapping.
5. **`.Schedule()`, not `.ScheduleParallel()`** (§5 said parallel with `[NativeDisableParallelForRestriction]`): conversions are rare one-shots and the `ReviveJob` precedent right beside it is single-threaded, which makes the lookup writes trivially safe.
6. **Palette convention is hardcoded in the job** as `"Skin"` → `"Zombie"` + `AlternateColorMode.Enable` — the convention already documented on `CharacterPalette` / `ChangeDesignRequest`. Per-unit conversion palettes would be a `UnitSO` field, not a request payload.
7. **`Undead` is enabled on conversion** when present, so a converted unit ends in the same state as a reanimated corpse.

**Known gaps this phase does not close**

- **Compiles green (C# + Burst); not rebaked, not play-tested.**
- **No test fixture.** The invariants worth pinning (target resolution, the in-flight-swap defer) need a `World` + a `UnitDataLibrary` blob, i.e. a PlayMode fixture; writing one without being able to run it — or to revert the fix and watch it fail — would violate the project's own test rule. Owed with the gate.
- **Conversion does not survive save/load for non-minions.** `UnitData`/`UtilityBrain` are not `IPersist`; only the design half (`CharacterPalette`, `PersistedDesign`) persists. A converted citizen reloads with its human brain and a zombie skin. Fixing it means persisting the unit type — a Save-system decision, not this spec's.
- **§9.4 bite conversion is an owner call, not just a code task.** Today the game's zombie-creation path is *corpse revive* (`ReviveRequest` → `becomesUnitType`). Converting on a zombie's killing blow puts two different mechanics on the same moment; which one the demo wants — bite converts the living, or death-then-reanimate — decides whether `DamageEventSystem` should divert the killing blow at all.
