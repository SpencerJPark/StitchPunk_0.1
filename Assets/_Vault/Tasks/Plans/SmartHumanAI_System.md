# Smart Human AI + Behaviour Modules — Plan

> **Status:** H0–H3 BUILT 2026-10-17 (see §11), not played yet. H4 next. Owner answers are SH-D1–SH-D9 (§9).
> **Comes before:** CampLoop Phase 6 (raid). Smarter humans make the raid and the rival-engineer fight better.
> **Read first:** `Memories/Code/Systems_AI.md` (pipeline, the "Adding a new behavior domain" checklist), `Plans/RangedCombat_System.md` (guns, phase H5), `Plans/CrowdScaleAwareness_System.md` (scale, phase H6).

## 1. Why the bandits feel boring today (grounded 2026-10-17)

- **They never wander.** `NavigationAwarenessSystem` only emits Wander toward **waypoints** in the spatial hash, and `CampLoop_Sub` has none. The sandbox objects removed when the scene was built were the citizens' destinations.
- **Nothing to do when calm.** The camp has no Sit, Eat or music spots, so need-driven options never appear.
- **Slow to react.** `EnemyAwarenessSystem` runs only while a unit's `ActionRequest` is enabled, which is between behaviours. A bandit on a walk doesn't notice you until the walk ends.
- **No AI ranged option.** Only the player can throw (`ThrownItemSystemGroup`). Guns need the unbuilt projectile system.
- **Adding an ability is scattered.** It means editing a `BrainSO` action list, plus a `BehaviorSO`, plus maybe an awareness system. There is no single "this ability" asset, and no shared human base brain.

What already works and is reused: the awareness → scoring → winner → behaviour-interpreter pipeline, and these behaviours:
- Wander, Sit, Talk, Flee, melee single/continuous;
- item pickup for EquipWeapon (threatened + unarmed), UseHealingItem, Eat, Drink;
- self-defence on hit;
- `ThrownItemSystem` / `ThrownItemHitSystem` (player throws, proximity hit into the DamageBus).

## 2. Goals

1. **Behaviour modules:** reusable abilities you add to a brain (Throw Rock, Listen to Music, Eat, Heal, Patrol…) as one asset each (SH-D1).
2. **Camp life:** patrol, sit at the campfire, eat/drink from supplies, listen to music (SH-D3).
3. **Weapons:** rocks now (pick up, throw, land, pick up again), guns right after (SH-D2).
4. **Smart humans:**
   - grab a weapon when unarmed (before or mid-fight);
   - keep range with ranged weapons;
   - heal or retreat when hurt;
   - alert the camp (SH-D4).
5. **Dumb zombies:** melee only, react on contact, cheap (SH-D5).
6. **Scale:** ~50 smart humans + ~500 zombies (SH-D6).

## 3. Phases

| Phase | What | Depends on |
|---|---|---|
| **H0** | **Behaviour modules + brain inheritance** (data/bake only, no behaviour change) | — |
| **H1** | **Camp life**: waypoints + activity spots in the camp, Listen-to-Music module, idle that stays inside the guard area | H0 |
| **H2** | **Think rate + alert**: humans re-evaluate mid-behaviour on a staggered timer; a spotter alerts same-faction allies | H0 |
| **H3** | **Rocks**: Rock item, scattered rocks, Throw module (AI throw through the existing thrown-item pipeline), Grab Weapon now includes rocks and works mid-fight | H0, H2 |
| **H4** | **Tactics**: Keep Range, Heal When Hurt (combat priority), Retreat to Allies | H2, H3 |
| **H5** | **Guns**: build `RangedCombat_System.md` (projectiles), Gun item, Shoot module (Keep Range reused) | H4 + its own spec decisions |
| **H6** | **Zombie trim + scale pass**: zombie brain = melee + orders; profiling at 50/500; spatial-hash hostile queries; stagger tuning | H2 (can start in parallel) |

Each phase ships compile-gated with PlayMode fixtures (revert-to-fail with Burst off) and adds a section to a `verify-smart-ai.md` play checklist.

## 4. H0: Behaviour modules (the extensibility core)

