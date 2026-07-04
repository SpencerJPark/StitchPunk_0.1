---
name: execute-plan
description: Execute (build) an approved Stitch Punk plan — a self-contained system spec authored by dots-task-creator in Assets/_Vault/Tasks/Plans/<System>_System.md (with ← DECISION markers, a file manifest, build phases, and a verification section). Use whenever the user wants to BUILD / IMPLEMENT an existing plan rather than design one — "execute the X plan", "build the X system from the plan", "enact/implement the plan for Y", "build out the approved spec", "run the X plan". The skill first asks clarifying questions until every ← DECISION marker and ambiguity is resolved, then builds the system phase-by-phase using the dots-* scaffolding skills the plan lists under Skills Needed, updates the vault docs, moves the completed plan into Assets/_Vault/Tasks/Verification/ alongside a verify-<system>.md steps file, and commits + pushes to main. This is the execution counterpart to dots-task-creator. Do NOT use for: planning / designing / spec'ing a new system (use dots-task-creator), scaffolding a single C#/baker/blob/AI file (use the dots-* skills directly), or debugging existing code.
---

# execute-plan

The **execution** half of the Stitch Punk planning loop. `dots-task-creator` writes a plan into `Assets/_Vault/Tasks/Plans/`; this skill turns an approved plan into working code, retires the plan into `Assets/_Vault/Tasks/Verification/` with its verification steps, and commits the result.

**A "plan" is** a self-contained spec doc at `Assets/_Vault/Tasks/Plans/<System>_System.md`: a Context section, Skills Needed, data model, systems, a file manifest, numbered build phases, a verification section, and inline `← DECISION` markers (plus a closing Open-Decisions checklist) for choices Spencer was meant to lock. See `dots-task-creator` for how plans are authored.

## When to use

Trigger on execution phrasing for an existing plan:
- "execute / build / enact / implement the X plan"
- "build the X system from the plan" / "build out the approved spec"
- "run the plan for Y"

