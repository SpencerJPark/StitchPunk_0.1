---
tags: [task, claude, code, systems]
related: "[[Demo/Overview]], [[Tasks/Spencer/Design_Decisions]], [[Memories/Code/Systems]], [[Memories/Code/RULES]]"
---

# Code — New Systems

New systems needed for the demo, in the recommended build order from [[Demo/Overview]]. Each system lists the demo scenes it enables, design questions that must be answered first (see [[Tasks/Spencer/Design_Decisions]]), and implementation notes.

**Before starting any system:** check [[Tasks/Spencer/Design_Decisions]] for open questions. A system with unanswered design questions will need rework.

---

## Build Order

### 1. Dialogue System + NPC Dialogue Components
- **Enables scenes:** 03, 05, 08, 12, 13, 14, 15
- **Design gate:** Dialogue format answered in [[Tasks/Spencer/Design_Decisions]]
- **Notes:** Keep it minimal for the demo — one speaker at a time, subtitle or speech bubble style. NPC dialogue data should be SO-driven and baked into BlobAssets per [[Memories/Code/RULES]]. Dialogue trigger is an IEnableableComponent on the NPC entity. See [[Memories/Code/Authoring]] for baking pattern.

### 2. Narrative Event System
- **Enables scenes:** 08, 09, 10, 11, 12, 18
- **Notes:** Scripted sequence trigger — enables a chain of events (move NPC to position, play animation, fire dialogue, etc.) via an authored sequence asset. Keep it data-driven. This is the spine of Phase II–VI.

### 3. Cinematic Camera
- **Enables scenes:** 05, 08, 10, 11, 18
- **Notes:** Per-scene camera override using a simple CameraEvent component + CameraManager MonoBehaviour bridge. Blend into/out of gameplay camera. See [[Memories/Code/MonoBehaviours]] for the bridge pattern.

### 4. Feral Zombie AI Behavior
- **Enables scenes:** 06
- **Notes:** Does NOT use the motivation scoring pipeline. Simple `ISystem`: if target in range → chase; if adjacent → attack. No `NeedsAction`, no `ActionOption` buffer. Add a `FeralZombie` brain tag and a dedicated `FeralZombieSystem` in `AIExecutionSystemGroup`.

### 5. Student Duel AI (Scripted Fencing Opponent)
- **Enables scenes:** 04
- **Design gate:** Duel flow designed by Spencer
- **Notes:** Scripted state machine rather than motivation scoring — attack, block, dodge states driven by authored timing data. Simple SO config for difficulty/timing.

### 6. Inventory System (Key Items)
- **Enables scenes:** 09, 18
- **Notes:** Demo only needs key items (brain device, etc.) — not a full equipment system. `KeyItem` IComponentData on the player entity, item type enum, pickup trigger. The `EquipRequest` / `AttachedTo` components in [[Memories/Code/Components]] may be reusable here.

### 7. RTS Camera Mode + UI + Wave Summoning
- **Enables scenes:** 07, 14
- **Design gate:** RTS UI layout, unit cap, summoning cost answered in [[Tasks/Spencer/Design_Decisions]]
- **Notes:** Camera toggles to top-down on RTS mode enter. UI shows selected units, summon buttons, resource count. Wave summoning creates units from a pool near the player's base. Reuses horde system from [[Memories/Code/Systems_Movement]].

### 8. Caravan Driving Entity + Input
- **Enables scenes:** 12, 13
- **Design gate:** Caravan perspective (top-down or 3rd-person) answered in [[Tasks/Spencer/Design_Decisions]]
- **Notes:** Caravan is an entity with `UnitMover`-style movement but driven by player input rather than pathfinding. PlayerFollowerSystem may be reusable — check [[Memories/Code/Systems_Movement]].

### 9. Camp Mode (Mini RTS Base)
- **Enables scenes:** 14
- **Notes:** Enters when caravan is parked. Player can gather nearby resources + summon units from caravan. Reuses wave summoning from step 7. Camp has a radius; units defend it.

### 10. Factory Production Line (Minimal)
- **Enables scenes:** 16
- **Design gate:** Factory UI granularity answered in [[Tasks/Spencer/Design_Decisions]]
- **Notes:** Demo scope: 1 product, 1 production line, 1 buyer, 1 trade route. Do not build the full economy. Undead staff the line (reuses revival + minion assignment). Product output triggers a trade event.

### 11. Notebook/Journal UI + Suspect Tracker
- **Enables scenes:** 11 (and forward)
- **Design gate:** Photo vs silhouette format answered in [[Tasks/Spencer/Design_Decisions]]
- **Notes:** The group photo from scene 11 becomes the suspect board. Player taps a face to view details. Status: unknown → investigated → cleared/suspect. UI only — no gameplay logic in the demo.

### 12. Fire/Hazard System
- **Enables scenes:** 09
- **Notes:** Visual + collision hazard. Fire entity with `FireHazard` IComponentData — damages units in range via `Hurt` buffer (reuses existing damage system). Spread is visual-only for the demo.

### 13. NPC Crowd System
- **Enables scenes:** 05 (dining hall), 15 (city street)
- **Notes:** First major DOTS crowd stress test — profile early. Reuses Unit + AI structures but with simplified scoring (NPCs just wander/idle). Profile target: 200+ NPCs at 60fps.

### 14. Trade / Buyer System (Minimal)
- **Enables scenes:** 16
- **Notes:** Follows factory production. 1 buyer entity in the city. When product stock > 0 + player visits buyer → trade event fires → currency added. Keep SO-driven.

### 15. Caravan + Room Customisation UI
- **Enables scenes:** 17
- **Design gate:** UI layout from Spencer
- **Notes:** Preview + confirm pattern. Limited slot options for the demo.

### 16. World Map UI
- **Enables scenes:** 18
- **Notes:** The demo sting. A stylised map expands to show the full world scope. Implement last — it's 2 minutes of the demo and needs no gameplay systems, just a polished cinematic moment.
