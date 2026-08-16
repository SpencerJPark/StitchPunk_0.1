// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Builds small clip/registry blob fixtures with BlobBuilder for the C1 pure-function tests.
    /// Every returned BlobAssetReference is caller-owned and must be disposed by the test fixture.
    /// </summary>
    internal static class TestBlobFactory
    {
        internal sealed class ClipSpec
        {
            internal ulong clipId = 1;
            internal string debugName = "TestClip";
            internal float duration = 1f;
            internal LoopMode defaultLoop = LoopMode.Once;
            internal float defaultBlendIn;
            internal float defaultBlendOut;
            internal TransformTrackSpec[] transformTracks = Array.Empty<TransformTrackSpec>();
            internal SpriteTrackSpec[] spriteTracks = Array.Empty<SpriteTrackSpec>();
            internal EventSpec[] events = Array.Empty<EventSpec>();
            internal int vatFrameStart = -1;
            internal int vatFrameCount;
            internal float vatFps;
        }

        internal sealed class TransformTrackSpec
        {
            internal int targetIndex;
            internal TrackBlendOp blendOp = TrackBlendOp.Override;
            internal AnimatedChannels channels = AnimatedChannels.PositionXY;
            internal TransformKeySpec[] keys = Array.Empty<TransformKeySpec>();
        }

        internal struct TransformKeySpec
        {
            internal float normalizedTime;
            internal float3 position;
            internal float rotationZ;
            internal float3 scale;
            internal Interpolation interpolation;
        }

        internal sealed class SpriteTrackSpec
        {
            internal int targetIndex;
            internal SpriteFrameMode mode = SpriteFrameMode.Slice;
            internal SpriteKeySpec[] keys = Array.Empty<SpriteKeySpec>();
        }

        internal struct SpriteKeySpec
        {
            internal float normalizedTime;
            internal int sliceIndex;
            internal float4 atlasRect;
        }

        internal struct EventSpec
        {
            internal float normalizedTime;
            internal uint eventKey;
            internal int intParam;
            internal float floatParam;

            /// <summary>Window length in seconds; 0 (the default) makes the marker pulse-only.</summary>
            internal float windowSeconds;
        }

        internal static TransformKeySpec Key(
            float normalizedTime,
            float positionX,
            float positionY,
            Interpolation interpolation = Interpolation.Linear,
            float rotationZ = 0f,
            float scaleX = 1f,
            float scaleY = 1f,
            float positionZ = 0f)
        {
            return new TransformKeySpec
            {
                normalizedTime = normalizedTime,
                position = new float3(positionX, positionY, positionZ),
                rotationZ = rotationZ,
                scale = new float3(scaleX, scaleY, 1f),
                interpolation = interpolation
            };
        }

        internal static BlobAssetReference<ClipBlob> BuildClip(ClipSpec clipSpec)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref ClipBlob clipRoot = ref builder.ConstructRoot<ClipBlob>();
                FillClip(ref builder, ref clipRoot, clipSpec);
                return builder.CreateBlobAssetReference<ClipBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        /// <summary>
        /// Builds a registry in the canonical layout <c>ClipRegistryBuilder</c> produces: the
        /// <c>clips</c> array sorted by ascending <c>clipId</c> regardless of the order the specs
        /// were handed in, with <c>sortedClipIds</c> holding those same ids in those same positions.
        /// A clip's dense index is therefore its position in the ascending-id order, which is what
        /// <c>ClipRegistryUtil.TryResolveClip</c>'s binary search returns.
        /// </summary>
        internal static BlobAssetReference<ClipRegistryBlob> BuildRegistry(
            ClipSpec[] clipSpecs,
            uint[] targetIds,
            ulong setKey = 1,
            byte layerCount = 4)
        {
            ClipSpec[] canonicalClipSpecs = new ClipSpec[clipSpecs.Length];
            Array.Copy(clipSpecs, canonicalClipSpecs, clipSpecs.Length);
            Array.Sort(canonicalClipSpecs, CompareClipSpecsByClipId);

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref ClipRegistryBlob registryRoot = ref builder.ConstructRoot<ClipRegistryBlob>();
                registryRoot.schemaVersion = 2;
                registryRoot.setKey = setKey;
                registryRoot.vatSetKey = 0;
                registryRoot.layerCount = layerCount;

                BlobBuilderArray<ClipBlob> clipArray =
                    builder.Allocate(ref registryRoot.clips, canonicalClipSpecs.Length);
                BlobBuilderArray<ulong> sortedClipIdArray =
                    builder.Allocate(ref registryRoot.sortedClipIds, canonicalClipSpecs.Length);
                for (int denseClipIndex = 0; denseClipIndex < canonicalClipSpecs.Length; denseClipIndex++)
                {
                    FillClip(ref builder, ref clipArray[denseClipIndex], canonicalClipSpecs[denseClipIndex]);
                    sortedClipIdArray[denseClipIndex] = canonicalClipSpecs[denseClipIndex].clipId;
                }

                uint[] sortedTargets = new uint[targetIds.Length];
                Array.Copy(targetIds, sortedTargets, targetIds.Length);
                Array.Sort(sortedTargets);

                BlobBuilderArray<uint> sortedTargetIdArray =
                    builder.Allocate(ref registryRoot.sortedTargetIds, sortedTargets.Length);
                BlobBuilderArray<float3> targetBoundsExtentsArray =
                    builder.Allocate(ref registryRoot.targetBoundsExtents, sortedTargets.Length);
                for (int targetIndex = 0; targetIndex < sortedTargets.Length; targetIndex++)
                {
                    sortedTargetIdArray[targetIndex] = sortedTargets[targetIndex];
                    targetBoundsExtentsArray[targetIndex] = new float3(0.5f, 0.5f, 0.5f);
                }

                registryRoot.vatInfo = new VatTextureInfoBlob
                {
                    flavor = VatFlavor.BoneMatrix,
                    textureWidth = 0,
                    rowsPerFrame = 1,
                    boneOrVertexCount = 0
                };

                return builder.CreateBlobAssetReference<ClipRegistryBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        /// <summary>
        /// Orders specs by ascending clip id — the canonical order <c>ClipRegistryBuilder</c> bakes
        /// (architecture section 4.5.1), where a clip's dense index is its position. Sorting here
        /// lets a fixture hand clips over in any order and still get the layout the runtime expects,
        /// so a test can exercise out-of-order authoring without hand-computing dense indices.
        /// </summary>
        private static int CompareClipSpecsByClipId(ClipSpec firstSpec, ClipSpec secondSpec)
        {
            return firstSpec.clipId.CompareTo(secondSpec.clipId);
        }

        private static void FillClip(ref BlobBuilder builder, ref ClipBlob clip, ClipSpec clipSpec)
        {
            clip.clipId = clipSpec.clipId;
            clip.debugName = new FixedString64Bytes(clipSpec.debugName);
            clip.duration = clipSpec.duration;
            clip.defaultLoop = clipSpec.defaultLoop;
            clip.defaultBlendIn = clipSpec.defaultBlendIn;
            clip.defaultBlendOut = clipSpec.defaultBlendOut;
            clip.vatFrameStart = clipSpec.vatFrameStart;
            clip.vatFrameCount = clipSpec.vatFrameCount;
            clip.vatFps = clipSpec.vatFps;
            clip.offsetBounds = new AABB { Center = float3.zero, Extents = float3.zero };

            BlobBuilderArray<TransformTrackBlob> trackArray =
                builder.Allocate(ref clip.transformTracks, clipSpec.transformTracks.Length);
            for (int trackIndex = 0; trackIndex < clipSpec.transformTracks.Length; trackIndex++)
            {
                TransformTrackSpec trackSpec = clipSpec.transformTracks[trackIndex];
                trackArray[trackIndex].targetIndex = trackSpec.targetIndex;
                trackArray[trackIndex].blendOp = trackSpec.blendOp;
                trackArray[trackIndex].channels = trackSpec.channels;
                BlobBuilderArray<TransformKeyBlob> keyArray =
                    builder.Allocate(ref trackArray[trackIndex].keys, trackSpec.keys.Length);
                for (int keyIndex = 0; keyIndex < trackSpec.keys.Length; keyIndex++)
                {
                    TransformKeySpec keySpec = trackSpec.keys[keyIndex];
                    keyArray[keyIndex] = new TransformKeyBlob
                    {
                        normalizedTime = keySpec.normalizedTime,
                        position = keySpec.position,
                        rotation = new float3(0f, 0f, keySpec.rotationZ),
                        scale = keySpec.scale,
                        interpolation = keySpec.interpolation
                    };
                }
            }

            BlobBuilderArray<SpriteTrackBlob> spriteTrackArray =
                builder.Allocate(ref clip.spriteTracks, clipSpec.spriteTracks.Length);
            for (int spriteTrackIndex = 0; spriteTrackIndex < clipSpec.spriteTracks.Length; spriteTrackIndex++)
            {
                SpriteTrackSpec spriteTrackSpec = clipSpec.spriteTracks[spriteTrackIndex];
                spriteTrackArray[spriteTrackIndex].targetIndex = spriteTrackSpec.targetIndex;
                spriteTrackArray[spriteTrackIndex].mode = spriteTrackSpec.mode;
                BlobBuilderArray<SpriteKeyBlob> spriteKeyArray =
                    builder.Allocate(ref spriteTrackArray[spriteTrackIndex].keys, spriteTrackSpec.keys.Length);
                for (int spriteKeyIndex = 0; spriteKeyIndex < spriteTrackSpec.keys.Length; spriteKeyIndex++)
                {
                    SpriteKeySpec spriteKeySpec = spriteTrackSpec.keys[spriteKeyIndex];
                    spriteKeyArray[spriteKeyIndex] = new SpriteKeyBlob
                    {
                        normalizedTime = spriteKeySpec.normalizedTime,
                        sliceIndex = spriteKeySpec.sliceIndex,
                        atlasRect = spriteKeySpec.atlasRect
                    };
                }
            }

            BlobBuilderArray<EventMarkerBlob> eventArray =
                builder.Allocate(ref clip.events, clipSpec.events.Length);
            for (int eventIndex = 0; eventIndex < clipSpec.events.Length; eventIndex++)
            {
                EventSpec eventSpec = clipSpec.events[eventIndex];
                eventArray[eventIndex] = new EventMarkerBlob
                {
                    normalizedTime = eventSpec.normalizedTime,
                    eventKey = eventSpec.eventKey,
                    intParam = eventSpec.intParam,
                    floatParam = eventSpec.floatParam,
                    windowSeconds = eventSpec.windowSeconds
                };
            }
        }
    }
}
