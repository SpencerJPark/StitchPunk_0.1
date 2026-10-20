using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// LH-D17: draws placeholder emote art (atlas + 13 materials + quad). Owner art is safe unless overwriteArt is ticked.
public static class EmoteAtlasGenerator
{
    private const string ArtFolder = "Assets/Art/CrowdLife";
    private const string AtlasPath = ArtFolder + "/EmoteAtlas.png";
    private const string QuadPath = ArtFolder + "/EmoteQuad.asset";
    private const string OverwritePrefsKey = "StitchPunk.CrowdLife.OverwriteEmoteArt";
    private const string OverwriteMenuPath = "Tools/Stitch Punk/Crowd Life/Toggle Overwrite Emote Art";
    private const int AtlasSize = 1024;
    private const int CellSize = 256;
    private const int CellsPerRow = 4;

    private static readonly string[] IconNames =
    {
        "Happy", "Laugh", "Heart", "Angry", "Sad", "Question", "Exclaim", "Idea", "Sweat", "Music", "Sleep", "Drunk", "Drink",
    };

    private static readonly Color32 Cream = new Color32(0xF2, 0xE6, 0xCF, 255);
    private static readonly Color32 Outline = new Color32(0x2B, 0x24, 0x20, 255);
    private static readonly Color32 Red = new Color32(0xD2, 0x3B, 0x34, 255);
    private static readonly Color32 Blue = new Color32(0x3A, 0x7B, 0xD5, 255);
    private static readonly Color32 Yellow = new Color32(0xF5, 0xC8, 0x2E, 255);
    private static readonly Color32 Green = new Color32(0x4C, 0xAF, 0x50, 255);
    private static readonly Color32 Brown = new Color32(0x8A, 0x55, 0x2B, 255);
    private static readonly Color32 Grey = new Color32(0x7A, 0x74, 0x6E, 255);
    private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    private static Color32[] atlasPixels;
    private static int cellOriginX;
    private static int cellOriginY;

    private static bool OverwriteArt
    {
        get { return EditorPrefs.GetBool(OverwritePrefsKey, false); }
        set { EditorPrefs.SetBool(OverwritePrefsKey, value); }
    }

    [MenuItem(OverwriteMenuPath)]
    private static void ToggleOverwriteEmoteArt()
    {
        OverwriteArt = !OverwriteArt;
    }

    [MenuItem(OverwriteMenuPath, true)]
    private static bool ToggleOverwriteEmoteArtValidate()
    {
        Menu.SetChecked(OverwriteMenuPath, OverwriteArt);
        return true;
    }

    [MenuItem("Tools/Stitch Punk/Crowd Life/Generate Placeholder Emotes")]
    public static void GeneratePlaceholderEmotes()
    {
        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "CrowdLife");

        bool atlasExisted = File.Exists(AtlasPath);
        bool wroteAtlas = false;
        if (!atlasExisted || OverwriteArt)
        {
            WriteAtlasPng();
            wroteAtlas = true;
        }

        Texture2D atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        int materialCount = 0;
        for (int iconIndex = 0; iconIndex < IconNames.Length; iconIndex++)
        {
            WriteMaterial(IconNames[iconIndex], iconIndex, atlasTexture);
            materialCount++;
        }