**`BehaviourModuleSO`** (`Data/SOs/`, new): one ability, everything it needs in one place.

```
BehaviourModuleSO
  displayName, description              // shows in the inspector so a brain reads like a feature list
  UtilityActionSO action                 // scoring: actionType, priority tier, considerations
  BehaviorSO behaviour                   // execution: the command sequence
  ModuleTags requires                    // e.g. NeedsHands, Ranged, Social — for validation + later editor filtering
  module settings (small struct)         // per-module tunables read by its awareness, e.g. throw range, alert radius
```

**`BrainSO`** gains:
- `BrainSO baseBrain`: inheritance. `HumanBaseBrain` → `BanditBrain`, `CitizenBrain`. `ZombieBaseBrain` → `PlayerZombieBrain` variants.
- `List<BehaviourModuleSO> modules`: added on top of the base. The existing `actions` list stays during migration.

**Baking:** `BrainLibraryBakingSystem` flattens the base chain plus modules into today's per-unit-type action list. The blob layout and the runtime are unchanged, so H0 can't break behaviour. Bake validation reports:
- duplicate action types;
- a module whose behaviour isn't in `_BehaviorLibrary`;
- a base-brain cycle.

**Migration:** wrap each existing action in a module (Wander, Sit, Talk, Eat, Drink, Heal, EquipWeapon, Flee, MeleeSingle, MeleeContinuous), then build:
- `HumanBaseBrain`: social + needs + items + flee + melee;
- `BanditBrain`: HumanBase + combat modules;
- `ZombieBaseBrain`: melee only.

`UnitBrainCoverageTests` already guards the "every controllable unit has a brain" invariant.

**Per-unit uniqueness** (e.g. one bandit who plays music) is out of scope (SH-D1 = brains only). Make a `MusicianBanditBrain` that inherits `BanditBrain` + 1 module.

## 5. H1: Camp life

- **Patrol:** place `WaypointAuthoring` points inside the guard idle radius (4–6 per camp). `NavigationAwareness` then produces Wander naturally. `GuardPostAwarenessSystem` keeps its leash and stays a fallback.
- **Campfire:** `InteractionAuthoring` seat spots around the fire, using the existing Sit behaviour (Energy / Comfort).
- **Supplies:** a supply crate `InteractionAuthoring` satisfying Hunger/Thirst. Eat and Drink currently come only from consumable items, so the module adds an "eat at interaction" path, which is a variant of Sit with ModifyMotivation Hunger.
- **Listen to Music** (new module):
  - A gramophone interaction point that satisfies **Fun**.
  - Behaviour: Approach the spot (slot around it) → PlayAnimation idle-sway loop → WaitTime 10–20 s → ModifyMotivation Fun +60 → Release.
  - Several bandits can share it (`maxUsers` 4).
- **Need tuning:** the Bandit SO's motivations and decay rates should make these happen every 30–90 s. ← DECISION: decay so a calm camp cycles patrol → fire → food → music about every minute (recommended).
- **Animations:** units have no animation profile right now (`MaleCitizen.profile.asset` was deleted in `c07dfd75`). Behaviours run, but sit, sway and throw won't show until a profile exists. This is the owner's art track, not a blocker.

## 6. H2: Think rate + alert

- **`ThinkRate` component** (from the brain or module settings):
  - Humans: `interval = 0.25 s`, phase-staggered by entity index so ~1/15 of humans think each frame at 60 fps.
  - Zombies: no ThinkRate, so they stay request-gated and cheap (SH-D5).
- **`ThinkScheduleSystem`** (UtilityMotivation group, before awareness) enables `ActionRequest` when a human's think timer elapses. That makes awareness, Enemy/Item/Flee, re-run mid-behaviour. `WinnerSelection` already preempts only when the new winner **outranks** `activePriority`, so a re-think never restarts the same action (same-action guard).
- **Alert the camp** (module "Raise Alarm"):
  - When a human first acquires a `CombatTarget`, it writes an `AllyAlert { target, position }` to same-faction units within `alertRadius` (15 m).
  - Alerted units get a `ThreatEntry` on that target (so the existing self-defence and combat awareness picks it up) and an immediate think.
  - One alert per spotter per 5 s.

