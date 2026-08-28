# CharacterRig Finish + Hardening — Design Spec

> **Status:** ◐ partially built (2026-07-03 autonomous pass) — **items 2, 3, 5, 6 landed in code** (bake warnings, palette capacity guard + ceiling comment, `DesignApplyUtilTests` fixture, SortLayers rename); awaiting Editor compile + Test Runner. **Remaining: item 1** (verify-doc rewrite — `Verification/verify-characterrig.md` still mentions the abandoned `ExplicitTable`/`colorAxis` fields) **and item 4** (the enum shipped as `UnitPartId` in `Data/Enums/PartEnums.cs`, still `Male*`-prefixed — so the variant decision is genuinely unmade; pair it with Direction_System §2).
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #1 — [in-flight]; "nothing below it should start until this closes."

---

**Skills Needed:**
- `dots-test` — `DesignApplyUtilTests` EditMode fixture (§5)

---

## 1. Purpose & v1 scope

Close out the partially-landed CharacterRig: stale verification doc, silent-failure gaps in the `PartLibrary` bake, a capacity ceiling that fails at runtime instead of bake, and one enum decision that's cheap now and expensive later. This is finish-work on committed code (`10bd205`), not new architecture.

## 2. Work items (from the audit, execution order)

1. **Rewrite `verify-characterrig.md` against the shipped tag model** — it still describes `ExplicitTable`/`StrideFormula`/`colorAxis` (the abandoned shape×color grid); the shipped design is tag-driven `PartTagRange` lists + `CharacterPalette` string groups. Rewrite the checklist **before** running verification or it chases fields that no longer exist.
2. ✅ *(built 2026-07-03)* **Bake warnings in `PartLibraryBakingSystem.cs`:**
   - Duplicate `PartDefinitionSO.id` → currently last-one-wins silently → `Debug.LogWarning` naming both SOs.
   - `ToFixed` / `CopyFromTruncated` → tags > ~29 bytes can silently collide → log on truncation with the offending tag.
   (Same medicine as the BehaviorBakeValidation plan — silent-last-one-wins is the codebase's recurring bake hazard; consider a tiny shared `BakeWarn` util if a third case appears.)
3. ✅ *(built 2026-07-03 — guard lives in `DesignApplyUtil.SetTag`: warns + drops instead of throwing; ceiling comment added to the component)* **`CharacterPalette.groups` capacity guard** — `FixedList512Bytes` ÷ 64-byte `PaletteEntry` ≈ 7 entries; groups are free-text and designed to grow, so the 8th fails at **runtime**. Add a bake/apply-time guard logging at ≥ 6 distinct groups. Note the ceiling in the component comment (the audit watch-list item about FixedList payloads on save-path components applies here).
4. **← DECISION: `PartDefId` gender prefixing** (`Male*`, 17 values today) — (a) append-only enum doubling per variant (Female*/Child*/Rotter*) vs (b) `PartDefinitionSO` grows a variant dimension and the enum stays body-slot-shaped. *Recommendation: decide with the Direction_System decision in the same sitting — both reshape part authoring, and B-style variant dimensions compound (gender × direction) if both go dimensional.*
5. ✅ *(built 2026-07-03 — 11 tests in `Tests/DesignApplyUtilTests.cs`, including the capacity-guard drop)* **`DesignApplyUtilTests` EditMode fixture** — `SliceAtOffset` / `TagPoolSize` stride-and-offset math: documented edge cases (empty-tag double-count avoidance, clamp-to-fallback) become characterization tests via `dots-test`. The whole design pipeline is about to lean on this math.
6. ✅ *(built 2026-07-03 — `sortPass`/`compareIndex`/`swapTemp`)* **Trivial:** `CharacterRigAuthoring.Baker.SortLayers` used `i`/`j` loop names — RULES.md violation, renamed.

## 9. Build phases

Items are independent; suggested order 5 → 2 → 3 → 6 (code, can land now) then 1 → run verification (Editor session) with 4 decided before any new part SOs are authored.

## 10. Verification

Item 2: author a deliberate duplicate-id SO pair → bake warns; delete → clean. Item 3: dummy rig with 7 palette groups → guard fires. Item 5: EditMode suite green. Item 1: run the rewritten checklist end-to-end in the Editor — that IS the rig's verification close-out.

## Open decisions (collected)

- [ ] §2.4 — PartDefId variant strategy (decide alongside Direction_System §2).
- [ ] §2.2 — warn vs hard-fail on duplicate part ids (recommend warn, consistent with BehaviorBakeValidation's recommendation).
