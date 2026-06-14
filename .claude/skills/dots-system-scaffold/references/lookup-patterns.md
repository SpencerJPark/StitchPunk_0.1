# ComponentLookup / BufferLookup — Attribute Rules

The single place this codebase most often gets wrong.

## The attribute decision table (from `RULES.md`)

| Intent | Attribute |
|---|---|
| Read-only access to a lookup or native container | `[ReadOnly]` — **imported from `Unity.Collections`**, not `Unity.Entities` |
| Parallel job writes to lookup, each worker touches a DIFFERENT entity (e.g. one brain per body) | `[NativeDisableParallelForRestriction]` |
| Single-threaded job (`.Schedule()` not `.ScheduleParallel()`) writes to lookup | No attribute needed — single-threaded writes are always safe |

**Gotcha:** forgetting `using Unity.Collections;` compiles fine in some editor versions but breaks Burst silently. Always add the `using` whenever `[ReadOnly]` appears anywhere in the file.

## Mandatory lifecycle

Every `ComponentLookup<T>` / `BufferLookup<T>` field needs three touches:

1. **Declare** on the system as a field.
2. **Initialise** once in `OnCreate` with `state.GetComponentLookup<T>(isReadOnly: ...)`.
3. **Update every frame** in `OnUpdate` with `lookup.Update(ref state)` **before** scheduling the job that uses it.

Skipping step 3 is the #1 silent bug — you get stale data and it looks right in the inspector.

## Parallel vs single-threaded writes

If two workers could ever touch the same target entity, you cannot use `ScheduleParallel` with `[NativeDisableParallelForRestriction]`. Use `.Schedule()` instead. Example: a job that iterates bodies and writes to whichever brain they're linked to is safe (unique pairing). A job that iterates attacks and writes to whichever victim they target is NOT safe (two attacks can hit the same victim same frame).

## Reading from the iterated entity — use `in`/`ref`, NOT a lookup

If you can access the component through the job's `Execute(...)` parameters, do that — lookups are only for cross-entity work.

Wrong:
```csharp
public ComponentLookup<Health> healthLookup;
public void Execute(Entity entity, ref Hurt hurt)
{
    Health health = healthLookup[entity];   // ← stop, just add ref Health to Execute
}
```

Right:
```csharp
public void Execute(ref Health health, ref Hurt hurt)
{
    // ...
}
```

## Canonical shapes

### Shape 1 — body touches its linked brain (unique pairing → parallel-safe)

```csharp
[NativeDisableParallelForRestriction] public ComponentLookup<NeedsAction> needsActionLookup;
// in Execute:
Entity brain = brainLink.brain;
if (needsActionLookup.HasComponent(brain))
    needsActionLookup.SetComponentEnabled(brain, false);
```

### Shape 2 — job reads a config on an unrelated entity (read-only → parallel-safe)

```csharp
[ReadOnly] public ComponentLookup<GameSettings> gameSettingsLookup;
// in Execute:
GameSettings settings = gameSettingsLookup[settingsEntity];
```

### Shape 3 — multiple writers may target the same victim (not safe → single-threaded)

```csharp
public ComponentLookup<Health> healthLookup;   // no attribute — Schedule() only
// schedule with: new ApplyHurtJob{}.Schedule(state.Dependency);
```

## Singleton components

If there's exactly one entity with the component, use `SystemAPI.GetSingleton<T>()` or `SystemAPI.GetSingletonRW<T>()` — don't declare a lookup.

Exception: `UnitPrefabEntry` exists on BOTH the library entity AND on every baked prefab, so `GetSingleton<UnitPrefabEntry>()` throws. Use:
```csharp
Entity libraryEntity = SystemAPI.GetSingletonEntity<UnitDataLibrary>();
UnitPrefabEntry prefabs = SystemAPI.GetComponent<UnitPrefabEntry>(libraryEntity);
```
See `Gotchas.md` for the full list of "looks like a singleton but isn't" cases.
