using Unity.Burst;
using Unity.Entities;

public static class ResourceSnapshotMapping
{
    public static int Read(in PlayerResourcesSnapshot snapshot, ResourceType resourceType)
    {
        switch (resourceType)
        {
            case ResourceType.Wood: return snapshot.wood;
            case ResourceType.ScrapMetal: return snapshot.scrapMetal;
            case ResourceType.Corpse: return snapshot.corpse;
            case ResourceType.Electricity: return snapshot.electricity;
            case ResourceType.Currency: return snapshot.currency;
            case ResourceType.RevivalRound: return snapshot.revivalRound;
            default: return 0;
        }
    }

    public static void Write(ref PlayerResourcesSnapshot snapshot, ResourceType resourceType, int amount)
    {
        switch (resourceType)
        {
            case ResourceType.Wood: snapshot.wood = amount; break;
            case ResourceType.ScrapMetal: snapshot.scrapMetal = amount; break;
            case ResourceType.Corpse: snapshot.corpse = amount; break;
            case ResourceType.Electricity: snapshot.electricity = amount; break;
            case ResourceType.Currency: snapshot.currency = amount; break;
            case ResourceType.RevivalRound: snapshot.revivalRound = amount; break;
        }
    }
}

[BurstCompile]
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateBefore(typeof(PersistentSaveSystem))]
public partial struct ResourceSaveSyncSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new CopyStackIntoSnapshotJob().Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Player))]
    private partial struct CopyStackIntoSnapshotJob : IJobEntity
    {
        private void Execute(in DynamicBuffer<ResourceStack> resourceStacks, ref PlayerResourcesSnapshot snapshot)
        {
            for (int rowIndex = 0; rowIndex < resourceStacks.Length; rowIndex++)
            {
                ResourceStack row = resourceStacks[rowIndex];
                ResourceSnapshotMapping.Write(ref snapshot, row.resourceType, row.amount);
            }
        }
    }
}
