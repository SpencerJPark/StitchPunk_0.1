using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Card Atlas")]
public class TerrainCardAtlas : ScriptableObject
{
    public Material material;
    [Tooltip("One UV rect per sprite cell in the atlas texture.")]
    public List<Rect> spriteUvRects = new List<Rect>();
}
