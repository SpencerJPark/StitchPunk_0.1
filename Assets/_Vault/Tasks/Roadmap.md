---
title: Stitch Punk master roadmap (spec of specs)
created: 2026-11-12
area: roadmap
status: draft for the owner's review; the order in section 3 is the recommendation
---

# Stitch Punk: master roadmap

This file decides **what gets built next and in what order**, across code, art, animation, UI and sound. It does not hold the detail of any one system: each row points to a spec that exists or names a spec that has to be written. When a row is picked up, run the normal pipeline (`plan-system` → `Tasks/Plans/` → build → `Tasks/Verification/`).

Status is deliberately thin here because it rots. For the live state run:
- `ls Assets/_Vault/Tasks/Plans Assets/_Vault/Tasks/Verification` (what is specced, what is built and unplayed)
- `git log --oneline -30`
- `Spencer/Playtest_2026-11-08.md` (the open play checks)

---

## 1. The game, as this roadmap understands it

You are a necro engineer driving a caravan workshop across an English-countryside wasteland in the 1900s. The hero is weak on foot, so you play it like an RTS inside an RPG.

**The loop, one week of game time:**

1. **Mail arrives.** A pigeon brings letters: trade orders (deliver these units by Friday) and missions (clear this camp, find this person, escort this cart).
2. **Travel.** Drive there, or zoom out to the miniature view and fast travel. World events can interrupt: a storm rolls in, the circus is in the next village, the sky whale is overhead.
3. **Post up.** Park the caravan. Gather wood, scrap and bodies. Burn wood for electricity. Raise the dead.
4. **Fight or work.** Clear the camp, beat the boss, or march the ordered units to the drop-off.
5. **Get paid.** Coin, reputation, unlocks.
6. **Invest.** Caravan upgrades first, then a factory in the city that fills standing orders without you, so you can take bigger missions further out.
7. **The mystery moves.** Story missions advance the murder investigation and open new regions.

Everything below is ordered by one question: **what is the shortest path to that loop being playable and fun, then good-looking, then deep?**

**What exists against that loop (2026-11-12):**

| Loop step | State |
|---|---|
| Mail: trade letters | Built, never played. Letter is a UI Toolkit placeholder, pigeon is a sphere |
| Mail: missions | **Nothing.** No mission, quest or objective code exists |
| Travel: caravan, streaming terrain, fast-travel zoom | Built, partly played; World Features (forests, villages, rail) building now |
| World events | Weather visuals exist (`WorldAtmosphereDriver`: storms, lightning, rain). No event gameplay. Sky whale model exists (`Models/SkyWhaleBackUp.fbx`) |
| Post up: workstation, furnace, craft, summon, squads, gathering | Built, never played end to end |
| Fight: bandits with rocks, guns, tactics, healing | Built, play pass owed |
| Bosses | **Nothing** (Camp Loop phase 7, the rival engineer, is the nearest plan) |
| Get paid: currency, reputation | Built with trade |
| Invest: factory | **Parked.** `ProductionSystem` sits commented out in `Core/Unused/`. Nothing to spend coin on yet |
| Story | Dialogue, narrative events, cutscenes built; no mission spine; the 18-scene demo guide is at 0 of 18 |
| **Characters on screen** | **No unit animates.** The MaleCitizen actor profile was deleted in `c07dfd75`; every unit is a sliding placeholder |
| Sound | The system is built; the library holds one sound (`AttackSwing`) |
| Environment art | Paved road kit pieces, one building, one stool, one cooking pot, the caravan |

The two biggest facts: **a lot is built and almost none of it has been played**, and **the game has no animated characters, so nothing built can be judged for feel.**

---

## 2. Rules for using this roadmap

1. **Play before you build more.** A milestone does not start until the previous milestone's play checks are ticked or consciously waived.
2. **One vertical slice at a time.** Each milestone ends in something you can play for ten minutes, with art and sound at the level named for that milestone.
3. **Three lanes run in parallel**, so you and the Claude sessions never wait on each other:
   - **Code lane** (Claude sessions): systems, tools, tests.
   - **Content lane** (you): rigs, animation clips, kit dressing, sound, UI art.
   - **Play lane** (you, short and often): the verify checklists. This is the lane that is starved today.
4. **Placeholder first, final later.** Every asset list below is ordered P1 (the loop reads wrong without it) → P2 → P3. Finish all P1 across the whole milestone before any P2.
5. **New ideas go to `Raw/`,** not into the current milestone.

