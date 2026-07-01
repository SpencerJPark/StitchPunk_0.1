using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct ItemLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        ItemLibrarySO librarySO = null;
        foreach (RefRO<ItemLibraryReference> reference in SystemAPI.Query<RefRO<ItemLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        Dictionary<int, ItemSO> soByIndex =
            BlobLibraryUtils.BuildEnumLookup(librarySO.items, so => (int)so.itemType);
        int typeCount = BlobLibraryUtils.EnumCount<ItemType>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref ItemLibraryBlob root = ref builder.ConstructRoot<ItemLibraryBlob>();
        BlobBuilderArray<ItemBlob> itemsBuilder = builder.Allocate(ref root.items, typeCount);

        BlobLibraryUtils.FillWithLookup(itemsBuilder, typeCount, soByIndex, DefaultItem, MapItem);

        BlobAssetReference<ItemLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<ItemLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<ItemLibrary> holder in SystemAPI.Query<RefRW<ItemLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<ItemLibrary> holder in SystemAPI.Query<RefRW<ItemLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }

    static ItemBlob DefaultItem(int i) => new ItemBlob
    {
        itemType          = (ItemType)i,
        category          = ItemCategory.None,
        weaponAttack      = DamageSource.None,
        onHitEffect       = EffectType.None,
        consumeEffect     = EffectType.None,
        pickupRange       = 1.5f,
        consumeDuration   = 1f,
        baseUtility       = 0f,
        throwSpeed        = 10f,
        throwArc          = 4f,
        throwDamage       = 10,
        throwRagdollForce = 1f,
        throwLaunchForceY = 0f,
        throwLaunchForceX = 0f,
    };

    static ItemBlob MapItem(ItemSO so) => new ItemBlob
    {
        itemType          = so.itemType,
        category          = so.category,
        weaponAttack      = so.weaponAttack,
        onHitEffect       = so.onHitEffect,
        consumeEffect     = so.consumeEffect,
        pickupRange       = so.pickupRange,
        consumeDuration   = so.consumeDuration,
        baseUtility       = so.baseUtility,
        throwSpeed        = so.throwSpeed,
        throwArc          = so.throwArc,
        throwDamage       = so.throwDamage,
        throwRagdollForce = so.throwRagdollForce,
        throwLaunchForceY = so.throwLaunchForceY,
        throwLaunchForceX = so.throwLaunchForceX,
    };
}
