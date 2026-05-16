using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct InteractionLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InteractionLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        InteractionLibrarySO librarySO = null;
        foreach (RefRO<InteractionLibraryReference> reference in SystemAPI.Query<RefRO<InteractionLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null)
        {
            Debug.Log("[InteractionLibraryBaking] librarySO is NULL — baker didn't assign it");
            return;
        }

        int typeCount = System.Enum.GetValues(typeof(ActionType)).Length;

        // Build a lookup first so each index is allocated exactly once.
        // Calling builder.Allocate on the same BlobArray field twice corrupts the blob
        // because the patcher can overwrite adjacent fields (including satisfiedMotivation).
        System.Collections.Generic.Dictionary<int, InteractionSO> soByIndex =
            new System.Collections.Generic.Dictionary<int, InteractionSO>();
        foreach (InteractionSO so in librarySO.interactions)
        {
            if (so != null)
                soByIndex[(int)so.interactionActionType] = so;
        }

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref InteractionLibraryBlob root = ref builder.ConstructRoot<InteractionLibraryBlob>();
        BlobBuilderArray<InteractionBlob> interactionsBuilder = builder.Allocate(ref root.interactions, typeCount);

        for (int i = 0; i < typeCount; i++)
        {
            if (soByIndex.TryGetValue(i, out InteractionSO so))
            {
                interactionsBuilder[i].actionType          = so.interactionActionType;
                interactionsBuilder[i].priority            = so.priority;
                interactionsBuilder[i].maxOccupants        = so.maxOccupants;
                interactionsBuilder[i].range               = so.range;
                interactionsBuilder[i].satisfiedMotivation = so.motivationSatisfaction.motivationType;
                interactionsBuilder[i].restorationAmount   = so.motivationSatisfaction.value;

                int factionCount = so.factionsThatCanInteractWith != null ? so.factionsThatCanInteractWith.Length : 0;
                BlobBuilderArray<FactionType> factionsBuilder =
                    builder.Allocate(ref interactionsBuilder[i].allowedFactions, factionCount);
                for (int f = 0; f < factionCount; f++)
                    factionsBuilder[f] = so.factionsThatCanInteractWith[f];
            }
            else
            {
                interactionsBuilder[i].actionType          = (ActionType)i;
                interactionsBuilder[i].priority            = 0;
                interactionsBuilder[i].maxOccupants        = 1;
                interactionsBuilder[i].range               = 1.5f;
                interactionsBuilder[i].satisfiedMotivation = MotivationType.None;
                interactionsBuilder[i].restorationAmount   = 0f;
                builder.Allocate(ref interactionsBuilder[i].allowedFactions, 0);
            }
        }

        BlobAssetReference<InteractionLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<InteractionLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<InteractionLibrary> holder in SystemAPI.Query<RefRW<InteractionLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobRef;
        }
    }

    // Do NOT dispose here — the blob was transferred to the runtime world.
    // Disposing it in the baking world's OnDestroy would free memory the runtime still uses (use-after-free).
    // The runtime world's InteractionLibrary component owns the blob from this point on.
}