---

## 3. The order (milestones)

| # | Milestone | You can… when it is done | Size |
|---|---|---|---|
| **M0** | **Play what is built** | tick the 39 checks in `Playtest_2026-11-08.md`; fixes land | days |
| **M1** | **Characters move** | watch a human, a zombie and the player idle, walk, attack, die and rise, with sound | 2–3 weeks |
| **M2** | **The week loop** | get a letter, take a mission and a trade order, travel, camp, fight, deliver, get paid, save | 4–6 weeks |
| **M3** | **A place worth being** | drive through one dressed region: village, farms, forest, bandit camp, roads, a rail line | 4–6 weeks, overlaps M2 |
| **M4** | **Enemies and the first boss** | fight four distinct enemy types and beat the rival engineer | 3–4 weeks |
| **M5** | **World events** | harvest a thunderstorm, visit the circus, survive the sky whale | 4–5 weeks |
| **M6** | **The factory** | set up a small line in the city that fills an order while you are away | 3–4 weeks |
| **M7** | **Interface and feel** | read real letters, a real HUD, menus, a journal; hits feel good | 3–4 weeks, starts in M2 |
| **M8** | **Sound and music pass** | play with the screen off and know what is happening | runs alongside M1–M7, final pass here |
| **M9** | **The story demo** | play the 18-scene academy-to-city demo (`Demo/Phases.md`) | long; reuses everything above |
| **M10** | **Ship prep** | performance on Steam Deck and Switch, save robustness, build, settings | ongoing, final pass here |

**Why this order:** M1 comes before more systems because nothing can be judged for feel while units slide. M2 before M3 because the loop decides what the world needs. Bosses before events because events reuse boss tech (the whale is a boss in the sky). Factory after the loop is fun by hand, since it automates a loop that has to be worth automating. The story demo is last on purpose: it is mostly content on top of systems M1–M8 make.

**The one call that is yours (see section 9, Q1):** this puts the open-world loop ahead of the academy demo. `Home.md` still says the demo is the goal.

---

## 4. Milestones as specs

Legend: **✅ spec exists** · **◐ built, unplayed** · **📝 spec to write** · lanes **[C]** code, **[A]** art, **[N]** animation, **[U]** UI, **[S]** sound, **[P]** play.

### M0. Play what is built

| Id | Work | Lane | Notes |
|---|---|---|---|
| M0-1 | Run `Spencer/Playtest_2026-11-08.md` sections 1–6 | P | Restart the Editor first (Burst cache). Write what you see next to each box |
| M0-2 | Fix pass on what fails | C | One session, fixes against the full `verify-*.md` files |
| M0-3 | Restore `Plans/DamageReaction_System.md` | C | The file is **empty in the working tree and at HEAD**; the full spec is in git at `da3f538b` |
| M0-4 | Retire stale docs: `Home.md` goal line, `Demo/Overview.md` status table (says dialogue, caravan, trade "needed"; all built), `Spencer/Art_Assets.md` (holds an old toolkit prompt, not an art list) | C | Point them at this file |
| M0-5 | Close or delete the old verify files nobody will ever run (pre-toolkit animation, ragdoll2D, colour palette v1) | C+P | Standing rule: unseen checkpoints close as accepted |

**Done when:** the six sections are ticked or have a note, and the fix pass is merged.

### M1. Characters move

This is the content milestone that unblocks judging everything else. Detail of every clip: `Spencer/Animations_Needed.md`.

| Id | Work | Lane | Spec |
|---|---|---|---|
| M1-1 | Human actor profile + rig rebuilt (the deleted MaleCitizen profile), bound by name | N+C | `NewPlans/ActorContentRebuild_System.md` recipe; scripts in `_Scripts/Editor/ContentAuthoring/` |
| M1-2 | Human P1 clips: `Idle`, `Walk`, `RunningWalk`, `MeleeSingle` (+`Attack` event), `Throw`, `Sit`, `Talk` | N | Animations_Needed, Humans |
| M1-3 | Player as a toolkit actor: `Idle`, `Walk`, `RunningWalk`, roll, melee, throw, **revive** | N+C | Code_Audit_2026-09 item 5 |
| M1-4 | Zombie rig + P1 clips: `Idle`, `Walk`, `MeleeSingle`, **`Resurrection`** | N | One rig, three timings (Zombie, Runner, Brute) |
| M1-5 | Ragdoll bodies on the new rig, death launch tuned | N+P | `NewPlans/RagdollTuning_System.md` T4–T10 |
| M1-6 | Hit react hook (small flinch on a non-lethal hit) | C | 📝 tiny spec; nothing requests it today |
| M1-7 | Bandit look: a second outfit/palette on the human rig, so friend and foe read apart at RTS zoom | A | `Verification/ColorPalette_System.md` wiring |
| M1-8 | First sound set (section 8, Set A: feet, hits, revive, zombie voice, UI click) | S | Sound system is built; add `SoundSO`s to `_SoundLibrary` |
| M1-9 | Caravan horse: Rive speed input wired | N+C | Caravan spec CV-O2 |

