---
name: dots-blob-library
description: Scaffold the full SO → BlobAsset library pipeline in Stitch Punk — FooSO + FooLibrarySO + FooLibraryBlob + FooLibrary/FooLibraryReference components + FooLibraryAuthoring + FooLibraryBakingSystem in PostBakingSystemGroup. Use this whenever the user says anything like "make a new library", "bake a list of ScriptableObjects into a blob", "expose X data to systems at runtime via blob", "add a FooLibrary", "new blob asset", or describes data that lives in a bundle of SOs and needs to be readable from Burst jobs. The pipeline spans five files and is the most repetitive, bug-prone pattern in the codebase — BlobBuilder off-by-ones, forgetting PostBakingSystemGroup, skipping the IsCreated dispose guard, or putting the system in the wrong SystemGroup all fail silently. Follow this skill any time a new domain needs a "library" of SOs to feed ECS data.
---

# dots-blob-library

Scaffolds the Stitch Punk SO → BlobAsset library pipeline. A *library* is how static/authoring data (brains, factory recipes, animations, unit prefabs, attack stats) gets baked from a bundle of `ScriptableObject`s into a single `BlobAssetReference<T>` that every system can read cheaply from Burst.

This pipeline spans **five files across four folders**. Getting any one wrong (skipping `PostBakingSystemGroup`, forgetting to dispose on reload, reading past a `BlobArray` length) is a silent failure at runtime. Read this skill every time the user wants a new library — don't rely on the subagent recognising the pattern from memory.

## When to use

Trigger on any phrasing along the lines of:
- "make a new library" / "new Foo library" / "FooLibrary"
- "bake a list of `FooSO` into a blob"
- "expose these SOs to ECS / to Burst / to systems"
- "new BlobAsset for X"
- "turn these configs into a blob the jobs can read"

Also trigger when the user names a domain that already has a library and they want to add a *new* one — if the codebase has `BrainLibrary`, `FactoryLibrary`, `AttackLibrary`, `AnimationLibrary`, `ScoringLibrary`, `UnitDataLibrary`, it's the same pattern.

**Don't** trigger when the user wants a single SO baked into a component via `UnityObjectRef<T>` — that's just an authoring baker, not a library. This skill is for collections that need `BlobArray<T>` on the runtime side.

## Canonical references in the project

Read these side-by-side when scaffolding a new library:

1. `Assets/_Scripts/Data/SOs/FactoryLibrarySO.cs` — the thinnest library SO (just wraps `ProductionRecipeSO[]`).
2. `Assets/_Scripts/Data/Structs/FactoryBlobs.cs` — the entry blob + root blob + index helper.
3. `Assets/_Scripts/Components/EntityLibraries/EntityLibraries.cs` — shared file for all library components (`FooLibrary` + `FooLibraryReference`). Append new pairs here; don't create a separate component file per library.
4. `Assets/_Scripts/Authoring/EntityLibraries/FactoryLibraryAuthoring.cs` — 20-line authoring.
5. `Assets/_Scripts/Systems/PostBakingSystemGroup/FactoryLibraryBakingSystem.cs` — the baking system.

Also: `Assets/_Scripts/Systems/PostBakingSystemGroup/BrainLibraryBakingSystem.cs` for the nested-`BlobArray` case (each entry carries its own inner arrays).

Background docs (single source of truth):
- `Assets/_Vault/Memories/Code/Data.md` — ScriptableObject pattern + BlobAsset baking.
- `Assets/_Vault/Memories/Code/Systems.md` — where `PostBakingSystemGroup` sits in execution order.
- `Assets/_Vault/Memories/Code/RULES.md` — no `var`, no single-letter names, `[ReadOnly]` from `Unity.Collections`.

## Anatomy — the five files

For a new library called `Foo`:

