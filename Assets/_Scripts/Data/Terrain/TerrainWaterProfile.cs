using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Water Profile", fileName = "TerrainWaterProfile")]
public class TerrainWaterProfile : ScriptableObject
{
    [Tooltip("x = fraction of the river length, y = full channel width in metres.")]
    public AnimationCurve widthAlongSpline = AnimationCurve.Linear(0f, 6f, 1f, 10f);

    [Tooltip("Water depth at the channel centre.")]
    public float bedDepthMetres = 2f;

    [Tooltip("Horizontal distance over which the bank blends from the bed up to the surrounding terrain.")]
    public float bankSlopeMetres = 4f;

    [Tooltip("Band along each channel edge that is always shallow and wadeable, even where the water is deep.")]
    public float shallowBandMetres = 1.5f;

    [Tooltip("Water deeper than this is a wall for units.")]
    public float deepBlocksBelowDepthMetres = 1.2f;

    public Material waterMaterial;

    public float flowSpeed = 0.5f;

    [Tooltip("Added to the water surface mesh height.")]
    public float surfaceOffsetMetres = 0f;

    public float WidthAt(float fractionOfLength) => Mathf.Max(0.5f, widthAlongSpline != null && widthAlongSpline.length > 0 ? widthAlongSpline.Evaluate(fractionOfLength) : 6f);
}
