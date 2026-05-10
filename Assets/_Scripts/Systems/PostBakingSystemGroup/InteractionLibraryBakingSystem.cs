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

        int typeCount = System.Enum.GetValues(typeof(ActionType)).Length;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref InteractionLibraryBlob root = ref builder.ConstructRoot<InteractionLibraryBlob>();
        BlobBuilderArray<InteractionBlob> interactionsBuilder = builder.Allocate(ref root.interactions, typeCount);

        for (int i = 0; i < typeCount; i++)
        {
            interactionsBuilder[i].actionType          = (ActionType)i;
            interactionsBuilder[i].priority            = 0;
            interactionsBuilder[i].maxOccupants        = 1;
            interactionsBuilder[i].range               = 1.5f;
            interactionsBuilder[i].satisfiedMotivation = MotivationType.None;
            interactionsBuilder[i].restorationAmount   = 0f;
            builder.Allocate(ref interactionsBuilder[i].allowedFactions, 0);
        }

        foreach (InteractionSO so in librarySO.interactions)
        {
            if (so == null) continue;

            int index = (int)so.interactionActionType;
            interactionsBuilder[index].actionType          = so.interactionActionType;
            interactionsBuilder[index].priority            = so.priority;
            interactionsBuilder[index].maxOccupants        = so.maxOccupants;
            interactionsBuilder[index].range               = so.range;
            interactionsBuilder[index].satisfiedMotivation = so.motivationSatisfaction.motivationType;
            interactionsBuilder[index].restorationAmount   = so.motivationSatisfaction.value;

            int factionCount = so.factionsThatCanInteractWith != null ? so.factionsThatCanInteractWith.Length : 0;
            BlobBuilderArray<FactionType> factionsBuilder =
                builder.Allocate(ref interactionsBuilder[index].allowedFactions, factionCount);
            for (int f = 0; f < factionCount; f++)
                factionsBuilder[f] = so.factionsThatCanInteractWith[f];
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
