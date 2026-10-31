using Unity.Entities;
using Unity.Mathematics;

public enum TerrainTileState : byte { Unloaded = 0, Loading = 1, Loaded = 2 }

// sceneGuid is the tile SubScene's guid (SceneSystem.LoadSceneAsync(world, Hash128, params) / UnloadScene(world, sceneEntity), spike S2).
// state and sceneEntity are runtime state written only by TerrainStreamingSystem; baked as Unloaded / Entity.Null.
[InternalBufferCapacity(0)]
public struct TerrainTileEntry : IBufferElementData
{
    public int2 coordinate;
    public Unity.Entities.Hash128 sceneGuid;
    public TerrainTileState state;
    public Entity sceneEntity;
}