## 7. H3: Rocks

- **Item:** `ItemType.Rock` (append), a Throwable category, a `ThrowableItem { damageSource, minRange, maxRange, speed }` blob entry in `ItemLibrary`, and a rock prefab (pickable `ItemAuthoring`). Rocks are scattered around the camp, and loose rocks anywhere count.
- **Grab Weapon** (extends `ItemAwarenessSystem` EquipWeapon):
  - candidates include throwables;
  - mid-fight it fires only when the item is closer than the target (or the target is beyond melee) and within 6 m;
  - before a fight, alerted or threatened units grab first.
  - ← DECISION: mid-fight pickup max detour 6 m (recommended).
- **Throw module:**
  - `ThrowAwareness` emits a ProjectileSingle-tier option when holding a throwable and the target is within [min, max] range (rock: 3–12 m).
  - `ThrowBehaviour`: FaceTarget → PlayActionAnimation (throw) → WaitForAnimEvent (Release, fallback 0.35 s) → **`RequestThrow`** (new behaviour command).
  - `RequestThrow` detaches the held item and gives it `ThrownItem` velocity toward the target's predicted position. It reuses `ThrownItemSystem` / `ThrownItemHitSystem` (DamageBus). The landed rock becomes a loose pickable item again.
  - **Rock damage uses the existing `Throw` attack (20)**, which is more than every melee attack: Punch 5, Claw 8, Slash 15. The owner's rule (SH-D9): a rock beats a fist. Cooldown 1.5 s; re-fetching the landed rock is the natural rate limiter.

## 8. H4–H6 sketch

- **Keep Range:**
  - A unit holding a ranged item, whose target is closer than `minRange`, emits a back-off move to a point at `preferredRange` away from the target, at combat tier +0.
  - It uses `FormationSlots`-style spread, so a group of throwers fans out instead of stacking.
- **Heal When Hurt:** `UseHealingItem` gets a combat-time priority bump below 35 % HP when a healing item is within 8 m and no enemy is within 3 m.
- **Retreat to Allies:**
  - At < 30 % HP and outnumbered (hostiles within 10 m > allies within 10 m), fall back toward the nearest ally cluster or the guard anchor.
  - Flee awareness already handles citizens; humans with Bravery 1 use Retreat instead of Flee.
- **Guns (H5):** build `RangedCombat_System.md` (its ← DECISIONs still open: projectile prefab source, pooling), then a Gun item + Shoot module. Keep Range is reused with gun ranges. Ammo is out of v1.
- **Zombies + scale (H6):**
  - Zombie brain = melee + player orders only; no item, flee or social modules.
  - Target: 50 humans × 4 Hz staggered + 500 zombies request-gated.
  - Change `EnemyAwareness` from a full faction-list scan to the spatial hash (today it is O(units × hostiles)).
  - Profile against `CrowdScaleAwareness_System.md` and fold that spec in.

## 9. Decisions recorded (owner, 2026-10-17; do not re-ask)

| Id | Decision |
|---|---|
| SH-D1 | Reusable abilities are **behaviour modules on brains**; brains can inherit a base. No per-instance modules. |
| SH-D2 | **Rocks now, guns right after** (guns need the projectile system). |
| SH-D3 | Camp life: **patrol/wander, sit at campfire, eat/drink from supplies, listen to music**. |
| SH-D4 | Human tactics: **grab weapon when unarmed (pre- or mid-fight), keep range with ranged weapons, heal/retreat when hurt, alert the camp**. |
| SH-D5 | **Zombies: melee only, react on contact**, no items, no tactics, cheap. |
| SH-D6 | Scale target **~50 smart humans + ~500 zombies**. |
| SH-D7 | Modules are **Inspector assets now**; a visual brain/module editor is a separate later spec. |
| SH-D8 | This comes **before CampLoop Phase 6** (raid). |
| SH-D9 | **Thrown rocks out-damage melee**. They reuse the `Throw` attack (20) against Punch 5 / Claw 8 / Slash 15. |

## 10. Build approach

