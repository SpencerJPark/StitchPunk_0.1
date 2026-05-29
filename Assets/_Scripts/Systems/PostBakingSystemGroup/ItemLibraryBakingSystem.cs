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

        Dictionary<int, ItemSO> soByIndex = new Dictionary<int, ItemSO>();
        foreach (ItemSO so in librarySO.items)
        {
            if (so == null) continue;
            soByIndex[(int)so.itemType] = so;
        }

        int typeCount = System.Enum.GetValues(typeof(ItemType)).Length;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref ItemLibraryBlob root = ref builder.ConstructRoot<ItemLibraryBlob>();
        BlobBuilderArray<ItemBlob> itemsBuilder = builder.Allocate(ref root.items, typeCount);

        for (int i = 0; i < typeCount; i++)
        {
            if (soByIndex.TryGetValue(i, out ItemSO so))
            {
                itemsBuilder[i].itemType          = so.itemType;
                itemsBuilder[i].category          = so.category;
                itemsBuilder[i].weaponAttack      = so.weaponAttack;
                itemsBuilder[i].onHitEffect       = so.onHitEffect;
                itemsBuilder[i].consumeEffect     = so.consumeEffect;
                itemsBuilder[i].pickupRange       = so.pickupRange;
                itemsBuilder[i].consumeDuration   = so.consumeDuration;
                itemsBuilder[i].baseUtility       = so.baseUtility;
                itemsBuilder[i].throwSpeed        = so.throwSpeed;
                itemsBuilder[i].throwArc          = so.throwArc;
                itemsBuilder[i].throwDamage       = so.throwDamage;
                itemsBuilder[i].throwRagdollForce = so.throwRagdollForce;
                itemsBuilder[i].throwLaunchForceY = so.throwLaunchForceY;
                itemsBuilder[i].throwLaunchForceX = so.throwLaunchForceX;
            }
            else
            {
                itemsBuilder[i].itemType          = (ItemType)i;
                itemsBuilder[i].category          = ItemCategory.None;
                itemsBuilder[i].weaponAttack      = AttackType.None;
                itemsBuilder[i].onHitEffect       = EffectType.None;
                itemsBuilder[i].consumeEffect     = EffectType.None;
                itemsBuilder[i].pickupRange       = 1.5f;
                itemsBuilder[i].consumeDuration   = 1f;
                itemsBuilder[i].baseUtility       = 0f;
                itemsBuilder[i].throwSpeed        = 10f;
                itemsBuilder[i].throwArc          = 4f;
                itemsBuilder[i].throwDamage       = 10;
                itemsBuilder[i].throwRagdollForce = 1f;
                itemsBuilder[i].throwLaunchForceY = 0f;
                itemsBuilder[i].throwLaunchForceX = 0f;
            }
        }

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
}
