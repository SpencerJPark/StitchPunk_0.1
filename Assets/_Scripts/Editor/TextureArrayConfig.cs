// TexturePackerConfig.cs
using UnityEngine;
using System;

[CreateAssetMenu(fileName = "TexturePackerConfig", menuName = "Textures/Texture Packer Config")]
public class TexturePackerConfig : ScriptableObject
{
    public bool isTextureArray = false;
    
    public int baseSize = 512;
    public TextureFormat format = TextureFormat.RGBA32;
    public bool linear = false;
    
    public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
    public FilterMode filterMode = FilterMode.Point;
    
    // For single texture
    public Texture2D[] mips;
    
    // For texture array
    public Slice[] slices;

    [Serializable]
    public class Slice
    {
        public string name;
        public Texture2D[] mips;
    }
}