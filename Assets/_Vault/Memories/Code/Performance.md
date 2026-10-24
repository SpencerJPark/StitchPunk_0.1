# Performance — measurements and how to take them

Measurements only, newest first. Each one names its scene and unit counts, because a number without its setup means nothing. The plan these feed is `Tasks/NewPlans/GamePerformancePass_System.md`.

## How to measure from the MCP (no Profiler window needed)

`execute_code` can run `ProfilerRecorder`s across calls. Keep them in `AppDomain.CurrentDomain.SetData`, wait for a few hundred frames, then read them back with `GetSample`. The Editor must be focused or frame times are throttled (`InternalEditorUtility.isApplicationActive`).

Traps that cost time on the first run:
- **System marker names are `Default World <System>`**, with the namespace for package systems (`Default World DotsMovementToolkit.DStarLiteSystem`). A bare system name gives an invalid recorder.
- **A Burst-compiled `ISystem` reports its time under the `Burst Jobs` category.** Its `Scripts`-category marker reads ~0. Record both and use whichever is non-zero. A system that is *not* Burst-compiled shows its time under `Scripts`.
- Job markers are `<JobName> (Burst)` in `Burst Jobs`, summed across worker threads.
- List the real names with `ProfilerRecorderHandle.GetAvailable` + `GetDescription` instead of guessing.
- `Main Thread` includes Editor overhead. Compare `PlayerLoop` and `SimulationSystemGroup` between runs.

**Reading the Profiler window instead:** while the window still holds frames (paused or stopped, same Editor session), `ProfilerDriver.firstFrameIndex..lastFrameIndex` plus `ProfilerDriver.GetHierarchyFrameDataView(frame, 0 /*main thread*/, ViewModes.MergeSamplesWithTheSameName, columnTotalTime, false)` reads every frame. 1,300 frames take about 2 s. To keep a capture, save it from the Profiler toolbar as a `.data` file under `Library/` (never `Assets/`). `ProfilerDriver.LoadProfile(path, false)` loads it back for the same read.

## 2026-10-23 — CampLoop, after the spawn-init fix and A* (MCP recorders)

Setup: `Scenes/CampLoop.unity` in Play mode, Editor focused, about 590 frames. 7 `PathfindingAgent` units, all on A*. Compare with the owner's 69-frame capture earlier the same day (30–60 ms frames).

| Marker | before (capture) | after |
|---|---|---|
| SimulationSystemGroup | 16.6 | 3.4 |
| SpawnInitSystemGroup | 5.2 (two exceptions thrown per frame) | 0.09 |
| MovementSystemGroup | 3.55 | 0.65 |
| DStarLiteSystem | 2.1 | 0.005 (early exit, no D* agents) |
| AStarSystem (main thread) | — | 0.02 |
| AStarPathJob (worker threads, summed) | — | 0.19 |

What it shows:
- The spawn-init exceptions were the largest fixable cost (`Gotchas.md`, main-thread lookup writes). D* Lite's cost is gone because characters no longer use it.
- `AStarPathJob` runs on most frames because game code re-requests every frame (`Systems_Movement.md`). It is off the main thread and the same-goal reuse keeps it small.
- An early-exit `IsEmpty` on an enabled bit still waits for the jobs that write that bit. `DStarLiteSystem` measured 0.10 ms of pure waiting until `AStarSystem` moved to `OrderLast`.

## 2026-10-19 — CampLoop, zombies vs bandits (owner's capture)

Setup: the owner's Profiler window capture of a fight, 1,301 frames read through `HierarchyFrameDataView`. At pause: 30 `UtilityBrain` units (6 enabled), 30 D* Lite agents, 31 with `Dead` (2 enabled), 1,597 entities.

| Marker | avg ms | max ms | frames > 2 ms |
|---|---|---|---|
| PlayerLoop | 12.1 | 57.2 | |
| SimulationSystemGroup | 4.9 | 36.6 | |
| `JobHandle.Complete` (all main-thread waits, summed) | 1.17 | 17.7 | 105 (8%) |
| DStarLiteSystem | 0.97 | 2.5 | 9 |
| `DebugResourceMenu.OnGUI` (game debug IMGUI) | 0.85 | 6.0 | 10 |
| UtilityAISystemGroup | 0.39 | 2.0 | |
| ItemAttachSystem | 0.23 | 2.4 | 1 |
| PlayerKnockoutSystem | 0.16 | 2.4 | 2 |
| NavGridDebugRenderSystem | 0.075 | **12.0** | 6 |
| NavGridSystem | 0.03 | **15.0** (waiting on jobs) | 2 |

What it shows:
- Simulation cost barely moved from 7 to 30 units (4.9 ms both times). The cost is fixed overhead, not per-unit scaling, so crowd-scale work (PP-P5) is not urgent.
- The worst frames (14–30 ms) are movement-toolkit grid work: `NavGridDebugRenderSystem` (a debug overlay, running in play) and `NavGridSystem` waiting on a grid rebuild. They are rare but are the visible hitches.
- `DebugResourceMenu.OnGUI` costs nearly as much as D* Lite. It is debug UI and should be off or toggleable outside debugging.

## 2026-10-19 — CampLoop, small scene (baseline)

Setup: `Scenes/CampLoop.unity` in Play mode, Editor focused, about 580 frames. 7 `UtilityBrain` units, 21 D* Lite agents, 697 entities.

| Marker | avg ms | max ms |
|---|---|---|
| Main Thread (incl. Editor) | 17.1 | 26.9 |
| PlayerLoop | 12.1 | 15.8 |
| SimulationSystemGroup | 4.9 | 8.2 |
| **DStarLiteSystem** (main thread, not Burst-compiled) | **1.10** | **2.88** |
| PresentationSystemGroup | 0.65 | 1.9 |
| UtilityAISystemGroup (all of AI) | 0.39 | 0.98 |
| ItemAttachSystem (not Burst-compiled) | 0.22 | 1.89 |
| InteractionSpatialHashSystem (incl. its `.Complete()`) | 0.14 | 0.35 |
| DamageResolutionSystem | 0.06 | 0.34 |
| EnemyAwarenessSystem | 0.04 | 0.24 |
| WaypointRegistrationSystem | 0.025 | 0.13 |
| FactionRegistrySystem | 0.017 | 0.17 |
| CorpseCellSystem / RagdollLaunchInitSystem | 0.007 | 0.05 |

What it shows:
- **D* Lite is the largest single system: about 23% of the simulation, at only 21 agents.** It runs on every one of the 580 frames (minimum 0.32 ms), even when no `PathRequest` was enabled. Its `OnUpdate` is not `[BurstCompile]` and loops over `PathRequest` + `LocalTransform` on the main thread. That likely forces the system to wait on every job writing `LocalTransform`, which would explain the fixed minimum cost. Each request also resets the whole grid and searches with a linear-scan open set. Found by measuring, missed by the code review.
- The AI and game-manager items from the review are all under 0.15 ms at this scale. They only grow with unit count, so they are worth re-measuring at a bigger crowd, not fixing first.
