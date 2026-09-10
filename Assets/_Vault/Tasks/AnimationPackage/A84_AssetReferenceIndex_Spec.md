# Amendment A84 — Asset Reference Index: "where is this used"

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.31.0`.
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

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Time a full `FindAssets` over the six types
  with `execute_code` (`Stopwatch`); record for D1. Count how many toolkit assets the project has.
- [ ] **T1 — Surface file (orchestrator).** Write §4.1's enum, struct and the static class with
  method stubs throwing `NotImplementedException`, so wave workers compile against it.
- [ ] **T2 — Index body + matcher + fixture** — Files: `AssetReferenceIndex.cs`, new
  `TrackTargetMatchResolver.cs`. Read §3's asset files at the ranges named. Fixture (own file is a
  third file — the orchestrator adds `Tests/EditMode/TrackTargetMatchResolverTests.cs` from the
  worker's report): `TagBoundTrack_MatchesTargetWearingThatTag_NotByRawId` — a track with
  `tagId = 7, targetId = 99` matches a target `Id = 5, tagId = 7` and does not match `Id = 99,
  tagId = 0`. Revert-to-fail: make the resolver compare `targetId` only.
- [ ] **T3 — Postprocessor** — Files: new `AssetReferenceIndexPostprocessor.cs`. No fixture.
- [ ] **T4 — `CountTracksForTarget` calls the index** — Files: `RigHierarchyPane.cs` (or the window
  pre-A83), the method body only. Read the method and its two callers.
- [ ] **T5 — Rig + profile delete confirmations quote the index** — Files: `RigCatalogColumn.cs`,
  `ActorProfileCatalogColumn.cs` (the `DeleteRequested` handlers only).
- [ ] **T6 — Clip set delete confirmation quotes the index; `AnimEventBindingUtility` delegates** —
  Files: `ClipSetsPanel.cs` (delete handler only), `AnimEventBindingUtility.cs` (bodies become
  index calls; signatures unchanged).
- [ ] **T7 — Docs + changelog** — Files: `CHANGELOG.md` (`## [0.31.0]`), `Documentation~/clip-editor.md`
  (a "Deleting things" paragraph: what the confirmation shows).
- **Gate the wave.** Compile; `TrackTargetMatchResolverTests`, `ClipEditorHierarchySelectionTests`.
  Commit `A84-T1..T7`.
- [ ] **T8 — Orchestrator edits.** `Conformance_G` allowlist gains `AssetReferenceIndex`;
  `package.json` `0.31.0`; vault note section "Asset Reference Index (A84)" with D2 and the D1
  measurement.
- [ ] **T9 — Drive.** Full suites. Against `Assets/A84Scratch` copies: a rig referenced by one
  profile → right-click Delete on Rigs shows "Referenced by 1 profile"; a tag-bound track on a
  target → hierarchy row shows animated. Delete the scratch folder; `git status` clean.
- [ ] **T10 — Close.** HANDOFF §4 paragraph, roadmap checkbox.
- [ ] **T11 — ⏸ owner checkpoint.** Message: "Right-click Delete on any rig, clip set or profile
  that something uses — the dialog now says what. Tell me if the wording or the ten-name cap reads
  wrong."

---

## 6. Deliberately out of scope

- Blocking a delete because of references (A77 chose trash-and-confirm; unchanged).
- Indexing scene objects or prefabs (`ActorAuthoring` references to profiles) — assets only.
- A UI for the index — A93/A94.

## 7. Build log

_(empty)_