**Done when:** in `CampLoop.unity` a bandit walks, throws, gets punched, ragdolls, and rises as a zombie that shambles after you, all with sound, and none of it slides.

### M2. The week loop

| Id | Work | Lane | Spec |
|---|---|---|---|
| M2-1 | **Mission system**: `MissionSO` (objectives: clear a camp, deliver, fetch, escort, reach, talk), mission state on a singleton buffer (same reason as trade orders: `IPersist` cannot save Entity fields), rewards through `ResourceChangeRequest`, world markers via the terrain location registry | C | 📝 `Mission_System.md`. The biggest missing system |
| M2-2 | **Letters as one inbox**: trade orders, mission offers and story letters share the pigeon, the mail button and the letter screen | C+U | 📝 amendment to `Verification/Trade_System.md` |
| M2-3 | **Journal**: active missions, accepted orders with due days, reputation; later the suspect board | C+U | 📝 `Journal_System.md` (absorbs the notebook from the demo guide) |
| M2-4 | Camp Loop phase 6: raid on the parked caravan | C | ✅ `Plans/CampLoop_System.md` §8 |
| M2-5 | Damage Reaction (fight / flee / help / forgive) | C | ✅ after M0-3 restores it |
| M2-6 | Save: entity remap, travel autosave, slot handling | C | ✅ `Plans/Save_System.md` phases 4–6 |
| M2-7 | Zombie conversion composer | C | ✅ `Plans/ZombieConversion_System.md` (small) |
| M2-8 | Something to spend coin on: caravan upgrades v1 (storage, command radius, a third squad, furnace rate) bought at a village smith | C+U | 📝 `CaravanUpgrades_System.md`. Without a sink, coin means nothing until M6 |
| M2-9 | Three starter missions + one story mission authored | C+content | Clear the bandit camp; deliver stokers to the Bellfounders; find a missing farmhand (ends in a zombie twist); story: a letter from the groundskeeper |
| M2-10 | Hand-over prompt and drop-off marker art (trade leaves these unfinished by design) | U+A | Trade spec, "unfinished by design" |
| M2-11 | First 10-minute loop play pass, then a tuning pass on costs, pay and week length | P+C | |

**Done when:** from a fresh save you can play one full in-game week without the debug menu and want to play a second.

### M3. A place worth being

Runs beside M2. Code is already moving (the World Features session); the content lane is the gap.

| Id | Work | Lane | Spec |
|---|---|---|---|
| M3-1 | World Features WF-1 → WF-10: forests, cuttable trees, fields, village stamp, rail | C | ✅ `Plans/WorldFeatures_System.md` (building) |
| M3-2 | Terrain completion remainder: Decals / Props / Water panels, platform budgets read at runtime, hitch work | C | ✅ `NewPlans/TerrainCompletion_Plan.md` |
| M3-3 | **Kit import and prefab library** (section 6): every kit piece gets the painterly material, a collider, a nav footprint, and a scatter or stamp entry | A+C | 📝 `EnvironmentKit_Pipeline.md` |
| M3-4 | **Region One, "the Vale"**: one ~1 km region dressed to final-ish quality: a village, two farms, a wood, a bandit camp in a ruin, a ford, a station halt | A | Section 6 kit lists |
| M3-5 | Ground material set: grass, dry grass, mud, ploughed field, cobble, gravel road, forest floor, riverbank | A | `Memories/Code/Shaders.md`, Painted/Ground layers |
| M3-6 | Schedules + waypoints: villagers work by day, drink at night, sleep | C | ✅ `Plans/SchedulesWaypoints_System.md` (the `WorldClock` it needs exists) |
| M3-7 | Day/night lamps, window glow, chimney smoke on houses | A+C | `WorldAtmosphereDriver` already drives sun and ambient; `StitchGlass` shader in progress |
| M3-8 | Strategic (zoomed-out) HUD: heading to X, weather word, clock | U | ✅ Terrain P12-6 |
| M3-9 | Train travel | C | 📝 `TrainTravel_System.md` (WF §12 lists its questions). Can slip to M5 |