Same as CampLoop: orchestrator plus parallel Sonnet `worker`s of two files max, `devloop` gate per wave, Editor compile + PlayMode fixtures per phase, revert-to-fail with Burst off, a Sonnet `verifier` on each phase's runtime logic, then commit per phase. Units and gather/interaction points go at the **scene root**, never parented (see the Gotchas.md entry from 2026-10-16).

## 11. Build log

**2026-10-17: H0 + H1 built** (51324624, plus the follow-up re-curve).
- **H0, behaviour modules:** BehaviourModuleSO; BrainSO.baseBrain + modules + CollectActions; the bake flattens. 11 modules; HumanBaseBrain and ZombieBaseBrain. Flattened action sets were verified identical before and after, apart from the two new human modules. BrainCompositionTests go red when the base walk is skipped.
- **H1, camp life:**
  - EatFromSupplies and ListenToMusic, as ActionType, BehaviorType, actions, behaviours and interactions.
  - Chair is now open to Bandits.
  - Bandit needs decay on about a 1-minute cycle.
  - CampLoop_Sub camp: 5 waypoints, 4 campfire seats, a crate and a gramophone, all root objects.
- **Verifier finding:** motivation curves only see x = 0.5–1, so need actions lost to Wander. Sit, EatFromSupplies and ListenToMusic were re-curved (Gotchas.md). Occupancy (`maxOccupants`) is not enforced by InteractionAwareness; more bandits than slots may queue at one spot. Needs only decay while a unit is choosing (ActionRequest), which slows the cycle.
- **Not played yet.** No animations until units get a profile again.

**2026-10-17: H2 + H3 built.**
- **H2 premise correction:** nothing ever disabled `ActionRequest`, so humans already thought every frame; the slow reaction was not request-gating. H2 therefore *throttles* instead: `ThinkRate` (from `UnitSO.thinkInterval`, Bandit + MaleCitizen 0.25 s, golden-ratio stagger) and `ThinkScheduleSystem` enable ActionRequest only when idle or due. `MotivationDecaySystem` is no longer gated on ActionRequest. Zombies keep thinking every frame until H6 sets their interval.
- **Raise Alarm** is a `UnitSO.alertRadius` setting (Bandit 15 m), not a module: H0 modules carry actions only. `AllyAlertSystem`: a spotter with a combat target (not Flee) alerts same-faction allies in radius (5 s cooldown); an alerted ally emits its attacks at priority 3 against that target for 6 s, ignoring its own awareness range, and re-thinks immediately.
- **H3 rocks:**
  - `ItemType.Rock` is appended; Rock.asset deals 20 throw damage, speed 14, AI range 3–12 m (`ItemSO.aiThrowMin/MaxRange`).
  - `Prefabs/Items/Rock.prefab` is built from the DOTSTestScene rock. 8 "Camp Rock" root objects sit 5–9 m around the camp.
  - **Grab weapon:** ItemAwareness grabs mid-fight at priority 4 when the weapon is within 6 m and closer than the target, or the target is beyond 2.5 m. Before a fight it also grabs when only threatened or engaged. It skips items that are still airborne.
  - **Throw:** `ThrowAwarenessSystem` emits `ActionType.Throw` at priority 4 when the unit holds an AI-throwable and the target is in range. `ThrowBehaviour` runs PlayActionAnimation → Wait 0.35 → `RequestThrow` → Wait 1.15. `AiThrowRequest` then goes to `AiThrowItemSystem`, which unparents the item and launches it at the target.
  - **Module:** the Throw Rock module is on BanditBrain.
- **Hit changes:** thrown hits now carry the thrower as the damage source, so victims fight back, and they skip same-faction targets.
- **Fixed on the way:** items never baked `PlayerInteractable`, so thrown items didn't fly; see Gotchas.
- **Tests:** SmartHumanAiTests has 4 PlayMode tests, all proven red with Burst off. EditMode passes 84/84 and PlayMode 63/63.
- **Known gaps:**
  - The throw aims at the target's current position, with no lead.
  - There's no face-target command.
  - A Throw chosen just as the target dies still waits out about 1.5 s.
