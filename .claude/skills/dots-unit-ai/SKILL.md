---
name: dots-unit-ai
description: >
  Scaffold or extend a unit AI decision behaviour in the Stitch Punk pipeline
  -- writing awareness systems that detect conditions and emit ActionOption
  entries, wiring new ActionType/MotivationType enums, registering Burst
  function pointers in SelectionFunctions, writing action execution systems
  that drive PathRequest/AttackRequest/AnimationRequest, and setting up
  ActionInterruptRequest for urgent reactions. Use this skill whenever the
  user wants to add a new reactive or scheduled unit behaviour ("make units
  react to X", "add a daily schedule", "units should panic when Y", "add an
  awareness system for Z", "wire up ActionType.Foo", "add an interrupt for W",
  "how does ActionOption flow into ActionSelectionSystem"), or needs to
  understand the AIActionSelectionSystemGroup -> ActionSystemGroup pipeline.
  Also use for adding new MotivationType/ActionType enums, new IEnableableComponent
  action tags, and player-controlled (minion) equivalents of any AI behaviour.
  Do NOT use for: baking ScriptableObjects into BlobAssets (use dots-blob-library),
  MonoBehaviour authoring bakers (use dots-authoring-baker), generic non-AI
  ISystem scaffolding (use dots-system-scaffold), movement system configuration,
  animation blend setup, or debugging why an existing behaviour is broken.
---

## Pipeline at a Glance

```
SimulationSystemGroup
  PlayerSystemGroup                  <- player input only
  MinionActionSelectionSystemGroup   <- player-commanded units
  AIActionSelectionSystemGroup       <- utility-driven AI
    UtilityMotivationSystemGroup
    UtilityAwarenessSystemGroup           <- writes ActionOption buffer entries
      [your new awareness system here]
    AIScoringSystemGroup             <- multiplies utility by motivation curves
  ActionSystemGroup                  <- shared by ALL unit types
    ActionSelectionSystemGroup
      ActionInterruptSystem          <- handles ActionInterruptRequest (OrderFirst)
      ActionSelectionSystem          <- reads ActionOption buffer -> sets CurrentAction
    ActionExecutionSystemGroup       <- enables PathRequest / AttackRequest / AnimationRequest
      [your new action system here]
  MovementSystemGroup                <- reads PathRequest
  CombatSystemGroup                  <- reads AttackRequest
  AnimationSystemGroup               <- reads AnimationRequest / SetAnimation buffer
```

The contract between stages is the `ActionOption` buffer. Awareness systems append options; they never talk directly to action systems. Action systems only read `CurrentAction` (set by `ActionSelectionSystem`).

---

## What to Build for a New Behaviour

Work through this decision tree top-to-bottom before writing anything:

| Question | Yes -> | No -> |
|---|---|---|
| Does an existing `MotivationType` semantically fit? | Reuse it | Add entry to `AiEnums.cs` |
| Does the new behaviour execute identically to an existing action (same state machine, same requests)? | Reuse the existing action tag and system; only add a new `ActionType` that maps to the same tag in `SelectionFunctions` | Need a new IEnableableComponent tag and a new action system |
| Does the behaviour need to *preempt* whatever the unit is currently doing immediately? | Set `ActionInterruptRequest` enabled in the awareness system | Let normal scoring / priority handle it |

**Practical examples of reuse vs. new systems:**
- A new "collect item" interaction vs. "sit in chair" -> same walk-to-target-then-animate pattern -> both can enable `InteractAction`, no new system needed.
- Flee vs. melee -> completely different state machines -> separate action systems required.
- Explosion reaction vs. rain reaction -> likely same "cower / seek cover" state machine -> one `EnvironmentalAction` tag shared between both `ActionType` values.

---

## Naming Convention -- EnabledRefRW Parameters

When a job parameter is `EnabledRefRW<Foo>` or `EnabledRefRO<Foo>`, name the parameter `fooEnabled`. This applies to all action tags, request components, and any other enableable component in a job's `Execute` signature.

