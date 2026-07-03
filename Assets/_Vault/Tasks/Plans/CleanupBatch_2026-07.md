# Cleanup Batch — July 2026 — Design Spec

> **Status:** ◐ partially executed (2026-07-03 autonomous pass) — rows 3, 4, 7, 8, 10 done (see per-row notes); **remaining: rows 1, 2, 5, 6** (they need Editor assets, rebake verification, or a decision). Everything from the pass awaits one Editor compile + Test Runner check.

---

**Skills Needed:** none (mechanical edits) — `dots-test` only if the EffectLibrary fix grows a guard test.

---

## Code fixes (audit #14)

| # | Fix | Where | Notes |
|---|---|---|---|
| 1 | Add `Thirst` to `NeedType`; give Feed/Hydrate distinct effects | `Data/Enums/AiEnums.cs`, EffectLibrary SOs, Water/Bread item SOs | Water restoring Hunger is a live design bug. Touches Motivation buffers → rebake + check `MotivationDecaySystem` curve wiring for the new need |
| 2 | EffectLibrary enum-index collision — Bandage + MedKit share Healing's slot | EffectLibrary blob + baking system | Same silent last-one-wins class as the PartLibrary duplicate-id gap; add a bake-time duplicate-slot warning while in there (mirror the BehaviorBakeValidation pattern) |
| 3 | ✅ **DONE** — Deleted `UnitStateType` AND its one consumer `UnitStateSO.cs` (audit said zero consumers; grep found the SO — itself referenced by nothing and no `.asset` uses it) | `AiEnums.cs`, `Data/SOs/UnitStateSO.cs` | Both removed with metas |
| 4 | ✅ **DONE** — `FlowFeildSystem.cs` → `FlowFieldSystem.cs` (filename-only typo; the class inside was already `FlowFieldSystem`, so zero code references changed). `MotivationDegregationSystem` had **already been renamed** to `MotivationDecaySystem` before this pass — that half of the row was stale | Movement | Gotchas.md + Systems_Movement.md typo notes removed |
| 5 | `groundBufferOverride` authored but unconsumed | `BodyPartAuthoring` | Wire into the ragdoll sim or delete the field — unconsumed authoring fields are the two-source-of-truth trap |
| 6 | `#region` in `UnitSelectionManager` + friends | MonoBehaviours | ← DECISION: enforce (strip regions) vs amend RULES.md to scope the ban to ECS code. *Audit + review both recommend amending — regions in 15KB manager classes are defensible* |

## Docs truth pass (audit #3)

| # | Fix | Where |
|---|---|---|
| 7 | ✅ **DONE** — rewrote the ECB-buffer-remap entry around the current `BodyPart`/`BodyPartInitSystem` truth (lesson preserved), replaced the two stale `NeedsAction` entries with the `SpawnStateInitSystem` reality, rewrote "9 motivation components" → `Motivation` buffer + `MotivationChangeRequest`, and removed the resolved Filenames section | `Gotchas.md` |
| 8 | ✅ **DONE** — reconciled: the three stub files existed but were **empty husks** (0–2 lines, no systems), so both the audit ("deleted") and CLAUDE.md ("remain") were half-right. Deleted the empty files + metas; CLAUDE.md now points at the SchedulesWaypoints plan | `Assets/CLAUDE.md` + stub files |
| 9 | ⬜ remaining — CLAUDE.md Current Status is ~90 lines and grows per feature | Move per-system detail into vault notes; keep CLAUDE.md a pointer. ← DECISION on trim aggressiveness still open |
| 10 | ✅ **DONE** — Sound/PlayerAttack/DamageEvent/DamageEvent-v2 rows → ✔️ done with corrected `../Completed/` links; Dialogue row → built (no spec, pre-dates workflow) | `Tasks/Plans/README.md` |

## Execution notes

- Order is free except: #2 before any new EffectSO is authored; #8 before anyone builds "missing" stubs that exist (or deletes existing ones believed missing).
- Each numbered row = one commit, message referencing this doc.
- Rebake + play-smoke after #1 and #5 (both touch baked data); the rest are compile-only risk.

## Open decisions (collected)

- [ ] #6 — `#region` rule: amend RULES.md scope (recommended) vs enforce everywhere.
- [ ] #9 — how aggressive to trim CLAUDE.md Current Status (pointer-only vs keep one-paragraph summaries).
