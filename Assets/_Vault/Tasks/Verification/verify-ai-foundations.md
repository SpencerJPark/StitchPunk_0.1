---
title: Verify AI Foundations pass
status: awaiting play-test
created: 2026-10-18
area: AI
---

## Goal
Confirm the 2026-10-17 AI Foundations pass (spec: [AIFoundations_System.md](AIFoundations_System.md)) behaves the same as before where intended, and that exclusive targets and overrides now work. Compile, EditMode 86/86 and PlayMode 66/66 are already green; this checklist covers what only play can show.

## Setup
- [ ] Reopen the subscenes or enter Play once to **rebake**: brain blobs (priority variants), behaviour blobs (`minimumInterruptPriority`), the remapped motivation curves and the module `actions` lists all need it.
- [ ] Console after the bake: no `[BrainLibraryBaking]` "has no actions" warnings for the 12 modules.

## Scene: `Assets/Scenes/CampLoop.unity` (bandit camp)
- [ ] **Camp needs still win:** bandits sit, eat from supplies and listen to music about as often as before (the curve remap is exact; the per-consideration makeup slightly favours two-consideration needs).
- [ ] **One per chair:** watch two idle bandits near a free single-seat chair. Only one walks to it, and the other picks something else within about a frame.
- [ ] **Shared spots stay shared:** up to 4 at the gramophone, 2 at the supply crate (InteractionSO `maxOccupants`).
- [ ] **Rocks:** two bandits never run for the same loose rock. A thrown rock is re-fetched by one bandit only.
- [ ] **Override:** walk the player (or a hostile) into a bandit's awareness while it sits. Within about 0.25 s it drops the chair and attacks (tier 3 beats tier 1).
- [ ] **Idle is not twitchy:** idle bandits still choose a new activity as soon as one finishes. There should be no visible delay from the 0.1 s idle re-think.
- [ ] **Flinch → fight / flee:** hit a citizen or bandit. It fights back after the 0.3 s flinch, and a badly hurt coward breaks off and flees. ThreatDecay now ticks the flinch.
- [ ] **Leash:** a guard chased past its leash still walks home.

## Minions
- [ ] An ordered minion sent to an occupied chair or item still goes (player orders bypass capacity).
- [ ] An uncommanded minion still self-defends after being hit.
