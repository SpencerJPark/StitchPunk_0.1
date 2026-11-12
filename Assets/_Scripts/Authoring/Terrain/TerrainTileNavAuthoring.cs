using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Holds one terrain tile's baked nav costs; bakes to a TileNavCost blob the tile streaming systems read.
public class TerrainTileNavAuthoring : MonoBehaviour
{
    public byte[] costs;
    public byte[] surface;
    public int cellsPerSide = 100;
    public int layerCount = 1;
    public Vector2Int coordinate;

    // What the bytes above were baked from; the editor re-bakes nav only when either no longer matches the tile.
    public int bakedFromTileDataRevision = -1;
    public int bakedFromBlockerHash;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<TerrainTileNavAuthoring>
    {
        public override void Bake(TerrainTileNavAuthoring authoring)
        {
            int expectedLength = authoring.layerCount * authoring.cellsPerSide * authoring.cellsPerSide;
            bool costsValid = authoring.costs != null && authoring.costs.Length == expectedLength;
            bool surfaceValid = authoring.surface != null && authoring.surface.Length == expectedLength;
            if (!costsValid || !surfaceValid)
            {
                // An unbaked tile is treated as unloaded/wall by the consumers, so skip rather than bake garbage.
                Debug.LogWarning("TerrainTileNavAuthoring: tile " + authoring.coordinate + " has missing or mis-sized nav data (expected " + expectedLength + " cells); not baked.");
                return;
            }

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref TileNavCostBlob root = ref builder.ConstructRoot<TileNavCostBlob>();
            root.cellsPerSide = authoring.cellsPerSide;
            root.layerCount = authoring.layerCount;
            BlobBuilderArray<byte> costArray = builder.Allocate(ref root.costs, expectedLength);
            BlobBuilderArray<byte> surfaceArray = builder.Allocate(ref root.surface, expectedLength);
            for (int cellIndex = 0; cellIndex < expectedLength; cellIndex++)
            {
                costArray[cellIndex] = authoring.costs[cellIndex];
                surfaceArray[cellIndex] = authoring.surface[cellIndex];
            }

            BlobAssetReference<TileNavCostBlob> blob = builder.CreateBlobAssetReference<TileNavCostBlob>(Allocator.Persistent);
            builder.Dispose();

            AddBlobAsset(ref blob, out Unity.Entities.Hash128 blobHash);
            AddComponent(GetEntity(TransformUsageFlags.None), new TileNavCost
            {
                blob = blob,
                coordinate = new int2(authoring.coordinate.x, authoring.coordinate.y)
            });
        }
    }
}
