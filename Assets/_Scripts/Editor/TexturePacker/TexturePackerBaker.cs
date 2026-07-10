#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ===========================================================================
//  Texture Packer Baker
//  Pure data-in -> PNG-out. Knows nothing about GraphView, so the packing
//  logic can be exercised without opening the window.
//
//  Source decoding follows the PainterlyMaskPacker pattern: read the file
//  bytes straight off disk and LoadImage them, which is byte-exact and works
//  regardless of the source's import settings (readable, compressed, sRGB).
//  Anything LoadImage cannot decode (TGA/PSD/EXR, or a texture with no file
//  on disk) falls back to a linear RenderTexture blit, which reads whatever
//  the Editor can display at the cost of the importer's colour handling.
// ===========================================================================

/// <summary>One resolved output channel, ready to bake. Mirrors <see cref="PackChannel"/> with the GUID resolved to a path.</summary>
public struct PackChannelJob
{
    public string sourceAssetPath;
    public int sourceChannel;
    public bool invert;
    public float defaultValue;

    public bool IsWired => sourceChannel >= 0 && !string.IsNullOrEmpty(sourceAssetPath);
}

/// <summary>Everything the baker needs for one pack: four channels, an output size, and a destination.</summary>
public struct PackJobDescription
{
    public PackChannelJob[] channels;
    public Vector2Int resolution;
    public string outputAssetPath;
}

/// <summary>Which view of the packed result the preview thumbnail shows.</summary>
public enum PackPreviewChannel
{
    RGB = 0,
    R = 1,
    G = 2,
    B = 3,
    A = 4
}

public static class TexturePackerBaker
{
    /// <summary>Decoded source pixels, cached between the preview mini-bakes and the real bake.</summary>
    private sealed class DecodedSource
    {
        public Color32[] pixels;
        public int width;
        public int height;
        public long fileWriteTicks;
    }

    private static readonly Dictionary<string, DecodedSource> SourceCache = new Dictionary<string, DecodedSource>();

    /// <summary>Drop every cached decode. Called when the packer window closes, and after a bake.</summary>
    public static void ClearSourceCache()
    {
        SourceCache.Clear();
    }

    // -------------------------------------------------------------------------
    // Bake — write the packed PNG to disk and import it
    // -------------------------------------------------------------------------

