# Amendment A90 — Camera-data fallback warning and a shipped writer sample

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.37.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 1.
> **Predecessors:** the billboard and LOD systems; the game's `AnimationToolkitCameraBridge.cs`
> (`Assets/_Scripts/MonoBehaviours/Managers/`) is the host-side writer this sample generalises.
> **Executor:** one orchestrator; `worker` subagents in **one wave of three**, each ≤ 2 files.
> Small amendment — one session.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A90** on the DOTS Animation Toolkit package (head `0.36.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A90_CameraDataWarning_Spec.md`. Read it, the roadmap
§3 protocol, then only what §3 here names. T0 yours; one wave (T1–T3); one gate; T4–T6 yours.
Stop at T7.

---

## 1. Goal

`BillboardResolveSystem` and `AnimLodDistanceSystem` both `RequireForUpdate<AnimationToolkitCameraData>`.
A project that never writes that singleton gets silent spherical billboarding and no LOD, with no
message. The singleton's own comment names a `ToolkitCameraSync` sample MonoBehaviour that T0 must
confirm exists (the `Samples~` listing on 2026-09-10 shows no such file). After this amendment: one
warning, once per world, when billboard roots or LOD actors exist and no camera data has appeared
after a grace period, naming the component and the sample; and a runtime sample MonoBehaviour that
writes the singleton from `Camera.main` so a new project has a one-drag fix.

---

## 2. Decisions (recorded — do not re-ask)

- **A90-D1 — A system, not a baker check.** Whether a writer exists is only knowable at run time.
  `CameraDataMissingWarningSystem` in `AnimationToolkitSystemGroup`, `RequireForUpdate` on **either**
  `BillboardRootElement` or `AnimLod` via `EntityQuery` with `Any`, runs unbursted (it logs a
  managed string), counts frames, and after 120 frames without an `AnimationToolkitCameraData`
  singleton logs one `Debug.LogWarning` and disables itself (`state.Enabled = false`). If the
  singleton appears first, it disables itself silently.
- **A90-D2 — The message names the fix:** "DOTS Animation Toolkit: no AnimationToolkitCameraData
  singleton after 120 frames — billboards fall back to spherical and LOD stays at full rate. Write
  the singleton from your camera, or import the Camera Sync sample." No amendment or spec citation
  (`Conformance_F` scans strings too? — T0 checks; if it does, the message still has none).
- **A90-D3 — The sample is a runtime MonoBehaviour under `Samples~/CameraSync/`** with its own
  asmdef referencing `DotsAnimationToolkit.Runtime` and `Unity.Entities`: on `LateUpdate`, find or
  create the singleton entity in `World.DefaultGameObjectInjectionWorld` and write `position` and
  `forward` from `Camera.main`. Forward is pass-invariant by construction (camera transform, not a
  render matrix) — the singleton's comment explains why that matters.
- **A90-D4 — The game's bridge is not replaced.** `AnimationToolkitCameraBridge.cs` stays; the
  sample is for other hosts. Nothing game-side changes.
- **A90-D5 — `Samples~` is compile-checked** in T4 via the temporary-assembly trick the vault
  records ("Samples~ is excluded from Unity compilation and rots silently").

---

## 3. Read first

- `Runtime/Components/AnimationToolkitSingletons.cs` lines 15–35.
- `Runtime/Systems/BillboardResolveSystem.cs` lines 20–40; `AnimLodDistanceSystem.cs` lines 15–50.
- `Runtime/Systems/ConfigBootstrapSystem.cs` in full — the package's other bootstrap-shaped system.
- `Runtime/Systems/AnimationToolkitSystemGroups.cs` in full (short).
- `Samples~/Cutscene/DotsAnimationToolkit.Samples.Cutscene.asmdef` and `CutsceneSampleHost.cs`
  lines 1–40 — the sample idiom.
- `Assets/_Scripts/MonoBehaviours/Managers/AnimationToolkitCameraBridge.cs` in full — what the
  sample generalises (read only; do not edit).
- `Documentation~/billboarding.md` — grep `AnimationToolkitCameraData`.

---

## 4. Design

### 4.1 `Runtime/Systems/CameraDataMissingWarningSystem.cs` (T1)

`public partial struct CameraDataMissingWarningSystem : ISystem` — no `[BurstCompile]` on
`OnUpdate` (managed log). Fields: `EntityQuery consumersQuery`, `int framesWithoutCamera`.
`OnCreate`: build the `Any` query; `state.RequireForUpdate(consumersQuery)`. `OnUpdate`: if
`SystemAPI.HasSingleton<AnimationToolkitCameraData>()` → `state.Enabled = false`; else increment,
and at 120 log D2 and disable. Placed via `[UpdateInGroup(typeof(AnimationToolkitSystemGroup),
OrderFirst = true)]`.

### 4.2 `Samples~/CameraSync/ToolkitCameraSync.cs` + asmdef (T2)

Per D3. `[DisallowMultipleComponent]`, one public `Camera cameraOverride` (null = `Camera.main`).

### 4.3 PlayMode fixture (T1)

`CameraDataMissingWarning_LogsOnceThenDisables`: manual `World`, one entity with `AnimLod`, update
the system 121 times with `LogAssert.Expect(LogType.Warning, new Regex("AnimationToolkitCameraData"))`,
then 10 more updates with no further expectation and `LogAssert.NoUnexpectedReceived()` **after**
those updates (the HANDOFF §2 trap: the call inspects logs already received). Revert-to-fail: remove
`state.Enabled = false`.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Confirm whether `ToolkitCameraSync` exists
  anywhere (grep `Samples~` and `Runtime/`); fix the singleton's comment in T4 if it names a file
  that does not exist. Check `Conformance_F`'s scan for string literals.
- [ ] **T1 — System + PlayMode fixture [parallel-safe]** — Files: new
  `CameraDataMissingWarningSystem.cs`, new `Tests/PlayMode/CameraDataMissingWarningSystemTests.cs`.
  Read `ConfigBootstrapSystem.cs`, an existing PlayMode fixture (grep `GetOrCreateSystem` under
  `Tests/PlayMode/` and read one).
- [ ] **T2 — Sample [parallel-safe]** — Files: new `Samples~/CameraSync/ToolkitCameraSync.cs`,
  new `Samples~/CameraSync/DotsAnimationToolkit.Samples.CameraSync.asmdef`. Read the cutscene
  sample's asmdef and the game bridge.
- [ ] **T3 — Docs + changelog [parallel-safe]** — Files: `Documentation~/billboarding.md` (the
  camera-data paragraph gains the warning text and the sample name; a `Samples~/CameraSync/README.md`
  is the second file), `CHANGELOG.md` `## [0.37.0]`.
- **Gate the wave.** PlayMode `CameraDataMissingWarningSystemTests`. Commit `A90-T1..T3`.
- [ ] **T4 — Orchestrator edits.** `package.json` (version and the `samples` array gains Camera
  Sync); the singleton comment; **compile-check `Samples~/CameraSync`** through a temporary asmdef
  copy under `Assets/A90Scratch/` then delete it; `Conformance_A` may need the sample asmdef listed
  — follow what the test says.
- [ ] **T5 — Drive.** Full suites. In `DOTSTestScene`, disable the game's bridge, enter Play, see
  the one warning at ~frame 120, confirm it does not repeat; re-enable the bridge. Import the sample
  into the scratch folder, drop it on the camera, confirm the warning is gone.
- [ ] **T6 — Close.** HANDOFF §1's "still true" paragraph loses its "no warning" clause; §4;
  roadmap checkbox.
- [ ] **T7 — ⏸ owner checkpoint.** Message: "Nothing to look at unless you remove the camera
  bridge — then one console warning at about two seconds tells you what to add. Read the warning
  text and say if it is clear."

---

## 6. Deliberately out of scope

- A default camera writer inside the package runtime (the package must not pick a host's camera —
  the system's own comment is the rule).
- Making the LOD or billboard systems run without camera data.

## 7. Build log

_(empty)_