**Done when:** you can drive from the village to the bandit ruin and take screenshots you would post.

### M4. Enemies and the first boss

| Id | Work | Lane | Spec |
|---|---|---|---|
| M4-1 | Enemy roster v1, each with one clear idea (table below) | C+A+N | 📝 `EnemyRoster_System.md` |
| M4-2 | Smart AI H6: cheap zombies, spatial-hash awareness, 50 humans / 500 zombies | C | ✅ `SmartHumanAI_System.md` H6 + `CrowdScaleAwareness_System.md` |
| M4-3 | **Boss framework**: phases by health, telegraphed specials, arena bounds, boss health bar, intro cutscene hook, loot | C+U | 📝 `Boss_System.md` |
| M4-4 | **Boss 1: the Rival Corpse Engineer** (Camp Loop phase 7): an enemy workstation that summons waves; break it and the engineer flees or fights | C+A+N | ✅ sketch in `CampLoop_System.md` §8, promoted to a boss |
| M4-5 | **Boss 2: the Bandit Chief** at the ruin: a mounted or armoured human with a gun line | C+A+N | 📝 in `Boss_System.md` |
| M4-6 | Player kit growth: the three starter minion types from `Design_Decisions.md` (Carpenter Beetle, Hunter, Deadnote) | C+A+N | 📝 `MinionTypes_System.md`; needs your one-line idea for each (Q4) |
| M4-7 | Combat feel pass: hit stop, camera kick, blood decals, damage numbers or not, knockback tuning | C+A+S | 📝 `CombatFeel_Pass.md` |
| M4-8 | Performance pass on a real fight | C | ✅ `NewPlans/GamePerformancePass_System.md` P0a, P1–P4 |

**Enemy roster v1 (proposal; edit freely):**

| Enemy | Idea | Reuses | New art / anim |
|---|---|---|---|
| Bandit bruiser | Melee, charges, pulls friends | Built | Outfit, club |
| Bandit rock-thrower / rifleman | Range, telegraphed shot, friendly fire | Built (H3, H5) | Rifle pose, `Shoot` |
| Bandit medic | Runs to the hurt and bandages them | H4 heal | Satchel |
| Feral zombie | Fast, dumb, hunts by sound; the demo's scene 06 | Zombie brain | Feral palette, lunge |
| Rotter | Slow, explodes into a hazard zone on death | `HazardZoneSystem` | Bloat mesh, burst VFX |
| Rival minion | Enemy copy of your own units | Player zombies, other faction | Rival colours |
| Constable / Crown soldier | Disciplined gun line; appears if your reputation drops | H5 | Uniform |
| Circus strongman, knife thrower | Event enemies (M5) | Bruiser, thrower | Costumes |

**Done when:** each enemy changes how you play, and Boss 1 takes more than one attempt.

### M5. World events

One framework, three events. Terrain P12-3 already defines the hooks (`StrategicEventMarker`, `StrategicZoomInRequest`): an event appears on the map, and touching it zooms you in.

| Id | Work | Lane | Spec |
|---|---|---|---|
| M5-1 | **Event director**: weekly schedule + random roll off `WorldClock`, region rules, map markers, a warning letter or HUD line, save state | C | 📝 `WorldEvents_System.md` |
| M5-2 | **Thunderstorm harvest** (first: the weather already exists). Lightning strikes become gameplay: a lightning rod on the caravan or planted in the field converts a strike to Electricity; strikes hurt units in the open; a struck corpse may rise on its own | C+A+S | 📝 section of `WorldEvents_System.md` |
| M5-3 | **Travelling circus**: arrives at a village for a few days. Tents, a ringmaster vendor (rare parts, a unique minion), sideshow mini-missions, pickpockets; at night it may be a front for something | C+A+N+S | 📝 `Circus_Event.md` |
| M5-4 | **White whale attack**: the sky whale drifts over a region; its shadow is the warning. It drops things (debris, feral dead, cargo) and can be driven off or brought down as a multi-stage fight for a huge payout | C+A+N+S | 📝 `SkyWhale_Event.md`; uses the M4 boss framework |
| M5-5 | Smaller filler events: broken-down merchant cart, funeral procession (bodies…), bandit toll on a bridge, travelling doctor | C+content | In `WorldEvents_System.md` |