    /// <summary>
    /// Packs the described channels into a PNG at <c>description.outputAssetPath</c>, overwriting in
    /// place so an existing texture keeps its GUID, import settings, and material references.
    /// Returns true on success; failures are logged with the reason.
    /// </summary>
    public static bool Bake(PackJobDescription description)
    {
        if (!ValidateDescription(description, out string validationError))
        {
            Debug.LogError("Texture Channel Packer: " + validationError);
            return false;
        }

        int width = description.resolution.x;
        int height = description.resolution.y;

        if (!ComposePixels(description, width, height, out Color32[] packedPixels))
        {
            return false;
        }

        // Drop the alpha channel entirely when nothing needs it — a fully opaque
        // alpha would otherwise double the file size for no information.
        bool needsAlpha = description.channels[PackChannelIndex.Alpha].IsWired ||
                          !Mathf.Approximately(description.channels[PackChannelIndex.Alpha].defaultValue, 1f);
        TextureFormat outputFormat = needsAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24;

        Texture2D packedTexture = new Texture2D(width, height, outputFormat, false, true);
        packedTexture.SetPixels32(packedPixels);
        packedTexture.Apply();
        byte[] pngBytes = packedTexture.EncodeToPNG();
        Object.DestroyImmediate(packedTexture);

        string absoluteOutputPath = Path.GetFullPath(description.outputAssetPath);
        string outputDirectory = Path.GetDirectoryName(absoluteOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        bool isNewAsset = !File.Exists(absoluteOutputPath);
        File.WriteAllBytes(absoluteOutputPath, pngBytes);
        AssetDatabase.ImportAsset(description.outputAssetPath, ImportAssetOptions.ForceUpdate);

        // Only stamp import settings the first time. After that the texture is the
        // user's to configure — a repack must not silently undo their changes.
        if (isNewAsset)
        {
            TextureImporter packedImporter = AssetImporter.GetAtPath(description.outputAssetPath) as TextureImporter;
            if (packedImporter != null)
            {
                packedImporter.sRGBTexture = false;
                packedImporter.mipmapEnabled = true;
                packedImporter.wrapMode = TextureWrapMode.Repeat;
                packedImporter.alphaSource = needsAlpha
                    ? TextureImporterAlphaSource.FromInput
                    : TextureImporterAlphaSource.None;
                packedImporter.SaveAndReimport();
            }
        }

        ClearSourceCache();
        Debug.Log("Texture Channel Packer: baked " + width + "x" + height + " " + outputFormat +
                  " -> " + description.outputAssetPath + (isNewAsset ? " (new asset)" : " (overwritten in place)"));
        return true;
    }

    // -------------------------------------------------------------------------
    // Preview — small in-memory bake, no disk writes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a small preview of the packed result. <paramref name="previewChannel"/> selects the
    /// composite or a single channel shown as greyscale. Caller owns the returned texture.
    /// </summary>
    public static Texture2D BakePreview(PackJobDescription description, int maxDimension, PackPreviewChannel previewChannel)
    {
        int sourceWidth = Mathf.Max(1, description.resolution.x);
        int sourceHeight = Mathf.Max(1, description.resolution.y);
        float scale = Mathf.Min(1f, (float)maxDimension / Mathf.Max(sourceWidth, sourceHeight));

        int previewWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
        int previewHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));

        if (!ComposePixels(description, previewWidth, previewHeight, out Color32[] packedPixels))
        {
            return null;
        }

        if (previewChannel != PackPreviewChannel.RGB)
        {
            IsolateChannel(packedPixels, (int)previewChannel - 1);
        }
        else
        {
            // The composite is shown opaque — a packed alpha channel would otherwise
            // punch holes in the thumbnail and hide the colour channels underneath.
            for (int pixelIndex = 0; pixelIndex < packedPixels.Length; pixelIndex++)
            {
                packedPixels[pixelIndex].a = 255;
            }
        }

