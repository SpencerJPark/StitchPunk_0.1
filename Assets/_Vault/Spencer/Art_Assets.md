---
tags: [task, spencer, art, environments, characters]
related: "[[Demo/Phases]], [[Spencer/Audio]]"
---

// claude --dangerously-skip-permissions



# Art Assets Needed

Grouped by demo phase. Check off when the asset is in Unity and set up. See [[Demo/Phases]] for which scene each asset is used in.

---

## Phase I — Academy

### Environments
- [ ] Dorm room (scene 01) — bed, trunk, window; roommate version and player version
- [ ] School kitchen (scene 02) — industrial, 1900s style; undead workers at stations
- [ ] Reanimation lab (scene 03) — dissection tables, lab equipment, cadaver freezer
- [ ] School gymnasium (scene 04) — fencing piste, bleachers
- [ ] School dining hall (scene 05) — long tables, NPC crowd capacity ~50
- [ ] School dormitory corridor at night (scene 06) — dim lighting, emergency atmosphere
- [ ] School exterior (establishing shot)

### Characters
- [ ] Roommate — male student, named NPC, full animation set
- [ ] Professor / Headmaster — authority figure, full animation set
- [ ] All suspect characters (from group photo scene 11) — how many? List them with designs
- [ ] Generic student NPCs (crowd fill, 2–3 variants)
- [ ] Undead kitchen workers (2–3 variants, working poses)
- [ ] Feral zombie (scene 06) — dishevelled, aggressive animation set

### Animations (new, Phase I)
- [ ] Fencing attack / block / dodge (player + duel opponent)
- [ ] Revival ceremony (player crouching over cadaver)
- [ ] Sit / eat (dining hall NPCs)
- [ ] Working poses (kitchen undead: stir, mop, wash)

---

## Phase II — Exam & Murder

### Environments
- [ ] Tournament arena (scene 07) — raised stage, audience, two opponent bases
- [ ] Headmaster's private office (scene 08) — warm, personal, wood-panelled
- [ ] Burning school corridor (scene 09) — fire hazard art, smoke, orange lighting
- [ ] Escape route / school exit path

### Characters
- [ ] Masked figure (scene 08) — child-sized silhouette, distinctive costume, fast movement animation
- [ ] Government officials / soldiers (scene 10 preview)

### Animations
- [ ] Masked figure: run, fire-start, disappear
- [ ] Headmaster: draw weapon, get shot, collapse

---

## Phase III — Morning After

### Environments
- [ ] School ruins exterior — same building, burned/collapsed state (scene 10)
- [ ] Government cordon — barriers, officials
- [ ] Photo location — cleared rubble, group stands in front of ruins

---

## Phase IV — Open World

### Environments
- [ ] Open world road — 2 zones minimum: academy area → city outskirts
- [ ] Roadside details: travellers, curiosities, distant landmarks
- [ ] Bandit camp (scene 14) — makeshift roadblock, tents, fire

### Characters
- [ ] Groundskeeper (scene 12) — weathered, working-class
- [ ] Groundskeeper's wife (scene 12)
- [ ] Bandit NPCs (scene 14) — 3–4 variants
- [ ] Roadside traveller NPCs (scene 13, background)

### Assets
- [ ] Caravan exterior model — horse-drawn or steam-powered? (confirm in [[Spencer/Design_Decisions]])
- [ ] Resources (scene 14) — what items can be gathered from the camp environment?

---

## Phase V — City & Factory

### Environments
- [ ] City street scene (scene 15) — dense, industrial Victorian; NPC crowd stress test
- [ ] Factory interior (scene 16) — dusty conveyor belts, machinery, enormous potential
- [ ] Factory barn (for caravan parking)
- [ ] Caravan interior — living space, customisable elements

### Characters
- [ ] City civilian NPCs (5+ variants for crowd)
- [ ] City vendors / passersby (interactive, named)
- [ ] Trade buyer NPC (scene 16)
- [ ] Roommate — additional animations for companion dialogue

### Assets
- [ ] Factory production line props (conveyor belt, machinery, product)

---

## Phase VI — Demo Ending

### Assets
- [ ] Brain device (key item — carried since scene 09)
- [ ] Record player prop (the brain mounts onto it)
- [ ] World map art — stylised, full scope, zooms out from city location
- [ ] Professor's voice (audio) → see [[Spencer/Audio]]

---

## Shared / Cross-Phase

- [ ] Player character — finalised design, full animation set including all new actions
- [ ] Notebook / journal UI art — cover, pages, photo frame, suspect card design
- [ ] Inventory UI art — key item slots
- [ ] RTS UI design — unit icons, summon buttons, resource display

---

## Cinematic Camera Scene Setup

Each scene that uses a cinematic shot (05, 08, 10, 11, 18) needs these two GameObjects added in the scene hierarchy:

- [ ] **Scene 05** — Add `CinemachineCamera` (assign to `CameraManager.cinematicCam`) + empty `CinematicTarget` Transform (assign to `CameraManager.cinematicTarget`). Set the camera's **Follow** and **LookAt** to the `CinematicTarget`. Configure body/aim in Cinemachine inspector for the shot feel.
- [ ] **Scene 08** — Same setup as above.
- [ ] **Scene 10** — Same setup as above.
- [ ] **Scene 11** — Same setup as above.
- [ ] **Scene 18** — Same setup as above.

> The cinematic cam and target are already wired in code — this is purely editor scene work.
