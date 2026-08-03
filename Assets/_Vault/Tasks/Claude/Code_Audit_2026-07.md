---
tags: [task, claude, code, audit, roadmap]
related: "[[Memories/Code/Systems]], [[Memories/Code/Systems_AI]], [[Memories/Code/RULES]], [[Tasks/Plans/README]]"
created: 2026-07-01
status: active
---

# Code Audit & Idea Backlog — July 2026

Successor to the May-29 `Code_Health_Audit.md` (all six of its items were completed and the doc retired). This is a fresh pass over the post-rig codebase: **red flags found**, **things I'd change**, and **systems the game still needs**, merged into one execution-ordered backlog. Ordering optimizes for a **playable vertical slice** while the CharacterRig / unit-control work is in flight.

Each numbered item is a candidate for a full plan via `dots-task-creator`. Sizes: S (< half day), M (1–2 sessions), L (multi-session system).

---


## What's healthy (verified this pass — preserve)

- **Zero rule violations found**: no `var`, no `.Run()`, no TODO/FIXME markers, no managed allocs spotted in job paths. Rare for a codebase this size.
- **SystemGroups.cs** still reads top-to-bottom in 30 seconds; every new group (Buildings, Design, Sound, Save) slotted in correctly with comments explaining ordering intent.
- **Blob library pattern** held its shape through five new libraries (Part, Behavior, Brain, Sound, Factory). `PartLibraryBakingSystem` seeds safe defaults for every enum slot — missing SO can't index out of range. Keep doing exactly this.
- **The behavior-command interpreter solved the old audit's #1 issue** (action-type explosion). A new unit action is now an SO + enum value, not a 300-line system.
- **EditMode tests exist** (5 fixtures) for the bug-prone pure math: AI curves, blob utils, direction quantization, pathfinding.

---

## Execution order

### 1. Finish the CharacterRig — verification, migration, and hardening its bake path — `L` — [in-flight]

The rig is the foundation everything unit-facing stacks on, and it's currently *partially landed*: code committed, design reworked once already (grid → tag ranges), Editor migration incomplete. Nothing below it should start until this closes. Red flags to fold into the finish work:

- **`verify-characterrig.md` is stale against the shipped design.** It still describes `ExplicitTable` / `StrideFormula` / `colorAxis` (the shape×color grid), but commit `10bd205` replaced that with tag-driven `PartTagRange` lists + `CharacterPalette` string groups. Rewrite the checklist against the tag model *before* running it, or the verification will chase fields that no longer exist.
- **PartLibrary bake has silent-failure gaps** (`PartLibraryBakingSystem.cs`):
  - Duplicate `PartDefinitionSO.id` in the library → last-one-wins silently. Log a bake warning.
  - `ToFixed` uses `CopyFromTruncated` → two tags longer than ~29 bytes can silently collide into the same tag. Log on truncation.
- **`CharacterPalette.groups` capacity is ~7 entries** (`FixedList512Bytes` ÷ 64-byte `PaletteEntry`). Fine for Skin/Hair today, but palette groups are free-text and designed to grow without code changes — an eighth group will fail at runtime, not at bake. Add a bake/apply-time guard that logs when a rig's distinct group count approaches capacity.
- **`PartDefId` is gender-prefixed (`Male*`).** Decide *now*, while only 17 values exist, whether female/child/rotter variants double the enum (append-only, fine) or whether the SO grows a variant dimension. Cheap decision today, expensive re-key later.
- **Add a `DesignApplyUtilTests` EditMode fixture.** `SliceAtOffset` / `TagPoolSize` stride-and-offset math is exactly the class of pure logic every other fixture covers, it has documented edge cases (empty-tag double-count avoidance, clamp-to-fallback), and it's about to be exercised by the whole design pipeline. Use `dots-test`.
- Trivial while in there: `CharacterRigAuthoring.Baker.SortLayers` uses `i`/`j` loop names, violating RULES.md.

### 2. BehaviorSO bake validation for unimplemented commands — `S`

`BehaviorCommandType` declares **SpawnEntity, ModifyStat, StartDialogue, ApplyForce** — and `BehaviorExecutionSystem` has a handler for none of them (13 `case` arms, none of these). A designer wiring one into a `BehaviorSO` gets a silent no-op today. The bake step already validates `interruptionCleanup` for non-blocking-only commands — extend the same validation to reject (or warn on) command types with no interpreter arm. One table of "implemented commands" shared between validator and interpreter kills the sync hazard. Highest severity-to-effort ratio in this audit: the whole point of the SO-driven behavior workflow is that designers can't author broken data.

