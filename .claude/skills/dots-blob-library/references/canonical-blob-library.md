# Canonical Blob Library — Full Walkthrough

A complete, copy-ready example of every file in a new Stitch Punk blob library. Built around a hypothetical `DialogueLibrary` that bakes a list of `DialogueSequenceSO` into a `BlobAssetReference<DialogueLibraryBlob>`.

The six files below mirror, one-for-one, the real `FactoryLibrary` stack. If you're scaffolding a new library, copy this structure and rename.

---

## 1. `Assets/_Scripts/Data/SOs/DialogueSequenceSO.cs` — per-entry SO (already exists for most domains)

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "DialogueSequence_", menuName = "Dialogue/Sequence")]
public class DialogueSequenceSO : ScriptableObject
{
    public string sequenceId;
    public float  autoAdvanceSeconds;
    // Whatever other fields the entry needs. Keep this blittable-friendly
    // if the values are going into the blob directly.
}
```

---

## 2. `Assets/_Scripts/Data/SOs/DialogueLibrarySO.cs` — the wrapper SO

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "_DialogueLibrary", menuName = "Dialogue/DialogueLibrary")]
public class DialogueLibrarySO : ScriptableObject
{
    public DialogueSequenceSO[] sequences;
}
```

Convention: file name starts with `_` to sort above the individual SOs in the project window (matches `_FactoryLibrary`, `_BrainLibrary`).

---

## 3. `Assets/_Scripts/Data/Structs/DialogueBlobs.cs` — entry + root blob

```csharp
using Unity.Entities;
using Unity.Collections;

public struct DialogueEntryBlob
{
    public FixedString64Bytes sequenceId;
    public float              autoAdvanceSeconds;
}

public struct DialogueLibraryBlob
{
    public BlobArray<DialogueEntryBlob> entries;

    public int FindIndexById(in FixedString64Bytes sequenceId)
    {
        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            if (entries[entryIndex].sequenceId.Equals(sequenceId))
                return entryIndex;
        }
        return -1;
    }
}
```

Notes:
- Strings go as `FixedString*Bytes` (from `Unity.Collections`). `System.String` is not blittable into a blob.
- Add a `FindIndex` helper if callers will look up by a key — keeps the lookup in one place.

---

## 4. `Assets/_Scripts/Components/EntityLibraries/EntityLibraries.cs` — append to the existing file

```csharp
public struct DialogueLibrary : IComponentData
{
    public BlobAssetReference<DialogueLibraryBlob> blob;
}

public struct DialogueLibraryReference : IComponentData
{
    public UnityObjectRef<DialogueLibrarySO> library;
}
```

Don't create `DialogueLibraryComponents.cs`. These two structs live with every other library's pair in `EntityLibraries.cs`.

---

## 5. `Assets/_Scripts/Authoring/EntityLibraries/DialogueLibraryAuthoring.cs`

```csharp
using Unity.Entities;
using UnityEngine;

public class DialogueLibraryAuthoring : MonoBehaviour
{
    [Header("Library")]
    [Tooltip("ScriptableObject holding every DialogueSequenceSO that should be baked into the DialogueLibrary blob.")]
    public DialogueLibrarySO library;

    public class Baker : Baker<DialogueLibraryAuthoring>
    {
        public override void Bake(DialogueLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new DialogueLibraryReference { library = authoring.library });
            AddComponent(entity, new DialogueLibrary());
        }
    }
}
```

Why no `DependsOn(authoring.library)` here? The authoring only stores a reference; it doesn't read fields off the SO. The baking system reads the SO at bake time (when the reference component is seen in a BakingSystem query) and Unity's incremental baker already re-runs `PostBakingSystemGroup` when the SO changes.

---

## 6. `Assets/_Scripts/Systems/PostBakingSystemGroup/DialogueLibraryBakingSystem.cs`

```csharp
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct DialogueLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DialogueLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        DialogueLibrarySO librarySO = null;
        foreach (RefRO<DialogueLibraryReference> reference in SystemAPI.Query<RefRO<DialogueLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null || librarySO.sequences == null || librarySO.sequences.Length == 0) return;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref DialogueLibraryBlob root = ref builder.ConstructRoot<DialogueLibraryBlob>();
        BlobBuilderArray<DialogueEntryBlob> entriesBuilder =
            builder.Allocate(ref root.entries, librarySO.sequences.Length);

        for (int entryIndex = 0; entryIndex < librarySO.sequences.Length; entryIndex++)
        {
            DialogueSequenceSO sequenceSO = librarySO.sequences[entryIndex];
            if (sequenceSO == null) continue;

            entriesBuilder[entryIndex].sequenceId         = sequenceSO.sequenceId;
            entriesBuilder[entryIndex].autoAdvanceSeconds = sequenceSO.autoAdvanceSeconds;
        }

        BlobAssetReference<DialogueLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<DialogueLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<DialogueLibrary> holder in SystemAPI.Query<RefRW<DialogueLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();

            holder.ValueRW.blob = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<DialogueLibrary> holder in SystemAPI.Query<RefRW<DialogueLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();
        }
    }
}
```

---

## Reading the blob at runtime

From any other system:

```csharp
if (!SystemAPI.TryGetSingleton<DialogueLibrary>(out DialogueLibrary dialogueLibrary)) return;
if (!dialogueLibrary.blob.IsCreated) return;

ref DialogueLibraryBlob dialogueBlob = ref dialogueLibrary.blob.Value;
int index = dialogueBlob.FindIndexById(someId);
if (index < 0) return;

ref DialogueEntryBlob entry = ref dialogueBlob.entries[index];
// read entry.autoAdvanceSeconds etc.
```

`ref` is load-bearing — `BlobArray<T>` elements are not copied on access.
