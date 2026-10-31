---
title: SmartHumanAI H4 — Tactics
status: spec ready
created: 2026-10-31
area: AI
parent: "[[SmartHumanAI_System]]"
---

# SmartHumanAI H4 — Tactics (Keep Range, Heal When Hurt, Retreat to Allies)

> **Status:** 🔨 built 2026-10-31. The code, assets and 3 camp bandages are in, PlayMode 133/133 and EditMode 204/204 pass, and the 5 new tests were proven red with Burst off. The owner play pass is owed (`verify-smart-ai.md` § H4). Decisions H4-D1–D10 are architecture calls recorded under the standing delegation rule. None needed the owner, because SH-D4 already settles the product side ("keep range with ranged weapons, heal/retreat when hurt").
> **Parent:** `SmartHumanAI_System.md` §8 (sketch), §9 (SH-D1–D9). **Read first:** `Memories/Code/Systems_AI.md`.

## 1. Context

H3 gave bandits rocks: grab, throw at 3–12 m, melee otherwise. H4 adds the three tactics SH-D4 names. All three are **awareness options on the existing pipeline**. They add no new groups, no request components and no new behaviour commands. Every new system goes in `UtilityAwarenessSystemGroup`, and `SystemGroups.cs` is not touched (another session has it open for Terrain).

## 2. Skills Needed

`dots-system-scaffold` (RetreatAwarenessSystem), `dots-test` (PlayMode fixtures). Assets (actions, behaviours, modules, prefab) are authored in the Editor through `execute_code`, so Unity writes the GUIDs.

## 3. Decisions (recorded, H4-D1–D10)

