---
title: Damage Reaction — fight, flee, retreat, help or forgive
status: spec drafting
created: 2026-10-31
area: AI
---

# Damage Reaction System — fight, flee, retreat, help or forgive

> **Status:** 📝 spec drafted 2026-10-31. **Four owner product calls are open (DR-Q1–Q4, §9).** Architecture calls DR-D1–D10 are recorded. Build after SmartHumanAI H5; this spec **replaces RC-D8** ("friendly hits make no ThreatEntry").
> **Read first:** `Memories/Code/Systems_AI.md` (tiers, SelfDefence/Flee/Retreat/AllyAlert, Living Humans), `SmartHumanAI_H4_System.md`, `RangedCombat_System.md` §0.
> **Absorbs:** AF-P6 (perception cache) and `CrowdScaleAwareness_System.md` for the "who is near me" queries this needs.

## 1. Goal (owner, 2026-10-31)

When a human is hurt, by an enemy, an ally, or a stray rock, it chooses to **fight, run, fall back to allies, or help**, weighing several factors instead of fixed cut-offs. A human who **sees** someone attacked makes the same choice. Friendly fire goes through the same choice, so baiting enemies into hurting each other is a real strategy, but one stray hit doesn't empty the camp.

## 2. Today (what this replaces)

| Piece | Today | Problem |
|---|---|---|
| `SelfDefenceAwarenessSystem` | Always fights back after a 0.3 s flinch | No choice |
| `FleeAwarenessSystem` | Flee at tier 3; break-off at < 30 % HP by `(1−hp)(1−bravery) > 0.35` | One formula |
| `RetreatAwarenessSystem` (H4) | < 30 % HP and outnumbered → safer ally | Hard cut-off |
| `AllyAlertSystem` (H2) | Bandit spotter → allies within 15 m attack | Bandits only; no witnesses |
| `DamageEventSystem` | ThreatEntry only for hostile-faction sources | Friendly hits are ignored (RC-D8) |
| Drunk Brawl (LH) | Ambient brawl, non-lethal clamp at 35 % | Only from drink, never from a hit |

Each system bumps its own tier, so the highest number wins. It is a ladder, not a decision.

## 3. Design

**3.1 One appraisal, many executors.** `ReactionAppraisalSystem` (UtilityAwareness, before SelfDefence/Flee/Retreat) decides the **response** and writes `ReactionState { ReactionResponse response; Entity subject; float committedUntil; float score; }` on the unit. The existing systems keep their execution logic, but they **emit only when `ReactionState.response` names them**:

- Fight → SelfDefence
- Flee → Flee
- Retreat → Retreat
- CallHelp / DefendOther → AllyAlert generalised
- Brawl → the Brawl option
- Forgive → none (glare emote)

Their hard thresholds become appraisal inputs. The tiers stay: the response maps to a tier (Fight = Defend, Flee/Retreat break-off = Critical), so preemption still follows AF-D1.

**3.2 When it runs (event-driven, cheap).**
- On a **new hit**: `DamageEventSystem` appends `DamageReceived { source, amount, isFriendly, wasTargeted, time }` to the victim. This is written for every sourced hit, friendly included, and the faction gate moves out of DamageEventSystem into the appraisal.
- On a **witnessed attack** (§3.4).
- Every **think** while the response is not None, so it can change its mind as health or the odds shift.

**3.3 Factors → response scores.** Each response is a utility from considerations: curves on the existing `ConsiderationAuthoring`, authored on a new `ReactionProfileSO` per brain and baked to a blob. Brains differ by data, not code (SH-D1).

| Factor | Source | Leans toward |
|---|---|---|
| Health ratio | `Health` | low → Flee / Retreat |
| Bravery | personality | high → Fight / DefendOther |
| Temper (Anger) | personality | high → Fight, Brawl (friendly) |
| Intoxication | `Intoxication` | Brawl, less Flee |
| Armed, and the weapon's value vs the attacker's | `UnitEquip`, `aiWeaponValue` | armed → Fight; out-gunned → Flee / Retreat |
| Allies vs hostiles within 10 m | perception cache | outnumbered → Retreat / CallHelp |
| Attacker kind (enemy / ally / necro / zombie / environment) | faction + unit type | per DR-Q2 |
| Accidental vs deliberate | `DamageReceived.wasTargeted` | accidental friendly → Forgive |
| Grudge vs this ally | `Grudge` buffer (§3.5) | rising → Brawl → Fight |
| Guard duty | `GuardPost` | guards lean Fight / DefendOther |

**3.4 Witnesses (help).** `DamageEventSystem` also writes the frame's sourced hits to a `WitnessEvents` singleton list: victim, attacker, position. `WitnessSystem` uses the spatial hash to give each human within `witnessRadius` (8 m; 15 m for alert-raisers) a `WitnessedAttack` entry, with no line-of-sight check in v1. The appraisal then scores **DefendOther** (attack the attacker), **CallHelp** (run to the nearest guard or ally cluster and raise the alarm), **Flee**, or ignore. The victim's relationship to the witness matters: same faction, or a Friendly-trait conversation partner. `AllyAlert` becomes CallHelp's executor, and bandits keep their spotter alarm as a high CallHelp prior.