```csharp
// correct
EnabledRefRW<PathRequest>           pathRequestEnabled,
EnabledRefRW<ActionRequest>         actionRequestEnabled,
EnabledRefRW<ActionInterruptRequest> actionInterruptRequestEnabled,
EnabledRefRW<MeleeSingleAction>     meleeSingleActionEnabled,

// wrong -- do not use the bare component name
EnabledRefRW<PathRequest>           pathRequest,   // x
EnabledRefRW<ActionRequest>         actionRequest, // x
```

---

## Associated Behaviour Data -- Blob Pointer Pattern

When a new behaviour needs configuration data (stats, thresholds, parameters), do **not** put those values in a raw `IComponentData` struct with inline fields. Instead follow the project's blob-pointer pattern: add an enum type to the relevant enums file, add the data for each type as an entry in a `*LibraryBlob`, and place a small pointer component on the entity that holds only the enum value.

```csharp
// pointer component on the entity -- holds only the type key
public struct ExplosionPointer : IComponentData
{
    public ExplosionType type;  // enum -> indexes into ExplosionLibraryBlob
}

// awareness / action system reads config from the blob via the pointer
ref ExplosionBlob blob = ref explosionLibrary.Value.explosions[(int)pointer.type];
float detectionRange = blob.radius;
float intensity      = blob.intensity;
```

For ephemeral **runtime context** that awareness systems must pass to action systems within the same decision cycle (e.g., the computed blast origin position that may already be gone next frame), a small dedicated context component is appropriate because it holds *instance* data, not *configuration* data:

```csharp
// runtime context written by awareness, read by action system
public struct ExplosionReactionContext : IComponentData
{
    public float3 blastOrigin;  // instance value, not in blob
    public float  blastRadius;  // cached from blob at detection time
}
```

The rule of thumb: if the value is the same for every entity using that type, it belongs in the blob. If it varies per entity per event, it belongs in a context component.

---

## Step 1 -- Declare Enums

**File:** `Assets/_Scripts/Data/Enums/AiEnums.cs`

Add entries to `MotivationType` and/or `ActionType` as needed. Add a `MotivationType` only if no existing one semantically covers the new need. Always add a new `ActionType` for each distinct behaviour variation even if they share an execution system.

```csharp
// MotivationType -- add only if genuinely new need
public enum MotivationType { ..., EnvironmentalSafety }

// ActionType -- add for each distinct variation
public enum ActionType { ..., ExplosionReaction, RainReaction }
```

---

## Step 2 -- Write the Awareness System

**Folder:** `Assets/_Scripts/Systems/AIActionSelectionSystemGroup/UtilityAwarenessSystemGroup/`
**Group:** `[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]`
**Filter:** `WithAll<ActionRequest, AIBrain>()` -- only run for AI units that need a new decision.

The system's only job is to inspect the world and append zero or more `ActionOption` entries to the unit's buffer. It must never touch `CurrentAction`, action tags, or request components.

```csharp
[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
public partial struct EnvironmentalAwarenessSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        // RequireForUpdate any singleton data your system needs
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new EnvironmentalAwarenessJob
        {
            // inject singleton/shared data here
        }.ScheduleParallel(state.Dependency);   // parallel is fine; each entity owns its own buffer
        // assign state.Dependency if the job returns a handle
    }
}

[BurstCompile]
[WithAll(typeof(ActionRequest), typeof(AIBrain))]
partial struct EnvironmentalAwarenessJob : IJobEntity
{
    // [ReadOnly] SharedComponentData, NativeArray lookups, etc.

    void Execute(
        in LocalTransform transform,
        in Personality personality,
        ref DynamicBuffer<ActionOption> options,
        EnabledRefRW<ActionInterruptRequest> actionInterruptRequestEnabled)  // include only if this system can interrupt
    {
        // 1. Detect condition (nearby explosion, weather state, etc.)
        bool conditionMet = /* your detection logic */;
        if (!conditionMet) return;

        // 2. Score the option
        float utilityScore = /* distance fit, severity, personality modifier, etc. */;

        // 3. Append to buffer -- scoring systems multiply this by the motivation curve
        options.Add(new ActionOption
        {
            actionType     = ActionType.ExplosionReaction,
            motivationType = MotivationType.EnvironmentalSafety,
            priority       = 2,          // 0 idle * 1 normal * 2 combat * 3 self-defence
            utilityScore   = utilityScore,
            interaction    = false,
            targetEntity   = Entity.Null
        });

        // 4. Optionally force an immediate interrupt for high-urgency reactions
        //    Only do this when waiting for the next selection cycle is unacceptable.
        actionInterruptRequestEnabled.ValueRW = true;
    }
}
```

