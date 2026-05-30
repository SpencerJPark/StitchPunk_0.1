using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

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

        if (librarySO == null) return;

        Dictionary<int, InteractionSO> soByIndex =
            BlobLibraryUtils.BuildEnumLookup(librarySO.interactions, so => (int)so.interactionActionType);
        int typeCount = BlobLibraryUtils.EnumCount<ActionType>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref InteractionLibraryBlob root = ref builder.ConstructRoot<InteractionLibraryBlob>();
        BlobBuilderArray<InteractionBlob> interactionsBuilder =
            builder.Allocate(ref root.interactions, typeCount);

        for (int i = 0; i < typeCount; i++)
        {
            if (soByIndex.TryGetValue(i, out InteractionSO so))
            {
                interactionsBuilder[i].actionType          = so.interactionActionType;
                interactionsBuilder[i].priority            = so.priority;
                interactionsBuilder[i].maxOccupants        = so.maxOccupants;
                interactionsBuilder[i].range               = so.range;
                interactionsBuilder[i].satisfiedNeed = so.motivationSatisfaction.needType;
                interactionsBuilder[i].restorationAmount   = so.motivationSatisfaction.value;
                interactionsBuilder[i].duration            = so.duration;

                int factionCount = so.factionsThatCanInteractWith != null
                    ? so.factionsThatCanInteractWith.Length : 0;
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
                interactionsBuilder[i].satisfiedNeed = NeedType.None;
                interactionsBuilder[i].restorationAmount   = 0f;
                interactionsBuilder[i].duration            = 4f;
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

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<InteractionLibrary> holder in SystemAPI.Query<RefRW<InteractionLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }
}