**Done when:** in three in-game weeks each of the three big events happens once and you chose to engage with it.

### M6. The factory

| Id | Work | Lane | Spec |
|---|---|---|---|
| M6-1 | **Factory v2 spec**. The July `FactoryMinimalLoop_System.md` predates the caravan, trade and resource ledger, so re-spec it against them. Your call on record: the player sets up a small multi-step line themselves | C | 📝 rewrite of `Plans/FactoryMinimalLoop_System.md` |
| M6-2 | Stations: intake (bodies, scrap, wood) → stitching table → charging rig → dispatch. Minions staff stations; a station's rate depends on who works it | C+A+N | in M6-1 |
| M6-3 | Dispatch: finished units walk or ride to a client and fill a standing order with no player present (the two-tier abstract sim from the terrain plan carries them) | C | in M6-1 |
| M6-4 | Factory interior kit and a build/placement UI on a grid (`BuildingPlacementManager` exists from the old prototype) | A+U | section 6 |
| M6-5 | Factory upgrades as the main coin sink; standing contracts as the main income | C+U | in M6-1 |
| M6-6 | Factory risks: break-ins, inspections, a strike of the living workers next door | C | later |

**Done when:** you leave for a mission and come back to coin earned by the line you built.

### M7. Interface and feel

Starts in M2 with placeholders; this is the pass that makes them real. The HUD and letter exist as UI Toolkit placeholders (`Assets/UI/CampLoop/`); the plan on record is Rive for the final art.

| Id | Work | Lane | Notes |
|---|---|---|---|
| M7-1 | **UI style sheet for the game** (not the editor guide): paper, ink, brass, wax seals, stitched leather; type choices; one icon set | U | 📝 `GameUiStyle.md`. Do this before any final UI art |
| M7-2 | **Letters**: envelope lands, wax seal per sender (`letterSeal` exists and is unused), unfold animation, handwriting per client, stamp for accepted or declined, stack of unread mail | U+S | The signature screen of the game |
| M7-3 | **HUD**: resources, clock and weather, squad cards with unit portraits, player vitals, active mission line, mail badge, minimap or compass | U | Replace `ResourceHud.uxml`, `SquadHud.uxml` |
| M7-4 | Workstation menu: summon, craft, repair, furnace as a physical workbench | U | Replace `WorkstationMenu.uxml` |
| M7-5 | Journal and map: missions, orders, reputation, suspect board, world map with events | U | With M2-3 |
| M7-6 | Dialogue: your call is white text on black that pops in; add speaker name and a skip | U | `Design_Decisions.md` |
| M7-7 | Menus: main, pause, settings (audio, controls, display), save slots, game over | U | `MainMenuUI`, `SettingsMenuUI` exist as stubs |
| M7-8 | World-space UI: health bars, selection rings, order markers, drop-off poles, interact prompts, emote icons (replace the generated `EmoteAtlas.png`) | U+A | |
| M7-9 | Controller pass: every screen works on a pad (Switch and Deck targets) | C+U | |
| M7-10 | Juice: screen shake, hit flash, pickup pops, coin count-up, summon smoke, revive lightning | A+C+S | With M4-7 |

### M8. Sound and music

Plan in section 8. The final mix pass sits here; the sets are made alongside M1–M7.

### M9. The story demo

`Demo/Phases.md` is the scene list (18 scenes, academy → murder → road → city → the package). By now most of its "needed" systems exist. What stays specific to it:

| Id | Work | Lane |
|---|---|---|
| M9-1 | Cast: roommate, professor/headmaster, groundskeeper and wife, the suspects, the masked figure | A+N |
| M9-2 | Academy kit: dorm, kitchen, lab, gym, dining hall, office, burning corridor | A |
| M9-3 | Fencing duel (your call: full AI opponent, health-based win) | C+N |
| M9-4 | Tournament: stationary hut as the workstation, first RTS battle | C |
| M9-5 | Fire hazard + escape sequence, key-item inventory (the brain device) | C |
| M9-6 | Cutscenes authored for scenes 05, 08, 10, 11, 12, 18 | content |
| M9-7 | Voice: the professor's record-player line and the other VO in `Spencer/Audio.md` | S |
| M9-8 | World map reveal | U |

