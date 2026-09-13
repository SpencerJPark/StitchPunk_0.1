# Amendment A89 — Stale VAT bake detection

> **Status:** ✅ built 2026-09-13 as `0.35.0` (A88 is not built; it takes the next free minor when it runs — §7). ⏸ T8 owner checkpoint open.
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

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Grep the hashing code; list D2's inputs
  against what it folds today; time a compute over the sample tentacle set with `execute_code`.
  Record in §7.
- [x] **T1 — Resolver + fixture [parallel-safe]** — Files: new `VatSourceHashResolver.cs`, new
  `Tests/EditMode/VatSourceHashResolverTests.cs`. Fixture:
  `ChangingAPartKind_ChangesTheHash_AndReportsRigChanged` — two in-memory rigs differing only in
  one target's `kind` → different `ComputeRigKey`; `Resolve` with a set stamped with the first →
  `Stale`, reason contains "rig". Revert-to-fail: drop `kind` from the fold.
- [x] **T2 — Badge element [parallel-safe]** — Files: new `VatFreshnessBadgeElement.cs`.
- [x] **T3 — VAT Bake tab hosts the badge; bake writes via the resolver [parallel-safe]** — Files:
  `VatBakePanel.cs` (the resolved-line range + `Bind` handlers), `VatTextureSetBuilder.cs` (the
  hash write site only).
- [x] **T4 — Clip Sets tab hosts the badge + docs [parallel-safe]** — Files: `ClipSetsPanel.cs`
  (the `vatTextures` row only), `Documentation~/rigged-characters.md` (a "Is my bake current?"
  paragraph). Changelog is T5.
- **Gate the wave.** `VatSourceHashResolverTests`, the existing VAT builder fixtures (grep
  `VatBakeClipBuilder` under `Tests/EditMode/`). Commit `A89-T1..T4`.
- [x] **T5 — Orchestrator edits.** `CHANGELOG.md` `## [0.36.0]` (state D2's one-time staleness if
  it applies); `package.json`; vault note "The VAT bake asks the rig" gains the resolver.
- [x] **T6 — Drive.** Full suites. Bake the sample tentacle → Fresh. Flip one target's Kind → Stale,
  "rig changed". Rebake → Fresh. Add a clip to the set → Stale, "clips changed". Capture both tabs.
- [x] **T7 — Close.** HANDOFF §4, roadmap checkbox.
- [ ] **T8 — ⏸ owner checkpoint.** Message: "VAT Bake tab: a dot beside the resolved-parts line
  says Fresh / Stale / Unbaked with the reason on hover; Clip Sets shows the same on the VAT
  Textures row. ⚠ Want a Rebake button on the Clip Sets badge that jumps to VAT Bake?"

---

## 6. Deliberately out of scope

- Auto-rebake, background bake, bake-on-save.
- Hashing texture bytes (serialized inputs only).

## 7. Build log

### T0 — baseline and grounding (2026-09-13, head `7bb90dae`)

- **Gate:** compile clean, 0 errors. Suite totals inherited from A87 (EditMode 831 with the standing
  `Conformance_A` failure, PlayMode 283), not re-run.
- **Version drift:** A88 (`0.35.0`) is not built, so A89 takes `0.35.0`, the next free minor.
  Owner instruction, 2026-09-13.
- **Drift 1, confirmed.** "Nothing ever compares them" is wrong. `ClipValidation.ValidateBind`
  already has V08 (`vatSourceHashRecomputed` + `recomputedVatSourceHash`: Error while authoring,
  Warning at Bake), but it is dormant. No editor caller passes a recomputed hash; only
  `AuthoringTestAssets` does. `ValidationBadgeElement.HasErrors` has no reader, so waking V08
  gates nothing.
- **Drift 2, confirmed.** The hash is `VatTextureBaker.ComputeSourceHash(input, elementCount,
  totalFrames)` (`:623`, called at `:266`), not in `VatBakeClipBuilder`. It folds flavor, input
  rate, element count, total frames, full precision, and per clip the id, target, loopSafe,
  resolved rate, `animationClip.length` and `name.GetHashCode()`. It omits the rig's stable id,
  `sourceNodePath`, `kind`, bone-track key data, the AnimationClip GUID and its content.
  - Both derived inputs can be computed without sampling. `elementCount` is the resolved part's
    `PrefabRenderer.bones.Length` or `sharedMesh.vertexCount`. The bake instance is an
    `Object.Instantiate` of the same prefab.
  - `totalFrames` is Σ `max(1, round(length × rate)) + (loopSafe ? 1 : 0)`, with length taken
    from the clip or else `ClipAsset.duration` (`SampleClip :478-532`).
  - D1 holds without amendment.