        Texture2D previewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.RGBA32, false, true);
        previewTexture.hideFlags = HideFlags.HideAndDontSave;
        previewTexture.filterMode = FilterMode.Bilinear;
        previewTexture.SetPixels32(packedPixels);
        previewTexture.Apply();
        return previewTexture;
    }

    private static void IsolateChannel(Color32[] pixels, int channelIndex)
    {
        for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
        {
            Color32 pixel = pixels[pixelIndex];
            byte channelValue = channelIndex switch
            {
                PackChannelIndex.Red => pixel.r,
                PackChannelIndex.Green => pixel.g,
                PackChannelIndex.Blue => pixel.b,
                _ => pixel.a
            };
            pixels[pixelIndex] = new Color32(channelValue, channelValue, channelValue, 255);
        }
    }

    // -------------------------------------------------------------------------
    // Composition — the actual channel packing
    // -------------------------------------------------------------------------

    private static bool ComposePixels(PackJobDescription description, int width, int height, out Color32[] packedPixels)
    {
        packedPixels = new Color32[width * height];

        // Every channel starts at its flat default; wired channels overwrite it below.
        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            PackChannelJob channel = description.channels[channelIndex];
            if (channel.IsWired)
            {
                continue;
            }

            // Invert applies to sampled values only. An unwired channel is exactly its slider,
            // so a stale invert toggle can never silently flip a flat default.
            byte defaultByte = ToByte(channel.defaultValue);
            for (int pixelIndex = 0; pixelIndex < packedPixels.Length; pixelIndex++)
            {
                WriteChannel(ref packedPixels[pixelIndex], channelIndex, defaultByte);
            }
        }

        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            PackChannelJob channel = description.channels[channelIndex];
            if (!channel.IsWired)
            {
                continue;
            }

            DecodedSource source = DecodeSource(channel.sourceAssetPath);
            if (source == null)
            {
                packedPixels = null;
                return false;
            }

            bool sizesMatch = source.width == width && source.height == height;
            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    // An exact size match copies the byte straight across; anything else
                    // is bilinearly resampled, so mixed-resolution sources just work.
                    byte sampledValue = sizesMatch
                        ? ReadChannel(source.pixels[pixelY * width + pixelX], channel.sourceChannel)
                        : SampleChannelBilinear(source, channel.sourceChannel, pixelX, pixelY, width, height);

                    if (channel.invert)
                    {
                        sampledValue = (byte)(255 - sampledValue);
                    }

                    WriteChannel(ref packedPixels[pixelY * width + pixelX], channelIndex, sampledValue);
                }
            }
        }

        return true;
    }

    /// <summary>Bilinear sample of one channel, mapping destination pixel centres onto the source.</summary>
    private static byte SampleChannelBilinear(DecodedSource source, int sourceChannel, int destinationX, int destinationY, int destinationWidth, int destinationHeight)
    {
        float normalizedX = (destinationX + 0.5f) / destinationWidth;
        float normalizedY = (destinationY + 0.5f) / destinationHeight;

        float sampleX = normalizedX * source.width - 0.5f;
        float sampleY = normalizedY * source.height - 0.5f;

        int leftColumn = Mathf.FloorToInt(sampleX);
        int bottomRow = Mathf.FloorToInt(sampleY);
        float horizontalFraction = sampleX - leftColumn;
        float verticalFraction = sampleY - bottomRow;

        int clampedLeft = Mathf.Clamp(leftColumn, 0, source.width - 1);
        int clampedRight = Mathf.Clamp(leftColumn + 1, 0, source.width - 1);
        int clampedBottom = Mathf.Clamp(bottomRow, 0, source.height - 1);
        int clampedTop = Mathf.Clamp(bottomRow + 1, 0, source.height - 1);

        float bottomLeft = ReadChannel(source.pixels[clampedBottom * source.width + clampedLeft], sourceChannel);
        float bottomRight = ReadChannel(source.pixels[clampedBottom * source.width + clampedRight], sourceChannel);
        float topLeft = ReadChannel(source.pixels[clampedTop * source.width + clampedLeft], sourceChannel);
        float topRight = ReadChannel(source.pixels[clampedTop * source.width + clampedRight], sourceChannel);

        float bottomBlend = Mathf.LerpUnclamped(bottomLeft, bottomRight, horizontalFraction);
        float topBlend = Mathf.LerpUnclamped(topLeft, topRight, horizontalFraction);
        float blended = Mathf.LerpUnclamped(bottomBlend, topBlend, verticalFraction);

        return (byte)Mathf.Clamp(Mathf.RoundToInt(blended), 0, 255);
    }

    private static byte ReadChannel(Color32 pixel, int channelIndex)
    {
        return channelIndex switch
        {
            PackChannelIndex.Red => pixel.r,
            PackChannelIndex.Green => pixel.g,
            PackChannelIndex.Blue => pixel.b,
            _ => pixel.a
        };
    }

    private static void WriteChannel(ref Color32 pixel, int channelIndex, byte value)
    {
        switch (channelIndex)
        {
            case PackChannelIndex.Red: pixel.r = value; break;
            case PackChannelIndex.Green: pixel.g = value; break;
            case PackChannelIndex.Blue: pixel.b = value; break;
            default: pixel.a = value; break;
        }
    }

    private static byte ToByte(float normalizedValue)
    {
        return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(normalizedValue) * 255f), 0, 255);
    }

    // -------------------------------------------------------------------------
    // Source decoding — byte-exact first, importer blit as a fallback
    // -------------------------------------------------------------------------

    private static DecodedSource DecodeSource(string assetPath)
    {
        string absolutePath = Path.GetFullPath(assetPath);
        long fileWriteTicks = File.Exists(absolutePath) ? File.GetLastWriteTimeUtc(absolutePath).Ticks : 0L;

        if (SourceCache.TryGetValue(assetPath, out DecodedSource cachedSource) && cachedSource.fileWriteTicks == fileWriteTicks)
        {
            return cachedSource;
        }

        DecodedSource decodedSource = DecodeByteExact(absolutePath) ?? DecodeViaBlit(assetPath);
        if (decodedSource == null)
        {
            Debug.LogError("Texture Channel Packer: could not read pixels from " + assetPath +
                           " — it is neither a decodable PNG/JPG nor a texture the Editor can blit.");
            return null;
        }

        decodedSource.fileWriteTicks = fileWriteTicks;
        SourceCache[assetPath] = decodedSource;
        return decodedSource;
    }

    /// <summary>
    /// Reads the file straight off disk. Byte-exact and immune to import settings,
    /// but only PNG and JPG can be decoded this way.
    /// </summary>
    private static DecodedSource DecodeByteExact(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        string extension = Path.GetExtension(absolutePath).ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
        {
            return null;
        }

        byte[] fileBytes = File.ReadAllBytes(absolutePath);
        Texture2D decodeTarget = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!decodeTarget.LoadImage(fileBytes))
        {
            Object.DestroyImmediate(decodeTarget);
            return null;
        }

        DecodedSource decodedSource = new DecodedSource
        {
            pixels = decodeTarget.GetPixels32(),
            width = decodeTarget.width,
            height = decodeTarget.height
        };
        Object.DestroyImmediate(decodeTarget);
        return decodedSource;
    }

    /// <summary>
    /// Fallback for formats LoadImage cannot decode. Blits the imported texture through a
    /// linear RenderTexture, which reads any displayable texture — but the values that come
    /// back have passed through the importer, so an sRGB-flagged source is colour-converted.
    /// </summary>
    private static DecodedSource DecodeViaBlit(string assetPath)
    {
        Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (importedTexture == null)
        {
            return null;
        }

        RenderTexture blitTarget = RenderTexture.GetTemporary(
            importedTexture.width, importedTexture.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

        RenderTexture previousActive = RenderTexture.active;
        Graphics.Blit(importedTexture, blitTarget);
        RenderTexture.active = blitTarget;

        Texture2D readbackTexture = new Texture2D(importedTexture.width, importedTexture.height, TextureFormat.RGBA32, false, true);
        readbackTexture.ReadPixels(new Rect(0f, 0f, importedTexture.width, importedTexture.height), 0, 0);
        readbackTexture.Apply();

        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(blitTarget);

        DecodedSource decodedSource = new DecodedSource
        {
            pixels = readbackTexture.GetPixels32(),
            width = readbackTexture.width,
            height = readbackTexture.height
        };
        Object.DestroyImmediate(readbackTexture);

        Debug.LogWarning("Texture Channel Packer: " + assetPath + " is not a PNG/JPG, so it was read through a " +
                         "RenderTexture blit. Values reflect the texture's import settings (sRGB, compression) " +
                         "rather than the file on disk — re-export as PNG for a byte-exact pack.");
        return decodedSource;
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private static bool ValidateDescription(PackJobDescription description, out string errorMessage)
    {
        if (description.channels == null || description.channels.Length != PackChannelIndex.Count)
        {
            errorMessage = "the job description must carry exactly four channels.";
            return false;
        }

        if (description.resolution.x < 1 || description.resolution.y < 1)
        {
            errorMessage = "output resolution must be at least 1x1.";
            return false;
        }

        if (string.IsNullOrEmpty(description.outputAssetPath))
        {
            errorMessage = "no output path was chosen.";
            return false;
        }

        if (!description.outputAssetPath.StartsWith("Assets/"))
        {
            errorMessage = "the output path must be inside the project's Assets folder.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
#endif
