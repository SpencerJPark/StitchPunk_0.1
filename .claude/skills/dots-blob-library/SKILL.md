---
name: dots-blob-library
description: The five-file SO → BlobAsset library pipeline in Stitch Punk (SO, blob structs, the EntityLibraries.cs component pair, authoring, and a PostBakingSystemGroup baking system). Use for any new FooLibrary or "bake these SOs into a blob" request — the baking-system attributes, allocator split, and dispose guards all fail silently when missed.
---

# SO → BlobAsset library

Live exemplar to copy: `Assets/_Scripts/Systems/PostBakingSystemGroup/EffectLibraryBakingSystem.cs`.
Read it before writing — the notes below are only the traps it won't tell you about.

## File manifest

| File | Location |
|---|---|
| `FooSO`, `FooLibrarySO` | `_Scripts/Data/` |
| `FooBlob`, `FooLibraryBlob` | `_Scripts/Data/` (blob structs, blittable only) |
| `FooLibrary` + `FooLibraryReference` | **append to `_Scripts/Components/EntityLibraries/EntityLibraries.cs`** — never a new per-library file |
| `FooLibraryAuthoring` | `_Scripts/Authoring/EntityLibraries/` |
| `FooLibraryBakingSystem` | `_Scripts/Systems/PostBakingSystemGroup/` |

## The traps

- **Both attributes or nothing bakes:** `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` *and*
  `[UpdateInGroup(typeof(PostBakingSystemGroup))]`. Missing either = no blob, no error.
- **Allocator split:** `new BlobBuilder(Allocator.Temp)` for the builder,
  `CreateBlobAssetReference(Allocator.Persistent)` for the result.
- **Dispose guard in two places:** `if (holder.ValueRO.library.IsCreated) holder.ValueRW.library.Dispose();`
  before reassigning in `OnUpdate`, *and* again in `OnDestroy`. Skipping it leaks across domain reloads.
- **Enum-index the array, don't length-match it.** Size with
  `BlobLibraryUtils.EnumCount<FooType>()`, map with `BlobLibraryUtils.BuildEnumLookup(...)`, and fill
  every unmatched slot in the `else` branch so each enum value has a valid entry. Sizing to
  `librarySO.foos.Length` and scanning for a match is the old shape — it breaks on sparse/reordered enums.
  (`BlobLibraryUtils` lives in `_Scripts/Utils/` and also offers `FillWithPreFill` / `FillWithLookup`.)
- **Nested arrays go through the builder handle:**
  `builder.Allocate(ref entriesBuilder[i].field, n)` — never `ref root.entries[i].field`.
  Allocate a zero-length array in the `else` branch too; an unallocated `BlobArray` throws on read.
- Holder field is named `library` (matches every existing library).
