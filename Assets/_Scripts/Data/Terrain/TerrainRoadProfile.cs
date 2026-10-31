using UnityEngine;

// Decal rules (ruts, edge stones) are added in phase P6 when TerrainDecalSet exists.
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Road Profile", fileName = "TerrainRoadProfile")]
public class TerrainRoadProfile : ScriptableObject
{
    public float widthMetres = 4f;

    [Tooltip("Extra band beyond the width that the surface edge fringe may use; informational for the baker.")]
    public float shoulderMetres = 2f;

    public TerrainSplatLayer surfaceLayer = TerrainSplatLayer.Dirt;

    [Tooltip("Stable wobble of the surface edge.")]
    public float edgeNoiseMetres = 0.3f;

    public bool carveHeight = true;

    [Tooltip("Distance beyond the half width over which the carve blends back into the terrain.")]
    public float carveShoulderMetres = 4f;

    [Tooltip("x = 0 at the bed edge, 1 at the end of the shoulder; y = how much of the carve is applied.")]
    public AnimationCurve carveBlendCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Tooltip("The bed height follows the terrain smoothed over this length so the road does not copy every bump.")]
    public float heightSmoothingMetres = 20f;

    [Tooltip("255 means leave the nav default alone.")]
    public byte navCostByte = 255;

    public float HalfWidthMetres => widthMetres * 0.5f;
}
