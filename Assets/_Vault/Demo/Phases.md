---
tags: [demo, phases, checklist]
related: "[[Demo/Overview]], [[Tasks/Plans/README|Tasks/Plans]], [[Spencer/Art_Assets]]"
---

# Demo — Phase Checklist

Check off a scene when it is **fully playable end-to-end** (code + art + audio in place).

Legend: ✅ BUILT · 🔄 IN PROGRESS · ⬜ NEEDED · `[C]` = blocked on Claude · `[S]` = blocked on Spencer · `[B]` = both

---

## Phase I — Academy: Day One (~20 min)

- [ ] **01. Wake up in dorm** — 🔄 IN PROGRESS `[S]`
  - Systems: Movement ✅, Animation ✅, Interaction ✅
  - Blocked on: Dorm room environment art, ambient NPC (roommate)

- [ ] **02. Kitchen fetch** — ⬜ NEEDED `[B]`
  - Systems: NPC undead workers, Interaction system ✅, World-building scene
  - Blocked on: Kitchen environment `[S]`, NPC undead worker setup `[C]`

- [ ] **03. Reanimation class — raise & command** — 🔄 IN PROGRESS `[B]`
  - Systems: Revival mechanic ✅, Minion commands ✅, Brain swap ✅, Horde manager (1 unit)
  - Blocked on: Lab environment `[S]`, Professor NPC + dialogue `[C+S]`, task sequence scripting `[C]`

- [ ] **04. PE — fencing duel** — 🔄 IN PROGRESS `[B]`
  - Systems: Solo combat 🔄, Student duel AI ⬜, Animation (fencing)
  - Blocked on: Fencing AI opponent `[C]`, gymnasium environment + fencing animations `[S]`

- [ ] **05. Lunch — meet the full cast** — ⬜ NEEDED `[B]`
  - Systems: Cinematic camera ⬜, Dialogue system ⬜, NPC crowd (dining hall scale)
  - Blocked on: Dialogue system `[C]`, all suspect character models `[S]`, dining hall environment `[S]`

- [ ] **06. Midnight — the accidental zombie** — ⬜ NEEDED `[B]`
  - Systems: Feral zombie AI ⬜, Solo combat ✅, Night lighting
  - Blocked on: Feral zombie AI `[C]`, night lighting setup `[S]`, feral animation set `[S]`

---

## Phase II — The Final Exam & The Murder (~15 min)

- [ ] **07. Tournament — first RTS battle** — ⬜ NEEDED `[B]`
  - Systems: RTS camera ⬜, RTS UI ⬜, Wave summoning ⬜, Horde manager, AI (opponent base)
  - Blocked on: RTS systems `[C]`, tournament arena environment `[S]`, RTS UI design `[S]`

- [ ] **08. Private meeting + murder** — ⬜ NEEDED `[B]`
  - Systems: Dialogue system ⬜, Narrative event system ⬜, Cinematic camera ⬜, Masked figure AI
  - Blocked on: All systems `[C]`, headmaster model + office environment `[S]`, masked figure model `[S]`

- [ ] **09. Extract the brain & escape** — ⬜ NEEDED `[B]`
  - Systems: Inventory system ⬜, Narrative event (escape sequence) ⬜, Fire/hazard system ⬜
  - Blocked on: Inventory + fire systems `[C]`, burning corridor environment + hazard art `[S]`

---

## Phase III — The Morning After (~5 min)

- [ ] **10. Government arrives at the scene** — ⬜ NEEDED `[B]`
  - Systems: NPC crowd ⬜, Narrative event system ⬜, Scripted sequence
  - Blocked on: Narrative system `[C]`, school ruins exterior + government NPC models `[S]`

- [ ] **11. Group photo — suspect board** — ⬜ NEEDED `[B]`
  - Systems: Notebook/journal UI ⬜, Suspect tracker ⬜, Cinematic camera ⬜
  - Blocked on: Notebook UI `[C]`, photo/suspect art assets `[S]`

- [ ] **12. The groundskeeper's gift — the caravan** — ⬜ NEEDED `[B]`
  - Systems: Caravan entity ⬜, Companion system (roommate) ⬜, Narrative event ⬜
  - Blocked on: Caravan systems `[C]`, groundskeeper + wife models `[S]`, caravan exterior art `[S]`

---

## Phase IV — Open World: Road to the City (~10 min)

- [ ] **13. Drive the caravan** — ⬜ NEEDED `[B]`
  - Systems: Caravan driving ⬜, Open world scene ⬜, Companion dialogue ⬜
  - Blocked on: Caravan driving + open world `[C]`, road environment + LOD setup `[S]`; also needs [[Spencer/Design_Decisions]] — caravan perspective answered

- [ ] **14. Bandit encounter — hybrid RTS** — ⬜ NEEDED `[B]`
  - Systems: Camp mode ⬜, Resource gathering ⬜, RTS combat ⬜, Corpse collection ⬜, Bandit AI
  - Blocked on: Camp mode `[C]`, bandit camp environment + bandit models `[S]`

---

## Phase V — The City & The Factory (~8 min)

- [ ] **15. Enter the city** — ⬜ NEEDED `[B]`
  - Systems: NPC crowd system ⬜ *(profile early)*, Interaction system ✅, City scene
  - Blocked on: NPC crowd system `[C]`, city street environment + city NPC models `[S]`

- [ ] **16. The factory — first product** — ⬜ NEEDED `[B]`
  - Systems: Factory/production line ⬜ (minimal), Trade/buyer system ⬜ (minimal)
  - Blocked on: Factory systems `[C]`; factory interior art + production line visuals `[S]`; also needs [[Spencer/Design_Decisions]] — factory UI granularity answered

- [ ] **17. Settle in — caravan & room customisation** — ⬜ NEEDED `[B]`
  - Systems: Caravan customisation UI ⬜, Room/base UI ⬜
  - Blocked on: UI systems `[C]`, caravan interior + room interior art `[S]`

---

## Phase VI — The Package: Demo Ending (~2 min)

- [ ] **18. A mysterious delivery** — ⬜ NEEDED `[B]`
  - Systems: Narrative event system ⬜, Key item (brain device) ⬜, World map UI ⬜, Cinematic camera ⬜
  - Blocked on: World map UI `[C]`, brain device model + record player prop `[S]`, world map art `[S]`

---

## Summary Progress

| Phase | Scenes | Playable |
|---|---|---|
| I — Academy | 6 | 0 / 6 |
| II — Exam & Murder | 3 | 0 / 3 |
| III — Morning After | 3 | 0 / 3 |
| IV — Open World | 2 | 0 / 2 |
| V — City & Factory | 3 | 0 / 3 |
| VI — Demo Ending | 1 | 0 / 1 |
| **Total** | **18** | **0 / 18** |

> Update the table as scenes become playable. A scene is playable when code + art + audio are all in.
