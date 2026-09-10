// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Channel slot indices, names and port colours for the packer; every channel-indexed API takes one of these, never a bare integer.</summary>
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

    public struct PackChannelBinding
    {
        public string sourceAssetPath;
        // -1 = unwired.
        public int sourceChannel;
        public bool invert;
        public float defaultValue;

        public bool IsWired => sourceChannel >= 0 && !string.IsNullOrEmpty(sourceAssetPath);
    }

    public struct PackRequest
    {
        public PackChannelBinding[] channels;
        public Vector2Int resolution;
        public string outputAssetPath;
    }

    public enum PackPreviewChannel
    {
        RGB = 0,
        R = 1,
        G = 2,
        B = 3,
        A = 4
    }

    // Pixels are bottom-up, as Texture2D.GetPixels32 returns them.
    public sealed class PackSourcePixels
    {
        public Color32[] pixels;
        public int width;
        public int height;
        public long fileWriteTicks;
    }
}