### M10. Ship prep

Performance budgets per platform (`TerrainPlatformProfile`), save versioning, player build (you run builds, never Claude), settings, crash logging, the Playtest Copilot package for outside testers, store page capture.

---

## 5. Animation production order

The full list with names and event markers is `Spencer/Animations_Needed.md`; keep adding rows there. This is only the order.

| Wave | Milestone | Clips |
|---|---|---|
| 1 | M1 | Human: Idle, Walk, RunningWalk, MeleeSingle, Throw, Sit, Talk. Player: Idle, Walk, Run, Roll, Melee, Throw, Revive. Zombie: Idle, Walk, MeleeSingle, Resurrection |
| 2 | M2 | Human: EquipWeapon, UseHealingItem, Shoot, Flee, hit react, DefensiveIdle/Walk. Player: knocked out + get up, mount / dismount, craft, summon gesture. Zombie: Spawn, CaravanHopDown, harvest + carry. Props: horse walk / trot, furnace, caravan doors |
| 3 | M3 | Camp and village life: Eat, Drink, DrinkAlcohol, Brawl, PassOut, ListenToMusic, Sleep, Smoke, work loops (hoe, hammer, sweep, carry) |
| 4 | M4 | One set per new enemy (idle, move, attack, special). Boss sets: intro, two specials, stagger, death. The three starter minion types |
| 5 | M5 | Circus acts (juggle, strongman lift, knife throw), whale (swim, dive, drop, wounded, fall), lightning-struck riser |
| 6 | M6 | Station work loops (stitch, crank, haul, pack), conveyor and machine cycles |
| 7 | M7–M9 | Faces (talk mouth, angry, happy, scared, drunk), talk variants, cast-specific cutscene acting |

Rule: every attack clip carries its `Attack` event marker on the day it is made, or the timing falls back to a timeout.

---

## 6. Environment art and the modular kit

**I could not identify the purchased kit in the project.** What is imported is `Models/Roads/` (paved main road, road and sidewalk pieces at 5, 10 and 20 m), `Building1.fbx`, a stool, a cooking pot and the caravan. Tell me the kit's name or import it and I will turn this section into a piece-by-piece list (Q2). Until then this is the plan it has to serve.

### 6.1 Pipeline for every kit piece (spec M3-3)

1. Import, fix scale to the 1 m grid, pivot at ground centre.
2. Assign the painterly material (palette UV or gradient map, per `Shaders.md`), never the kit's own material.
3. Collider (box or simple convex), nav footprint, a `TerrainScatterMarker` or stamp entry so it can be placed by the World Features tools.
4. LOD or impostor if it will be seen from the strategic zoom.
5. Thumbnail and a name that sorts: `Env_<Set>_<Piece>_<Size>`.

### 6.2 Sets, in build order, and what goes with what

