# Utility AI Waypoint System - DOTS

## Architecture Overview

Needs-based utility AI where NPCs navigate between waypoints to satisfy their needs.
Each waypoint offers actions that modify NPC needs when performed.
Enableable components let downstream systems only iterate NPCs actively performing a specific action.

### System Execution Order

```
AISystemGroup (every frame)
│
├── SpatialHashSystem            → Rebuild spatial hash of NPCs and waypoints
├── NeedsDecaySystem             → Decay needs using NeedsDecayConfig singleton
├── NeedsHealthSystem            → Health affects safety need
├── ActionLockUpdateSystem       → Lock timers + stuck detection (only ticks decision timer when UNLOCKED)
│
├── AIAwarenessSystemGroup       → Build action options (only when unlocked + timer expired)
│   ├── AddInnateActionsSystem   → Clear buffer, add Idle/Wander
│   ├── WaypointQuerySystem      → Spatial hash query for nearby waypoints
│   └── TraitScoreModifierSystem → Personality traits adjust scores
│
├── AISelectionSystemGroup       → Pick best action
│   └── ActionSelectionSystem    → Weighted random from top 3, saves previousWaypoint
│
└── AIExecutionSystemGroup       → Execute chosen action
    ├── AIExecutionSystem        → Move to waypoint, enable ActiveXxx, apply needs, disable on complete
    └── [YOUR CUSTOM SYSTEMS]    → Query [WithAll(typeof(ActiveEat))] etc.
```

---

## Key Fix: NPCs No Longer Forget Waypoints

The bug was `decisionTimer` counting down while locked, causing re-evaluation before arrival.

**Fix:** `ActionLockUpdateSystem` only decrements `decisionTimer` when UNLOCKED.
Locked NPCs stay committed until: action completes, timeout fires, or stuck detection triggers.

## Waypoint Selection Order (No Ping-Pong)

1. Action completes → `actionLock.isComplete = true`
2. `ActionSelectionSystem` saves `waypoint` → `previousWaypoint`, clears, unlocks
3. Next decision tick: picks new waypoint (excluding previousWaypoint)
4. Locks onto new waypoint

---

## Enableable Component Pattern

This is the core scalability mechanism. Every action type has a matching `ActiveXxx` component.

**How it works at runtime:**

1. NPC arrives at waypoint → `AIExecutionSystem` calls:
   - `actionEnableHelper.SetActionEnabled(entity, ActionType.Eat, true)` → enables `ActiveEat`
   - `actionEnableHelper.SetBehaviorEnabled(entity, WaypointActionBehavior.AnimateInPlace, true)` → enables `ActiveAnimateInPlace`

2. Downstream systems query only active NPCs:
   ```csharp
   [BurstCompile]
   [WithAll(typeof(ActiveEat))]
   public partial struct EatingEffectsJob : IJobEntity
   {
       // Only iterates NPCs currently eating. ECS skips entire chunks
       // where nobody has ActiveEat enabled.
   }
   ```

3. Action completes → `AIExecutionSystem` calls:
   - `actionEnableHelper.ClearAllActiveActions(entity)` → disables everything

**Why this scales:** With 100 action types and 10,000 NPCs, a system for "Fishing" only iterates the ~50 NPCs actually fishing, not all 10,000.

### Files involved in the enable pattern:

| File | Purpose |
|------|---------|
| `Components/CapabilityAndTraitTags.cs` | Defines `ActiveXxx` structs |
| `Systems/ActionEnableHelper.cs` | Centralizes all lookups + switch statements |
| `Authoring/BrainBakeHelper.cs` | Adds all `ActiveXxx` to brain entities at bake time |
| `Systems/AIExecutionSystem.cs` | Calls helper to enable/disable at runtime |

---

## How to Add a New Action Type (Step by Step)

### Example: Adding "Fish"

**Step 1: Add enums**

`Components/ActionType.cs`:
```csharp
Fish,
```

`Components/AnimationType.cs`:
```csharp
Fish,
```

**Step 2: Add capability tag**

`Components/CapabilityAndTraitTags.cs`:
```csharp
public struct CanFish : IComponentData { }
```

**Step 3: Add enableable active component**

`Components/CapabilityAndTraitTags.cs`:
```csharp
public struct ActiveFish : IComponentData, IEnableableComponent { }
```

**Step 4: Register in ActionEnableHelper**

`Systems/ActionEnableHelper.cs` — 4 places to update:

