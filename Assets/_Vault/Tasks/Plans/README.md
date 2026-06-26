# System Design Plans

This folder holds one **self-contained design-doc `.md` per system** Stitch Punk still needs. Each doc is detailed enough that Spencer can edit it inline and hand it back as an **executable spec** — the C# is only written after a doc is approved.

## Workflow

1. Pull the next system from the raw braindump in [`../futureneedsplan.md`](../futureneedsplan.md).
2. Claude asks a Q&A round to flesh out the architecture and lock the foundational decisions.
3. Claude drafts `Plans/<System>.md` with the spec + inline **← DECISION** markers for any sub-choices left open.
4. Spencer edits the doc (resolves the markers, tweaks scope).
5. Spencer hands the doc back → Claude builds it in the suggested phases.

Every system shares the codebase's architectural identity: **accessible from outside via data components (the "request model")**, and **entered either by a component on the entity it acts on, or by another system spawning a one-frame signal entity** — the `LoggingSystem` pattern (spawn `LogMessage` entity → presentation system reads + acts + destroys).

## Authored by `dots-task-creator`

The planning workflow below is codified in the **`dots-task-creator`** project skill (`.claude/skills/dots-task-creator/`). Invoke it (or just say "plan the X system") to run the Q&A and generate a new spec in this folder from the standard template. See the [Skills index](../../Memories/Code/Skills.md).

## Skills Needed convention

Each plan doc lists, near the top under a **`Skills Needed`** heading, the **project skills** (in `.claude/skills/`) relevant to building it — by name (e.g. `dots-blob-library`, `dots-system-scaffold`, `dots-authoring-baker`, `dots-unit-ai`). This tells the build step which scaffolding skills to invoke. See the [Skills index](../../Memories/Code/Skills.md) for what each one does.

## Status legend
⬜ not started · 📝 spec drafting · ✅ spec ready · 🔨 building · ✔️ done

## Systems

| System | Doc | Status |
|---|---|---|
| **Player Attack** (melee swing → AttackRequest; separate CombatTarget; revives PlayerAttackSystem) | [PlayerAttack_System.md](PlayerAttack_System.md) | ✅ spec ready |
| **Sound** (SFX / ambient loops / layered music) | [Sound_System.md](Sound_System.md) | ✅ spec ready |
| **DamageEvent** (attack/damage refactor → one-frame signal entity, deletes Hurt buffer) | [DamageEvent_System.md](DamageEvent_System.md) | ✅ spec ready |
| Dialogue System + UI | — | ⬜ not started |
| **Save System** (generic `IPersist` serializer, minion design, travel + manual save) | [Save_System.md](Save_System.md) | ✅ spec ready |
| Building System (structures, storage) | — | ⬜ not started |
| **Despawn System** (central `Despawn` funnel: pool-vs-destroy via `DespawnMode` + `PoolOwner`, per-type cap, `Lifetime` TTL producer) | [Despawn_System.md](Despawn_System.md) | ✅ spec ready |
| **Player Resource System** (`ResourceStack` ledger + delta-buffer mutation + IPersist snapshot + HUD) | [PlayerResource_System.md](PlayerResource_System.md) | ✅ spec ready |
| Game UI — Health | — | ⬜ not started |
| Minion Systems → new state machine | — | ⬜ not started |
| **Minion Revival & Life-State** (revive→zombie minion via `SwapBrainRequest`, `Alive` deprecation) | [MinionRevival_System.md](../Verification/MinionRevival_System.md) | 🔨 built — code landed (Ph1–4), verify pending |
| **Brain Control Split** (UtilityBrain=decision / StateMachine=execution; death blank-slate, player-controlled revive, minion self-defence) | [BrainControlSplit_System.md](../Verification/BrainControlSplit_System.md) | 🔨 built — code landed, verify pending |
| Animations (content) | — | ⬜ not started |
| **Unit Design** (per-part random texture indices, minion persistence) | [UnitDesign_System.md](UnitDesign_System.md) | 🔨 built — code landed, verify pending |
| Human → Zombie Conversion | — | ⬜ not started |
| Menu UI | — | ⬜ not started |
| Interactions/Behaviors (bulk, AI-assisted SO setup) | — | ⬜ not started |
| Trade System Group | — | ⬜ not started |
| Vehicle System (driving, caravan base) | — | ⬜ not started |
| Direction System (multi-facing characters) | — | ⬜ not started |

> Build-order notes for narrative/scene-driven systems (Dialogue, Narrative Events, Cinematic Camera, Feral Zombie AI, etc.) live in the lower half of [`../futureneedsplan.md`](../futureneedsplan.md) and will graduate into their own docs here as they're picked up.