### 3. Docs truth pass — `S`

The knowledge base has drifted behind the last three big commits, and this codebase leans on its vault harder than most:

- **`Gotchas.md`** — the top entry documents the `AnimatorTarget`/`AnimatorAuthoring` remap fix; every one of those symbols was deleted by the rig commit. The "Motivations are 9 separate components" entry predates the `Motivation` buffer + `NeedType` refactor. Both now actively mislead.
- **`Assets/CLAUDE.md` Current Status** — says awareness stubs `Schedule`/`Weather`/`Enviroment` remain; they don't exist in `_Scripts` anymore. Also this section is ~90 lines and growing per feature — consider moving per-system detail into the vault notes and keeping CLAUDE.md to a pointer, or it will keep rotting.
- **`Tasks/Plans/README.md` status table** — Sound listed "spec ready" (built + verified), Dialogue System + UI listed "not started" (`DialogueUIManager` is 18KB and the editor window is 50KB), UnitDesign/PlayerAttack rows point at moved files.
- Pick **one source of truth for system status** (the README table is the natural one) and have the others link to it.

### 4. Direction System — make the decision before authoring more parts — `M` (decision `S`)

`futureneedsplan.md` defers "multi-facing characters: model swap or more?" — but part-prefab authoring is happening *right now*. If direction ends up meaning per-direction texture slices, that changes `PartTagRange` layouts, `baseImageIndex` conventions, and possibly `PartDef` itself; deciding after 30 part SOs exist means re-authoring all of them. Run the `dots-task-creator` Q&A for the direction system now, even if implementation waits. (`DirectionUtils` + tests already exist for quantization — the groundwork is half-laid.)

### 5. Human → Zombie conversion — `M`

The vertical-slice payoff of everything just built, and it's nearly free now: the rig commit explicitly made zombification "a palette shift" (`ChangeDesignRequest` Skin→Zombie tag), and `SwapBrainRequest` already exists from Minion Revival. This is mostly a small system + one narrative/interaction trigger wiring the two together, plus the non-randomizable Zombie tag ranges in the part SOs. Demo-defining feature, days not weeks. Listed "not started" in the plans README.

### 6. Minion order robustness — `M` — [current work area]

`MinionActionSelectionSystem` is clean, but the attack order hardcodes `ActionType.MeleeSingle`. The moment a ranged or thrown-weapon minion exists, player attack orders on it break silently (its brain has no MeleeSingle def → `actionDefIndex` miss). Resolve the action from the unit's `AvailableAttack` buffer / brain config instead — same resolution `RequestAttack` already does at execution time, moved up to order time. Do this before #8 (ranged), since it's a prerequisite for ordering ranged minions at all. While in here: the command surface is move/attack/interact/follow — sketch what the slice needs (stop? hold position? return-to-player?) so the enum grows once, not four times.

### 7. Despawn System — `M` — [spec ready]