| Id | Decision |
|---|---|
| H4-D1 | **Keep Range and Retreat are new `ActionType`s** (`KeepRange`, `Retreat`, appended) with their own `BehaviorType`s and behaviours. They are not Wander: GuardPost reads "Wander to a point near the anchor" as *walking home*, and a Retreat to the anchor would be mistaken for the leash return. |
| H4-D2 | **Neither is a combat action** (`IsCombatAction` unchanged). The leash never strips them, and the GuardPost leash (tier 5) still beats both. |
| H4-D3 | **Both are emitted at `AIPriorityTier.Critical`** (authored tier Defend). Tiers are the only preemption path (AF-D1), and both must break off a live tier-3 melee fight. Throw (also Critical) can't collide with Keep Range: Throw needs distance ≥ min range and Keep Range needs < min range. |
| H4-D4 | **Keep Range lives in `ThrowAwarenessSystem`.** It already resolves the held throwable, its range and the target. Destination: target + (away direction rotated by a per-unit fan angle of −40°…+40° in 20° steps, from the entity index) × preferred range. Preferred range = `min + 0.3 × (max − min)` (rock: 5.7 m). The fan is the "FormationSlots-style spread", so throwers don't stack on one point. |
| H4-D5 | **Keep Range has a 4 s per-unit cooldown** (`TacticsCooldown.keepRangeReadyAtSeconds`, a new component baked on every brain next to `ThinkRate`). Without it, a melee chaser at running speed turns the thrower into an endless kiter that never throws. It is written the frame the option is offered. |
| H4-D6 | **The guard leash clamps Keep Range** (closes the prompt's open question). For a unit with `GuardPost`, a destination farther than `0.8 × leashRadius` from the anchor is pulled back onto that circle, so backing off never walks a guard out of its leash and into the tier-5 return. |
| H4-D7 | **Heal When Hurt is a context bump in `ItemAwarenessSystem`.** UseHealingItem goes to Critical when: the unit is in a combat context (threat, combat option, or live combat action), health < 35 %, the heal item is ≤ 8 m away, and no live hostile (threat attacker or combat target) is within 3 m. Otherwise it keeps its authored Ambient tier. |
| H4-D8 | **Retreat yields to critical Flee through one shared predicate.** `FleeAwarenessSystem.WouldBreakOffToFlee(healthRatio, bravery)` holds the existing formula, and Retreat is skipped whenever it is true. Cowards flee and brave units retreat, so the two Critical options never compete. |
| H4-D9 | **Retreat rule:** health < 30 %, has a threat or combat option, and outnumbered (live hostiles within 10 m > live same-faction allies within 10 m). The destination is the ally nearest to self, within awareness range, that stands at least 3 m farther from the nearest hostile than self does, plus a `FormationSlots.RoundedSlotOffset` slot. Fallback is the GuardPost anchor if it is farther from that hostile than self. With neither, there's no option and the unit fights on. Arriving next to allies ends "outnumbered", so the unit re-joins the fight. That is the hysteresis. Gated on `ActionRequest` (4 Hz is fast enough for a Critical break-off). It is O(faction list), the same as EnemyAwareness, until H6 moves it to the spatial hash. |
| H4-D10 | **Who gets what:** `KeepRangeModule` + `RetreatModule` go on **BanditBrain** only. Citizens keep Flee (parent §8). The heal bump applies to every brain with UseHealingItem (HumanBase). **Healing items in play:** `Prefabs/Items/Bandage.prefab` (built like Rock.prefab), with 3 "Camp Bandage" root objects by the CampLoop supply crate. Nothing places a healing item in the game today, so without them the heal tactic can never fire. |

## 4. Data model

- `ActionType.KeepRange`, `ActionType.Retreat`; `BehaviorType.KeepRange`, `BehaviorType.Retreat` (append-only, `Data/Enums/AiEnums.cs`).
- `TacticsCooldown : IComponentData { double keepRangeReadyAtSeconds; }`, in `Components/AI/ThinkComponents.cs`, baked in `UnitBakingUtil` beside `ThinkRate`.
- Assets: `KeepRangeBehaviour` / `RetreatBehaviour` (`Approach`, FloatParam stop 0.5 / 1.0, IntParam `StanceType.Running`; no cleanup), `KeepRangeAction` / `RetreatAction` (priority Defend 3, one consideration copied from ThrowAction), `KeepRangeModule` / `RetreatModule`, both added to BanditBrain. Both behaviours go into `_BehaviorLibrary`.

## 5. Systems

| System | Change |
|---|---|
| `ThrowAwarenessSystem` | + Keep Range branch (H4-D4/D5/D6); reads optional `GuardPost`, writes own `TacticsCooldown` |
| `ItemAwarenessSystem` | + heal bump (H4-D7) |
| `FleeAwarenessSystem` | extract `WouldBreakOffToFlee` (behaviour-identical) |
| `RetreatAwarenessSystem` (new) | H4-D9; after Enemy/SelfDefence/Flee, before GuardPost |

## 6. Build phases

1. **Data:** enums, `TacticsCooldown` + bake. Editor recompile (the offline gate can't see new Data members from Systems until then).
2. **Systems, parallel workers:** (a) Throw/KeepRange, (b) ItemAwareness heal bump, (c) Flee predicate + RetreatAwarenessSystem. One gate after the wave.
3. **Assets** via `execute_code`: behaviours, actions, modules, library and brain wiring, Bandage prefab, 3 camp bandages.
4. **Tests** (`Tests/PlayMode/SmartHumanAiTacticsTests.cs`): Keep Range offered at Critical with the destination ≈ preferred range away, and not re-offered inside the cooldown; heal bumped to Critical, but not with a hostile at 2 m; Retreat offered for a brave outnumbered unit and not for a coward. Each is proven red with Burst off.
5. **Live check** in CampLoop via `execute_code`, then docs, memory, commit.

## 7. Verification (owner, appended to `verify-smart-ai.md`)

- [ ] CampLoop: walk up to a rock-holding bandit. It runs back about 5 m (fanned, not in a line), then throws.
- [ ] Hurt a bandit below ~35 % while standing back. It runs for a camp bandage and heals.
- [ ] Hurt a bandit below 30 % with two zombies on it and allies at the camp. It runs to them, then fights again alongside them.

## 8. Open decisions

None. All are recorded above.
