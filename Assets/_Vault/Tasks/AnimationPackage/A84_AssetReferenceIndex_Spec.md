# Amendment A84 — Asset Reference Index: "where is this used"

> **Status:** 🔨 built 2026-09-12 as `0.31.0` (commits `A84-T1..T7`, `A84-T8..T10`); ⏸ T11 owner checkpoint open.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1, first.
> **Predecessors:** A83 (the hierarchy pane owns `CountTracksForTarget`). A77 recorded "the
> reference sweep that would price a delete still does not exist" — this is that sweep.
> **Executor:** one orchestrator; `worker` subagents in **one wave of six**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A84** on the DOTS Animation Toolkit package (head `0.30.0`). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A84_AssetReferenceIndex_Spec.md`. Read it, the roadmap §3
protocol, then only what §3 here names. You are the orchestrator; write T1 yourself before
spawning, then one wave (T2–T7), one gate, then T8–T10 yourself. Stop at T11.

---

## 1. Goal

Nothing in the package can answer "what references this?" A rig delete confirms without knowing
which profiles point at it; a clip set delete does not know which profiles list it; the hierarchy
pane's `CountTracksForTarget` matches by raw `targetId` and so undercounts tag-bound tracks;
an event key's binding count exists only for clips (`AnimEventBindingUtility`) and ignores
cutscenes and `ActorAnimationDefinition.ragdollAtEventKey`.

After this amendment one editor service, `AssetReferenceIndex`, scans the project's toolkit assets
once, keeps the result current on asset import/delete, and answers six queries. Delete
confirmations on every catalog quote it. A92 (refactors), A93 (Events tab) and A94 (Health tab)
read it.

---

## 2. Decisions (recorded — do not re-ask)

- **A84-D1 — Editor-only, in-memory, rebuilt from `AssetDatabase`.** No serialized cache asset.
  A full scan of every `ClipAsset`, `ClipSetAsset`, `RigAsset`, `ActorProfileAsset`,
  `CutsceneAsset`, `VatTextureSetAsset` via `AssetDatabase.FindAssets("t:…")` is the source of
  truth; an `AssetPostprocessor.OnPostprocessAllAssets` hook marks the index dirty; the next query
  rebuilds. T0 measures the scan on this project; if it exceeds 250 ms the rebuild becomes
  incremental for the changed GUIDs only, otherwise stay simple.
- **A84-D2 — Tag-aware track matching.** A track "uses" a rig target when `track.tagId != 0 &&
  target.tagId == track.tagId`, or when `track.tagId == 0 && track.targetId == target.Id`. This is
  the rule `CountTracksForTarget` should have had; the index owns it and the pane calls the index.
- **A84-D3 — Six queries, each returning a `List<AssetReference>`** (§4.1): rigs → profiles and
  clip sets; clips → clip sets and profile animations and cutscene slots; clip sets → profiles;
  event keys → clip markers, cutscene markers, profile ragdoll triggers; tags → clip tracks and rig
  targets; VAT sets → clip sets.
- **A84-D4 — Delete confirmations quote the index in one line per referencing asset type**, e.g.
  "Referenced by 2 profiles, 1 clip set." The list of names goes in the dialog body, capped at ten
  with "+N more". The delete itself is unchanged (OS trash, A77).
- **A84-D5 — Static class, plain noun on the allowlist.** `AssetReferenceIndex` is neither a
  `Utility` (it writes nothing) nor a `Resolver` (it is not pure); it goes on `Conformance_G`'s
  plain-noun allowlist in T8. Lives in `Editor/ClipUtilities/`.
- **A84-D6 — No UI of its own.** The Events and Health tabs are where the index becomes visible.

---

## 3. Read first

- `Editor/ClipUtilities/AnimEventBindingUtility.cs` in full — the partial precedent; A84 keeps it
  and makes it delegate to the index.
- `Editor/ClipUtilities/VocabularyRegistryProvider.cs` lines 1–80 (the `RegistryChanged` pattern).
- `Authoring/Assets/ActorProfileAsset.cs` lines 130–180 (`ActorLayerDefinition`,
  `ActorAnimationDefinition`: `clip`, `directionSlots`, `ragdollAtEventKey`).
- `Authoring/Assets/CutsceneAsset.cs` — grep `ClipAsset` and `CutsceneEventMarker` for the slot and
  marker lists.
- `Authoring/Assets/ClipAsset.cs` lines 260–330 (`SpriteTrack.targetId/tagId`, `EventMarker`) and
  grep `class TransformTrack` for its `targetId/tagId`.
- `Editor/ClipEditor/Panes/RigHierarchyPane.cs` — grep `CountTracksForTarget` (after A83; if A83
  has not run, it is `ClipEditorWindow.cs:4081`).
- Delete sites: grep `DeleteRequested` in `RigCatalogColumn.cs`, `ActorProfileCatalogColumn.cs`,
  `ClipSetsPanel.cs`, `RecipeCatalogColumn.cs` (recipes are not indexed; leave that one).

---

## 4. Design

### 4.1 `Editor/ClipUtilities/AssetReferenceIndex.cs` (T1 surface, T2 body)

```csharp
public enum AssetReferenceKind : byte
{
    ProfileRig, ProfileClipSet, ProfileAnimationClip, ProfileRagdollEvent,
    ClipSetClip, ClipSetVatTextures,
    ClipTrackTarget, ClipEventMarker,
    CutsceneSlotClip, CutsceneEventMarker,
    RigTargetTag
}

