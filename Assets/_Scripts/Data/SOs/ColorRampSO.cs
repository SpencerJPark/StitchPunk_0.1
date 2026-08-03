using UnityEngine;

/// <summary>
/// One authored colour ramp. The gradient here is baked to a PNG in
/// Assets/Textures/ColorRamps/ by ColorRampGenerator (Bake button on this asset's inspector,
/// or Stitch Punk ▸ Bake All Color Ramps), and that PNG is what a material's Ramp Tex slot points at.
///
/// The shader maps a texture's luminance onto the ramp INVERTED — light pixels sample the LEFT
/// end, dark pixels the RIGHT end. So author left-to-right as highlight → shadow.
///
/// Unity's own gradient editor gives the two things this replaces a hand-rolled stop list with:
/// unlimited keys, and a Blend/Fixed mode toggle. Fixed mode = hard-edged bands with zero
/// interpolation, which is how you get cel/toon banding without any shader work.
///
/// Baking always overwrites the same PNG path, so the texture's GUID and import settings survive —
/// materials pointing at a ramp never lose it when you re-bake.
/// </summary>
[CreateAssetMenu(fileName = "Color Ramp", menuName = "Colors/Color Ramp")]
public class ColorRampSO : ScriptableObject
{
    [Tooltip("Left = what light pixels become, right = what dark pixels become. Set the gradient's " +
             "mode to Fixed for hard toon bands, Blend for a smooth painterly ramp.")]
    public Gradient gradient = new Gradient();

    [Tooltip("Horizontal resolution of the baked ramp. 256 is plenty for a smooth blend; drop it " +
             "only if you want visible quantisation.")]
    [Range(8, 1024)]
    public int width = 256;

    [Tooltip("On for normal colour ramps (the baked colours are what you picked). Turn off only if " +
             "a ramp is feeding data rather than colour.")]
    public bool sRGB = true;
}
