---
title: Verify — Minion Order Robustness
status: active
created: 2026-09-15
area: code
---

## Goal

Confirm player orders survive units that are not `MeleeSingle`-only, and that the two new verbs work. An attack order
now resolves at order time to the first `AvailableAttack` entry whose `ActionType` has an action def in the unit's brain
(`AIUtils.ResolveOrderedAttack`); no such entry refuses the order with a Warning. Built 2026-09-15 in the A102 / Despawn
/ Minion Orders parallel batch (spec: [`MinionOrderRobustness_System.md`](MinionOrderRobustness_System.md), §12.4 log).
Proven only by `StitchPunk.Tests.AttackResolutionTests` (EditMode, revert-to-fail); no scene run.

**Worth knowing before you play:** every unit asset today carries one attack, action `MeleeContinuous` (17), and
`PlayerZombieBrain` has a `MeleeContinuous` def but no `MeleeSingle` def. The old hard-coded `MeleeSingle` lookup
therefore missed on zombie minions and dropped their attack orders; they should now attack.

**Before anything: rebake.** `UnitBakingUtil.AddPlayerControlled` bakes `OnMinionStopCommand` and `OnMinionReturnCommand`
(disabled) on every player-controllable unit. Reopen the subscene or re-enter Play.

## Play checks (`DOTSTestScene`)

- [ ] **Attack order.** Select a zombie minion, attack-order an enemy: it approaches and attacks. The console line
  `[MinionOrder] Unit N -> Attack T with <ActionType>` names the attack it picked (expect `MeleeContinuous`).
- [ ] **Refusal.** Give a test minion only an `AvailableAttack` entry its brain has no def for and order an attack: expect
  the Warning `refused Attack … for <UnitType>`, and the minion keeps doing what it was doing.
- [ ] **X = Stop.** X, R and F are read inside the command input, like Follow: **hold X while you right-click** to issue a
  command mid-approach. The unit drops to Idle and AI resumes normally afterwards.
- [ ] **R = ReturnToPlayer.** **Hold R while you right-click** with a minion across the map: it paths to where the player
  stood that frame and stops there (one-shot, not a follow).
- [ ] **F still follows** continuously, unchanged.
- [ ] **Keys feel.** Is "hold a key + right-click" what you want for Stop/Return, or should X and R act on their own
  press? (A standalone key would need its own input action; not built.)
