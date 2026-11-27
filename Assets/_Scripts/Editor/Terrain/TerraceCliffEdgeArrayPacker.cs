#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class TerraceCliffEdgeArrayPacker
{
    public const string ArrayAssetPath = "Assets/PaintedGround/Generated/EdgeTiles/TerraceCliffEdges.asset";
    const string SourceFolder = "Assets/PaintedGround/Textures/EdgeTiles/";
    const int ExpectedWidth = 1024;
    const int ExpectedHeight = 512;

    public static readonly string[] SliceNames =
    {
        "DirtRim", "GrassRim", "ForestRim", "StoneRim",
        "DirtFoot", "GrassFoot", "ForestFoot", "StoneFoot",
    };

    const int GrassRimSliceIndex = 1;
    const int GrassFootSliceIndex = 5;

    // The existing edge atlases import linear with mipmaps (sRGBTexture 0, enableMipMap 1); the array mirrors that.
    const bool ArrayIsLinear = true;
    const bool ArrayHasMipmaps = true;

    [MenuItem("Stitch Punk/Terrain/Edge Tiles/Pack Cliff Edge Sets")]
    public static void PackCliffEdgeSets()
    {
        byte[][] sliceBytes = new byte[SliceNames.Length][];
        for (int sliceIndex = 0; sliceIndex < SliceNames.Length; sliceIndex++)
        {
            string sourcePath = SourceFolder + "T_EdgeAtlas_" + SliceNames[sliceIndex] + ".png";
            if (File.Exists(sourcePath))
            {
                sliceBytes[sliceIndex] = File.ReadAllBytes(sourcePath);
            }
        }

        Texture2DArray packedArray = new Texture2DArray(ExpectedWidth, ExpectedHeight, SliceNames.Length, TextureFormat.RGBA32, ArrayHasMipmaps, ArrayIsLinear);
        packedArray.wrapMode = TextureWrapMode.Clamp;
        packedArray.filterMode = FilterMode.Bilinear;

        for (int sliceIndex = 0; sliceIndex < SliceNames.Length; sliceIndex++)
        {
            byte[] bytes = sliceBytes[sliceIndex];
            if (bytes == null)
            {
                int fallbackIndex = sliceIndex < GrassFootSliceIndex ? GrassRimSliceIndex : GrassFootSliceIndex;
                Debug.LogWarning("TerraceCliffEdgeArrayPacker: missing T_EdgeAtlas_" + SliceNames[sliceIndex] + ".png, using " + SliceNames[fallbackIndex] + " for slice " + sliceIndex + ".");
                bytes = sliceBytes[fallbackIndex];
                if (bytes == null)
                {
                    Debug.LogError("TerraceCliffEdgeArrayPacker: fallback " + SliceNames[fallbackIndex] + " is missing too, aborting.");
                    Object.DestroyImmediate(packedArray);
                    return;
                }
            }

            Texture2D decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, ArrayIsLinear);
            if (!decoded.LoadImage(bytes))
            {
                Debug.LogError("TerraceCliffEdgeArrayPacker: could not decode the PNG for slice " + SliceNames[sliceIndex] + ", aborting.");
                Object.DestroyImmediate(decoded);
                Object.DestroyImmediate(packedArray);
                return;
            }
            if (decoded.width != ExpectedWidth || decoded.height != ExpectedHeight)
            {
                Debug.LogError("TerraceCliffEdgeArrayPacker: " + SliceNames[sliceIndex] + " is " + decoded.width + "x" + decoded.height + ", expected " + ExpectedWidth + "x" + ExpectedHeight + ", aborting.");
                Object.DestroyImmediate(decoded);
                Object.DestroyImmediate(packedArray);
                return;
            }

            packedArray.SetPixels32(decoded.GetPixels32(), sliceIndex, 0);
            Object.DestroyImmediate(decoded);
        }
        packedArray.Apply(ArrayHasMipmaps, false);

        EnsureFolderExists("Assets/PaintedGround/Generated");
        EnsureFolderExists("Assets/PaintedGround/Generated/EdgeTiles");

        Texture2DArray existingArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(ArrayAssetPath);
        if (existingArray != null)
        {
            EditorUtility.CopySerialized(packedArray, existingArray);
            EditorUtility.SetDirty(existingArray);
            Object.DestroyImmediate(packedArray);
        }
        else
        {
            AssetDatabase.CreateAsset(packedArray, ArrayAssetPath);
        }
        AssetDatabase.SaveAssets();

        Debug.Log("TerraceCliffEdgeArrayPacker: packed " + SliceNames.Length + " slices into " + ArrayAssetPath);
    }

    static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }
        string parentPath = Path.GetDirectoryName(folderPath).Replace('\\', '/');
        string folderName = Path.GetFileName(folderPath);
        AssetDatabase.CreateFolder(parentPath, folderName);
    }
}
#endif