        WriteQuadMesh();
        AssetDatabase.SaveAssets();
        Debug.Log("EmoteAtlasGenerator: atlas " + (wroteAtlas ? "written" : "kept (overwriteArt off)") +
                  ", " + materialCount + " materials, quad mesh at " + ArtFolder);
    }

    private static void EnsureFolder(string parentFolder, string folderName)
    {
        if (!AssetDatabase.IsValidFolder(parentFolder + "/" + folderName))
        {
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }
    }

    private static void WriteAtlasPng()
    {
        atlasPixels = new Color32[AtlasSize * AtlasSize];
        for (int iconIndex = 0; iconIndex < IconNames.Length; iconIndex++)
        {
            int column = iconIndex % CellsPerRow;
            int rowFromTop = iconIndex / CellsPerRow;
            cellOriginX = column * CellSize;
            // Texture rows count from the bottom, so a cell that is row 0 from the top starts at y 768.
            cellOriginY = (CellsPerRow - 1 - rowFromTop) * CellSize;
            DrawBubble();
            DrawIcon(iconIndex);
        }

        Texture2D atlasTexture = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, false);
        atlasTexture.SetPixels32(atlasPixels);
        atlasTexture.Apply();
        byte[] pngBytes = atlasTexture.EncodeToPNG();
        Object.DestroyImmediate(atlasTexture);
        atlasPixels = null;

        File.WriteAllBytes(AtlasPath, pngBytes);
        AssetDatabase.ImportAsset(AtlasPath);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.sRGBTexture = true;
        importer.SaveAndReimport();
    }

    private static void WriteMaterial(string iconName, int iconIndex, Texture2D atlasTexture)
    {
        string materialPath = ArtFolder + "/Emote_" + iconName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        bool isNew = material == null;
        if (isNew)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        }

        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = 3000;
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseMap", atlasTexture);

        int column = iconIndex % CellsPerRow;
        int rowFromTop = iconIndex / CellsPerRow;
        material.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f));
        material.SetTextureOffset("_BaseMap", new Vector2(column * 0.25f, (CellsPerRow - 1 - rowFromTop) * 0.25f));
        material.enableInstancing = true;

        if (isNew)
        {
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            EditorUtility.SetDirty(material);
        }
    }

    private static void WriteQuadMesh()
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(QuadPath);
        bool isNew = mesh == null;
        if (isNew)
        {
            mesh = new Mesh();
            mesh.name = "EmoteQuad";
        }

        mesh.Clear();
        // Order bottom-left, top-left, top-right, bottom-right; clockwise seen from -Z, so the visible side faces -Z.
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
        };
        mesh.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();

        if (isNew)
        {
            AssetDatabase.CreateAsset(mesh, QuadPath);
        }
        else
        {
            EditorUtility.SetDirty(mesh);
        }
    }

    // Cell-local coordinates: 0..256, y up (texture space), origin bottom-left of the current cell.
    private static void DrawBubble()
    {
        FillCircle(128f, 128f, 120f, Outline);
        FillCircle(128f, 128f, 114f, Cream);
    }

    private static void DrawIcon(int iconIndex)
    {
        switch (iconIndex)
        {
            case 0: // Happy
                DrawEyes(Outline);
                DrawArc(128f, 128f, 52f, 205f, 335f, 8f, Outline);
                break;
            case 1: // Laugh
                FillCircle(128f, 132f, 58f, Outline);
                FillRect(60f, 132f, 196f, 200f, Cream);
                FillRect(98f, 118f, 158f, 132f, Cream);
                FillCircle(128f, 108f, 24f, Red);
                FillRect(98f, 132f, 158f, 140f, Cream);
                DrawEyes(Outline);
                break;
            case 2: // Heart
                FillCircle(100f, 150f, 34f, Red);
                FillCircle(156f, 150f, 34f, Red);
                FillTriangle(70f, 138f, 186f, 138f, 128f, 64f, Red);
                break;
            case 3: // Angry
                FillCircle(128f, 128f, 108f, Red);
                DrawCapsule(78f, 168f, 118f, 144f, 8f, Outline);
                DrawCapsule(178f, 168f, 138f, 144f, 8f, Outline);
                FillCircle(98f, 126f, 11f, Outline);
                FillCircle(158f, 126f, 11f, Outline);
                DrawArc(128f, 62f, 42f, 30f, 150f, 8f, Outline);
                break;
            case 4: // Sad
                DrawEyes(Outline);
                DrawArc(128f, 62f, 44f, 30f, 150f, 8f, Outline);
                FillTriangle(166f, 140f, 182f, 124f, 174f, 156f, Blue);
                FillCircle(174f, 118f, 13f, Blue);
                break;
            case 5: // Question
                DrawArc(128f, 164f, 34f, -70f, 180f, 14f, Blue);
                DrawCapsule(139f, 134f, 128f, 100f, 7f, Blue);
                FillCircle(128f, 64f, 12f, Blue);
                break;
            case 6: // Exclaim
                FillTriangle(104f, 186f, 152f, 186f, 128f, 106f, Red);
                FillCircle(128f, 186f, 24f, Red);
                FillCircle(128f, 70f, 14f, Red);
                break;
            case 7: // Idea
                FillCircle(128f, 146f, 46f, Yellow);
                FillTriangle(96f, 128f, 160f, 128f, 128f, 92f, Yellow);
                FillRect(110f, 70f, 146f, 96f, Grey);
                DrawCapsule(128f, 210f, 128f, 222f, 5f, Yellow);
                DrawCapsule(68f, 190f, 58f, 198f, 5f, Yellow);
                DrawCapsule(188f, 190f, 198f, 198f, 5f, Yellow);
                break;
            case 8: // Sweat
                FillCircle(128f, 94f, 42f, Blue);
                FillTriangle(88f, 108f, 168f, 108f, 128f, 196f, Blue);
                FillCircle(112f, 100f, 9f, Cream);
                break;
            case 9: // Music
                FillCircle(98f, 84f, 28f, Outline);
                DrawCapsule(122f, 90f, 122f, 188f, 6f, Outline);
                DrawCapsule(122f, 188f, 172f, 146f, 8f, Outline);
                break;
            case 10: // Sleep
                DrawZ(64f, 70f, 70f, Blue);
                DrawZ(144f, 144f, 44f, Blue);
                break;
            case 11: // Drunk
                DrawRing(98f, 100f, 32f, 8f, Green);
                DrawRing(158f, 148f, 24f, 7f, Green);
                DrawRing(108f, 178f, 15f, 6f, Green);
                break;
            default: // Drink
                DrawRing(166f, 118f, 24f, 10f, Brown);
                FillRect(78f, 62f, 160f, 172f, Brown);
                FillRect(78f, 150f, 160f, 182f, Cream);
                FillCircle(96f, 182f, 13f, Cream);
                FillCircle(126f, 186f, 15f, Cream);
                FillCircle(150f, 182f, 11f, Cream);
                break;
        }
    }

    private static void DrawEyes(Color32 color)
    {
        FillCircle(96f, 152f, 12f, color);
        FillCircle(160f, 152f, 12f, color);
    }

    private static void DrawZ(float left, float bottom, float size, Color32 color)
    {
        float halfWidth = size * 0.11f;
        float right = left + size;
        float top = bottom + size;
        DrawCapsule(left, top, right, top, halfWidth, color);
        DrawCapsule(right, top, left, bottom, halfWidth, color);
        DrawCapsule(left, bottom, right, bottom, halfWidth, color);
    }

    private static void SetCellPixel(int localX, int localY, Color32 color)
    {
        if (localX < 0 || localY < 0 || localX >= CellSize || localY >= CellSize)
        {
            return;
        }
        atlasPixels[(cellOriginY + localY) * AtlasSize + cellOriginX + localX] = color;
    }

    private static void FillCircle(float centerX, float centerY, float radius, Color32 color)
    {
        DrawRing(centerX, centerY, radius, radius, color);
    }

    // thickness == radius paints a solid disc.
    private static void DrawRing(float centerX, float centerY, float radius, float thickness, Color32 color)
    {
        float innerRadius = radius - thickness;
        int minX = Mathf.FloorToInt(centerX - radius);
        int maxX = Mathf.CeilToInt(centerX + radius);
        int minY = Mathf.FloorToInt(centerY - radius);
        int maxY = Mathf.CeilToInt(centerY + radius);
        for (int pixelY = minY; pixelY <= maxY; pixelY++)
        {
            for (int pixelX = minX; pixelX <= maxX; pixelX++)
            {
                float deltaX = pixelX + 0.5f - centerX;
                float deltaY = pixelY + 0.5f - centerY;
                float distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
                if (distance <= radius && distance >= innerRadius)
                {
                    SetCellPixel(pixelX, pixelY, color);
                }
            }
        }
    }

    private static void FillRect(float minX, float minY, float maxX, float maxY, Color32 color)
    {
        for (int pixelY = Mathf.FloorToInt(minY); pixelY < Mathf.CeilToInt(maxY); pixelY++)
        {
            for (int pixelX = Mathf.FloorToInt(minX); pixelX < Mathf.CeilToInt(maxX); pixelX++)
            {
                SetCellPixel(pixelX, pixelY, color);
            }
        }
    }

    private static void DrawCapsule(float startX, float startY, float endX, float endY, float halfWidth, Color32 color)
    {
        float segmentX = endX - startX;
        float segmentY = endY - startY;
        float segmentLengthSquared = Mathf.Max(segmentX * segmentX + segmentY * segmentY, 0.0001f);
        int minX = Mathf.FloorToInt(Mathf.Min(startX, endX) - halfWidth);
        int maxX = Mathf.CeilToInt(Mathf.Max(startX, endX) + halfWidth);
        int minY = Mathf.FloorToInt(Mathf.Min(startY, endY) - halfWidth);
        int maxY = Mathf.CeilToInt(Mathf.Max(startY, endY) + halfWidth);
        for (int pixelY = minY; pixelY <= maxY; pixelY++)
        {
            for (int pixelX = minX; pixelX <= maxX; pixelX++)
            {
                float offsetX = pixelX + 0.5f - startX;
                float offsetY = pixelY + 0.5f - startY;
                float along = Mathf.Clamp01((offsetX * segmentX + offsetY * segmentY) / segmentLengthSquared);
                float nearestX = offsetX - segmentX * along;
                float nearestY = offsetY - segmentY * along;
                if (nearestX * nearestX + nearestY * nearestY <= halfWidth * halfWidth)
                {
                    SetCellPixel(pixelX, pixelY, color);
                }
            }
        }
    }

    // Angles in degrees, counter-clockwise from +X, drawn as a chain of capsules.
    private static void DrawArc(float centerX, float centerY, float radius, float startDegrees, float endDegrees, float thickness, Color32 color)
    {
        const int SegmentCount = 24;
        float previousX = 0f;
        float previousY = 0f;
        for (int segmentIndex = 0; segmentIndex <= SegmentCount; segmentIndex++)
        {
            float angleRadians = Mathf.Lerp(startDegrees, endDegrees, segmentIndex / (float)SegmentCount) * Mathf.Deg2Rad;
            float pointX = centerX + Mathf.Cos(angleRadians) * radius;
            float pointY = centerY + Mathf.Sin(angleRadians) * radius;
            if (segmentIndex > 0)
            {
                DrawCapsule(previousX, previousY, pointX, pointY, thickness * 0.5f, color);
            }
            previousX = pointX;
            previousY = pointY;
        }
    }

    private static float TriangleEdgeSign(float pointX, float pointY, float ax, float ay, float bx, float by)
    {
        return (pointX - bx) * (ay - by) - (ax - bx) * (pointY - by);
    }

    private static void FillTriangle(float ax, float ay, float bx, float by, float cx, float cy, Color32 color)
    {
        int minX = Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx)));
        int maxX = Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx)));
        int minY = Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy)));
        int maxY = Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy)));
        for (int pixelY = minY; pixelY <= maxY; pixelY++)
        {
            for (int pixelX = minX; pixelX <= maxX; pixelX++)
            {
                float pointX = pixelX + 0.5f;
                float pointY = pixelY + 0.5f;
                float signAB = TriangleEdgeSign(pointX, pointY, ax, ay, bx, by);
                float signBC = TriangleEdgeSign(pointX, pointY, bx, by, cx, cy);
                float signCA = TriangleEdgeSign(pointX, pointY, cx, cy, ax, ay);
                bool hasNegative = signAB < 0f || signBC < 0f || signCA < 0f;
                bool hasPositive = signAB > 0f || signBC > 0f || signCA > 0f;
                if (!(hasNegative && hasPositive))
                {
                    SetCellPixel(pixelX, pixelY, color);
                }
            }
        }
    }
}