| Set | Milestone | Kit pieces to use | Custom pieces to make | Dress with | Ground and light |
|---|---|---|---|---|---|
| **Roadside** | M3 | Gravel and paved road, fences, stone walls, gates, signposts, milestones | Stitch Punk signposts, wanted posters, a pigeon post box | Hedgerows, cart ruts decal, puddles, a broken cart | Gravel, verge grass |
| **Bandit camp / ruin** | M3 | Ruined walls, tents, crates, barrels, campfire, palisade | Gramophone, loot crate, strongbox, bar plank, bandage box (all exist as gameplay, need meshes) | Bottles, bedrolls, weapon rack, stolen goods | Trampled mud; fire light at night |
| **Village** | M3 | Cottages, shop fronts, church, pub, well, market stalls, lamp posts | Pub sign, smith (the M2 upgrade vendor), trade drop-off pole, notice board (mission giver) | Washing lines, carts, chickens, smoke | Cobble centre, mud lanes; window glow |
| **Farm** | M3 | Farmhouse, barn, sheds, fences, hay, troughs, windmill | Scarecrow (a joke about your trade), grave plot | Field parcels from WF-6, tools, sacks | Ploughed field, dry grass |
| **Woodland** | M3 | Trees (three sizes), stumps, logs, rocks, bushes | Cuttable tree + stump pair (WF-3), hanging tree | Mushrooms, fallen fence, crows | Forest floor, dappled light |
| **River and crossing** | M3 | Bridge, ford stones, jetty, reeds | Toll booth (bandit toll event) | Boats pulled up, nets | Riverbank, shallows |
| **Rail** | M3/M5 | Track, sleepers, platform, station house, signal, water tower | Station name boards, the train | Luggage, lamps, coal heap | Ballast gravel |
| **Graveyard** | M4 | Headstones, crypt, iron fence, chapel | Open graves (a body source), body cart | Lanterns, flowers, fog card | Mud, dead grass |
| **Rival engineer camp** | M4 | Tents, crates, machinery | Rival workstation, tesla coils, body racks | Cables, sparks | Scorched ground; blue electric light |
| **Circus** | M5 | Tents, wagons, stalls, flags, cages | Big top, ringmaster wagon, sideshow booths, organ | Bunting, posters, torches | Sawdust; warm lamps |
| **Storm kit** | M5 | — | Lightning rod (caravan and field), capacitor, scorch decal | Rain cards, wind-bent grass | Wet ground variant |
| **City street** | M6 | Terraces, shops, factories, chimneys, lamp posts, tram lines | Your factory front, client buildings (Bellfounders, Lady Ashgrove's house, the Crown depot) | Crowds, carts, smoke, soot | Cobble, pavement; smog grade |
| **Factory interior** | M6 | Walls, floors, beams, pipes, tanks, conveyors | Stitching table, charging rig, intake hopper, dispatch door | Body hooks, tools, crates | Stone flags; furnace glow |
| **Academy** | M9 | Gothic school pieces, desks, beds, tables | Reanimation lab bench, fencing piste, record player, the brain device | Books, jars, portraits | Wood floor; candle light, then fire |

**Colour rule so sets belong together:** the kit is recoloured through the painterly palette, so each set gets one ramp. Countryside is olive, straw and slate; anything necro-engineered (yours or the rival's) adds one accent, brass with green or blue electric glow; enemies' things are red-brown. That accent is how the player finds the gameplay object in a dressed scene.

### 6.3 Characters and creatures (art, not animation)

Order: human base rig (M1) → bandit and villager outfits by part swap (M1/M3) → zombie variants (M1) → three starter minion types (M4) → enemy roster and bosses (M4) → circus cast and whale (M5) → city crowd and clients (M6) → story cast (M9). The part and palette system (`UnitPartSO`, colour variations) is the tool for variety; aim for five heads, five hairs, four outfits before modelling anything bespoke.

### 6.4 VFX list

Revive lightning, summon smoke (exists as a burst), furnace and chimney smoke, roll dust (exists), blood and hit sparks, muzzle flash and tracer, wind-up flash (exists), rock impact, Rotter burst, fire, rain and lightning (exist), lightning-strike capture, whale shadow and debris, coin pickup, order marker pulse.

---

## 7. Systems list in one place (code lane queue)

In order. A session picks the top unbuilt row.

1. M0-2 fixes from the play pass · M0-3 restore Damage Reaction
2. World Features WF-1…10 (in progress) · Terrain completion remainder
3. M1-6 hit react · M1-3 player as toolkit actor (scripted half)
4. **M2-1 Mission system** · M2-2 letters inbox · M2-3 journal
5. M2-5 Damage Reaction · M2-4 Raid · M2-6 Save phases 4–6 · M2-7 Zombie conversion
6. M2-8 Caravan upgrades
7. M3-6 Schedules + waypoints · M3-8 strategic HUD
8. M4-2 AI H6 scale · M4-3 Boss framework · M4-4 Rival engineer · M4-1 roster · M4-6 minion types
9. M4-7 Combat feel · M4-8 Performance pass
10. M5-1 Event director · M5-2 Thunderstorm · M5-3 Circus · M5-4 Sky whale · M3-9 Trains
11. M6-1 Factory v2
12. M7 UI builds as the art for each screen lands
13. M9 demo-specific systems (duel, fire, key items, tournament)
14. M10 ship prep

Parked on purpose, pick up only if they block something: Feature Isolation, CharacterRig hardening, Cleanup Batch rows 1/2/5/6, Ranged Combat spec (superseded in practice by H5; check before deleting). The three sellable packages (animation, movement, worktree) and Playtest Copilot are a separate product track; they do not block the game except where M1 needs the animation tool.

---

## 8. Sound production plan

The runtime is done (`SoundSystemGroup`: one-shots, loops, layered music; `_AnimSoundEventMapping` ties clips to sounds). The library is empty. Work in sets; each set is a folder of files plus `SoundSO`s.

**Conventions (decide once):** 48 kHz WAV sources; mono for anything positional, stereo for music and ambience; names `SFX_<Group>_<Thing>_<nn>`, `AMB_<Place>_<Time>`, `MUS_<Cue>_<Layer>`; at least three variations of anything heard more than once a minute; mixer groups Music, Ambience, SFX-World, SFX-Combat, SFX-UI, Voice. Then run the `unity:optimize-audio` import pass once per set.

| Set | Milestone | Contents |
|---|---|---|
| **A. First touch** | M1 | Footsteps (grass, dirt, gravel, wood), punch and claw hits, swing whoosh, body fall, **revive** (electric crack + wet gasp), zombie groans ×6, human grunt / hurt / death ×4 each, UI click and confirm |
| **B. Caravan and camp** | M2 | Hooves, wheels on gravel and mud, harness, doors, furnace roar loop, chimney, crafting clanks, summon (shake, slam, smoke), axe on wood, scrap rattle, deposit thunk, pigeon wings and coo, letter paper, wax seal crack, coin |
| **C. Combat** | M2/M4 | Rock throw and hit, pistol, rifle, ricochet, wind-up swell (exists as a hook), bandage, bandit barks ("over there!", "I'm hit!"), roll, knock-out |
| **D. World beds** | M3 | Countryside day and night, woodland, river, village day and night, pub interior, rain light and heavy, wind, thunder near and far |
| **E. Enemies and bosses** | M4 | One voice set per enemy type, Rotter burst, rival tesla hum and discharge, boss intro sting, boss phase change |
| **F. Events** | M5 | Lightning strike close, capacitor charge, circus organ and crowd, barker calls, whale song (the warning, heard before it is seen), whale impact |
| **G. City and factory** | M6 | Street crowd, trams, factory hum, conveyor, stitching machine, charging rig, steam |
| **H. UI final** | M7 | Full UI kit: hover, select, back, page turn, stamp, error, mission complete, order failed |
| **I. Music** | M2 → M8 | Layered per the system: base + tension + combat layers. Cues in order: road theme, camp theme, combat, village, night, boss, circus, storm, city, factory, then the six demo cues in `Spencer/Audio.md` |
| **J. Voice** | M9 | The demo lines in `Spencer/Audio.md`; record-player treatment for the professor |

Sourcing order for speed: a commercial library for sets A–D first, replace the signature sounds with your own (revive, summon, letter, whale song) because those are what people will remember.

---

## 9. Decisions only you can make

| # | Question | My recommendation |
|---|---|---|
| Q1 | Is the first public slice the **open-world week loop** (this roadmap) or the **academy story demo** (`Home.md`)? | The loop. It is 80 % built, and the academy needs a whole second environment and cast |
| Q2 | Which modular kit did you buy, and is it imported somewhere I did not see? | Import it under `Assets/Models/Kit/<Name>/`, then I list it piece by piece |
| Q3 | Final game UI in **Rive** (the earlier plan) or stay on **UI Toolkit** with art? | UI Toolkit for menus, letters and journal (text-heavy, pad navigation), Rive only for small animated pieces: the pigeon, seals, the horse |
| Q4 | One line each for the Carpenter Beetle, Hunter and Deadnote | — |
| Q5 | Bosses: are the rival engineer, bandit chief and sky whale the right first three? | Yes |
| Q6 | Mission givers: letters only, or also people and notice boards in villages? | All three; letters for clients and story, the board for filler |
| Q7 | Does the thunderstorm raise unattended corpses? | Yes. It makes a storm a risk and a harvest at once |
| Q8 | Is the factory one fixed building in the city, or can you found more? | One for the first release, with rooms to unlock |

---

## 10. This week

1. **You:** the play pass (M0-1). Two hours, riskiest first.
2. **You:** rebuild the human rig and profile, then Idle and Walk (M1-1, first half of M1-2). Until this exists nothing else can be judged.
3. **Claude session A (running):** World Features.
4. **Claude session B:** M0 fixes as you report them, then restore Damage Reaction and build it.
5. **Claude session C:** write `Mission_System.md` with a Q&A round with you (Q6 is its first question).
6. **You, when stuck on animation:** answer Q1–Q8 here.