| # | File | Path | What it holds |
|---|------|------|---------------|
| 1 | `FooSO` (per-entry SO) | `Assets/_Scripts/Data/SOs/FooSO.cs` | One entry's data, one asset per `CreateAssetMenu`. Often already exists; extend fields if needed. |
| 2 | `FooLibrarySO` | `Assets/_Scripts/Data/SOs/FooLibrarySO.cs` | Wrapper SO: `public FooSO[] foos;` (or `List<FooSO>`). One asset per scene/game. |
| 3 | Blob structs | `Assets/_Scripts/Data/Structs/FooBlobs.cs` | `FooEntryBlob` + `FooLibraryBlob` (with `BlobArray<FooEntryBlob> entries`). Add `FindIndex(...)` helper when the lookup key is known. |
| 4 | Library components | **Append to** `Assets/_Scripts/Components/EntityLibraries/EntityLibraries.cs` | `public struct FooLibrary : IComponentData { public BlobAssetReference<FooLibraryBlob> blob; }` and `public struct FooLibraryReference : IComponentData { public UnityObjectRef<FooLibrarySO> library; }` |
| 5 | Authoring + Baker | `Assets/_Scripts/Authoring/EntityLibraries/FooLibraryAuthoring.cs` | MonoBehaviour with `public FooLibrarySO library;`, nested `Baker` bakes `FooLibraryReference { library = authoring.library }` and empty `FooLibrary()`. `TransformUsageFlags.None`. |
| 6 | Baking system | `Assets/_Scripts/Systems/PostBakingSystemGroup/FooLibraryBakingSystem.cs` | `ISystem` in `PostBakingSystemGroup` with `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]`. Reads the SO, builds the blob, writes it into every `FooLibrary` holder. |

The authoring only adds the *reference* + an empty library component. It **never** writes the blob itself — that's the baking system's job. This two-step split (authoring → baking system) is what lets the blob be rebuilt when the SO changes during incremental baking.

## Component-pair convention — same file, same shape

Library components live together in `EntityLibraries.cs`:

```csharp
public struct FooLibrary : IComponentData
{
    public BlobAssetReference<FooLibraryBlob> blob;
}

public struct FooLibraryReference : IComponentData
{
    public UnityObjectRef<FooLibrarySO> library;
}
```

Naming note: some older libraries in the file use `library` as the field name on the blob holder (e.g. `AttackLibrary.library`). For **new** libraries, prefer `blob` — the field holds a `BlobAssetReference`, and "library" is already taken by the struct name + the reference's SO field. Stay consistent within a single library.

## The baking system — always this shape

```csharp
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct FooLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FooLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        FooLibrarySO librarySO = null;
        foreach (RefRO<FooLibraryReference> reference in SystemAPI.Query<RefRO<FooLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null || librarySO.foos == null || librarySO.foos.Length == 0) return;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref FooLibraryBlob root = ref builder.ConstructRoot<FooLibraryBlob>();
        BlobBuilderArray<FooEntryBlob> entriesBuilder =
            builder.Allocate(ref root.entries, librarySO.foos.Length);

        for (int entryIndex = 0; entryIndex < librarySO.foos.Length; entryIndex++)
        {
            FooSO fooSO = librarySO.foos[entryIndex];
            if (fooSO == null) continue;

            entriesBuilder[entryIndex].someValue = fooSO.someValue;
            // ... copy remaining scalar fields ...
        }

        BlobAssetReference<FooLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<FooLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<FooLibrary> holder in SystemAPI.Query<RefRW<FooLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();

            holder.ValueRW.blob = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<FooLibrary> holder in SystemAPI.Query<RefRW<FooLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();
        }
    }
}
```

Six things that must match this shape exactly:

