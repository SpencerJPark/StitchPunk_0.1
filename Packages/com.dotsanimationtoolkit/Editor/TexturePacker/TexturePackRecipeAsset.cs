// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    [Serializable]
    public struct PackChannel
    {
        public string sourceTextureGuid;

        // -1 = unwired.
        public int sourceChannel;

        public bool invert;

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

    [Serializable]
    public struct SourceNodeLayout
    {
        public string sourceTextureGuid;
        public Vector2 position;
    }

    /// <summary>A saved Texture Channel Packer graph: which source feeds each output channel, the output resolution and asset path, and the canvas layout.</summary>
    [CreateAssetMenu(fileName = "NewTexturePackRecipe", menuName = "DOTS Animation Toolkit/Texture Pack Recipe", order = 40)]
    public sealed class TexturePackRecipeAsset : ScriptableObject
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

        // A freshly created recipe is false, so the first source dropped onto the canvas still sizes the output.
        public bool HasAnySource
        {
            get
            {
                if (sourceLayouts.Count > 0)
                {
                    return true;
                }

                for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    if (channels[channelIndex].IsWired)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public int WiredChannelCount
        {
            get
            {
                int wiredChannelCount = 0;

                for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                {
                    if (channels[channelIndex].IsWired)
                    {
                        wiredChannelCount++;
                    }
                }

                return wiredChannelCount;
            }
        }

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

        private void OnValidate()
        {
            if (channels == null || channels.Length != PackChannelIndex.Count)
            {
                channels = CreateDefaultChannels();
            }

            resolution.x = Mathf.Max(1, resolution.x);
            resolution.y = Mathf.Max(1, resolution.y);
        }

        public PackRequest ToPackRequest()
        {
            PackRequest packRequest = new PackRequest
            {
                resolution = resolution,
                outputAssetPath = outputAssetPath,
                channels = new PackChannelBinding[PackChannelIndex.Count]
            };

            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                PackChannel channel = channels[channelIndex];
                string sourceAssetPath = channel.IsWired
                    ? AssetDatabase.GUIDToAssetPath(channel.sourceTextureGuid)
                    : string.Empty;

                packRequest.channels[channelIndex] = new PackChannelBinding
                {
                    sourceAssetPath = sourceAssetPath,
                    sourceChannel = string.IsNullOrEmpty(sourceAssetPath) ? -1 : channel.sourceChannel,
                    invert = channel.invert,
                    defaultValue = channel.defaultValue
                };
            }

            return packRequest;
        }
    }
}