- **Drift 3, confirmed.** `VatTextureSetBuilder.cs:101` writes only part 0's `sourceHash`, so an
  edit to any other part never changes it.
- **Drift 4, confirmed.** `sourceRigKey = rig.StableId` (`:35`), and V40 compares against it.
  `sourceRigKey` is left untouched.
- **Drift 5, confirmed.** `ClipSetsPanel` has no `vatTextures` reference. The field lives in
  `ClipSetAssetEditor:72` (PropertyField) and is read at `ClipInspectorPane:1193`.
- **Probe** (`CreateTwoPartSampleAssets` into a scratch folder, since deleted):
  - Two parts resolve: Tentacle (12 bones, 26 vertices) and Fin (6 bones, 14 vertices). One clip
    carries 12 bone tracks plus one vatTrack whose source is a native `.asset` AnimationClip.
  - One compute costs **0.070 ms**: `TryResolve`, then GUID + `GetAssetDependencyHash` per
    source clip, then a walk over every bone key. D5 holds.
  - `GetAssetDependencyHash` changes when a clip edit is **saved**, and not for an unsaved
    in-memory edit. That is the right signal for "import timestamp".

### Design settled at T0 (escalated at T8, not re-specced)

- **One hash function.** `VatSourceHashResolver` is the only code that computes the stored
  value. `VatTextureSetBuilder.WriteSet` stamps
  `sourceHash = ComputeSourceHash(clipSet, rig, flavor)`, set-wide over every part, replacing the
  part-0 write.
  - `VatTextureBaker`'s per-part `VatBakeResult.sourceHash` stays as a per-part receipt: it is
    logged and pinned by three baker fixtures, but no longer stored.
  - The freshness badge calls `Resolve`, because V08 cannot say Unbaked or which side moved.
    `ValidationBadgeElement.Refresh` feeds V08 the same `ComputeSourceHash` whenever a rig and a
    texture set exist.
- **Two sub-hashes, one new field.** `sourceHash = Fold(ComputeClipsHash(clipSet),
  ComputeRigStructureHash(rig, flavor))`.
  - `VatTextureSetAsset.sourceRigStructureHash` (new, added at T0) stores the rig half, so
    `Resolve` names "rig changed" or "clips changed".
  - 0 means the set was baked before the field existed. Such a set reports "sources changed"
    without saying which.
  - `sourceRigKey` keeps storing `rig.StableId`, so V40 is untouched.
  - The spec's `ComputeRigKey` is renamed `ComputeRigStructureHash`, so it cannot be mistaken for
    `sourceRigKey`.
- **Clip side:** every clip that is VAT-bound (a `vatSource.sourceClip`, any `vatTracks` entry,
  or any bone track). Clips that are not VAT-bound are skipped, so adding a sprite-only clip is not
  a false stale. It folds:
  - stable id, `duration`, `frameRate`
  - `vatSource`: source clip identity and `loopSafe`
  - each vatTrack: `targetId`, source clip identity, `loopSafe`
  - each bone track: its name, and every key field bit-exact
  - Source clip identity is the GUID + `GetAssetDependencyHash`. An asset-less clip uses its
    name + length.
  - Strings go through FNV-1a, which replaces `name.GetHashCode()`.
  - Conservative: `duration` is folded even when an imported clip's length overrides it.
- **Rig side:**
  - stable id and the source prefab's GUID
  - every target's id, `sourceNodePath` and `kind`
  - every bone socket's id and `boneName` (the bake samples them)
  - flavor, plus each resolved part's target id, path and element count, or a failure sentinel
  - Not folded: `displayName` (file names only), `samplesPerSecond` (every ClipAsset carries
    `frameRate`, so the input rate is never used), `useFullPrecision` (a format choice, not a
    source, and not stored on the set).
- **One-time staleness:** every set baked before `0.35.0` reads Stale once, which goes in the
  changelog. The project holds no baked set today.
- **Triggers (D5):** selection change, `EditorApplication.projectChanged`, after a bake, and on
  Clip Sets after a pick. An unsaved in-memory edit shows on the next save or selection. There is
  no per-gesture hook and no polling.
- **Clip Sets host (drift 5):** a new read-only "VAT Textures" row in `ClipSetsPanel`'s editor
  column, holding the set name and the badge. It shows when the set is VAT-bound or has textures.
  - Its rig is the shared selection's rig when that matches `sourceRigKey`, otherwise a project
    lookup by stable id.
  - With no rig found, it reads Stale: "the rig it was baked from is not in the project".
  - The `ClipSetAssetEditor` inspector gets no badge.

