using UnityEngine;

// Baked to a PNG in Assets/Textures/ColorRamps/ by ColorRampGenerator. The shader maps luminance
// INVERTED (light = left end), so author highlight to shadow. Re-baking overwrites the same PNG, keeping its GUID.
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
