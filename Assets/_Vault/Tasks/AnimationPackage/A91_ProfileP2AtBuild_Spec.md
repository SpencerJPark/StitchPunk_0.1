# Amendment A91 — Profile P2 reported at save and at player build

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.38.0`.
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
  saveable; a player build with an unplayable name is a shipped bug. ⚠ The owner may prefer the
  save-time check silent and the badge sufficient — the checkpoint asks.
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

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Confirm how the Actor Editor badge calls the
  validator with the vocabulary; confirm `Validation` is an allowed `Conformance_G` suffix for
  the two hook classes (they are not static — no rule; but check the `Editor/ClipUtilities/`
  placement rule for `Utility` does not bite a non-utility there).
- [ ] **T1 — Scan + fixture [parallel-safe]** — Files: new `ProfileP2Scan.cs`, new
  `Tests/EditMode/ProfileP2ScanTests.cs`. Fixture: `ScanProfile_ReportsOnlyP2` — an in-memory
  profile with one unknown `animationKey` and one P1-class error (e.g. a duplicate layer name —
  grep the validator for what P1 is) → exactly one message, code P2. The vocabulary is a fake
  `IVocabularyRegistry`; the scan takes an optional registry parameter so the test does not touch
  `ProjectSettings/`. Revert-to-fail: drop the code filter.
- [ ] **T2 — Save hook + build hook [parallel-safe]** — Files: new `ActorProfileSaveValidation.cs`,
  new `ActorProfileBuildValidation.cs`. No fixture (Unity callback wiring).
- [ ] **T3 — Settings toggle + docs [parallel-safe]** — Files: `VocabularySettingsProvider.cs`
  (D5's bool), `Documentation~/actor-profiles.md` ("Name errors at save and build" paragraph).
- **Gate the wave.** `ProfileP2ScanTests`. Commit `A91-T1..T3`.
- [ ] **T4 — Orchestrator edits.** `CHANGELOG.md` `## [0.38.0]`; `package.json`; `Conformance_G`
  allowlist for `ProfileP2Scan`; HANDOFF §7 loses the P2 bullet.
- [ ] **T5 — Drive.** Full suites. Give a scratch profile an animation key not in the registry, save
  → one warning pinging the profile. Run a player build (`Build Settings`, any target) → fails with
  the message; toggle D5 off → build proceeds past the preprocessor (cancel the build after that
  point; no need to complete it). Delete the scratch profile.
- [ ] **T6 — Close.** HANDOFF §4, roadmap checkbox.
- [ ] **T7 — ⏸ owner checkpoint.** Message: "Profiles now warn on save and fail a player build when
  an animation name is not in the registry. ⚠ Keep the save-time warning, or is the Actor Editor
  badge enough and only the build should complain?"

---

## 6. Deliberately out of scope

- P1, P3, P4 at build (they are already reported at bake by `ActorProfileBuilder`).
- Making the bake P2-aware (D1).

## 7. Build log

_(empty)_
