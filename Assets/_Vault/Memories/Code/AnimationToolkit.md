---
tags: [memory, code, package, animation-toolkit]
related: "[[RULES]]"
---

# Packages/com.dotsanimationtoolkit — DOTS Animation Toolkit

A sellable UPM package, developed under a **separate doc system** from this
repo's own `_Vault/`: `Docs/AnimationToolkit/`. Before touching anything under
`Packages/com.dotsanimationtoolkit/`, read
[`Docs/AnimationToolkit/HANDOFF.md`](../../../Docs/AnimationToolkit/HANDOFF.md)
first — it names the active spec, the standing owner directives, and what a
session may not decide alone. Everything else in `Docs/AnimationToolkit/` is
closed-phase history; do not read it up front.

## The vocabulary pattern (target tags, event names)

As of amendment A51 (Phase E), two project-wide vocabularies exist —
`TargetTagRegistry` and `AnimEventKeyRegistry` — both auto-created under
`ProjectSettings/` on first use via `VocabularyRegistryProvider`, no asset to
create by hand. Both follow the same rule: **a name is typed in exactly one
place, the registry, through `VocabularyPicker`'s inline "Create …" row or its
"Edit …" row into the registry inspector.** Every other editor surface —
pickers, rig rows, timeline lanes — only ever selects, never accepts free
text. If you add a third vocabulary (or extend either of these two), route it
through `IVocabularyRegistry` and `VocabularyPicker`/`VocabularyPickerConfig`
rather than building a parallel dropdown — that duplication is exactly what
amendment E6 Task 3 undid for events.

**No raw ids in an editor surface.** A tag id or event key is display-only as
`(unresolved 0x1A2B3C4D)`, and only when the registry cannot name it (a
dangling reference after a delete). Anywhere else, resolve the name first.

See [`sharing-clips.md`](../../../Packages/com.dotsanimationtoolkit/Documentation~/sharing-clips.md)
for the target-tag authoring workflow and
[`animation-events.md`](../../../Packages/com.dotsanimationtoolkit/Documentation~/animation-events.md)
for events.

## Event lanes are per-name, not per-track

`ClipAsset.events` is one flat list; the Clip Editor's timeline gives each
distinct event name its own row (lane), addressed through
`EventLaneAddressing` — a pure function mapping `(laneIndex, localIndex)` to
a flat list position, recomputed on demand rather than cached. **The flat list
is not kept globally time-sorted** — each lane's own sort writes back only
into the flat slots that lane's markers already occupied. That's safe only
because nothing downstream needs global order: validation checks events
against V04/V09 (never V03), and `ClipRegistryBuilder.FillEvents` re-sorts by
time before baking regardless of authoring order. If you add code that reads
`ClipAsset.events` directly, do not assume it is time-ordered.

## Verification gate

Unity MCP only works while the Editor is open. After a `.cs` change:
`refresh_unity(compile: "request")` → poll `editor_state` until idle →
`read_console` for `error CS####` → `run_tests` EditMode
(`DotsAnimationToolkit.Tests.EditMode`) → PlayMode
(`DotsAnimationToolkit.Tests.PlayMode`, `init_timeout: 120000`). Check the
discovered **total**, not just pass/fail — `total: 0` with `resultState:
"Passed"` is a suite that silently stopped compiling.

## Do not spawn subagents against this package

Three processes driving one live Unity Editor already caused MCP lock
contention that grew `Logs/Editor.log` to 2.2 GB and broke test runs (per
HANDOFF.md). Work sequentially, one editor-connected agent at a time.
