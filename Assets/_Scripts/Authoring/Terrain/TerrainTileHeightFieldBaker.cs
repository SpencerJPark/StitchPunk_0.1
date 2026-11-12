using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Bakes off TerrainTile itself (which lives in the MonoBehaviours assembly, so the baker cannot be nested):
// every tile already carries one, so no tile scene needs a new component.
[BakingVersion("stitchpunk", 1)]
public class TerrainTileHeightFieldBaker : Baker<TerrainTile>
{
    public override void Bake(TerrainTile authoring)
    {
        TerrainTileData tileData = authoring.tileData;
        DependsOn(tileData);
        if (tileData == null) return;

        int samplesPerSide = TerrainTileData.HeightSamplesPerSide;
        BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref TerrainTileHeightBlob root = ref builder.ConstructRoot<TerrainTileHeightBlob>();
        root.samplesPerSide = samplesPerSide;
        root.sampleSpacingMetres = TerrainTileData.TileSizeMetres / (samplesPerSide - 1);
        BlobBuilderArray<float> groundHeights = builder.Allocate(ref root.groundHeights, TerrainTileData.HeightSampleCount);
        for (int sampleZ = 0; sampleZ < samplesPerSide; sampleZ++)
        {
            for (int sampleX = 0; sampleX < samplesPerSide; sampleX++)
            {
                groundHeights[sampleZ * samplesPerSide + sampleX] =
                    tileData.HeightAt(sampleX, sampleZ) + TerrainTileData.GroundTopHeightMetres;
            }
        }

        BlobAssetReference<TerrainTileHeightBlob> blob = builder.CreateBlobAssetReference<TerrainTileHeightBlob>(Allocator.Persistent);
        builder.Dispose();

        AddBlobAsset(ref blob, out Unity.Entities.Hash128 blobHash);
        AddComponent(GetEntity(TransformUsageFlags.None), new TerrainTileHeightField
        {
            blob = blob,
            coordinate = new int2(tileData.coordinate.x, tileData.coordinate.y)
        });
    }
}