```csharp
// 1. Add field
public ComponentLookup<ActiveFish> activeFishLookup;

// 2. Initialize in Create()
activeFishLookup = state.GetComponentLookup<ActiveFish>(false),

// 3. Update in UpdateLookups()
activeFishLookup.Update(ref state);

// 4. Add case in SetActionEnabled()
case ActionType.Fish:
    SetIfExists(ref activeFishLookup, entity, enabled);
    break;

// 5. Add disable in ClearAllActiveActions()
SetIfExists(ref activeFishLookup, entity, false);
```

**Step 5: Register in BrainBakeHelper**

`Authoring/BrainBakeHelper.cs`:
```csharp
baker.AddComponent<ActiveFish>(entity);
baker.SetComponentEnabled<ActiveFish>(entity, false);
```

**Step 6: Add capability to brain authoring**

`Authoring/CitizenBrainAuthoring.cs` Baker:
```csharp
AddComponent<CanFish>(entity);
```

**Step 7: Create waypoint in scene**

Add `WaypointAuthoring` to a fishing spot:
- actionType: Fish
- animation: Fish
- behavior: AnimateInPlace (or WanderArea)
- duration: 15
- Need modifiers: hunger 0.05, entertainment 0.08

**Step 8: (Optional) Create custom downstream system**

```csharp
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
[UpdateAfter(typeof(AIExecutionSystem))]
public partial struct FishingSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new FishingJob
        {
            deltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveFish))]
public partial struct FishingJob : IJobEntity
{
    public float deltaTime;

    public void Execute(
        ref CurrentInteraction interaction,
        ref Needs needs,
        in BrainLink brainLink)
    {
        // This ONLY runs on NPCs actively fishing
        // Custom logic: random catch chance, spawn particles, etc.
    }
}
```

---

## WaypointActionBehavior Types

| Behavior | What Happens | Use For |
|----------|-------------|---------|
| `AnimateInPlace` | NPC stays at position, plays animation | Eating, sleeping, sitting, working, fishing |
| `WanderArea` | NPC wanders randomly within `wanderRadius` | Socializing in parks, patrolling zones |
| `IdleInPlace` | NPC stands still at position | Waiting, guarding, resting |

---

## NeedsDecayConfig Singleton

Add `NeedsDecayConfigAuthoring` to any GameObject in your subscene.
All decay rates adjustable at runtime:

```csharp
RefRW<NeedsDecayConfig> config = SystemAPI.GetSingletonRW<NeedsDecayConfig>();
config.ValueRW.hungerDecayRate = 0.005f;       // Lunchtime
config.ValueRW.globalDecayMultiplier = 2.0f;    // Speed up simulation
```

### Time-of-Day Example

```csharp
[UpdateInGroup(typeof(AISystemGroup))]
[UpdateBefore(typeof(NeedsDecaySystem))]
public partial struct TimeOfDayNeedsSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<TimeOfDay>(out TimeOfDay time))
            return;

        RefRW<NeedsDecayConfig> config = SystemAPI.GetSingletonRW<NeedsDecayConfig>();
        float hour = time.currentHour;

        // Lunch (11-13): hunger decays 3x faster
        config.ValueRW.hungerDecayRate = (hour >= 11f && hour <= 13f) ? 0.003f : 0.001f;

        // Night (22-6): energy decays 4x faster
        bool isNight = hour >= 22f || hour <= 6f;
        config.ValueRW.energyDecayRate = isNight ? 0.003f : 0.0008f;
    }
}
```

---

## Scene Setup Checklist

1. **Subscene singleton objects:**
   - `NeedsDecayConfigAuthoring` on any GameObject
   - `SpatialHashSingletonAuthoring` on any GameObject (or let SpatialHashInitSystem create it)

2. **Each waypoint:**
   - `WaypointAuthoring` (configure radius, actions, occupants)
   - `WaypointDebugAuthoring` (optional, for gizmos)

3. **Each NPC (body + brain pair):**
   - Body: mesh/animator + `BodyBrainAuthoring` (drag brain reference)
   - Brain: `BrainLinkAuthoring` (drag body reference) + brain authoring (`CitizenBrainAuthoring`, etc.)

4. **Scene-level debug:**
   - `WaypointDebugToggle` on any GameObject (F3 to toggle)
   - `WaypointRuntimeDebugDrawer` on any GameObject (play-mode gizmos)

---

## Debug Visualization

| Component | When | What |
|-----------|------|------|
| `WaypointDebugAuthoring` | Editor (gizmos) | Blue = broadcast, Green = interaction, Cyan = wander, Yellow = approach line |
| `WaypointRuntimeDebugDrawer` | Play mode | Same + magenta lines from NPC to target, green/red dot = in range / traveling |
| `WaypointDebugToggle` | Runtime | F3 to toggle all debug on/off |