public readonly struct AssetReference
{
    public readonly UnityEngine.Object owner;     // the asset holding the reference
    public readonly AssetReferenceKind kind;
    public readonly string detail;                // "Layer Base ▸ Walk", "track Jaw", "marker @0.42"
}

public static class AssetReferenceIndex
{
    public static event Action Rebuilt;
    public static void MarkDirty();
    public static List<AssetReference> ReferencesToRig(RigAsset rig);
    public static List<AssetReference> ReferencesToClip(ClipAsset clip);
    public static List<AssetReference> ReferencesToClipSet(ClipSetAsset clipSet);
    public static List<AssetReference> ReferencesToVatTextures(VatTextureSetAsset textures);
    public static List<AssetReference> ReferencesToEventKey(uint eventKey);
    public static List<AssetReference> ReferencesToTag(uint tagId);
    public static int CountTracksBoundToTarget(ClipAsset clip, RigAsset rig, uint targetId); // D2
    public static string SummarizeForDialog(List<AssetReference> references);               // D4
}
```

### 4.2 `Editor/ClipUtilities/AssetReferenceIndexPostprocessor.cs` (T3)

An `AssetPostprocessor` whose `OnPostprocessAllAssets` calls `MarkDirty()` when any imported,
deleted or moved path ends in `.asset`. Nothing else.

### 4.3 Pure matching — `Editor/ClipUtilities/TrackTargetMatchResolver.cs` (T2, with the index)

`public static bool TrackBindsTarget(uint trackTargetId, uint trackTagId, RigTargetDefinition
target)` — D2 in one function so it is testable without assets.

---

## 5. Tasks

Boilerplate per roadmap §3.3. All wave tasks are `[parallel-safe]`.

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Time a full `FindAssets` over the six types
  with `execute_code` (`Stopwatch`); record for D1. Count how many toolkit assets the project has.
- [x] **T1 — Surface file (orchestrator).** Write §4.1's enum, struct and the static class with
  method stubs throwing `NotImplementedException`, so wave workers compile against it.
- [x] **T2 — Index body + matcher + fixture** — Files: `AssetReferenceIndex.cs`, new
  `TrackTargetMatchResolver.cs`. Read §3's asset files at the ranges named. Fixture (own file is a
  third file — the orchestrator adds `Tests/EditMode/TrackTargetMatchResolverTests.cs` from the
  worker's report): `TagBoundTrack_MatchesTargetWearingThatTag_NotByRawId` — a track with
  `tagId = 7, targetId = 99` matches a target `Id = 5, tagId = 7` and does not match `Id = 99,
  tagId = 0`. Revert-to-fail: make the resolver compare `targetId` only.
- [x] **T3 — Postprocessor** — Files: new `AssetReferenceIndexPostprocessor.cs`. No fixture.
- [x] **T4 — `CountTracksForTarget` calls the index** — Files: `RigHierarchyPane.cs` (or the window
  pre-A83), the method body only. Read the method and its two callers.
- [x] **T5 — Rig + profile delete confirmations quote the index** — Files: `RigCatalogColumn.cs`,
  `ActorProfileCatalogColumn.cs` (the `DeleteRequested` handlers only).
- [x] **T6 — Clip set delete confirmation quotes the index; `AnimEventBindingUtility` delegates** —
  Files: `ClipSetsPanel.cs` (delete handler only), `AnimEventBindingUtility.cs` (bodies become
  index calls; signatures unchanged).
- [x] **T7 — Docs + changelog** — Files: `CHANGELOG.md` (`## [0.31.0]`), `Documentation~/clip-editor.md`
  (a "Deleting things" paragraph: what the confirmation shows).
