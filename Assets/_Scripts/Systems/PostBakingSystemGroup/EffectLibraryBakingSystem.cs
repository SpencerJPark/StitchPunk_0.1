using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct EffectLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EffectLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EffectLibrarySO librarySO = null;
        foreach (RefRO<EffectLibraryReference> reference in SystemAPI.Query<RefRO<EffectLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        Dictionary<int, EffectSO> soByIndex =
            BlobLibraryUtils.BuildEnumLookup(librarySO.effects, so => (int)so.effectType);
        int typeCount = BlobLibraryUtils.EnumCount<EffectType>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref EffectLibraryBlob root = ref builder.ConstructRoot<EffectLibraryBlob>();
        BlobBuilderArray<EffectBlob> effectsBuilder = builder.Allocate(ref root.effects, typeCount);

        for (int i = 0; i < typeCount; i++)
        {
            if (soByIndex.TryGetValue(i, out EffectSO so))
            {
                effectsBuilder[i].effectType = so.effectType;
                effectsBuilder[i].value      = so.value;
                effectsBuilder[i].timer      = so.timer;

                int behaviourCount = so.behaviours != null ? so.behaviours.Length : 0;
                BlobBuilderArray<NeedType> behavioursBuilder =
                    builder.Allocate(ref effectsBuilder[i].behaviours, behaviourCount);
                for (int b = 0; b < behaviourCount; b++)
                    behavioursBuilder[b] = so.behaviours[b];
            }
            else
            {
                effectsBuilder[i].effectType = (EffectType)i;
                effectsBuilder[i].value      = 0f;
                effectsBuilder[i].timer      = 0f;
                builder.Allocate(ref effectsBuilder[i].behaviours, 0);
            }
        }

        BlobAssetReference<EffectLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<EffectLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<EffectLibrary> holder in SystemAPI.Query<RefRW<EffectLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<EffectLibrary> holder in SystemAPI.Query<RefRW<EffectLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }
}
