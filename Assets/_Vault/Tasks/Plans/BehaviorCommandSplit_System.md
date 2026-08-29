# Behavior Command Split — Design Spec

> **Status:** ✅ decisions stamped + currency-checked 2026-08-29 — ready to build. Builds **before** `../NewPlans/AnimationEventTiming_System.md` (its two new commands land in the split-out layout).
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #9 — "do it as the first commit of RangedCombat, or immediately before"

## 0. Currency check (2026-08-29) — what changed since drafting

Every claim below was re-verified against the working tree; the core surgery is unchanged, but three things landed since July that the extractor must know:

- **The roster grew to 13 implemented arms.** `PlayActionAnimation`, `RequestSocialResponse`, `StopAnimation`, `PlaySound` all post-date this spec (P3b social + P4 sound + migration). File is now 636 lines / 31 KB. The phases in §9 are updated to the current roster.
- **The animation seam is the toolkit's, not `AnimationRequest`.** The migration cutover (commits 4007c5a4 / 0112a838) rewrote all three animation arms to write the `AnimationCommand` buffer via `AnimationCommandUtil.Play/Stop` + `AnimationCommandPending`. The arms are already thin wrappers over that util — extract them verbatim; do not touch the util.
- **`BehaviorCommandCatalog` (Utils/) now exists and owns blocking truth.** `IsImplemented`/`IsBlocking` are consulted by bake validation, and `BehaviorCommandCatalogTests` pins both sets. Handlers must NOT re-declare blocking in a `CommandResult` — that would be a second source of truth. The handler return carries only *completed/advance*; blocking-ness stays the catalog's answer. Extraction changes neither set, so the catalog and its test are untouched (a diff there means the refactor changed behavior).
- **Re-verified, still true:** `BehaviorInterruptSystem.RunCleanupCommand` duplicates the `ModifyMotivation` / `ReleaseInteraction` / `StopAnimation` arms verbatim — §5's dedup target. The Item bookkeeping is still inline in `RunRequestPickup`. `WaitTime` gained qualifier-as-early-exit (shares `BehaviorQualifiers.Evaluate` with `LoopUntil`), which strengthens the case for those two sharing a file.

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
- Return contract: completed/advance only — blocking-ness is `BehaviorCommandCatalog.IsBlocking`'s answer (see §0), never re-declared per handler. Pin the exact advance semantics from the current arms during extraction, do not redesign them.

✅ DECIDED 2026-08-29: **grouped by family** — ~6 files instead of 15, and families share private helpers. Current-roster grouping: Movement (Approach, FleeFromTarget) · Wait/Loop (WaitTime, LoopUntil — share `BehaviorQualifiers`) · Animation (PlayAnimation, PlayActionAnimation, StopAnimation — all thin `AnimationCommandUtil` wrappers) · Item (RequestPickup + equip/attach bookkeeping) · Requests (RequestAttack, RequestSocialResponse, ModifyMotivation) · Misc (ReleaseInteraction, PlaySound). Exact file boundaries are the extractor's call; the constraint is that every arm the interrupt system duplicates ends up shared. The AnimationEventTiming plan's `WaitForAnimEvent`/`WaitForClipFinished` land in the Wait/Loop family file (its manifest's "homes per the Split plan's layout" resolves here).

## 5. Systems

- **Edited:** `BehaviorExecutionSystem.cs` — shrinks to system + job shell + dispatch (~200 lines).
- **Edited:** `BehaviorInterruptSystem.cs` — reuse the extracted non-blocking handlers for `interruptionCleanup` execution (it currently duplicates a subset of the interpreter's arms — kill that duplication while extracting; verify by diff).
- **No component/data changes. No ordering changes.**

## 8. Proposed file manifest

**New:** `Assets/_Scripts/Utils/BehaviorCommands/*.cs` (~6 files + context struct)
**Edited:** `BehaviorExecutionSystem.cs`, `BehaviorInterruptSystem.cs`

## 9. Build phases

Extraction is mechanical but must be **verifiable per step** — one command family per commit, compile + smoke-play between each:

1. Context struct + the simplest fire-and-advance arms (Animation family, ModifyMotivation, ReleaseInteraction, PlaySound).
2. Movement family (Approach, FleeFromTarget) — the blocking semantics are the risk zone; extract verbatim.
3. Requests + Item (RequestAttack, RequestSocialResponse, RequestPickup + the equip/attach bookkeeping).
4. Wait/Loop family (WaitTime incl. its qualifier early-exit, LoopUntil) — qualifier wiring already half-external in `BehaviorQualifiers`.
5. `BehaviorInterruptSystem` reuses the phase-1 handlers (ModifyMotivation, ReleaseInteraction, StopAnimation); delete its duplicated arms.

## 10. Verification

This is the highest-regression-risk plan in the batch precisely because it "changes nothing":
- After each phase: full compile + the standard AI smoke pass in DOTSTestScene (wander → interact → fight → flee → talk → sit — the CLAUDE.md behavior list).
- Git-diff discipline: extraction commits move code verbatim; any intentional change (the InterruptSystem dedup) is its own commit.
- **Stretch (recommended):** first PlayMode World fixture — spawn one unit + scripted `BehaviorLibrary` blob, tick `ActionExecutionSystemGroup`, assert command-index progression for a 3-command behavior. This is the "interpreter is the highest-value PlayMode target once split" note from the audit's watch list, and the split is what makes it writable.

## Open decisions (collected)

All stamped 2026-08-29 (owner approved; details inline):

- [x] §2 — file granularity: **per-family** (~6 files; grouping listed at the §2 stamp).
- [x] §10 — PlayMode interpreter fixture: **include in this plan.** The split is what makes it writable, and the AnimationEventTiming plan builds two new *blocking* commands immediately after — command-index progression is exactly the invariant they'll lean on. One fixture, phase-machine progression only; no coverage-chasing beyond it.
