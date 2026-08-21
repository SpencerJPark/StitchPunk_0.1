---
name: plan-system
description: Plan and build a Stitch Punk system through the vault pipeline — a spec at Assets/_Vault/Tasks/Plans/<System>_System.md with ← DECISION markers, then a phase-by-phase build that retires the spec into Tasks/Verification/ with a verify-<system>.md checklist. Use for "plan the X system" or "build the X plan"; not for scaffolding a single file.
---

# Plan → build → verify

One workflow, two entry points. **Planning** writes the spec; **building** resolves its decisions and
implements it. Do not write game code during the planning half.

## Planning

Ask the architecture questions in `AskUserQuestion` batches of ≤4 — recommend a default and list it first,
and only ask on genuine forks. The two that are specific to this codebase and always worth asking:

- **Entry pattern:** an enableable request component, a one-frame signal entity, or a singleton?
- **Group placement:** which group in `Assets/_Scripts/Systems/SystemGroups.cs`, and does this need a new
  top-level feature group (→ `dots-feature-group`)?

Write to `Assets/_Vault/Tasks/Plans/<System>_System.md` and register it in `Plans/README.md`. The spec holds:
Context · **Skills Needed** (which `dots-*` skills the build will use) · data model · systems · file manifest ·
numbered build phases · verification section · inline `← DECISION` markers plus a closing Open-Decisions checklist.

## Building

1. **Read the plan in full**, then resolve **every** open `← DECISION` marker with the user before writing
   code. This is the core of the build half — do not start with questions outstanding.
2. **Ground in the codebase first:** `RULES.md`, the relevant `_Vault/Memories/Code/*.md`, `SystemGroups.cs`,
   and the nearest existing systems named in the plan. Reuse them; don't reinvent.
3. **Implement the phases in order**, using the scaffolding skills the plan lists under Skills Needed.
   Unity generates `.meta` files — call `mcp__UnityMCP__refresh_unity` rather than hand-rolling GUIDs.
4. **Compile gate:** `refresh_unity` → poll `editor_state.isCompiling` → `mcp__UnityMCP__read_console`.
   Then a static self-review against the patterns read in step 2 (group attributes, `IsCreated` dispose
   guards, one-shot `DestroyEntity(query)` lifecycle, enableable-request consume/disable).
   If the Editor is unavailable, **say so plainly** and leave the real compile to verification —
   do not claim it compiles, and do not block the build.

## Retiring

`git mv` the plan into `Assets/_Vault/Tasks/Verification/` and write `verify-<system>.md` beside it —
frontmatter (`title`, `status`, `created`, `area`), a `## Goal`, then checkbox steps. **Capture only what
the Editor can actually check**: what to look at, in which scene, after which action. Update the links in
`Plans/README.md` so they point at the new location — this step is the one that keeps getting skipped, and
the README currently has several dead links because of it. Then update the affected
`_Vault/Memories/Code/*.md` notes and commit.