`Despawn_System.md` is approved and unbuilt. It jumps the queue for one reason: **projectiles need pooling**, and building ranged combat (#8) without the central despawn/pool funnel means bespoke projectile lifetime code that gets rewritten a month later. `Lifetime` TTL + pool-vs-destroy is exactly the projectile lifecycle. Execute the existing plan via `execute-plan`.

### 8. Ranged / projectile combat — `L`

The last unbuilt phase of the behavior-recreation queue, and currently **zero code**: no Projectile/Ranged/Shoot symbol exists under `Systems/`. `ActionType` and `BehaviorType` already reserve the values; `BehaviorCommandType.SpawnEntity` is the intended emission point (see #2 — it's unimplemented). Scope: projectile entity + movement/hit (DamageEvent v2 bus already takes source-agnostic damage — the hard half is done), `SpawnEntity` command arm, `ProjectileSingle/Continuous` behaviors, ranged `AttackType` entries, awareness range curves. Combat depth for the slice; also what makes the 1900s-industrial fantasy read.

### 9. Split BehaviorExecutionSystem's command handlers — `M`

At 30KB it's the third-largest file in the project and the single hottest growth point — every new command type (and #8 adds several) lands another arm in one switch inside one job. Before ranged inflates it: extract per-command logic into static utility classes (the `BehaviorQualifiers` precedent already proves the pattern), leaving the switch as thin dispatch. Do it as the *first commit* of #8, or immediately before. This is the same medicine the old audit prescribed for the action-system explosion, one level up.

### 10. Player Resource System + Health UI — `M` + `M` — [resource spec ready]

The demo's feedback layer. `PlayerResource_System.md` is approved and unbuilt (`ResourceStack` ledger + HUD); Health UI is listed not-started and `UI/` confirms nothing exists for it. Both are player-visible and neither blocks nor is blocked by anything above — they slot here, once the unit/combat spine is trustworthy. Resource system first (factory #11 needs it as its output sink).

### 11. Factory minimal loop — `L`

Phase 1 (grid + production tick) is coded but the loop has never run end-to-end: no `ProductionRecipeSO` assets, no `_FactoryLibrary` asset, no placement UI (Phase 2), no worker carry (Phase 3). Vertical-slice scope per `futureneedsplan.md` step 10: **1 product, 1 line, 1 buyer** — resist building the economy. Undead staffing reuses revival + minion orders (#6).

### 12. Schedules + waypoint reintroduction — `M`

The remaining "city feels alive" layer: schedule awareness (P5 of the behavior plan — the old stub was deleted, so this is a fresh `dots-unit-ai` build) and waypoints as scored awareness targets (already called out as "Next" in CLAUDE.md). Needed before any street/crowd demo scene reads as a living place. Not before this point — it's ambience, not spine.

### 13. Crowd-scale pass — enemy/social spatial hash + profile — `M`

Deferred from the May audit: `EnemyAwarenessSystem` / `SocialAwarenessSystem` still scan the faction multimap, not spatial cells (items got the spatial-hash upgrade; these didn't). Fine at dozens of units; the demo's NPC crowd scene targets 200+. Extend `SpatialHashRegistry` the same way `ItemAwarenessSystem` was done, then run the first real profile (Entity Debugger archetype-width check from the old audit too — `BodyPart` at `InternalBufferCapacity(32)` = 512B in-chunk per character is worth eyeballing while there).

### 14. Cleanup batch — `S` each — fill-in work, any idle moment

None urgent, all cheaper now than later (the `Equipt` rename precedent):

| Fix | Where | Why now |
|---|---|---|
| Add `Thirst` to `NeedType`; give Feed/Hydrate distinct effects | `AiEnums.cs`, EffectLibrary | Water restoring Hunger is a design bug waiting to be reported |
| EffectLibrary enum-index collision (Bandage + MedKit share Healing's slot) | EffectLibrary blob | Two SOs silently overwrite one slot — same class of bug as #1's duplicate-id gap |
| Delete `UnitStateType { None }` | `AiEnums.cs` | Dead enum, zero consumers |
| Rename `FlowFeildSystem.cs` / `MotivationDegregationSystem` | Movement, AI | Documented typos; callsite count only grows |
| `groundBufferOverride` authored but unconsumed | `BodyPartAuthoring` | Either wire it in the ragdoll sim or delete the field — unconsumed authoring fields are the two-source-of-truth pattern the old audit warned about |
| `#region` blocks in `UnitSelectionManager` + friends | MonoBehaviours | RULES.md says no regions, no exceptions — either enforce or amend the rule to scope it to ECS code (recommend amending; regions in 15KB manager classes are defensible) |

---

## Watch list (no action yet)

- **`FixedList512Bytes` payloads on save-path components** (`CharacterPalette`, `PersistedDesign`) — blittable-for-IPersist is elegant, but every capacity is a silent ceiling. Note ceilings in the component comments as they're added.
- **Palette/tag string comparisons** in `DesignApplyUtil` run only on design events (spawn/convert), not per-frame — fine. Revisit only if design changes ever become per-frame.
- **`ActionType` at 26 values** with ~10 unimplemented forward declarations (Repair, Build, Smoke, Patrol, Bathroom…) — harmless as reservations, but pair each with a behavior asset when implemented or prune at the next enum touch.
- **Test spine** — PlayMode/World tests for selection+interrupt are still unwritten (known). The interpreter (#9) is the highest-value PlayMode target once split.
