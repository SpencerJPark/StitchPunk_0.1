# Session prompt — G3 Cutscene Acceptance (paste this whole block into a fresh session)

You are running **G3 — Cutscene Acceptance, "Rendezvous and Depart"** on the Stitch Punk project.
The spec is `Assets/_Vault/Tasks/NewPlans/CutsceneAcceptance_System.md`. Read it in full, then
`Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §6 (the beat sheet you are authoring) and §4
(the execution protocol — it is binding), then the repo root `CLAUDE.md` and
`Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5, §6.

**Unity is currently closed.** Open the Editor first; nothing below can be verified without it. If
you cannot open it, say so and stop rather than reporting anything as compiling or passing.

## Why this spec matters more than the ones before it

**Seven owner checkpoints are queued and unverified** — G1, A63, A64, A65, G2, A66, A67. The owner
has been away and *nothing in the cutscene feature has been watched by a human*. G3 is not another
feature on that pile: it is the one cutscene that exercises every lane and every host contract at
once, so the checklist you produce is what finally converts all seven into "verified" or "here is
the failing step and the spec that owns it". Optimise everything you do for one uninterrupted
sitting at the end.

Your job is Phases 1–3, ending with `Tasks/Verification/verify-cutscene.md` ready to run. **Do not
attempt the checkpoint yourself** — you cannot; see "what you cannot verify" below.

## Close the blocker first

G3's checklist step 2 asks the minions to face their travel direction. **That cannot pass today.**
`CutsceneInteractions_System.md` §6 item 3 recorded it: `UnitFacing` and the `BodyPart` buffer come
from `CharacterRigAuthoring`, and **no prefab in the project has one** (all 42 scanned; measured in
a live world as `unitFacingEntities=0`, `bodyPartBuffers=0`). The toolkit half is confirmed working
— `CutsceneFacing` swung `186.6° → 0°` on a bound actor — so this is content, not code.

