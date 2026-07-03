# Behavior Command Split — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #9 — "do it as the first commit of RangedCombat, or immediately before"

---

**Skills Needed:**
- `dots-test` — first PlayMode World fixture over the interpreter once split (§10, stretch)

---

## 1. Purpose & v1 scope

`BehaviorExecutionSystem.cs` is ~30KB / 627 lines — the third-largest file in the project and the hottest growth point: every new `BehaviorCommandType` lands another arm in one switch inside one job, and RangedCombat adds several. Extract per-command logic into static handler methods, leaving the switch as thin dispatch. **Pure refactor: zero behavior change.** The `BehaviorQualifiers` extraction is the proven precedent.

**Also in scope (same surgery, from the structural review):** the Item-domain inline work — the equip/attach bookkeeping using `EquipBy`/`AttachedTo`/`AttachItemRequest`/`UnitEquip` lookups — moves behind the extracted `RequestPickup` handler as a first step toward "commands only enable requests" (the file's own stated rule).

## 2. Architecture

```
Utils/BehaviorCommands/            ← static, Burst-compatible, no state
  BehaviorCommandContext.cs        ← readonly ref struct-of-lookups passed to every handler
  ApproachCommand.cs               ← static bool Execute(ref ctx, ref stateMachine, in cmd, ...)
  RequestAttackCommand.cs
  PlayAnimationCommand.cs
  ...one file per command family (blocking families may share: WaitTime+LoopUntil)
```

- Handlers are `static` methods taking a `BehaviorCommandContext` (a plain struct bundling the job's `[ReadOnly]`/writable lookups + blob refs + dt) — the same shape `BehaviorQualifiers.Evaluate` already takes lookups. No managed state, `[BurstCompile]`-transparent (static methods called from the Burst job inline fine).
- The job keeps: phase machine (Execute → Complete), command-index advance, timeout/iteration guards, ECB plumbing. The switch body per arm becomes ~3 lines: call handler, interpret its blocking/advance result.
- Return contract: `CommandResult { bool blocking; bool completed; }` (or two bools) — pin the exact semantics from the current arms during extraction, do not redesign them.

**← DECISION:** one file per command vs grouped by family (movement / combat / animation / social / item). *Recommendation: grouped by family — ~6 files instead of 15, and families share private helpers (e.g., animation-layer clears).*

## 5. Systems

- **Edited:** `BehaviorExecutionSystem.cs` — shrinks to system + job shell + dispatch (~200 lines).
- **Edited:** `BehaviorInterruptSystem.cs` — reuse the extracted non-blocking handlers for `interruptionCleanup` execution (it currently duplicates a subset of the interpreter's arms — kill that duplication while extracting; verify by diff).
- **No component/data changes. No ordering changes.**

## 8. Proposed file manifest

**New:** `Assets/_Scripts/Utils/BehaviorCommands/*.cs` (~6 files + context struct)
**Edited:** `BehaviorExecutionSystem.cs`, `BehaviorInterruptSystem.cs`

## 9. Build phases

Extraction is mechanical but must be **verifiable per step** — one command family per commit, compile + smoke-play between each:

1. Context struct + the two simplest fire-and-advance families (Animation, Motivation).
2. Movement family (Approach, FleeFromTarget) — the blocking semantics are the risk zone; extract verbatim.
3. Combat + Social + Item families (RequestAttack, RequestSocialResponse, RequestPickup + the equip/attach bookkeeping).
4. LoopUntil/WaitTime + qualifier wiring (already half-external in `BehaviorQualifiers`).
5. `BehaviorInterruptSystem` reuses handlers; delete its duplicated arms.

## 10. Verification

This is the highest-regression-risk plan in the batch precisely because it "changes nothing":
- After each phase: full compile + the standard AI smoke pass in DOTSTestScene (wander → interact → fight → flee → talk → sit — the CLAUDE.md behavior list).
- Git-diff discipline: extraction commits move code verbatim; any intentional change (the InterruptSystem dedup) is its own commit.
- **Stretch (recommended):** first PlayMode World fixture — spawn one unit + scripted `BehaviorLibrary` blob, tick `ActionExecutionSystemGroup`, assert command-index progression for a 3-command behavior. This is the "interpreter is the highest-value PlayMode target once split" note from the audit's watch list, and the split is what makes it writable.

## Open decisions (collected)

- [ ] §2 — file granularity: per-family (recommended) vs per-command.
- [ ] §10 — include the PlayMode interpreter fixture in this plan vs defer to the FeatureIsolation plan's test spine.
