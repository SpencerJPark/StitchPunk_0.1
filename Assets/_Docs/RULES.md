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

- This is **Unity DOTS (ECS + Jobs + Burst)**. All game logic lives in Systems, not MonoBehaviours.
- Prefer `ISystem` (struct) over `SystemBase` (class) — it is Burst-compatible and has lower overhead.
- Always annotate `ISystem` structs with `[BurstCompile]`.
- **Never allocate managed memory inside a Burst job** — no `new List<>`, no `string`, no boxing.
- Use `NativeArray`, `NativeList`, `NativeHashMap`, etc. and dispose them properly (use `[DeallocateOnJobCompletion]` or manual `Dispose()` in `OnDestroy`).
- Never write logic inside `IComponentData` structs — components are pure data.

## ScriptableObject → BlobAsset Pattern

All SO data used at runtime must be baked into a BlobAsset in `PostBakingSystemGroup`. This makes the data Burst-accessible and avoids managed references in jobs.

Pattern:
1. Define the SO with a `Get(EnumType)` lookup method.
2. Create a matching BlobAsset struct in `Data/Structs/`.
3. Write a baking system in `Systems/PostBakingSystemGroup/` that reads the SO and writes the blob.
4. Systems access the blob via a singleton entity component, never the SO directly.

## General

- No `#region` blocks — organise with clear method/field ordering instead.
- Keep Authoring, Component, and System files strictly separated — no logic in Authoring or Components.
- `Core/Unused/` exists for legacy code. Move deprecated files there rather than deleting, but do not reference them from active code.
