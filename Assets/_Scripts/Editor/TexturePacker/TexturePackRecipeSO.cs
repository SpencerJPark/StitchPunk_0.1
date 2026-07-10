#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ===========================================================================
//  Texture Pack Recipe
//  A saved snapshot of a Texture Channel Packer graph: which source image
//  feeds each output channel, the invert/default settings, the output
//  resolution and asset path, plus enough node layout to restore the canvas.
//
//  Sources are stored by GUID, not path, so moving or renaming a source
//  texture does not break the recipe.
//
//  Editor-only asset (this type lives in StitchPunk.Editor) — it is never
//  baked into a build.
// ===========================================================================

/// <summary>
/// One output channel of the packed texture: where its greyscale values come
/// from, or the flat value to use when nothing is wired in.
/// </summary>
[Serializable]
public struct PackChannel
{
    /// <summary>GUID of the source texture asset. Empty when this channel is unwired.</summary>
    public string sourceTextureGuid;

    /// <summary>Which channel of the source feeds this output channel. 0=R 1=G 2=B 3=A, -1 = unwired.</summary>
    public int sourceChannel;

    /// <summary>Flip the sampled value (255 - value). Turns smoothness into roughness, etc.</summary>
    public bool invert;

    /// <summary>Flat 0-1 value written when <see cref="sourceChannel"/> is -1.</summary>
    public float defaultValue;

    public bool IsWired => sourceChannel >= 0 && !string.IsNullOrEmpty(sourceTextureGuid);

    public static PackChannel Unwired(float defaultValue)
    {
        return new PackChannel
        {
            sourceTextureGuid = string.Empty,
            sourceChannel = -1,
            invert = false,
            defaultValue = defaultValue
        };
    }
}

/// <summary>Canvas position of one source node, so a loaded recipe rebuilds the layout it was saved with.</summary>
[Serializable]
public struct SourceNodeLayout
{
    public string sourceTextureGuid;
    public Vector2 position;
}

/// <summary>Channel indices, so call sites never pass a bare integer.</summary>
public static class PackChannelIndex
{
    public const int Red = 0;
    public const int Green = 1;
    public const int Blue = 2;
    public const int Alpha = 3;
    public const int Count = 4;

    public static readonly string[] Names = { "R", "G", "B", "A" };

    public static readonly Color[] PortColors =
    {
        new Color(0.90f, 0.30f, 0.30f),
        new Color(0.35f, 0.80f, 0.35f),
        new Color(0.35f, 0.55f, 0.95f),
        new Color(0.85f, 0.85f, 0.85f)
    };
}

[CreateAssetMenu(fileName = "NewTexturePackRecipe", menuName = "Stitch Punk/Texture Pack Recipe")]
public class TexturePackRecipeSO : ScriptableObject
{
    [Tooltip("R, G, B, A — one entry per output channel. Always length 4.")]
    public PackChannel[] channels = CreateDefaultChannels();

    [Tooltip("Pixel size of the baked texture. Sources of other sizes are bilinearly resampled to it.")]
    public Vector2Int resolution = new Vector2Int(1024, 1024);

    [Tooltip("Project-relative path the packed PNG is written to. Re-baking overwrites it in place, preserving the GUID.")]
    public string outputAssetPath = string.Empty;

    [Tooltip("Canvas positions of the source nodes, restored when the recipe is loaded.")]
    public List<SourceNodeLayout> sourceLayouts = new List<SourceNodeLayout>();

    public Vector2 outputNodePosition = new Vector2(600f, 200f);

    /// <summary>Unwired defaults: colour channels black, alpha opaque.</summary>
    public static PackChannel[] CreateDefaultChannels()
    {
        return new[]
        {
            PackChannel.Unwired(0f),
            PackChannel.Unwired(0f),
            PackChannel.Unwired(0f),
            PackChannel.Unwired(1f)
        };
    }

    /// <summary>Guards against an asset saved before the channel count was fixed at four.</summary>
    private void OnValidate()
    {
        if (channels == null || channels.Length != PackChannelIndex.Count)
        {
            channels = CreateDefaultChannels();
        }

        resolution.x = Mathf.Max(1, resolution.x);
        resolution.y = Mathf.Max(1, resolution.y);
    }

    /// <summary>Resolves the GUID-referenced sources into asset paths for the baker.</summary>
    public PackJobDescription ToJobDescription()
    {
        PackJobDescription description = new PackJobDescription
        {
            resolution = resolution,
            outputAssetPath = outputAssetPath,
            channels = new PackChannelJob[PackChannelIndex.Count]
        };

        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            PackChannel channel = channels[channelIndex];
            string sourceAssetPath = channel.IsWired
                ? AssetDatabase.GUIDToAssetPath(channel.sourceTextureGuid)
                : string.Empty;

            description.channels[channelIndex] = new PackChannelJob
            {
                sourceAssetPath = sourceAssetPath,
                sourceChannel = string.IsNullOrEmpty(sourceAssetPath) ? -1 : channel.sourceChannel,
                invert = channel.invert,
                defaultValue = channel.defaultValue
            };
        }

        return description;
    }
}
#endif
