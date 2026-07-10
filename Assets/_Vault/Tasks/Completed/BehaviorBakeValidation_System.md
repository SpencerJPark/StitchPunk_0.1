# Behavior Bake Validation System — Design Spec

> **Status:** 🔨 built (2026-07-03, autonomous pass — recommended defaults adopted: bake severity = WARNING) · awaiting Editor compile + Test Runner + bake-warning verification — see [verify-behaviorbakevalidation.md](verify-behaviorbakevalidation.md).
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) → item #2 ("highest severity-to-effort ratio in this audit")

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-test` — EditMode fixture asserting the catalog covers every implemented command (§5)

---

## 1. Purpose & v1 scope

`BehaviorCommandType` declares **SpawnEntity, ModifyStat, StartDialogue, ApplyForce** — and `BehaviorExecutionSystem` has an interpreter arm for none of them. A designer wiring one into a `BehaviorSO` today gets a silent no-op at runtime. This plan makes the bake step reject (or warn on) any authored command the interpreter cannot run, from **one shared table** so the validator and the interpreter can never drift apart.

**v1 handles:** authored `executionSequence` + `interruptionCleanup` command validation at bake; a loud interpreter default-arm log as the runtime backstop.
**Out of v1:** implementing the four missing commands (SpawnEntity lands with the RangedCombat plan).

## 2. Architecture

A static catalog in `Utils/` (same precedent as `BehaviorQualifiers`):

```csharp
// Utils/BehaviorCommandCatalog.cs
public static class BehaviorCommandCatalog
{
    // The single source of truth: every command the interpreter has an arm for.
    public static bool IsImplemented(BehaviorCommandType commandType) { ... }
    // Already exists conceptually in the bake validator: commands legal in interruptionCleanup.
    public static bool IsNonBlocking(BehaviorCommandType commandType) { ... }
}
```

- `BehaviorLibraryBakingSystem` (PostBakingSystemGroup) validates every command of every `BehaviorSO` against `IsImplemented` — extending the existing `interruptionCleanup` non-blocking validation, which is the pattern to copy.
- `BehaviorExecutionSystem`'s switch gets a `default:` arm that logs the command name via `EnumLogNames.Name()` (Burst-safe — see [[feedback_burst_string_formats]]) once per behavior start, as the backstop for anything that slips past bake.

**← DECISION:** bake failure severity — hard error (behavior refused, baked as Idle) vs `Debug.LogWarning` and bake anyway. *Recommendation: warning in v1 — a wrong-but-visible behavior beats a mysteriously idle unit; escalate to error once the catalog has been stable for a few weeks.*

## 3. Entry points

None new — this hardens the existing SO → `BehaviorLibrary` blob bake path. No runtime request component.

## 4. Data model

No new data. The catalog is code (a switch over `BehaviorCommandType`), deliberately NOT data — it must change in the same commit as an interpreter arm.

## 5. Systems

- **Edited:** `PostBakingSystemGroup/BehaviorLibraryBakingSystem.cs` — add the `IsImplemented` sweep next to the existing cleanup validation; log offending SO name + command + index.
- **Edited:** `StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs` — `default:` log arm.
- **New test:** `Tests/BehaviorCommandCatalogTests.cs` (EditMode) — asserts every `BehaviorCommandType` enum value is classified by the catalog (no unhandled values), and pins the implemented set so adding an enum value without a catalog decision fails the Test Runner.

## 8. Proposed file manifest

**New:** `Assets/_Scripts/Utils/BehaviorCommandCatalog.cs`, `Assets/_Scripts/Tests/BehaviorCommandCatalogTests.cs`
**Edited:** `BehaviorLibraryBakingSystem.cs`, `BehaviorExecutionSystem.cs` (default arm only — do not restructure the switch here; that is the BehaviorCommandSplit plan)

## 9. Build phases

1. Catalog + EditMode test (pure code, no Editor needed).
2. Bake validation sweep in `BehaviorLibraryBakingSystem` + warning log.
3. Interpreter default-arm log.

## 10. Verification

Author a throwaway `BehaviorSO` containing `StartDialogue`, re-bake (re-open subscene) → console shows the warning naming the SO and command. Remove it → clean console. Run EditMode suite → catalog test green.

## Open decisions (collected)

- [ ] §2 — bake severity: warn (recommended) vs hard error.
