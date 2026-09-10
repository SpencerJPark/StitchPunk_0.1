# Amendment A89 — Stale VAT bake detection

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.36.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1.
> **Predecessors:** A78 (per-part bake, `VatTextureSetAsset.sourceHash` / `sourceRigKey`),
> A80 (VAT Bake fields are live pickers on the shared selection).
> **Executor:** one orchestrator; `worker` subagents in **one wave of four**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A89** on the DOTS Animation Toolkit package (head `0.35.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A89_StaleVatBakeDetection_Spec.md`. Read it, the
roadmap §3 protocol, then only what §3 here names. T0 yours; one wave (T1–T4); one gate; T5–T7
yours. Stop at T8.

---

## 1. Goal

A `VatTextureSetAsset` records the `sourceHash` and `sourceRigKey` it was baked from, and nothing
ever compares them to the current inputs. Edit a bone track, add a clip to the set, retarget a
part, and the actor plays the old texture with no error — the most common silent failure a buyer
will hit. After this amendment the hash is computable **without baking**, the VAT Bake tab shows a
stale badge with the reason, the Clip Sets tab shows the same badge on a set whose `vatTextures`
is stale, and A94's Health tab reuses the same check.

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A89-D1 — The current hash is exactly what the bake would write.** Extract the hashing from the
  bake path into `VatSourceHashResolver` (pure: clip set + rig → `ulong sourceHash`, `ulong
  rigKey`) and make the bake call the same function, so "stale" can never be a false positive
  caused by two implementations. T0 locates the hashing code (grep `sourceHash` in
  `Editor/VatBaking/`).
- **A89-D2 — Inputs folded into the hash:** every clip's stable id, its VAT source (`AnimationClip`
  GUID + import timestamp, or the authored bone tracks' key data), the rig's stable id, each VAT
  part's `sourceNodePath` and `kind`. If today's hash omits one of these, adding it is in scope and
  will mark every existing set stale once — say so in the changelog.
- **A89-D3 — Three states, one badge:** `Fresh` (hashes equal), `Stale` (either differs; tooltip
  names which — "clips changed" / "rig changed"), `Unbaked` (set has VAT parts but no texture set).
  Colours: `ToolkitPalette.Clean`, `Warning`, `Error`.
- **A89-D4 — No auto-rebake.** The badge is information; the Bake button is the action. ⚠ A
  "Rebake" affordance on the Clip Sets badge (jumps to VAT Bake with the set selected) — the
  checkpoint asks.
- **A89-D5 — The check is cheap enough to run on selection change and after any asset import**;
  it reads serialized fields only, never textures. T0 times it.

---

## 3. Read first

- `Editor/VatBaking/VatBakeClipBuilder.cs` and `VatTextureSetBuilder.cs` — grep `sourceHash`,
  `sourceRigKey` (the write sites: `VatTextureSetBuilder.cs:103`).
- `Authoring/Assets/VatTextureSetAsset.cs` lines 15–60.
- `Editor/VatBaking/VatBakePanel.cs` lines 190–260 (`Bind`, the shared-selection handlers) and grep
  `resolved` for the A78 read-only line the badge sits beside.
- `Editor/ClipEditor/Authoring/ClipSetsPanel.cs` — grep `vatTextures` for the editor column's row.
- `Editor/ClipEditor/ValidationBadgeElement.cs` lines 16–140 — the badge idiom to copy (do not
  reuse the element; it is bound to `ValidationMessage`).

---

## 4. Design

### 4.1 `Editor/VatBaking/VatSourceHashResolver.cs` (T1)

```csharp
public static class VatSourceHashResolver
{
    public static ulong ComputeSourceHash(ClipSetAsset clipSet, RigAsset rig);
    public static ulong ComputeRigKey(RigAsset rig);
    public static VatBakeFreshness Resolve(ClipSetAsset clipSet, RigAsset rig,
        VatTextureSetAsset textures, out string reason);
}

public enum VatBakeFreshness : byte { Unbaked = 0, Fresh = 1, Stale = 2 }
```

Uses the same fold the bake used (T0 names it); the bake's write site calls `ComputeSourceHash`.

### 4.2 `Editor/VatBaking/VatFreshnessBadgeElement.cs` (T2)

A 10 px dot + label, `Refresh(VatBakeFreshness, string reason)`; tooltip = reason. Two hosts.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Grep the hashing code; list D2's inputs
  against what it folds today; time a compute over the sample tentacle set with `execute_code`.
  Record in §7.
- [ ] **T1 — Resolver + fixture [parallel-safe]** — Files: new `VatSourceHashResolver.cs`, new
  `Tests/EditMode/VatSourceHashResolverTests.cs`. Fixture:
  `ChangingAPartKind_ChangesTheHash_AndReportsRigChanged` — two in-memory rigs differing only in
  one target's `kind` → different `ComputeRigKey`; `Resolve` with a set stamped with the first →
  `Stale`, reason contains "rig". Revert-to-fail: drop `kind` from the fold.
- [ ] **T2 — Badge element [parallel-safe]** — Files: new `VatFreshnessBadgeElement.cs`.
- [ ] **T3 — VAT Bake tab hosts the badge; bake writes via the resolver [parallel-safe]** — Files:
  `VatBakePanel.cs` (the resolved-line range + `Bind` handlers), `VatTextureSetBuilder.cs` (the
  hash write site only).
- [ ] **T4 — Clip Sets tab hosts the badge + docs [parallel-safe]** — Files: `ClipSetsPanel.cs`
  (the `vatTextures` row only), `Documentation~/rigged-characters.md` (a "Is my bake current?"
  paragraph). Changelog is T5.
- **Gate the wave.** `VatSourceHashResolverTests`, the existing VAT builder fixtures (grep
  `VatBakeClipBuilder` under `Tests/EditMode/`). Commit `A89-T1..T4`.
- [ ] **T5 — Orchestrator edits.** `CHANGELOG.md` `## [0.36.0]` (state D2's one-time staleness if
  it applies); `package.json`; vault note "The VAT bake asks the rig" gains the resolver.
- [ ] **T6 — Drive.** Full suites. Bake the sample tentacle → Fresh. Flip one target's Kind → Stale,
  "rig changed". Rebake → Fresh. Add a clip to the set → Stale, "clips changed". Capture both tabs.
- [ ] **T7 — Close.** HANDOFF §4, roadmap checkbox.
- [ ] **T8 — ⏸ owner checkpoint.** Message: "VAT Bake tab: a dot beside the resolved-parts line
  says Fresh / Stale / Unbaked with the reason on hover; Clip Sets shows the same on the VAT
  Textures row. ⚠ Want a Rebake button on the Clip Sets badge that jumps to VAT Bake?"

---

## 6. Deliberately out of scope

- Auto-rebake, background bake, bake-on-save.
- Hashing texture bytes (serialized inputs only).

## 7. Build log

_(empty)_
