using UnityEngine;

// mesh, colour and the chunk material are sub-assets of this asset: loose files do not bake into a subscene.
public class TerrainStrategicChunkData : ScriptableObject
{
    public Vector2Int superChunkCoordinate;
    public Vector2 worldMin;
    public Mesh mesh;
    public Texture2D colour;
}
