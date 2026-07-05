using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The authored source for the painterly palette atlas (T_PainterlyGradientLUT.png).
/// Each entry is one colour zone — a Unity Gradient authored in the inspector's gradient
/// editor. PainterlyGradientLUTGenerator bakes this list into a 64x64 texture: the gradients
/// are stacked top-to-bottom into equal horizontal bands (so every zone gets several rows of
/// vertical tolerance for UV mapping), each band sampled left-to-right across its width.
///
/// A model's mesh UVs are laid out so each part lands in the band of the colour it should be
/// (hilt UV.y in the brown band, blade UV.y in the grey band). The PainterlyGradientMap node
/// samples this by UV to pick the base colour; lighting does the shading.
///
/// This is authoring data only — nothing reads it at runtime. Re-run the generator after
/// editing to rebuild the texture in place (GUID + material references preserved).
/// </summary>
[CreateAssetMenu(fileName = "PainterlyGradientLUT", menuName = "Dots Animation/Painterly Gradient LUT")]
public class PainterlyGradientLUTSO : ScriptableObject
{
    [Tooltip("One gradient per colour zone, top to bottom. Up to 64. The generator splits the " +
             "64-row texture into this many equal bands so each zone has vertical UV tolerance.")]
    public List<Gradient> gradients = new List<Gradient>();
}