**Priority guide:**
- `0` -- idle / ambient (wander, sit)
- `1` -- motivation-driven (interact, socialize, eat)
- `2` -- threat-adjacent (engage enemy, pursue)
- `3` -- survival (self-defence, critical health flee) -- `ActionPrioritySystem` will discard everything below the max tier present

**Personality and motivation modifiers:**
- Read `Personality.bravery` (float, -1 -> +1) to scale fight-vs-flee tendency.
- Read the `Motivation` buffer to find the current value for a `MotivationType` and use it to gate or scale utility (e.g., only generate "eat" options when hunger motivation is above a threshold).

---

## Step 3 -- Register in SelectionFunctions

**File:** `Assets/_Scripts/Systems/ActionSystemGroup/ActionSelectionSystemGroup/SelectionFunctions.cs`

This is **always required** for every new `ActionType`. `ActionSelectionSystem` calls the registered function pointer to enable the correct action tag after selecting an option.

If the new `ActionType` reuses an existing action tag (no new system needed), simply point it at the same enabling logic:

```csharp
// Inside the function pointer table initialisation:
functions[(int)ActionType.ExplosionReaction] = BurstCompiler.CompileFunctionPointer<SelectionDelegate>(EnableEnvironmentalAction);
functions[(int)ActionType.RainReaction]      = BurstCompiler.CompileFunctionPointer<SelectionDelegate>(EnableEnvironmentalAction);

// Shared enablement function (or reuse an existing one if the tag is the same)
[BurstCompile]
static void EnableEnvironmentalAction(ref ComponentLookup<EnvironmentalAction> lookup, Entity entity)
{
    lookup.SetComponentEnabled(entity, true);
}
```

If the new `ActionType` gets its own action tag, write a new enablement function that enables that tag.

---

## Step 4 -- New Action Tag (only if new execution logic)

**File:** `Assets/_Scripts/Components/AI/AiComponents.cs` (add alongside existing action tags)

```csharp
public struct EnvironmentalAction : IComponentData, IEnableableComponent { }
```

Add it to the unit archetype in authoring so every unit that can exhibit this behaviour has the component (disabled by default). The authoring baker calls `AddComponent` and `SetComponentEnabled(entity, false)`.

---

## Step 5 -- New Action Execution System (only if new tag)

**Folder:** `Assets/_Scripts/Systems/ActionSystemGroup/ActionExecutionSystemGroup/`
**Group:** `[UpdateInGroup(typeof(ActionExecutionSystemGroup))]`
**Filter:** `WithAll<EnvironmentalAction>()` -- runs only while the tag is enabled.

Action systems are state machines. The pattern is:

1. **Pathing state** -- write `PathRequest` to move the unit, then wait.
2. **Arrived / in-range state** -- start the primary behaviour (animation, attack, etc.).
3. **Completion** -- disable own action tag, re-enable `ActionRequest` so selection runs again.

```csharp
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct EnvironmentalActionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        new EnvironmentalActionJob { DeltaTime = deltaTime }
            .ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(EnvironmentalAction))]
partial struct EnvironmentalActionJob : IJobEntity
{
    public float DeltaTime;

    void Execute(
        in CurrentAction currentAction,
        in LocalTransform transform,
        ref ActionTimer actionTimer,
        EnabledRefRW<EnvironmentalAction>   environmentalActionEnabled,
        EnabledRefRW<ActionRequest>         actionRequestEnabled,
        EnabledRefRW<PathRequest>           pathRequestEnabled,
        EnabledRefRW<AnimationRequest>      animationRequestEnabled,
        ref PathRequest                     pathData,
        ref ActionTimer                     timer,
        ref DynamicBuffer<SetAnimation>     animations)
    {
        // STATE 1: not yet at destination
        if (!pathRequestEnabled.ValueRO && !timer.isActive)
        {
            pathData = new PathRequest
            {
                targetPosition   = /* cover position or flee direction */,
                requestedMode    = PathMode.DStarLite,
                stoppingDistance = 0.5f
            };
            pathRequestEnabled.ValueRW = true;
            return;
        }

        // STATE 2: arrived -- play reaction animation
        if (!timer.isActive)
        {
            animations.Add(new SetAnimation
            {
                layer     = AnimationLayerType.Action,
                animation = AnimationType.Cower,
                speed     = 1f,
                looping   = false
            });
            animationRequestEnabled.ValueRW = true;
            timer = new ActionTimer { duration = 1.5f, isActive = true };
            return;
        }

        // STATE 3: count down
        timer.remaining -= DeltaTime;
        if (timer.remaining > 0f) return;

        // COMPLETE -- hand control back to selection
        environmentalActionEnabled.ValueRW = false;
        actionRequestEnabled.ValueRW       = true;
    }
}
```