**3.5 Friendly fire, with a damper.**
- A friendly hit adds to a per-ally `Grudge { Entity ally; float score; double lastHitTime; }` buffer. A deliberate hit adds 1.0 and an accidental one 0.35 (the attacker was targeting someone else). Grudges decay at 0.1/s.
- Below 1, Forgive dominates (glare/curse emote via the existing emote pool). Between 1 and 2, Brawl wins: the existing non-lethal 35 % clamp, a fistfight. Above 2, if deliberate, it becomes a hostile ThreatEntry and a lethal Fight, per DR-Q3.
- Temper and drink shift those thresholds down.

**3.6 Commitment.** A chosen response holds for at least `committedUntil` (2 s). It can be dropped early only on a large change: a health drop of ≥ 20 %, a new attacker, or the target dying. This keeps fight/flee flip-flopping out of the tier system.

## 4. Decisions recorded (DR-D1–D10)

| Id | Decision |
|---|---|
| DR-D1 | One appraisal system decides; existing systems execute (§3.1). No new tier numbers. |
| DR-D2 | `DamageReceived` is written for every sourced hit; the faction gate moves out of DamageEventSystem (replaces RC-D8). |
| DR-D3 | Per-brain `ReactionProfileSO` blob (curves), so citizen vs bandit vs guard is data. |
| DR-D4 | Witnesses through a frame-local `WitnessEvents` list and the spatial hash, 8 m, no line of sight in v1. |
| DR-D5 | Grudge buffer + decay is the friendly-fire damper (§3.5). The 35 % brawl clamp is reused. |
| DR-D6 | Commitment window 2 s, with early-out on a large change (§3.6). |
| DR-D7 | Zombies don't use it (SH-D5: dumb and cheap); they keep `SelfDefence`. Minions keep `MinionSelfDefence` (player orders win). |
| DR-D8 | Accidental = the attacker's `StateMachine.targetEntity` wasn't the victim at fire time. The projectile carries `intendedTarget`, so locked H5 shots count. |
| DR-D9 | Event-driven plus think-rate re-appraisal. Allies/hostiles-near counts come from the AF-P6 perception cache, built in phase 1 and shared with Retreat. |
| DR-D10 | Every response writes an emote (fight ✊, flee 💨, help ❗, forgive 😠), so the player can read the decision. Emote icons are reused or added in `EmoteIcon`. |

## 5. Systems and files

- **New:** `ReactionAppraisalSystem`, `WitnessSystem`, `PerceptionCacheSystem` (AF-P6), `GrudgeDecaySystem`.
- **New components:** `ReactionState`, `DamageReceived` (buffer), `WitnessedAttack` (buffer), `Grudge` (buffer), `WitnessEvents` (singleton).
- **New data:** `ReactionProfileSO` → `ReactionProfileBlob`. This is the `dots-blob-library` five-file pipeline, referenced by `BrainSO`.
- **Edited:** DamageEventSystem (DamageReceived + WitnessEvents, faction gate out), SelfDefence / Flee / Retreat / AllyAlert / DrunkAwareness (emit only on their response; thresholds removed), Projectile (`intendedTarget`).
- **Skills:** `dots-blob-library`, `dots-system-scaffold`, `dots-test`.

## 6. Phases (worktree; merges agreed with peers)

1. **Perception cache:** allies/hostiles within 10 m per human, once per think. Retreat moves onto it (it's a performance win on its own).
2. **DamageReceived + ReactionState + appraisal** for Fight/Flee/Retreat only. Behaviour must stay identical with profile curves fitted to today's formulas; characterise first, then change.
3. **Friendly fire:** Grudge, Forgive/Brawl/escalation, DR-Q3.
4. **Witnesses:** DefendOther / CallHelp, and AllyAlert folded in.
5. **Tuning pass** with the owner (curves, emotes) and the `verify-damage-reaction.md` checklist.

## 7. Tests

- An appraisal with fitted curves picks the same response as today's Flee/Retreat formulas across a grid of health × bravery.
- A friendly accidental hit → Forgive; a second and third deliberate hit → Brawl, then Fight (DR-Q3).
- A witness within 8 m of an attack gets a WitnessedAttack; a brave armed witness picks DefendOther, a coward picks Flee.
- The commitment window holds a response for 2 s unless health drops by ≥ 20 %.

## 8. Risks

- **Cascades:** one shot → brawl → witnesses join → riot. The damper, plus "witnesses of a *brawl* don't DefendOther", caps it.
- **Tuning debt:** curves replace 4 hand formulas. Phase 2 therefore fits them to today's behaviour first.
- **Cost:** witness fan-out on mass melee. It's capped at N witnesses per event (8) and only humans witness.

## 9. Open owner decisions ← DECISION

- [ ] **DR-Q1: How readily do citizens help?** *Recommended:* citizens step in only when armed, or brave (> 0.6) with the victim a conversation partner; otherwise they CallHelp (run for a guard) or Flee. Bandits and guards help readily.
- [ ] **DR-Q2: Is the necro a special attacker?** *Recommended:* yes, "dread". Being hit by the player or a zombie raises Flee and CallHelp for citizens; bandits treat it as a normal enemy.
- [ ] **DR-Q3: Can friendly fire turn lethal?** *Recommended:* only after repeated **deliberate** hits (grudge > 2). Accidents top out at a brawl.
- [ ] **DR-Q4: Who gets the system?** *Recommended:* every human (citizens, bandits, guards). Zombies and minions are excluded (DR-D7).