- **Gate the wave.** Compile; `TrackTargetMatchResolverTests`, `ClipEditorHierarchySelectionTests`.
  Commit `A84-T1..T7`.
- [x] **T8 — Orchestrator edits.** `Conformance_G` allowlist gains `AssetReferenceIndex`;
  `package.json` `0.31.0`; vault note section "Asset Reference Index (A84)" with D2 and the D1
  measurement.
- [x] **T9 — Drive.** Full suites. Against `Assets/A84Scratch` copies: a rig referenced by one
  profile → right-click Delete on Rigs shows "Referenced by 1 profile"; a tag-bound track on a
  target → hierarchy row shows animated. Delete the scratch folder; `git status` clean.
- [x] **T10 — Close.** HANDOFF §4 paragraph, roadmap checkbox.
- [ ] **T11 — ⏸ owner checkpoint.** Message: "Right-click Delete on any rig, clip set or profile
  that something uses — the dialog now says what. Tell me if the wording or the ten-name cap reads
  wrong."

---

## 6. Deliberately out of scope

- Blocking a delete because of references (A77 chose trash-and-confirm; unchanged).
- Indexing scene objects or prefabs (`ActorAuthoring` references to profiles) — assets only.
- A UI for the index — A93/A94.

## 7. Build log

- **T0 (2026-09-12).** Head `a1cc4e97`, `0.30.0`. Console clean, `isCompiling` false; inherited
  baseline EditMode 824 (one pre-existing `Conformance_A` failure), PlayMode 283. Project holds 24
  toolkit assets: 11 clips, 2 clip sets, 2 rigs, 2 profiles, 7 cutscenes, 0 VAT sets. **D1
  measurement:** six separate `FindAssets("t:…")` calls cost 207–328 ms across four rounds — the
  `t:` search is the cost, `LoadAllAssetsAtPath` is ~0 ms once cached (~120 ms cold). One combined
  call `FindAssets("t:ClipAsset t:ClipSetAsset t:RigAsset t:ActorProfileAsset t:CutsceneAsset
  t:VatTextureSetAsset")` returns the same 24 in 64–68 ms. Under 250 ms, so D1 stays simple: full
  rescan on the first query after a dirty mark, using the one combined filter.
