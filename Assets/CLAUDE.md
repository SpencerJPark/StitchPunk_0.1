# Stitch Punk — CLAUDE.md

This file is the entry point for Claude Code. Before working in any folder, read the relevant CONTEXT.md listed below. Be sure to keep these updated in the _Vault directory as these are meant to bee tools for you, so if you can or create a new directorty/script, make sure the docs reflect that to help you further down the line
Do not make any changes until you have 95% confidence in what you need to build. Ask me follow-up questions until you reach that confidence.

**You are going to help me by playing the role of an expert when it comes to coding and dots in game development**
---

Game Overview Context

**Stitch Punk** is a 2.5D real-time strategy game set in the 1900s industrial revolution. The player is a **necro engineer** who reanimates corpses as minions, manages a factory, and navigates a murder mystery. The core loop is RPG-style missions + factory building + trade management.

The world is one where **necromancy-based engineering** is the primary driver of technological advancement — "stitched together tech." Minions are Frankenstein constructs: layered, sewn-together parts.


Always write code explicitly, never use var
As a reminder code using [Readonly] needs to import from Unity.Collection
Preference using EntityJobs were it make sense in systems

Folder Map — Read Before Working

| Folder | Context File | What's Inside |
|---|---|---|
| `_Scripts/` | [RULES.md](_Vault/Memories/Code/RULES.md) | Hard technical rules for the whole codebase |
| `_Scripts/Authoring/` | [Authoring.md](_Vault/Memories/Code/Authoring.md) | Baker pattern, unit prefab setup, how to wire new units |
| `_Scripts/Authoring/Save/` | [Authoring.md](_Vault/Memories/Code/Authoring.md) | `GameDataAuthoring` — bakes the GameData singleton entity (save, settings) |
| `_Scripts/Components/` | [Components.md](_Vault/Memories/Code/Components.md) | IComponentData / IBufferElementData conventions |
| `_Scripts/Components/Save/` | [Components.md](_Vault/Memories/Code/Components.md) | `GameDataTag`, `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings` |
| `_Scripts/Systems/` | [Systems.md](_Vault/Memories/Code/Systems.md) | System group order, ISystem rules, Burst |
| `_Scripts/Systems/AISystemGroup/` | [Systems_AI.md](_Vault/Memories/Code/Systems_AI.md) | Motivation scoring, waypoint interactions, Brain/Body |
| `_Scripts/Systems/AnimationSystemGroup/` | [Systems_Animation.md](_Vault/Memories/Code/Systems_Animation.md) | Layered quad animation, clip SO pipeline |
| `_Scripts/Systems/MovementSystemGroup/` | [Systems_Movement.md](_Vault/Memories/Code/Systems_Movement.md) | Flowfield, D* Lite, horde formation |
| `_Scripts/Systems/SaveSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Play time tracking, auto-save timer, save/load to JSON on disk |
| `_Scripts/Data/` | [Data.md](_Vault/Memories/Code/Data.md) | ScriptableObject pattern, BlobAsset baking, enums, save DTOs |
| `_Scripts/Core/` | [Core.md](_Vault/Memories/Code/Core.md) | Singletons, legacy files, what to avoid |
| `_Scripts/MonoBehaviours/` | [MonoBehaviours.md](_Vault/Memories/Code/MonoBehaviours.md) | Managers, input, camera (non-ECS layer) |

Vault Structure — `_Vault/`

`_Vault/` is an [Obsidian](https://obsidian.md) vault. Open it directly in Obsidian for graph view, backlinks, and full-text search across all project knowledge. Start at `_Vault/Home.md`.

| Directory | Purpose |
|---|---|
| `_Vault/Memories/Code/` | Per-folder context files (read before working in that folder) |
| `_Vault/Tasks/Active/` | In-flight tasks — create one `.md` per task using `_Template.md` |
| `_Vault/Tasks/Done/` | Completed tasks — move finished tasks here |
| `_Vault/Raw/` | Unstructured ideas — drop notes here; ingest into context files when ready |
| `_Vault/Memories/Marketing/` | Marketing copy, tone of voice, audience notes |

**How Claude should use the Vault:**
- **Before starting work** in any folder → read the context file from the Folder Map above.
- **Tasks** → check `_Vault/Tasks/Active/` for open tasks in your work area. Update status when done.
- **Raw ideas** → if the user pastes a raw idea, save it to `_Vault/Raw/` using the template.
- **After solving a non-obvious problem** → save a note in `_Vault/Memories/Code/` so future sessions skip the re-discovery.
- **Keep memories current** — if you add a new system, component, or directory, update the relevant file in `_Vault/Memories/Code/`.

---

Current Status

We are working on the dialogue editor window. You can click on the nodes on the right to add them to the editor, but I would like to also be able to drag and drop. I noticed a bug where if I press on the different dialogue trees it deletes the connections and then if I do it again it deletes the actual tree. also if I press start twice it creates two starts and both can't be deleted properly. so we need to fix those bugs
and we need to add the ability to create new dialogue trees, add in the ability to set up refresher paths to the editor (my idea is that it will show up in the same area and will still use and end, but instead of start we use refresher. if none is present it just defaults to start. and I would like to better understand events and how to set them. I possibly will want to be able to pick my own custome dots based events or something like that that uses enableable components and i can pick which ones get enabled.
