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
