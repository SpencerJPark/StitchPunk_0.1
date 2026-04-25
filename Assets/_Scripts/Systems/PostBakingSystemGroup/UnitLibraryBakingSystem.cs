using Unity.Entities;
using Unity.Collections;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct UnitLibraryBakingSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (libraryRef, entity) in 
            SystemAPI.Query<RefRO<UnitDataLibraryReference>>()
            .WithNone<UnitDataLibrary>()
            .WithEntityAccess())
        {
            UnitLibrarySO librarySO = libraryRef.ValueRO.library.Value;
            if (librarySO == null || librarySO.units == null) continue;

            BlobAssetReference<UnitLibraryBlob> blobRef = CreateUnitLibraryBlob(librarySO);

            ecb.AddComponent(entity, new UnitDataLibrary
            {
                library = blobRef
            });
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (var holder in SystemAPI.Query<RefRW<UnitDataLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }

    private BlobAssetReference<UnitLibraryBlob> CreateUnitLibraryBlob(UnitLibrarySO librarySO)
    {
        BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref UnitLibraryBlob root = ref builder.ConstructRoot<UnitLibraryBlob>();

        BlobBuilderArray<UnitDataBlob> unitsArray = builder.Allocate(ref root.units, librarySO.units.Count);

        for (int i = 0; i < librarySO.units.Count; i++)
        {
            UnitSO unitSO = librarySO.units[i];
            
            unitsArray[i].unitType = unitSO.unitType;
            unitsArray[i].idleAnimation = unitSO.idleAnimation;
            unitsArray[i].movingAnimation = unitSO.movingAnimation;

            int mappingCount = unitSO.actionAnimations?.Length ?? 0;
            BlobBuilderArray<ActionAnimationMappingBlob> mappingsArray =
                builder.Allocate(ref unitsArray[i].actionAnimations, mappingCount);

            for (int j = 0; j < mappingCount; j++)
            {
                mappingsArray[j].action    = unitSO.actionAnimations[j].action;
                mappingsArray[j].animation = unitSO.actionAnimations[j].animation;
            }

            int stanceCount = unitSO.stanceAnimations?.Length ?? 0;
            BlobBuilderArray<StanceAnimationBlob> stanceArray =
                builder.Allocate(ref unitsArray[i].stanceAnimations, stanceCount);

            for (int j = 0; j < stanceCount; j++)
            {
                stanceArray[j].stance         = unitSO.stanceAnimations[j].stance;
                stanceArray[j].idleAnimation   = unitSO.stanceAnimations[j].idleAnimation;
                stanceArray[j].movingAnimation = unitSO.stanceAnimations[j].movingAnimation;
            }
        }

        BlobAssetReference<UnitLibraryBlob> blobRef = builder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
        builder.Dispose();

        return blobRef;
    }
}