Do **not** use for: planning or designing a system (that's `dots-task-creator`); scaffolding one C#/baker/blob/AI file (use `dots-system-scaffold` / `dots-authoring-baker` / `dots-blob-library` / `dots-unit-ai` directly); or debugging existing code.

## Workflow

### 1. Locate + read the plan
Resolve which `Assets/_Vault/Tasks/Plans/<System>_System.md` the user means (confirm in one line if ambiguous). Read it **in full** — Skills Needed, data model, systems, file manifest, build phases, verification, and every `← DECISION` marker + the Open-Decisions checklist.

### 2. Clarify until fully understood (the core of this skill)
**Before writing any code**, run `AskUserQuestion` in batches of ≤4 to resolve **every unresolved `← DECISION` marker**, any ambiguity in the manifest or phases, and to confirm scope (whole plan vs a subset of phases). Recommend a default for each question and list it first. Keep going for as many rounds as it takes — do not start building while open questions remain. Record the resolved choices so they can be written back into the plan/verify docs.

### 3. Ground in the codebase (reuse over reinvent)
Read the patterns the build will reuse so the code matches the project exactly:
- `Assets/_Vault/Memories/Code/RULES.md` — non-negotiable conventions.
- The relevant per-folder docs: `Systems.md`, `Components.md`, `Authoring.md`, `Data.md`, and sub-group docs (`Systems_AI.md`, `Systems_Animation.md`, `Systems_Movement.md`).
- `Assets/_Scripts/Systems/SystemGroups.cs` — the truth source for group ordering.
- The closest existing systems/components/utilities named in the plan — **actively reuse them; don't reinvent.**

Use the scaffolding skills the plan lists under **Skills Needed** for each new file: `dots-system-scaffold` (ISystem + IJobEntity), `dots-blob-library` (SO→Blob library), `dots-authoring-baker` (MonoBehaviour + Baker), `dots-unit-ai` (AI behaviour / ActionType / awareness / interrupts).

### 4. Build, phase by phase
Implement the plan's build phases **in order**. Honor the conventions: no `var`, explicit types everywhere, `[ReadOnly]` imported from `Unity.Collections`, **never `.Run()`** a job (use `.Schedule()` / `.ScheduleParallel()` with `state.Dependency`), `state.RequireForUpdate<GameSceneTag>()`, `EnabledRefRW`/`RO` params named `<component>Enabled`, global namespace (the codebase uses none).

For every **new `.cs` file and new folder**, hand-generate a Unity `.meta` so the commit is complete without opening Unity:
- Script meta: `fileFormatVersion: 2`, a unique 32-hex `guid`, then a `MonoImporter:` block.
- Folder meta: `fileFormatVersion: 2`, a unique `guid`, `folderAsset: yes`, then a `DefaultImporter:` block.

Generate GUIDs with `[guid]::NewGuid().ToString("N")` (PowerShell). Match an existing `.cs.meta` in the repo for the exact block shape.

### 5. Compile gate (manual — no Unity MCP) + static self-review
Always do the **static review** against the in-repo patterns you read in step 3 (group/order attributes, `IsCreated` dispose guards on blobs/native containers, one-shot `DestroyEntity(query)` lifecycle, enableable-request consume/disable, blittable `IPersist` structs, etc.).

There is **no Unity MCP / Editor bridge** — do not attempt `mcp__unity-mcp__*` tools. The compile gate is manual:
- Ask the user to focus Unity (triggers a recompile) and report the Console, or grep the Editor log (`C:/Users/spenc/AppData/Local/Unity/Editor/Editor.log`) for fresh `error CS####` / Burst `BC####` lines after they have done so. Zero errors = gate passed; fix anything surfaced and re-check.
- For a system with an on-screen result, ask the user to confirm visual state (or paste a screenshot).
- If the user isn't at the Editor, **say so plainly**, rely on static review only, and leave the real compile + play-test to the verification steps (step 7). Do not block the build on an unavailable Editor.

### 6. Full housekeeping
Keep the vault current (per `Assets/CLAUDE.md`):
- Flip the plan's status banner to 🔨 built.
- Update the plan's row in `Assets/_Vault/Tasks/Plans/README.md` (status + repoint the link to the new `Verification/` location).
- Update the relevant memory docs in `Assets/_Vault/Memories/Code/` (`Components.md`, `Systems.md`, `Authoring.md`, `Data.md`) with the new components / systems / authoring / data types.

### 7. Complete → move to Verification (move + steps)
Once built and housekept, retire the plan into the verification area:
- `git mv Assets/_Vault/Tasks/Plans/<System>_System.md Assets/_Vault/Tasks/Verification/<System>_System.md` — the completed plan leaves `Plans/`.
- Create `Assets/_Vault/Tasks/Verification/verify-<system>.md` beside it — the **verification steps**, in the established `verify-*.md` format:
  - YAML frontmatter: `title`, `status: active`, `created: <today, absolute date>`, `area: code`.
  - `## Goal`, then `## Steps` with `### <group>` headings and `- [ ]` checkboxes, then `## Notes`.
  - Capture every step the skill **could not** do itself — Editor asset creation, scene/prefab wiring, and Play-mode checks — distilled from the plan's verification section, plus any gotchas (e.g. hand-generated `.meta` GUIDs → confirm no duplicate-GUID warnings on first import).

### 8. Commit + push
Stage only the files this build touched — the new/edited `.cs` + `.meta`, the updated vault docs, and the moved/created `Tasks/Verification/` files. **Exclude unrelated working-tree changes** (e.g. `.claude/settings.local.json`). Commit with a clear message describing the system and a trailer:

```
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```

Push to `origin main` (the repo's solo direct-to-main workflow) and report the pushed commit hash.

## Mode note

This skill writes code → run it in **normal mode**. If invoked while in plan mode, present the execution plan (resolved decisions + manifest + phases) as the plan body and start building once the plan is approved.

## Pairing

`dots-task-creator` plans (writes `Tasks/Plans/`). `execute-plan` builds and retires the plan into `Tasks/Verification/`. Together: **plan → build → verify → commit.**
