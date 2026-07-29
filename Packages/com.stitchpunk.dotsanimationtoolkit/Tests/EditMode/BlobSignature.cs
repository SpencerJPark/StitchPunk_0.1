// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Text;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Renders a whole <see cref="ClipRegistryBlob"/> as a text signature covering every field of
    /// every element, with floats written as their exact <c>math.asuint</c> bit patterns so no
    /// formatting rounding can hide a difference. Two builds whose signatures are equal are equal
    /// blobs field for field — this is what the determinism fixtures compare, rather than trusting
    /// the content hash to speak for the bytes it keys.
    /// </summary>
    internal static class BlobSignature
    {
        /// <summary>Builds the signature of a registry blob.</summary>
        /// <param name="registry">The blob to describe.</param>
        /// <returns>A multi-line, order-sensitive description of every field in the blob.</returns>
        internal static string Describe(ref ClipRegistryBlob registry)
        {
            StringBuilder signature = new StringBuilder();
            signature.Append("schemaVersion=").Append(registry.schemaVersion).Append('\n');
            signature.Append("setKey=").Append(registry.setKey.ToString("X16")).Append('\n');
            signature.Append("vatSetKey=").Append(registry.vatSetKey.ToString("X16")).Append('\n');
            signature.Append("layerCount=").Append(registry.layerCount).Append('\n');

            signature.Append("sortedClipIds=[");
            for (int sortedPosition = 0; sortedPosition < registry.sortedClipIds.Length; sortedPosition++)
            {
                if (sortedPosition > 0)
                {
                    signature.Append(',');
                }
                signature.Append(registry.sortedClipIds[sortedPosition].ToString("X16"));
            }
            signature.Append("]\n");

            signature.Append("sortedTargetIds=[");
            for (int denseIndex = 0; denseIndex < registry.sortedTargetIds.Length; denseIndex++)
            {
                if (denseIndex > 0)
                {
                    signature.Append(',');
                }
                signature.Append(registry.sortedTargetIds[denseIndex].ToString("X8"));
            }
            signature.Append("]\n");

            signature.Append("targetBoundsExtents=[");
            for (int denseIndex = 0; denseIndex < registry.targetBoundsExtents.Length; denseIndex++)
            {
                if (denseIndex > 0)
                {
                    signature.Append(',');
                }
                AppendFloat3(signature, registry.targetBoundsExtents[denseIndex]);
            }
            signature.Append("]\n");

            signature.Append("vatInfo=")
                .Append(registry.vatInfo.flavor)
                .Append('/')
                .Append(registry.vatInfo.textureWidth)
                .Append('/')
                .Append(registry.vatInfo.rowsPerFrame)
                .Append('/')
                .Append(registry.vatInfo.boneOrVertexCount)
                .Append('\n');

            for (int denseClipIndex = 0; denseClipIndex < registry.clips.Length; denseClipIndex++)
            {
                signature.Append("clip[").Append(denseClipIndex).Append("]\n");
                DescribeClip(signature, ref registry.clips[denseClipIndex]);
            }
            return signature.ToString();
        }

        private static void DescribeClip(StringBuilder signature, ref ClipBlob clip)
        {
            signature.Append("  clipId=").Append(clip.clipId.ToString("X16")).Append('\n');
            signature.Append("  debugName=").Append(clip.debugName.ToString()).Append('\n');
            signature.Append("  duration=").Append(BitsOf(clip.duration)).Append('\n');
            signature.Append("  defaultLoop=").Append(clip.defaultLoop).Append('\n');
            signature.Append("  defaultBlendIn=").Append(BitsOf(clip.defaultBlendIn)).Append('\n');
            signature.Append("  defaultBlendOut=").Append(BitsOf(clip.defaultBlendOut)).Append('\n');
            signature.Append("  vatFrameStart=").Append(clip.vatFrameStart).Append('\n');
            signature.Append("  vatFrameCount=").Append(clip.vatFrameCount).Append('\n');
            signature.Append("  vatFps=").Append(BitsOf(clip.vatFps)).Append('\n');
            signature.Append("  offsetBoundsCenter=");
            AppendFloat3(signature, clip.offsetBounds.Center);
            signature.Append('\n');
            signature.Append("  offsetBoundsExtents=");
            AppendFloat3(signature, clip.offsetBounds.Extents);
            signature.Append('\n');

            for (int trackIndex = 0; trackIndex < clip.transformTracks.Length; trackIndex++)
            {
                ref TransformTrackBlob track = ref clip.transformTracks[trackIndex];
                signature.Append("  transformTrack[").Append(trackIndex).Append("] target=")
                    .Append(track.targetIndex)
                    .Append(" blendOp=").Append(track.blendOp)
                    .Append(" channels=").Append(track.channels)
                    .Append(" keyCount=").Append(track.keys.Length).Append('\n');
                for (int keyIndex = 0; keyIndex < track.keys.Length; keyIndex++)
                {
                    ref TransformKeyBlob key = ref track.keys[keyIndex];
                    signature.Append("    key t=").Append(BitsOf(key.normalizedTime))
                        .Append(" pos=");
                    AppendFloat3(signature, key.position);
                    signature.Append(" rot=").Append(BitsOf(key.rotationZ))
                        .Append(" scale=").Append(BitsOf(key.scale.x))
                        .Append('|').Append(BitsOf(key.scale.y))
                        .Append(" interp=").Append(key.interpolation).Append('\n');
                }
            }

            for (int trackIndex = 0; trackIndex < clip.spriteTracks.Length; trackIndex++)
            {
                ref SpriteTrackBlob track = ref clip.spriteTracks[trackIndex];
                signature.Append("  spriteTrack[").Append(trackIndex).Append("] target=")
                    .Append(track.targetIndex)
                    .Append(" mode=").Append(track.mode)
                    .Append(" keyCount=").Append(track.keys.Length).Append('\n');
                for (int keyIndex = 0; keyIndex < track.keys.Length; keyIndex++)
                {
                    ref SpriteKeyBlob key = ref track.keys[keyIndex];
                    signature.Append("    key t=").Append(BitsOf(key.normalizedTime))
                        .Append(" slice=").Append(key.sliceIndex)
                        .Append(" rect=").Append(BitsOf(key.atlasRect.x))
                        .Append('|').Append(BitsOf(key.atlasRect.y))
                        .Append('|').Append(BitsOf(key.atlasRect.z))
                        .Append('|').Append(BitsOf(key.atlasRect.w)).Append('\n');
                }
            }

            for (int eventIndex = 0; eventIndex < clip.events.Length; eventIndex++)
            {
                ref EventMarkerBlob marker = ref clip.events[eventIndex];
                signature.Append("  event[").Append(eventIndex).Append("] t=")
                    .Append(BitsOf(marker.normalizedTime))
                    .Append(" key=").Append(marker.eventKey)
                    .Append(" int=").Append(marker.intParam)
                    .Append(" float=").Append(BitsOf(marker.floatParam)).Append('\n');
            }
        }

        private static void AppendFloat3(StringBuilder signature, float3 value)
        {
            signature.Append(BitsOf(value.x)).Append('|')
                .Append(BitsOf(value.y)).Append('|')
                .Append(BitsOf(value.z));
        }

        private static string BitsOf(float value)
        {
            return math.asuint(value).ToString("X8");
        }
    }
}
