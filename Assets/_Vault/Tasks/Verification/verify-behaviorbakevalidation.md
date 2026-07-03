---
title: Verify — Behavior Bake Validation
status: active
created: 2026-07-03
area: code
---

## Goal

Confirm the `BehaviorCommandCatalog` pipeline: authoring an unimplemented `BehaviorCommandType` into a `BehaviorSO` warns at bake (naming the SO, sequence, index, and command), and the interpreter's default arm logs instead of silently skipping. Built without an Editor connection — **nothing below has been compile-checked yet.**

## Steps

### Compile + tests (first Editor session)

- [ ] Focus the Editor, let it recompile — console must be free of `error CS####` / `BC####` (new files: `Utils/BehaviorCommandCatalog.cs`, `Tests/BehaviorCommandCatalogTests.cs`; edited: `BehaviorLibraryBakingSystem.cs`, `BehaviorExecutionSystem.cs`).
- [ ] Test Runner ▸ EditMode ▸ run `BehaviorCommandCatalogTests` — all 3 green.
- [ ] Confirm no duplicate-GUID warnings on first import (new `.cs` files had no hand-made `.meta`; Unity generates them on this import).

### Bake warning path

- [ ] Add a `StartDialogue` command to any `BehaviorSO`'s `executionSequence` (throwaway edit).
- [ ] Re-open the DOTSTestScene subscene (re-bake) → console shows `[BehaviorLibraryBaking] '<asset>' executionSequence[N] is StartDialogue — no interpreter arm exists…`.
- [ ] Remove the throwaway command → re-bake → warning gone.

### Runtime backstop

- [ ] (Optional) leave the throwaway command in, enter Play mode, trigger the behavior → `[BehaviorExecution] Unimplemented command StartDialogue in <behavior> — skipping` appears once per pass (requires the StateMachine log category enabled in `LoggingConfig`), and the unit advances past it instead of stalling.

### Regression smoke

- [ ] Standard AI smoke in DOTSTestScene: wander → interact → fight → flee → talk → sit all still run (the cleanup-validation refactor delegated `IsBlockingCommand` to the catalog — semantics must be unchanged).

## Notes

- Bake severity was left at **warning** (the plan's recommended default) — escalate to hard error later by swapping `LogWarning` → `LogError` + skipping the command in `BehaviorLibraryBakingSystem` once the catalog has been stable for a few weeks.
- When `SpawnEntity` is implemented (RangedCombat plan): flip it in `BehaviorCommandCatalog.IsImplemented` AND `BehaviorCommandCatalogTests` in the same commit — the test fails otherwise, by design.
