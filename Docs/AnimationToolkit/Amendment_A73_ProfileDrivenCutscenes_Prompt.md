# Session prompt — Amendment A73 (paste this whole block into a fresh session)

You are running **Amendment A73 — Profile-Driven Cutscenes** on the DOTS Animation Toolkit package
in this repo. The spec is `Docs/AnimationToolkit/Amendment_A73_ProfileDrivenCutscenes_Spec.md`.
Read it in full, then its §2 "Read first" list in order, then
`Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 — that protocol is binding. Its §1 product
calls and §4 decisions (A73-D1…D9) are settled; do not re-ask the owner whether slots should keep
their own rig, whether the cutscene may write `ActorFacing`, or whether a block's end should hand a
layer back to auto locomotion (it does not — the ■ stop key does).

**This is a two-session amendment.** Session 1 runs T1–T3 (Authoring + Runtime; nothing visible)
and ends after the full suites with HANDOFF §4 updated. Session 2 runs T4–T8 (Editor, preview,
marks UX, docs) and ends at the ⏸ owner checkpoint. If you are session 2, read session 1's §7
build-log entries before opening any file — names may have drifted. **Package only** — the game
side is G6 (`Assets/_Vault/Tasks/NewPlans/CutsceneProfileCutover_System.md`) and runs after you; the
game assemblies are **expected red** from T1 onward (`CutsceneRequest.layerIndex`, the checkpoint
cutscene assets, `MaleCitizenContentAuthoring.cs`'s `directionSet` line). Record the exact failing
game fixtures in §7 and do not fix them.

Work the tasks in order. **T4 and T5 are `[parallel-safe]` with each other**: spawn one Sonnet
subagent each with the spec path and its task text, wait, then run one compile gate over both.
**Subagents never call any `mcp__UnityMCP__*` tool** — only you compile, run tests and commit.
Commit each task alone with an `A73-Tn:` prefix, staging paths explicitly, never `git add -A`.
Push when green.

## State you are building on

- **0.18.0 (A72)** is the last shipped version: every tab hosts one `TransportCoreElement`, colours
  come from `ToolkitPalette`, icon buttons from `ToolkitIcons.MakeIconButton`, boxed lists use
  `toolkit-box`. Any control you add follows that (spec §3.4).
- **A70/A71/G5 landed 2026-09-07**: `ActorProfileAsset`, `PlayAnimation`/`StopAnimation`,
  `ActorFacing` + `ActorFacingRepickSystem`, the Actor Editor tab, `MaleCitizen.profile.asset` with
  `Idle`/`Walk` on Base. Cutscenes still play raw clip ids on `CutsceneApi.TopLayer` — that is what
  you are replacing.
- Take your suite baselines from A72's closing HANDOFF §4 paragraph (toolkit EditMode 760, PlayMode
  277, the standing `Conformance_A` asmdef drift not yours). Counts must not drop except where the
  spec deletes a named test.

## The traps that will cost you a session if you rediscover them

Everything in `Amendment_A66_CutsceneEditorPolish1_Prompt.md`'s sections **"The traps that will cost
you a session if you rediscover them"** and **"Driving the Editor over MCP"** applies unchanged.
On top of those:

- **`CommandApplySystem` is `OrderFirst` in the logic group.** The cutscene system cannot run before
  it; commands drain next frame. A PlayMode fixture that asserts a `PlaybackLayer` change must
  update twice (the spec's fixtures are written that way).
- **`ActorFacingRepickSystem` only re-picks layers whose `animationKey != 0`.** A block issued as a
  raw `Play` would never turn. Every block must go out as `CommandKind.PlayAnimation`.
- **`PlaybackApi.PlayAnimation` takes an `EnabledRefRW<AnimationCommandPending>`**; from the cutscene
  system's `EntityManager` context you append the same `AnimationCommand` shape by hand and call
  `SetComponentEnabled<AnimationCommandPending>(entity, true)`, exactly as `ProcessClipBlocks` does today.
- **`Conformance_C` scans `Authoring/` raw text for `UnityEditor`** — the derived mark hold and the
  picker filter are two different assemblies; the editor registry reaches the builder only through
  `CutsceneDerivedHolds.EventNameRegistrySource`-shaped seams.
- **`Samples~` is not compiled.** Check `CutsceneSampleHost.cs` through a temp assembly (`Gotchas.md`)
  or the gate lies to you.
- **Scene-view GUI is unprovable from a background Editor**; the in-tab viewport and every UI Toolkit
  element are drivable. Say which you verified.

Write under A69's comment rule — one `<summary>` per file on the primary type, three lines maximum,
no `<remarks>`, no spec citations in non-test sources. `Conformance_F`/`G`/`H` enforce it.

Close your session by updating the spec's status line and §7 build log, `CHANGELOG.md` (session 2),
HANDOFF §4 (one paragraph) and — session 2 only — stopping at the ⏸ owner checkpoint with exactly
what to open, press and look at.