---

## The Interrupt Pattern

Use `ActionInterruptRequest` when a behaviour is urgent enough that waiting for the current action to finish naturally is unacceptable (self-defence, critical health, sudden environmental danger).

Set it enabled in the awareness system:
```csharp
interruptRequest.ValueRW = true;
```

`ActionInterruptSystem` (runs `OrderFirst` in `ActionSelectionSystemGroup`) will:
- Disable the currently active action tag
- Disable `PathRequest` (stops movement)
- Disable `ActionTimer`
- Re-enable `ActionRequest`

The unit then enters normal selection in the same frame, and because the awareness system just wrote high-priority options, those options win.

**Do not** set `ActionInterruptRequest` for behaviours that can tolerate a one-frame delay -- let priority tiers handle ordering instead. Frequent interrupts cause visible jitter.

---

## Downstream Request API

| Request | How to write | What it does |
|---|---|---|
| `PathRequest` | Enable component; set `targetPosition`, `requestedMode` (Stop/DStarLite/FlowField), `stoppingDistance` | `PathRequestSystem` configures the pathfinding agent next frame |
| `AttackRequest` | Enable component; set `targetEntity`, `attackType` | `AttackRequestSystem` fires a hit on the animation hit-frame |
| `AnimationRequest` | Enable component; add `SetAnimation` entries to buffer with `layer`, `animation`, `speed`, `looping` | `AnimationRequestSystem` applies layers and clears the buffer |
| `ActionInterruptRequest` | Enable component (no data needed) | `ActionInterruptSystem` tears down current action and re-enables `ActionRequest` |

Do not read or write these components in awareness systems -- they belong exclusively to action execution systems and their downstream consumers.

---

## Player-Controlled Units (Minions)

Minions have both `AIBrain` and `PlayerControlled`. When `PlayerControlled` is enabled, `ActionRequest` is disabled -- the unit is owned by player input systems in `MinionActionSelectionSystemGroup`. When the player releases control, `PlayerControlled` is disabled and `ActionRequest` is re-enabled, handing the unit back to AI selection.

Minions can still react defensively while player-controlled: `MinionSelfDefenceSystem` (in `MinionActionSelectionSystemGroup`) follows the same awareness -> interrupt pattern for incoming damage, running in parallel with player orders rather than replacing them.

If a new behaviour should apply to minions *under player control*, add it to `MinionActionSelectionSystemGroup`. If it should apply only when the unit is AI-driven, the standard `WithAll<AIBrain, ActionRequest>()` filter already handles it -- `ActionRequest` being disabled on player-controlled units means the awareness job skips them automatically.

---

## Checklist for a New Behaviour

- [ ] `AiEnums.cs` -- new `MotivationType` if needed, new `ActionType` always
- [ ] `UtilityAwarenessSystemGroup/` -- new awareness system appending `ActionOption` entries
- [ ] `SelectionFunctions.cs` -- register function pointer for the new `ActionType`
- [ ] `AiComponents.cs` -- new `IEnableableComponent` tag (only if new execution logic)
- [ ] Authoring baker -- `AddComponent` + `SetComponentEnabled(false)` for the new tag
- [ ] `ActionExecutionSystemGroup/` -- new action system (only if new tag)
- [ ] Set `ActionInterruptRequest` in awareness system if behaviour is high-urgency
