---
tags: [home, index]
---

# Stitch Punk — Vault Home

This is the knowledge base for **Stitch Punk** — a 2.5D necro-engineering RTS set in the 1900s industrial revolution. Open this folder in [Obsidian](https://obsidian.md) for graph view, backlinks, and full-text search.

**Current goal:** Get the ~1-hour demo across the finish line. See [[Demo/Overview]] for system status and [[Demo/Phases]] for the scene checklist.

---

## Demo Roadmap

| File | What it covers |
|---|---|
| [[Demo/Overview]] | Full system build status, design pillars, build order, scope risks |
| [[Demo/Phases]] | All 18 scenes with completion checkboxes — check off when scene is playable |

---

## Parallel Work Lanes

Spencer and Claude work in parallel. Each has their own task queue.

### Claude's Queue
Claude can pick these up and execute autonomously in any session.

| File | Contents |
|---|---|
| [[Tasks/Claude/Code_Bugs]] | Known bugs — root cause + fix instructions ready to execute |
| [[Tasks/Claude/Code_Systems]] | New systems in recommended build order |
| [[Tasks/Claude/Marketing_Copy]] | Copy and devlog scripts Claude can draft |

### Spencer's Queue
Tasks only Spencer can do. **Answer [[Tasks/Spencer/Design_Decisions]] first** — those are blocking Claude.

| File | Contents |
|---|---|
| [[Tasks/Spencer/Design_Decisions]] | Open design questions — answer these to unblock Claude |
| [[Tasks/Spencer/Art_Assets]] | All environments, characters, animations by phase |
| [[Tasks/Spencer/Audio]] | Music, SFX, voice acting |
| [[Tasks/Spencer/Content_Recording]] | Devlog recording schedule tied to demo milestones |

---

## Code Memories

Read the relevant file before working in any `_Scripts/` subfolder.

| File | What it covers |
|---|---|
| [[RULES]] | Hard coding rules — naming, DOTS patterns, job attributes |
| [[Authoring]] | Baker pattern, unit prefab structure, cross-entity baking |
| [[Components]] | All IComponentData / IBufferElementData types by system |
| [[Systems]] | System group execution order + full system file map |
| [[Systems_AI]] | Motivation scoring pipeline, waypoint interactions, Brain/Body split |
| [[Systems_Animation]] | Layered quad animation, clip SO → blob pipeline, spawn gotcha |
| [[Systems_Movement]] | FlowField / D* Lite, horde vs individual movement |
| [[Data]] | ScriptableObjects, BlobAssets, enums, save DTOs |
| [[Core]] | Singleton base classes, render features, legacy files |
| [[MonoBehaviours]] | ECS ↔ MonoBehaviour bridge, input, camera managers |
| [[Gotchas]] | Non-obvious traps — spawning, AI startup, component lookups, open bugs |

---

## Marketing

| File | What it covers |
|---|---|
| [[Memories/Marketing/Strategy]] | Audience-building strategy, devlog angle, platform priority, go-public checklist |

---

## Raw Ideas

[[Raw/_Template\|Raw/]] — Drop unstructured ideas here. Mark `ingested: true` and link outputs when processed.

---

## How Claude Uses This Vault

1. **Start of session** → check [[Tasks/Claude/Code_Bugs]] and [[Tasks/Claude/Code_Systems]] for what to work on.
2. **Before any folder** → read the Code Memory file from the table above.
3. **Design questions** → check [[Tasks/Spencer/Design_Decisions]] before building systems that need them answered.
4. **After solving a non-obvious problem** → update [[Memories/Code/Gotchas]] or the relevant memory file.
5. **Scene complete** → check it off in [[Demo/Phases]].
6. **New system added** → update the relevant `Memories/Code/` file and add it to [[Demo/Overview]] build status.
