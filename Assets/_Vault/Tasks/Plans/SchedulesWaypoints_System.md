# Schedules + Waypoints System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #12 — the "city feels alive" layer; P5 of the behavior-recreation queue. CLAUDE.md "Next": waypoints as downstream request targets.

---

**Skills Needed:**
- `dots-unit-ai` — `ScheduleAwarenessSystem` + waypoint awareness scoring (§5) — this is the skill's home turf
- `dots-blob-library` — `ScheduleLibrary` if schedules are SO-authored (§4)
- `dots-system-scaffold` — world-clock system (§2)

---

## 1. Purpose & v1 scope

Two related ambience features sharing the awareness pipeline. **Schedules:** citizens bias actions by time of day (work hours → head to workplace waypoint; evening → social/home). **Waypoints:** reintroduce `NavigationWaypoint` entities as *scored awareness targets* (waypoint entity → awareness emits option → `Approach` paths to it) — the infrastructure half already exists: `WaypointRegistrationSystem` + `SpatialHashRegistry.waypointCells` are live in `GameManagerSystemGroup`, and `BehaviorExecutionSystem` already reads `waypointCells` + `NavigationWaypoint` lookup. The old empty stub files (`ScheduleAwarenessSystem` etc.) were deleted in the 2026-07 cleanup — this is a fresh build, not a resurrection.

**v1 handles:** world clock singleton, one schedule archetype (work/wander/social windows), waypoint-seeking as a scored wander upgrade.
**Out of v1:** per-NPC unique schedules, interiors, weather/environment awareness (their stubs stay parked).

## 2. Architecture

- **World clock:** `WorldClock : IComponentData` singleton `{ float timeOfDay01; int day; float secondsPerDay; }`, ticked by a tiny `WorldClockSystem` in `GameManagerSystemGroup` (world service — not scene-gated content, but ← DECISION: should the clock pause outside GameSceneTag? *Recommendation: tick it inside a gated group instead — put it in `UtilityAISystemGroup`-adjacent or gate on GameSceneTag so menus don't advance time*).
- **Schedules as consideration curves, not code:** the needs-based scoring pipeline already samples curves per action (see [[project_needs_based_ai]]). A schedule window is a **time-of-day consideration curve** on the action SO (WorkAction peaks 08–17h, TalkAction evenings). `ConsiderationScoringSystem` gains one more input axis (`WorldClock.timeOfDay01`) — no new selection machinery.
- **Waypoint awareness:** `NavigationAwarenessSystem` (exists, minimal) upgrades: query `waypointCells` around the unit, emit `ActionType.Wander` options targeting waypoint entities with utility from waypoint type + schedule curve. Behavior side is already done (`Approach` handles entity targets).

## 4. Data model

**← DECISION:** schedule authoring — (a) curves directly on existing `UtilityActionSO`s (zero new types; schedule = another consideration) vs (b) a `ScheduleSO → ScheduleLibrary` blob keyed by unit type (heavier; per-archetype windows without touching every action SO). *Recommendation: (a) — the scoring system is literally built for this; add (b) only if archetypes need conflicting windows over the same actions.*
`NavigationWaypoint` likely grows a `WaypointType` enum field (Home / Work / Social / Wander) — check the existing struct before adding.

## 5. Systems

- **New:** `WorldClockSystem` (placement per §2 decision).
- **Edited:** `ConsiderationScoringSystem` — time-of-day consideration input (curve sampling infra exists).
- **Edited:** `UtilityAwarenessSystemGroup/NavigationAwarenessSystem.cs` — waypoint-cell scan + option emission (mirror `ItemAwarenessSystem`'s spatial-cell pattern, per the audit's crowd-scale direction).
- **Edited:** `Authoring` for waypoint type field; `Contracts.md` unchanged (all intra-AI).

## 9. Build phases

1. `WorldClock` + system + debug overlay of time.
2. Time-of-day consideration axis in scoring + curve field on `UtilityActionSO` (EditMode test: curve sampling at known times).
3. Waypoint types + `NavigationAwarenessSystem` emission → units drift between typed waypoints.
4. Schedule curves authored on Work/Talk/Sit actions → observable daily rhythm in DOTSTestScene.

## 10. Verification

Speed up `secondsPerDay` (60s day): citizens visibly migrate work-waypoints at "morning", social cluster at "evening". Entities window: `UtilityActions` shows waypoint options with time-varying scores. No pathfinding regressions (waypoint Approach reuses the existing entity-target path).

## Open decisions (collected)

- [ ] §2 — clock placement/gating (recommend gated so menus don't advance time).
- [ ] §4 — schedule = consideration curves on action SOs (recommended) vs ScheduleLibrary blob.
- [ ] §5 — waypoint claim/ownership (two units at one bench) — reuse `RecentInteraction` cooldowns vs a slot system (recommend cooldown reuse for v1).