### T1–T4 wave and gate (2026-09-13)

- **Workers:** four, 65–111k tokens each. The ledger hook failed at spawn because the shell cwd
  had drifted into the package; the edits were unaffected. Review fixes before the gate:
  - moved a comment that split an existing block in `ValidationBadgeElement`
  - removed an unused `using`
  - renamed a local called `set`
- **Gate:** compile clean. 80 targeted fixtures pass (the resolver, VAT baker, clip builder and
  `ClipValidationTests`).
- **Revert-to-fail:** one compile covered two mutations. Dropping `kind` from the rig fold failed
  `ChangingAPartKind…` ("Expected: not equal"). Dropping the clips hash from `ComputeSourceHash`
  failed `AddingAVatBoundClip…` ("Expected Stale, was Fresh"). The file was restored
  byte-identical (sha256 `4d1bf5e7…`).
- **Suites:** EditMode 833 (831 + 2, standing `Conformance_A` only), PlayMode 283.
  - Commits: `2abf151e` (T0–T4) and `d38eacd2` (T5).

### T6 — drive (2026-09-13)

- **SaveAssets guard.** `CreateTwoPartSampleAssets` and `VatTextureSetBuilder.WriteSet` both call
  `AssetDatabase.SaveAssets()`. At drive time the owner had two dirty non-shader assets:
  `EditorBuildSettings` and `Assets/Materials/RobertsCross.mat`.
  - Every drive call cleared the dirty flag on each dirty asset outside the scratch folder, and set
    it back in a `finally`. Nothing of the owner's was written, which git status confirms.
- **Host.** The drive used a floating `VatBakeWindow`, which owns its own `ActiveAssetSelection`,
  plus a detached `ClipSetsPanel` bound to that same selection. The owner's docked DOTS Animator
  window was never touched.
- **Bake (real private `Bake`, two parts):**
  - The VAT Bake badge went from **Unbaked** to **Fresh**.
  - The on-disk YAML stores `sourceHash` 4086868802694568373 (`0x38B77985DC3C59B5`), equal to the
    recomputed value, plus `sourceRigStructureHash` and `sourceRigKey` = `rig.StableId`.
  - At Fresh, V08 is silent; the only code is V10, which was already there.
- **Point 3 proven.** Toggling `loopSafe` on the Fin part's vatTrack only, then saving, makes
  V08 fire as an Error in `ValidationBadgeElement`.
- **Drive-found bug: `EditorApplication.projectChanged` does not fire on saving an existing
  asset.** A probe counter read 0 after the save, even a tick later, so both badges stayed Fresh.
  - Fixed with `Editor/VatBaking/VatSourceImportWatcher.cs`, an `AssetPostprocessor` relay
    coalesced through `delayCall`. Both panels subscribe to it in place of `projectChanged`.
  - D5 ("after any asset import") now holds as written.
- **After the fix (recompile clean), each step's result:**
  1. **Reselect** → both badges **Stale**, "Clips changed". The Fin-only edit shows on both
     tabs.
  2. **Flip Fin's Kind to Quad and save** → one tick later, both badges **Stale**, "Rig changed"
     (the watcher works).
  3. **Rebake** → VAT Bake **Fresh** straight away; Clip Sets **Fresh** a tick later. One part,
     since Fin is now Quad.
  4. **Add a VAT-bound clip through the Clip Sets picker handler** → Clip Sets **Stale**, "Clips
     changed", in the same call. After the set is saved, VAT Bake reads **Stale**, "Clips
     changed".
- **Captures** (Editor focused, pixelsPerPoint 2.5):
  - `Library/A89Captures/vat_bake_stale_rig_changed.png`: a yellow dot and "Stale" at the right
    end of the resolved-parts line.
  - `vat_bake_fresh_after_rebake.png`: the same spot, green "Fresh".
  - **The Clip Sets tab was not captured.** It exists only inside the owner's docked DOTS Animator
    window, so it was proven one level down.
- **Seen in the capture, not changed:** the resolved-parts line still read "baking 2 of 2 VAT
  parts · Tentacle, Fin" after the rig save and the one-part rebake.
  - `RefreshResolvedSources` runs on selection only, which is older A78 behaviour.
  - Hooking it to the watcher would also rebuild the preview's copy of the source hierarchy on
    every import batch, so the question goes to the owner at T8.
- **Cleanup:** window closed, panel disposed, undo cleared, `Assets/A89Drive` deleted. The three
  registry files are byte-identical to the pre-drive backups, and git status shows only the
  owner's pre-existing files.
