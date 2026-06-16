---
name: dots-task-creator
description: Author a self-contained system design/plan doc in the Stitch Punk vault (Assets/_Vault/Tasks/Plans/). Use whenever the user wants to PLAN a new system or feature rather than build it — "plan the X system", "flesh out X", "make a plan for Y", "let's design the Z system", "create a task plan for…", "spec out W". Runs a batched Q&A (AskUserQuestion rounds) to lock the architecture decisions, references the relevant project scaffolding skills (dots-blob-library / dots-system-scaffold / dots-authoring-baker / dots-unit-ai) under a Skills Needed heading, and writes the spec from the plan template with ← DECISION markers, build phases, and a verification section. Planning only — it does NOT write game code; a separate execution skill builds an approved plan. Do NOT use for: implementing or building a system, scaffolding individual C# files (use the dots-* skills directly), debugging, or any non-planning edit.
---

# dots-task-creator

Codifies the planning loop used to produce the Stitch Punk system specs in `Assets/_Vault/Tasks/Plans/`. Given a system or feature to plan, it runs a structured Q&A to lock the architecture, flags which scaffolding skills the build will use, and writes one **self-contained, editable spec** that Spencer can edit and then hand to the execution skill.

**This skill plans. It does not build.** Never write game code (`.cs`), bake assets, or modify systems from this skill. The only file it creates is the plan doc (plus an index line). A separate execution skill turns an approved plan into code.

## When to use

Trigger on planning phrasing:
- "plan the X system" / "let's design X" / "spec out X" / "flesh out X"
- "make a plan / task for Y"
- "I want to build Z eventually — let's plan it first"

Do **not** trigger when the user wants to actually implement something now, scaffold a single C#/baker/blob file (use `dots-system-scaffold` / `dots-authoring-baker` / `dots-blob-library` / `dots-unit-ai`), or debug existing code.

## The architectural identity every plan must respect

Every Stitch Punk system is **accessible from outside via data components** (the "request model") and is **entered one of two ways**:
1. **A component living on the entity it acts on** — an `IEnableableComponent` "request" (e.g. `AttackRequest`, `PathRequest`, `AnimationRequest`) that a system reads, acts on, clears, and disables.
2. **Another system spawning a one-frame signal entity** — the `LoggingSystem` pattern: spawn an entity carrying a data component, a system reads all of them, acts, then `DestroyEntity(query)`.

Pin down which of these the new system uses early — it's the first foundational question.

## Workflow

1. **Identify the system.** From the user's request and/or the raw braindump in `Assets/_Vault/Tasks/Plans/futureneedsplan.md`. Read that system's section if it exists. Confirm scope with the user in one line before diving in.

2. **Ground in the codebase.** Before asking questions, read the patterns the system will reuse so your questions are specific, not generic:
   - Per-folder context files in `Assets/_Vault/Memories/Code/` (RULES, Systems, Components, Data, Authoring, MonoBehaviours…).
   - `Assets/_Scripts/Systems/SystemGroups.cs` for group ordering.
   - The closest existing system to the one being planned (search for similar request components / system groups).
   - **Actively look for existing components, utilities, and patterns to reuse** — a good plan reuses; it doesn't reinvent.

3. **Flag Skills Needed.** Consult the index at `Assets/_Vault/Memories/Code/Skills.md` (mirrors `.claude/skills/`) and decide which scaffolding skills the *build* will use:
   - `dots-blob-library` — any SO→Blob library / enum-indexed config data.
   - `dots-system-scaffold` — each new `ISystem` + `IJobEntity`.
   - `dots-authoring-baker` — each new MonoBehaviour + Baker.
   - `dots-unit-ai` — new reactive/scheduled unit behaviour, `ActionType`/`MotivationType`, awareness, interrupts.

4. **Batched Q&A.** Drive the questions from `references/planning-questions.md`. Use `AskUserQuestion` in rounds of ≤4. Order: **foundational architecture first** (entry pattern, ECS-vs-MonoBehaviour split, system-group placement), then scope → data model → perf/scale → integration points. For each question **recommend a default** and put it first; only ask on genuine forks; record anything the user defers. Keep going for as many rounds as it takes to remove ambiguity — efficient, not fast.

5. **Write the spec.** Create `Assets/_Vault/Tasks/Plans/<System>_System.md` from `references/plan-template.md`. Fill every section with real type/file references found in step 2. Use `← DECISION` markers for sub-choices left to Spencer, and collect them into the closing Open-Decisions checklist. Include concrete Build Phases and a Verification section (how to test in `DOTSTestScene` / the Editor).

6. **Register it.** Add/maintain the row in `Assets/_Vault/Tasks/Plans/README.md` with status `✅ spec ready`.

7. **Stop.** Hand the doc back for Spencer to edit the `← DECISION` markers. Do not start building — that's the execution skill's job.

## Mode note

This skill writes a vault markdown doc, which is **not** a code change — run it in normal mode. If invoked while in plan mode, produce the spec as the plan-mode plan body and write the vault doc once the plan is approved.

## Reference files
- `references/planning-questions.md` — the DOTS question-category checklist that drives step 4.
- `references/plan-template.md` — the spec skeleton for step 5 (mirrors `Sound_System.md`).
