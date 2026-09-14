# Amendment A91 — Profile P2 reported at save and at player build

> **Status:** ✅ built 2026-09-13 as `0.36.0` (spec said `0.38.0`; A88 and A90 are unbuilt — §7). Accepted 2026-09-13 (T7 answered). A real player build was attempted 2026-09-13 but blocked by unrelated game compile errors; accepted as working for now, check tracked in `Assets/_Vault/Spencer/verify-a91-player-build.md`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1.
> **Predecessors:** A70 (profiles, P1–P4 validation), A71 (the Actor Editor badge that is today's
> only P2 surface).
> **Executor:** one orchestrator; `worker` subagents in **one wave of three**, each ≤ 2 files.
> Small amendment — one session.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A91** on the DOTS Animation Toolkit package (head `0.37.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A91_ProfileP2AtBuild_Spec.md`. Read it, the roadmap §3
protocol, then only what §3 here names. T0 yours; one wave (T1–T3); one gate; T4–T6 yours. Stop at
T7.

---

## 1. Goal

`ActorProfileValidation` rule P2 ("every `animationKey` a profile names is in the
`AnimationNameRegistry`") is skipped at bake, because `ActorProfileBuilder` runs in the Authoring
assembly and the registry lives under `ProjectSettings/`, reachable only through the editor-only
`VocabularyRegistryProvider`. HANDOFF §7 records the gap; the Actor Editor badge is the only place
P2 is reported, so a profile edited by hand or by script ships a key nothing can play by name.

After this amendment P2 is reported at the two moments that matter and the bake still does not
try: **on save** of any `ActorProfileAsset` (a console warning naming profile, layer and animation),
and **at player build** (an `IPreprocessBuildWithReport` that fails the build listing every P2
error across the project).

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A91-D1 — The bake stays P2-blind, on purpose.** Moving the registry into a bakeable asset or
  making Authoring read `ProjectSettings/` would either ship the vocabulary to players (it is
  authoring-only by design) or reference `UnityEditor` from Authoring (`Conformance_C`). Editor-side
  hooks are the correct boundary; the builder's own comment already says so.
- **A91-D2 — Save-time is a warning, build-time is an error.** A half-finished profile must be
  saveable; a player build with an unplayable name is a shipped bug. Confirmed by the owner
  2026-09-13: keep the save warning, because it fires only while the profile has P2 findings.
- **A91-D3 — One shared scan.** `ProfileP2Scan` (plain noun, allowlist; `Editor/ClipUtilities/`)
  returns `List<ValidationMessage>` for one profile or for every profile in the project, calling
  `ActorProfileValidation` with `VocabularyRegistryProvider.AnimationNames`. Both hooks and A94's
  Health tab call it.
- **A91-D4 — The save hook is an `AssetModificationProcessor.OnWillSaveAssets`** filtered to paths
  whose main asset is an `ActorProfileAsset`; it never blocks the save.
- **A91-D5 — The build hook is `IPreprocessBuildWithReport` with `callbackOrder = 0`**, throwing
  `BuildFailedException` with the joined messages. Skippable through an `EditorPrefs` bool exposed
  in the Vocabulary settings provider ("Fail player builds on profile name errors", default on).

---

## 3. Read first

- `Authoring/Validation/ActorProfileValidation.cs` lines 1–40 and 140–170 (the P2 emission and
  the "vocabulary supplied" contract).
- `Authoring/Build/ActorProfileBuilder.cs` lines 25–45 (the comment this amendment preserves).
- `Editor/ClipUtilities/VocabularyRegistryProvider.cs` lines 55–75 (`AnimationNames`).
- `Editor/Inspectors/VocabularySettingsProvider.cs` — grep `EditorPrefs` for the toggle idiom.
- `Editor/ClipEditor/ActorEditor/ActorEditorPanel.cs` — grep `ActorProfileValidation` to see how
  the badge supplies the vocabulary (copy that call shape).
- `Tests/EditMode/ActorProfileValidationTests.cs` — grep `P2`.

---

## 4. Design

### 4.1 `Editor/ClipUtilities/ProfileP2Scan.cs` (T1)

```csharp
public static class ProfileP2Scan
{
    public static List<ValidationMessage> ScanProfile(ActorProfileAsset profile);
    public static List<ValidationMessage> ScanProject();   // FindAssets t:ActorProfileAsset
    public static string FormatForConsole(ActorProfileAsset profile, ValidationMessage message);
}
```

Filters the validator's output to `ValidationCode.P2` only; other codes already have surfaces.

### 4.2 `Editor/ClipUtilities/ActorProfileSaveValidation.cs` (T2)

`AssetModificationProcessor` per D4; one `Debug.LogWarning(FormatForConsole(...), profile)` per
message so clicking the console line pings the profile.

### 4.3 `Editor/ClipUtilities/ActorProfileBuildValidation.cs` (T2)

`IPreprocessBuildWithReport` per D5.

---

## 5. Tasks

- [x] **T0 — Baseline (orchestrator).** Gate; totals. Confirm how the Actor Editor badge calls the
  validator with the vocabulary; confirm `Validation` is an allowed `Conformance_G` suffix for
  the two hook classes (they are not static — no rule; but check the `Editor/ClipUtilities/`
  placement rule for `Utility` does not bite a non-utility there).
- [x] **T1 — Scan + fixture [parallel-safe]** — Files: new `ProfileP2Scan.cs`, new
  `Tests/EditMode/ProfileP2ScanTests.cs`. Fixture: `ScanProfile_ReportsOnlyP2` — an in-memory
  profile with one unknown `animationKey` and one P1-class error (e.g. a duplicate layer name —
  grep the validator for what P1 is) → exactly one message, code P2. The vocabulary is a fake
  `IVocabularyRegistry`; the scan takes an optional registry parameter so the test does not touch
  `ProjectSettings/`. Revert-to-fail: drop the code filter.
- [x] **T2 — Save hook + build hook [parallel-safe]** — Files: new `ActorProfileSaveValidation.cs`,
  new `ActorProfileBuildValidation.cs`. No fixture (Unity callback wiring).
- [x] **T3 — Settings toggle + docs [parallel-safe]** — Files: `VocabularySettingsProvider.cs`
  (D5's bool), `Documentation~/actor-profiles.md` ("Name errors at save and build" paragraph).
- **Gate the wave.** `ProfileP2ScanTests`. Commit `A91-T1..T3`.
- [x] **T4 — Orchestrator edits.** `CHANGELOG.md` `## [0.38.0]`; `package.json`; `Conformance_G`
  allowlist for `ProfileP2Scan`; HANDOFF §7 loses the P2 bullet.
- [x] **T5 — Drive.** Full suites. Give a scratch profile an animation key not in the registry, save
  → one warning pinging the profile. Run a player build (`Build Settings`, any target) → fails with
  the message; toggle D5 off → build proceeds past the preprocessor (cancel the build after that
  point; no need to complete it). Delete the scratch profile.
- [x] **T6 — Close.** HANDOFF §4, roadmap checkbox.
- [x] **T7 — ⏸ owner checkpoint.** Message: "Profiles now warn on save and fail a player build when
  an animation name is not in the registry. ⚠ Keep the save-time warning, or is the Actor Editor
  badge enough and only the build should complain?"

---

## 6. Deliberately out of scope

- P1, P3, P4 at build (they are already reported at bake by `ActorProfileBuilder`).
- Making the bake P2-aware (D1).

## 7. Build log

**T0 (2026-09-13, head `f3dc66b4`, 0.35.0).** Baseline compile clean; inherited totals EditMode 833
(standing `Conformance_A` only), PlayMode 283. Drift against this spec, all settled here:

1. **Version.** Spec said `0.38.0` / head `0.37.0`; A88 and A90 are unbuilt, so A91 takes `0.36.0`.
2. **P1 is not "a duplicate layer name"** (T1 hint). P1 = layer count outside 2..8 or missing
   Base/Override bookends. T1's fixture uses a single-layer profile.
3. **P2 is only half skipped at bake.** `Validate` emits P2 for `animationKey == 0` even with a null
   registry, so `ActorProfileBuilder.Build`/`ComputeContentHash` already fail on it; only registry
   membership needs the vocabulary. The builder's doc comment ("judged … at entity bake") and
   `actor-profiles.md`'s paragraph under Validation are wrong. **Settled:** `ScanProfile` reports
   every P2 message (both halves, one code); T4 corrects the builder comment (no `UnityEditor` text),
   T3 rewrites the doc paragraph.
4. **Null means "skip".** `Validate(profile, null)` skips membership. **Settled:** `ScanProfile` and
   `ScanProject` take an optional `IVocabularyRegistry animationNames = null` (A94's pure rules can
   pass an in-memory one); null is replaced by `VocabularyRegistryProvider.AnimationNames` before
   `Validate`, never passed through. Second fixture covers it.
5. **`FakeAnimationNameRegistry` is private** to `ActorProfileValidationTests`. The new fixture
   carries its own.
6. **`VocabularySettingsProvider` has no `EditorPrefs` idiom** (it is a UI Toolkit `SettingsProvider`
   factory). **Settled:** the key and a `FailPlayerBuildsOnNameErrors` static property live on
   `ActorProfileBuildValidation`; `CreateProvider` gains an optional page-controls callback and the
   Animation Names page appends a UI Toolkit `Toggle`. EditorPrefs (per machine) per D5.
7. **No save/build hook precedent** in the package. Signatures verified live:
   `static string[] OnWillSaveAssets(string[])` on an `AssetModificationProcessor` subclass;
   `IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport)` + `callbackOrder`;
   `BuildFailedException(string)`. `Conformance_G` checks only static classes, so the two hook
   classes need nothing; `ProfileP2Scan` goes on `PlainNounStaticClasses` at T4.
8. **T0 probe deferred** (does `OnWillSaveAssets` fire for `SaveAssetIfDirty`?): `execute_code`
   assemblies are not scanned for processors, so the real hook is the probe, run after the gate.
9. **No real player build at T5** (owner instruction): the build hook is proven one level down
   (`TypeCache` contains it; `OnPreprocessBuild(null)` throws with the toggle on, returns with it
   off), so the hook must never read `report`.

**T1–T3 wave (three workers, 52–58k tokens each).** The gate found one error. T1's copy of the
private fake registry missed `GeneratedConstantsPath` (CS0535): the brief's read range ended one line
short. The orchestrator fixed it.

**Revert-to-fail** on `ProfileP2Scan`, both mutations in one compile:
- Filter dropped only when a registry is passed: `ScanProfile_ReportsOnlyP2` failed, 3 ≠ 1.
- Null registry passed through to `Validate`: `ScanProfile_WithNoRegistrySuppliedChecksTheProjectRegistry`
  failed, 0 ≠ 1.
- Restored from backup, sha256 identical.

Commits `3e022580` (T0–T3), `eceb96b5` (T4), `2d1641ec` (T5/T6 docs, minus this file).

**T5 suites:** EditMode 835 (833 + 2, standing `Conformance_A` only), PlayMode 283.

**T5 drive setup.** Scratch folder `Assets/A91Scratch/`, key `0xDEADBEEF`, confirmed absent from the
9-entry registry. The project's 2 real profiles had 0 P2 findings, so today's player builds are
unaffected. Nothing in the drive calls `SaveAssets`; the owner's dirty `EditorBuildSettings` and
`RobertsCross.mat` stayed unwritten.

- **Save.** `CreateAsset` gave 0 warnings. `SetDirty` + `SaveAssetIfDirty` gave exactly 1, naming the
  profile, its path, layer `Base`, entry 0 and the id. A second save with nothing dirty gave 0.
  - This answers the T0 probe: the hook fires for `SaveAssetIfDirty`.
  - It does not fire for `CreateAsset`, so a new profile warns on its next save.
- **Build**, proven one level down with no real player build:
  - `TypeCache` holds both hooks; `callbackOrder` is 0.
  - Toggle on (default): `OnPreprocessBuild(null)` threw `BuildFailedException` naming the scratch
    profile. Pref false: it returned.
- **Toggle.** It exists only on the Animation Names page, as the last child.
  - Mounted in a floating utility window: `value = false` wrote the pref false, `true` wrote true.
  - Without a panel, the change event does not dispatch.
  - The window was closed. The pref key was deleted, as it was absent before.
- **Cleanup.** Scratch folder deleted. The three registries are byte-identical to their backups, and
  git status shows only the owner's files.
- **Not captured.** The toggle is a stock UI Toolkit `Toggle` on a Project Settings page; the owner
  can look at Project Settings ▸ DOTS Animation Toolkit ▸ Animation Names.

**T7 (owner, 2026-09-13).**
1. **Save warning:** keep it while it is relevant to there being issues. That is the built behaviour.
   The hook warns only for a profile with P2 findings and goes quiet once they are fixed. No change.
2. **Real player build:** not now; the owner is away from the PC. The build hook stays proven one
   level down only. A real build is a later owner check, not a blocker.
3. **Toggle scope:** per machine (EditorPrefs), as built. No change.

### Real player build attempt (2026-09-13)

- The owner ran a player build. It stopped on unrelated script compile errors in `Assets/_Scripts/Editor/`
  (`PropertyDrawer`, `Editor`, `IMGUI` not found). Likely cause: `StitchPunk.Editor.asmdef` has
  `includePlatforms: []`, so it compiles into the player. Not changed; game-side.
- "Player build stopped" never reached the console, so the hook was not exercised.
- The build list's only scene was the missing `Assets/Scenes/Main.unity` when the session checked; the
  owner has since changed `EditorBuildSettings`.
- **Owner call:** accept as working for now. The follow-up check is tracked in `Assets/_Vault/Spencer/verify-a91-player-build.md`.
- Scratch recipe used (deleted afterwards): `Assets/A91BuildTest/A91BuildTest.profile.asset`, created by
  reflection. `CreateInstance`, `EnsureStableIds`, `EnsureBookends`, then one default
  `ActorAnimationDefinition` added to `layers[0].animations`, then `CreateAsset` with no `SaveAssets`.
  `ProfileP2Scan.ScanProject(null)` returned exactly one finding: "Layer 'Base' entry 0 has no animation
  name (animationKey is 0)".
