---
tags: [memory, code, rules]
related: "[[Authoring]], [[Systems]], [[Components]]"
---

# _Scripts — Hard Technical Rules

These rules apply to **every file** in `_Scripts/`. No exceptions.

---

## Naming

- **Never use `var`** — always declare the explicit type. Readability over brevity.
- **Never use single-character names** — this includes loop variables, `in` parameters, query components, anything. Name it for what it *is*.
  - Bad: `foreach (var e in query)` / `in Health c`
  - Good: `foreach (Entity unitEntity in query)` / `in Health unitHealth`
- Names should read like documentation. If you have to add a comment to explain a variable, the variable name is wrong.

## DOTS / ECS Patterns

- This is **Unity DOTS (ECS + Jobs + Burst)**. All game logic lives in [[Systems]], not MonoBehaviours.
- Prefer `ISystem` (struct) over `SystemBase` (class) — it is Burst-compatible and has lower overhead.
- Always annotate `ISystem` structs with `[BurstCompile]`.
- **Never call `.Run()` on a job** — always use `.Schedule()` (single-threaded worker) or `.ScheduleParallel()`. `.Run()` blocks the main thread and bypasses the job system entirely.
- **Never allocate managed memory inside a Burst job** — no `new List<>`, no `string`, no boxing.
- Use `NativeArray`, `NativeList`, `NativeHashMap`, etc. and dispose them properly (use `[DeallocateOnJobCompletion]` or manual `Dispose()` in `OnDestroy`).
- Never write logic inside `IComponentData` structs — see [[Components]] for conventions.

## ScriptableObject → BlobAsset Pattern

All SO data used at runtime must be baked into a BlobAsset in `PostBakingSystemGroup`. This makes the data Burst-accessible and avoids managed references in jobs. See [[Authoring]] for the baker pattern and [[Data]] for the full SO → Blob pipeline.

Pattern:
1. Define the SO with a `Get(EnumType)` lookup method.
2. Create a matching BlobAsset struct in `Data/Structs/`.
3. Write a baking system in `Systems/PostBakingSystemGroup/` that reads the SO and writes the blob.
4. Systems access the blob via a singleton entity component, never the SO directly.

## Job Field Attributes — `[ReadOnly]` and `[NativeDisableParallelForRestriction]`

`[ReadOnly]` on a job struct field comes from `Unity.Collections`, **not** `Unity.Entities`. Always add `using Unity.Collections;` whenever a file uses `[ReadOnly]`, even if nothing else from that namespace is needed. Missing this import compiles silently in some editor versions but breaks Burst.

Rules for annotating `ComponentLookup`, `BufferLookup`, and `NativeContainer` fields on job structs:

| Intent | Attribute |
|---|---|
| Read-only access to a lookup or container | `[ReadOnly]` — requires `using Unity.Collections;` |
| Write access from a parallel job to a lookup where each worker writes to a **different** entity (e.g. each body touches its own unique brain) | `[NativeDisableParallelForRestriction]` |
| Write access from a `.Schedule()` (single-threaded) job | No attribute needed — single-threaded writes are always safe |

Never omit `[ReadOnly]` on a lookup that isn't written to. Burst uses it to validate that parallel jobs don't race, and Unity's safety system uses it to detect scheduling conflicts at edit time.

---

## System placement & structure (enforced by SystemPlacementConformanceTests)

- **Every system declares `[UpdateInGroup]`.** A forgotten attribute silently auto-creates the system at an arbitrary point in `SimulationSystemGroup`.
- **A system file lives in the folder named after the group it updates in.** The folder tree under `Systems/` IS the group tree. Exemptions require an entry + reason in `SystemPlacementConformanceTests.PlacementExemptions`.
- **Every `ComponentSystemGroup` is declared in `SystemGroups.cs`** — the single ordering manifest. Never declare a group inline next to a system.
- **Scene gating is group-level.** Top-level feature groups derive from `GameSceneSystemGroup` (gates on `GameSceneTag` once). New systems declare only their DATA requirements — do not add per-system `RequireForUpdate<GameSceneTag>`.
- **Commented-out systems never stay in `Systems/`** — park dead code in `Core/Unused/` (or delete; git remembers).
- **Cross-feature communication goes through the contract components** indexed in [[Contracts]] (requests/events + the DamageBus). A behavior command or system that needs 3+ foreign-domain lookups should become a request handled by that domain instead.

## General

- No `#region` blocks — organise with clear method/field ordering instead.
- Keep [[Authoring]], [[Components]], and [[Systems]] files strictly separated — no logic in Authoring or Components.
- `Core/Unused/` exists for legacy code. Move deprecated files there rather than deleting, but do not reference them from active code. See [[Core]] for legacy file status.