- **Drift found in §3 reads (follow the code, not the spec):**
  - Clip sets do not reference rigs (`ClipSetAsset` has `clips` and `vatTextures` only), so
    "rigs → profiles and clip sets" is "rigs → profiles and cutscene slots" (`CutsceneSlot.rig`).
  - Cutscene slots hold no `ClipAsset`; clip blocks carry an `animationKey` resolved through the
    slot's profile. `CutsceneSlotClip` is dropped; the slot references that do exist are `rig`,
    `clipSets`, `profile` → kinds `CutsceneSlotRig`, `CutsceneSlotClipSet`, `CutsceneSlotProfile`.
  - T5 has the profile delete quote the index, but §4.1 lists no profile query. Added
    `ReferencesToProfile(ActorProfileAsset)` (cutscene slots are the only asset-side referrer).
  - Cutscene part tracks (`CutsceneKeyedTrack.tagId`) are tag-addressed; `ReferencesToTag` reports
    them as `CutsceneTrackTag`. `ClipTrackTarget` is renamed `ClipTrackTag` — the only query that
    yields a clip-track reference is the tag one.
  - The D2 rule already exists privately as `RigTargetReferenceResolver.TrackMatchesTarget`
    (`Editor/ClipEditor/Authoring/`). `TrackTargetMatchResolver` becomes its public home; the
    private copy delegates to it after the wave.
  - The rig and profile delete handlers are not in the catalog columns: after A82 the columns are
    thin event forwarders, and the `DisplayDialog` bodies live in `RigsPanel.RequestDeleteRig`
    (`Editor/ClipEditor/Authoring/RigsPanel.cs`) and
    `ActorEditorProfilesColumn.RequestDeleteProfile` (`Editor/ClipEditor/ActorEditor/`). T5 edits
    those two files.
- **T1 (2026-09-12).** `Editor/ClipUtilities/AssetReferenceIndex.cs` written with the drifted enum
  (fourteen kinds), the struct, and stubs throwing `NotImplementedException`.
- **T2–T7 wave (2026-09-12).** Six workers spawned together. T3/T4/T5/T6/T7 finished in 3–12
  tool uses each; T2 (703-line index body + resolver) hit the 40-turn cap with the file complete
  and clean — not resumed, reviewed by diff. Orchestrator additions after the wave: a raw-value
  overload `TrackBindsTarget(uint, uint, uint, uint)` so `RigTargetReferenceResolver.TrackMatchesTarget`
  delegates instead of duplicating the rule; the index's undeclared-target fallback routes through
  it. Gate: 0 compile errors; `TrackTargetMatchResolverTests` (2) + `ClipEditorHierarchySelectionTests`
  (3) pass; the tag-bound fixture proven to fail with a raw-id-only resolver, then restored.
  Committed `b86b95bf` (`A84-T1..T7`).
- **T8 (2026-09-12).** `Conformance_G` allowlist + `AssetReferenceIndex`; `package.json` and the
  conformance pin at `0.31.0`; vault section "Asset Reference Index (A84, 0.31.0)".
- **T9 (2026-09-12).** EditMode 826/826 discovered (824 baseline + 2), one failure = the standing
  `Conformance_A` asmdef drift; PlayMode 283/283. Drive against `Assets/A84Scratch` copies of
  `NewRig`, `ActorEditorScratch.profile`, `Blink`: profile→rig gave `ReferencesToRig` = 1 and
  `SummarizeForDialog` = "Referenced by 1 profile. / A84ScratchProfile"; a track `{targetId 99999,
  tagId 7777}` on a rig whose `Pelvis` wears tag 7777 gave `CountTracksBoundToTarget` = 1 for
  Pelvis and 0 for raw id 99999; `ReferencesToTag(7777)` listed the clip track and the rig target;
  deleting the profile asset dropped `ReferencesToRig` to 0 without a manual `MarkDirty` (the
  postprocessor fired). Scratch folder deleted, `git status` clean. **No capture:** the Editor was
  unfocused throughout (`isFocused` false), so the delete dialog was exercised through the same
  index call it makes, not by eye.
- **T10 (2026-09-12).** HANDOFF §4 paragraph, roadmap status line. The roadmap box stays unticked
  until T11 is answered (roadmap §1 rule).
