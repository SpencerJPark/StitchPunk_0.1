# Nested `BlobArray<T>` — the inner-allocation pattern

When an entry blob carries its own inner arrays, you allocate them off the **same** `BlobBuilder`, but the `ref` target is the field on the *outer* `BlobBuilderArray` element — not on `root`. Getting this wrong is the most common cause of silently empty inner arrays.

## The shape

```csharp
// Outer allocation — entries themselves
BlobBuilderArray<FooEntryBlob> entriesBuilder =
    builder.Allocate(ref root.entries, outerCount);

for (int entryIndex = 0; entryIndex < outerCount; entryIndex++)
{
    FooSO fooSO = librarySO.foos[entryIndex];
    if (fooSO == null) continue;

    // Scalar fields — write directly into the outer builder.
    entriesBuilder[entryIndex].id = fooSO.id;

    // Inner BlobArray — allocate via the OUTER builder, ref into the entry.
    int innerCount = fooSO.tags != null ? fooSO.tags.Length : 0;
    BlobBuilderArray<TagType> tagsBuilder =
        builder.Allocate(ref entriesBuilder[entryIndex].tags, innerCount);

    for (int tagIndex = 0; tagIndex < innerCount; tagIndex++)
        tagsBuilder[tagIndex] = fooSO.tags[tagIndex];
}
```

## Why not `ref root.entries[entryIndex].tags`

`root.entries` is a *read view* into the blob you're building — the `BlobArray` indexer returns by value, and the `ref` you'd get back doesn't point into the builder's writable scratch buffer. The allocation would succeed but be discarded.

`entriesBuilder[entryIndex]` returns a `ref` into the outer builder's scratch memory, which *is* where the builder will place the final blob. That's the only target that works.

## Real example — `BrainLibraryBakingSystem.cs`

Each `BrainEntryBlob` has three inner `BlobArray`s: `behaviours`, `randomBehaviours`, `attackFactions`. They're all allocated inside the per-entry loop:

```csharp
for (int i = 0; i < librarySO.brains.Count; i++)
{
    BrainSO brainSO = librarySO.brains[i];
    if (brainSO == null) continue;

    // ... scalars ...

    int behaviourCount = brainSO.behaviours != null ? brainSO.behaviours.Length : 0;
    BlobBuilderArray<BehaviourType> behavioursBuilder =
        builder.Allocate(ref entriesBuilder[i].behaviours, behaviourCount);
    for (int j = 0; j < behaviourCount; j++)
        behavioursBuilder[j] = brainSO.behaviours[j];

    int randomCount = brainSO.randomBehaviours != null ? brainSO.randomBehaviours.Length : 0;
    BlobBuilderArray<BehaviourType> randomBuilder =
        builder.Allocate(ref entriesBuilder[i].randomBehaviours, randomCount);
    for (int j = 0; j < randomCount; j++)
        randomBuilder[j] = brainSO.randomBehaviours[j];

    int attackCount = brainSO.attackFactions != null ? brainSO.attackFactions.Length : 0;
    BlobBuilderArray<FactionType> attackBuilder =
        builder.Allocate(ref entriesBuilder[i].attackFactions, attackCount);
    for (int j = 0; j < attackCount; j++)
        attackBuilder[j] = brainSO.attackFactions[j];
}
```

Three inner arrays, one pattern, same `builder`. Repeat as needed.

## Null-count guard

`fooSO.inner != null ? fooSO.inner.Length : 0` — `builder.Allocate` will happily take a count of 0, which produces an empty `BlobArray` that reads fine at runtime (`.Length == 0`). Don't skip the allocation for empty inner arrays — the blob's shape must stay consistent across rebuilds, and the outer `BlobArray` has already been sized by your outer allocation.

## Reading inner arrays at runtime

```csharp
ref FooEntryBlob entry = ref library.blob.Value.entries[entryIndex];
for (int tagIndex = 0; tagIndex < entry.tags.Length; tagIndex++)
{
    TagType tag = entry.tags[tagIndex];
    // ...
}
```

Again: `ref` on the entry load. Without it, each `entry.tags.Length` access copies `entry` — bad for performance and, in some call sites, actually wrong semantically (copy-then-index on a copy).