1. **`[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]`** — without this the system runs in the player world, not during bake, and the blob is never built.
2. **`[UpdateInGroup(typeof(PostBakingSystemGroup))]`** — must run after the authoring baker has created the `FooLibraryReference` entity.
3. **`Allocator.Temp` for the `BlobBuilder`, `Allocator.Persistent` for `CreateBlobAssetReference`.** The builder is scratch memory; the reference lives for the life of the subscene.
4. **Dispose-if-`IsCreated` before reassigning.** Incremental baking re-runs the system on SO edits; skipping this leaks blobs.
5. **OnDestroy also disposes every holder's blob.** Subscene unload runs OnDestroy; a missed dispose here is a shutdown leak.
6. **`using BlobBuilder builder = ...`** — lowercase `using` pattern gives deterministic cleanup. Don't omit the `using`.

## Nested `BlobArray<T>` inside entries

When an entry carries its own inner arrays (e.g., `BrainEntryBlob` has `BlobArray<BehaviourType> behaviours`), allocate the inner arrays off the same `BlobBuilder` **with `ref entriesBuilder[i].innerField`**:

```csharp
int behaviourCount = fooSO.behaviours != null ? fooSO.behaviours.Length : 0;
BlobBuilderArray<BehaviourType> behavioursBuilder =
    builder.Allocate(ref entriesBuilder[entryIndex].behaviours, behaviourCount);
for (int b = 0; b < behaviourCount; b++)
    behavioursBuilder[b] = fooSO.behaviours[b];
```

The `ref` target is `entriesBuilder[i].field` (the outer builder's handle into the entry), **not** `root.entries[i].field` — that would be a read, not a writable blob-builder handle. See `BrainLibraryBakingSystem.cs` for three nested arrays in one entry.

## The authoring — deliberately tiny

```csharp
using Unity.Entities;
using UnityEngine;

public class FooLibraryAuthoring : MonoBehaviour
{
    [Header("Library")]
    [Tooltip("ScriptableObject holding every FooSO that should be baked into the FooLibrary blob.")]
    public FooLibrarySO library;

    public class Baker : Baker<FooLibraryAuthoring>
    {
        public override void Bake(FooLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new FooLibraryReference { library = authoring.library });
            AddComponent(entity, new FooLibrary());
        }
    }
}
```

The authoring **must not** call `DependsOn` here and **must not** build the blob. The baking system handles both. `TransformUsageFlags.None` because the library is pure data.

## Common mistakes — check before finishing

- [ ] Baking system has **both** `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` and `[UpdateInGroup(typeof(PostBakingSystemGroup))]`. Missing either breaks baking silently.
- [ ] `state.RequireForUpdate<FooLibraryReference>()` in `OnCreate`.
- [ ] Authoring uses `TransformUsageFlags.None` (library is data-only).
- [ ] Authoring adds **both** `FooLibraryReference` and an empty `FooLibrary()` — systems that query `FooLibrary` need the component present even before the blob is assigned.
- [ ] `BlobBuilder` uses `Allocator.Temp` with `using`.
- [ ] `CreateBlobAssetReference` uses `Allocator.Persistent`.
- [ ] Before assigning a new blob, the old one is disposed via `if (holder.ValueRO.blob.IsCreated) holder.ValueRW.blob.Dispose();`.
- [ ] `OnDestroy` also loops the holders and disposes — shutdown leak guard.
- [ ] Null guards: outer SO null → early return; each `fooSO == null` → `continue`.
- [ ] Inner nested `BlobArray<T>` allocated via `builder.Allocate(ref entriesBuilder[i].field, ...)` (not `root.entries[i]`).
- [ ] Library components appended to existing `EntityLibraries.cs` — no new per-library component file.
- [ ] No `var`. Explicit types throughout.
- [ ] No single-letter semantic names. `entryIndex`, `behavioursBuilder`, not `i`, `b`. (Short loop counters like `i`, `j` are used in some existing blob systems but new code should prefer descriptive names per RULES.md.)
- [ ] `[ReadOnly]` attributes (if any) imported from `Unity.Collections`.

## Deeper references

- `references/canonical-blob-library.md` — full walked-through example of all six files in one page, copy-ready.
- `references/nested-blob-arrays.md` — the one-file deep-dive on inner `BlobArray<T>` allocation (most common bug source).
