using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct ScoringLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScoringLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        ConsiderationLibrarySO librarySO = null;
        foreach (var reference in SystemAPI.Query<RefRO<ScoringLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref AIScoringLibraryBlob root = ref builder.ConstructRoot<AIScoringLibraryBlob>();

        int curveCount = librarySO.curves.Count;
        BlobBuilderArray<AIScoringCurveEntryBlob> curvesBuilder = builder.Allocate(ref root.curves, curveCount);

        for (int i = 0; i < curveCount; i++)
        {
            ConsiderationCurveSO curveAsset = librarySO.curves[i];
            if (curveAsset == null) continue;

            ref AIScoringCurveEntryBlob entryBlob = ref curvesBuilder[i];
            entryBlob.needType = curveAsset.needType;

            int res = ConstGameData.SCORING_CURVE_RESOLUTION;
            entryBlob.curve.resolution = res;

            BlobBuilderArray<float> samplesBuilder = builder.Allocate(ref entryBlob.curve.samples, res);

            for (int s = 0; s < res; s++)
            {
                float input = math.lerp(-100f, 100f, (float)s / (res - 1));
                samplesBuilder[s] = curveAsset.curve.Evaluate(input);
            }
        }

        BlobAssetReference<AIScoringLibraryBlob> blobReference =
            builder.CreateBlobAssetReference<AIScoringLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<ScoringLibrary> holder in SystemAPI.Query<RefRW<ScoringLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobReference;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<ScoringLibrary> holder in SystemAPI.Query<RefRW<ScoringLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }
}