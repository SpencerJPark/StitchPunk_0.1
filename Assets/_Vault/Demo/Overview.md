---
tags: [demo, roadmap, overview]
related: "[[Demo/Phases]], [[Tasks/Claude/Code_Systems]], [[Tasks/Spencer/Design_Decisions]]"
---

# Stitch Punk — Demo Overview

**~1 hour demo · 6 phases · 18 scenes · Full cast introduced**

The demo covers every major system through narrative rather than explicit tutorials. The player's journey: nervous student → licensed corpse engineer → independent investigator.

---

## Design Pillars

1. **One new layer per phase.** Each phase introduces exactly one new gameplay system — player is never overwhelmed, but always learning.
2. **Story motivates mechanics.** Every system is introduced through narrative need. The player isn't "learning to drive the caravan" — they're chasing a suspect.
3. **Show the full world, don't build it.** Factory, open world, and RTS modes are introduced briefly. The goal is to create desire, not teach mastery.
4. **End on a hook.** The professor's voice through the record player tells the player exactly what the game is without spelling it out.

---

## System Build Status

| System | Status | Notes |
|---|---|---|
| Movement + player controller | ✅ BUILT | Foundation for all exploration phases |
| Animation system | ✅ BUILT | Feeds combat, revival, cinematics |
| Revival mechanic | ✅ BUILT | Core loop mechanic — complete |
| Minion command system | ✅ BUILT | PlayerControlled, PlayerOrder, MinionCommandSystem |
| Brain swap on revive | ✅ BUILT | Citizen → Zombie brain swap wired |
| Spawn init pattern | ✅ BUILT | NewlySpawned, SpawnInitSystemGroup |
| Save system | ✅ BUILT | SaveSystemGroup, JSON slots |
| Solo combat (attack/throw) | 🔄 IN PROGRESS | Mechanics built; fencing duel AI opponent + animations needed |
| NPC AI (citizen behavior) | 🔄 IN PROGRESS | Bug: PlayerControlled bake fix needed — see [[Tasks/Claude/Code_Bugs]] |
| Dialogue system | ⬜ NEEDED | Required for scene 05, 08, 12, 13, 14, 15 |
| Narrative event system | ⬜ NEEDED | Scripted sequences, triggered cutscenes |
| Cinematic camera | ⬜ NEEDED | Per-scene camera overrides |
| Feral zombie AI | ⬜ NEEDED | Simple chase/attack — scene 06 |
| Student duel AI | ⬜ NEEDED | Scripted fencing opponent — scene 04 |
| Inventory system | ⬜ NEEDED | Key items (brain device) — scene 09 |
| RTS camera + UI + wave summoning | ⬜ NEEDED | Tournament exam — scene 07 |
| Caravan driving + open world | ⬜ NEEDED | Phase IV |
| Camp mode (mini RTS base) | ⬜ NEEDED | Bandit encounter — scene 14 |
| Factory + production line | ⬜ NEEDED | Minimal: 1 product, 1 line — scene 16 |
| Trade/buyer system | ⬜ NEEDED | Minimal: 1 buyer, 1 route — scene 16 |
| Notebook/journal UI + suspect tracker | ⬜ NEEDED | Scene 11 — the photo becomes the board |
| Fire/hazard system | ⬜ NEEDED | Burning school — scene 09 |
| NPC crowd system | ⬜ NEEDED | City density stress test — scene 15 |
| World map UI | ⬜ NEEDED | Demo ending reveal — scene 18 |
| Rive package update | ⬜ NEEDED | Blocking minion selection UI verification |
| Minion selection UI | ⬜ NEEDED | SelectedVisualSystem + selection indicator |

---

## Suggested Build Order (from demo guide)

1. **Fix NPC AI bug** — citizens are currently broken; unblocks AI verification
2. **Dialogue system + NPC cast** — needed for 6 of 18 scenes
3. **Narrative event system** — drives cinematic moments throughout
4. **Cinematic camera** — Phase I school day scenes need this
5. **Feral zombie AI + student duel AI** — completes Phase I combat
6. **RTS camera + UI + wave summoning** — tournament exam (Phase II)
7. **Inventory + fire/hazard system** — escape sequence (Phase II)
8. **Caravan driving + open world** — Phase IV, two zones only
9. **Camp mode** — bandit encounter follows caravan naturally
10. **Factory + production line (minimal)** — Phase V, keep deliberately thin
11. **Notebook UI + suspect tracker** — visual goal board (Phase III)
12. **NPC crowd system** — city scene, profile early
13. **World map UI** — demo sting, implement last

See [[Tasks/Claude/Code_Systems]] for implementation task list. See [[Tasks/Spencer/Design_Decisions]] for open questions blocking implementation.

---

## Scope Notes & Biggest Risks

**Phase IV + V together** — open world driving, bandit camp, city crowd, factory production are four distinct systems in the same session. Keep factory deliberately thin (1 product, 1 buyer, 1 trade route). Do not build the full economy for 8 minutes of demo.

**NPC crowd in the city** — first major DOTS crowd density stress test. Profile early — it's also one of the most impressive moments in the demo if it runs well.

**The accidental zombie at midnight** — scene needs to feel genuinely scary after the warmth of dinner. Lighting and audio carry most of the weight. Simple AI is enough — the feral zombie doesn't need to be complex.

**The masked figure** — child-sized silhouette, fast movement, sets a fire and disappears. Keep AI simple but staging striking. This is a mystery hook, not a boss fight.

---

## Phase Summary

| Phase | Title | Length | New System |
|---|---|---|---|
| I | Academy — Day One | ~20 min | Tutorial / World-building |
| II | The Final Exam & The Murder | ~15 min | RTS Intro / Inciting Incident |
| III | The Morning After | ~5 min | Narrative Pivot |
| IV | Open World — Road to the City | ~10 min | Hybrid RTS / Exploration |
| V | The City & The Factory | ~8 min | Economy Intro |
| VI | The Package — Demo Ending | ~2 min | Hook / Cliffhanger |

Full scene breakdown with checkboxes → [[Demo/Phases]]