**A lead worth checking before you assume it is big:** `Assets/_Scripts/Authoring/Units/CharacterRigAuthoring.cs`
already `AddBuffer<BodyPart>` and `AddComponent(new UnitFacing …)` in its baker, and its own header
says `CharacterRigBakingSystem` / `BodyPartInitSystem` fill the buffer **from descendants**. So this
may be "add the component to `MaleCitizen.prefab` and rebake", not "hand-build a body-part tree".
Verify that in a live world (bake, then count `unitFacingEntities` / `bodyPartBuffers` and check
`BodyPart.entity` actually resolves to the toolkit's rig parts) before spending a session on it.
Remember `MaleCitizen` is the project's **only** toolkit actor — `PlayerUnit` / `BaseUnit` use a
separate copy of the body-part tree and will need their own pass.

If the gap turns out to be genuinely large, **stop after closing it** and hand off. A half-authored
acceptance cutscene is worse than none.

## Drift in the spec you will hit

The spec was written 2026-09-04, before A66/A67/A69 landed. Grep and follow the code; log drift in
the spec's §6 build log, never edit the spec to agree with you.

- **`CutscenePlaybackApi` no longer exists.** A69 renamed it to `CutsceneApi`. The skip call in
  checklist step 8 is `CutsceneApi.RequestSkip(EntityManager, Entity)`.
- **`CutsceneDebugTrigger` uses the new Input System**, not `KeyCode`: it polls
  `Keyboard.current[key].wasPressedThisFrame` with `[SerializeField] private Key key = Key.F9`. The
  skip key is a second serialized `Key`, not a `KeyCode`.
- Authoring the asset is now much less painful than the spec assumes — A66 and A67 shipped
  multi-select, box-select, copy/paste (Ctrl+C/X/V/D), Auto Key, a curve editor, viewport
  click-select, an in-tab gizmo with W/E/R, a frozen header column, and Ctrl+wheel / Home / Alt+P
  timeline navigation. Author through the real UI where you can; that is also a free smoke test of
  A66/A67, and anything that feels wrong is a finding worth logging.

## Suite baselines — take them from here, nothing older

Toolkit EditMode **724 discovered / 723 passed**, toolkit PlayMode **261/261**, `StitchPunk.Tests`
**59/59**, `StitchPunk.Tests.PlayMode` **7/7**. Package version **0.15.0**. The single EditMode
failure is the long-standing `Conformance_A` asmdef drift — **not yours, do not chase it.** Check
discovered totals did not drop, not just pass/fail.

## The traps, carried forward so you do not pay for them twice

**Driving the Editor over MCP.** `execute_code` runs as a method body, so `using` is a syntax error
— fully qualify everything. Only the CodeDom C# 6 backend exists: no local functions, no ref
returns, no target-typed `new`; `World.GetOrCreateSystem<T>()` picks the wrong overload (use
`GetOrCreateSystem(typeof(T))`); `UnityEngine.Object` vs `object` is ambiguous, so write
`UnityEngine.Object.DestroyImmediate`; `HashSet<T>` does not implement non-generic `ICollection`, so
read `Count` through reflection; extension methods need their static class
(`UnityEngine.UIElements.VisualElementExtensions.ChangeCoordinatesTo`). `AssetDatabase.DeleteAsset`
needs `safety_checks: false`. `GetWindow<T>()` stalls the Editor for about a minute — find windows
with `Resources.FindObjectsOfTypeAll<EditorWindow>()`. Large bash heredocs fail on this shell: write
patch scripts to the scratchpad and run them with `python`.

**`resolvedStyle` and `layout` are stale in the same `execute_code` call that rebuilt the UI** — they
come back `NaN` or as the previous values. Split across two calls; a repaint happens in between.

**What you cannot verify, and must not claim.** An unfocused Editor never repaints its Scene view,
so anything inside `SceneView.duringSceneGui` is never called — `RepaintAll`, `Repaint` and
`RepaintImmediately` all leave it uncalled. Screen captures lie under occlusion. Real pointer and
keyboard gestures cannot be driven. Say which half of your work needed the owner's hands.

**Probe the platform before designing on it.** A67 lost a design to an unverified assumption and
caught it with one six-line probe. If a plan rests on "hide it with X" or "render it via Y", measure
X and Y first and read the observable back.

**The Cutscene Editor panel has its own deferred-rebuild pair** (`RequestInspectorRebuild` /
`RequestTimelineRebuild`) and its own always-on `OnEditorTick`; the panel's `Tick` is the *transport*
tick and runs only while playing. Never rebuild a pane from a value-changed callback.

**Game-side DOTS traps** (all in the vault's `Gotchas.md`): `[WithDisabled]` is not a reliable
"react to a switch-off" filter — use `[WithPresent]` plus a `ValueRO` check; an `in` parameter beside
an `EnabledRefRW` of the same type is a run-time aliasing throw; a newly added `[BurstCompile]` job
can run as somebody else's compiled code for a run or two, so a failure naming a job you did not
touch, or a revert that stubbornly passes, means recompile and re-run.

**Two Editor-log files exist** and which is live varies by launch — check `LastWriteTime` on both
before trusting a grep. `Object.GetInstanceID()` is a **compile error** in Unity 6.5; use
`GetEntityId()`.

## Test discipline

The spec names **one** perf fixture in `StitchPunk.Tests.PlayMode` (§3): 600 `CutsceneTimelineSystem`
updates under a generous bound, to catch an accidental per-frame allocation or an O(n²) walk, not to
benchmark. That is the ceiling. Before keeping it, revert the thing it protects and watch it fail;
if it passes both ways, delete it. No fixtures for the authoring work.

## Working rules

Work the phases in order. After each: save → `mcp__UnityMCP__refresh_unity` → poll
`editor_state.isCompiling` → `mcp__UnityMCP__read_console` for `error CS` / `BC` → run only the
fixtures you touched → tick the phase → commit that phase alone with a `G3-Pn:` prefix, staging paths
explicitly, **never `git add -A`**. Push when green. Full suites once at the end.

**Never stage `ProjectSettings/EditorSettings.asset`** — PlayMode runs flip it. Watch for scene files
going dirty for reasons that are not yours: entering Play mode can move a camera and write the scene
on exit. Check `git status` before every commit and restore anything you did not intend to change.

For anything that saves, prove the write persists: drive it, save, reload from disk, assert. Delete
every scratch asset and scene object you create and confirm `git status` afterwards.

## Finish

Update the spec's status line and §6 build log, `git mv` the spec to `Tasks/Verification/`, update
`Plans/README.md`, and write `Tasks/Verification/verify-cutscene.md` in the shape §4 specifies.
Update `Docs/AnimationToolkit/HANDOFF.md` §4 and the roadmap's status table.

Then **stop at the ⏸ owner checkpoint** and hand back with: what to open, what key to press, and the
checklist to run — plus a plain statement of which steps you machine-verified and which need their
eyes. If a step cannot pass because a prior spec is incomplete, say which spec owns it rather than
patching around it.
