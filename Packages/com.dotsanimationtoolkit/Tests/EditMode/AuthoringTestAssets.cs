// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Creates the in-memory authoring assets the C2 fixtures run against and destroys every one of
    /// them again, so no ScriptableObject leaks between tests. Stable ids are assigned explicitly by
    /// the factories rather than left to the random generator, so a fixture's canonical ordering is
    /// something the test author chose and the assertions can state exactly.
    /// </summary>
    internal sealed class AuthoringTestAssets
    {
        private readonly List<Object> createdObjects = new List<Object>();

        /// <summary>
        /// Which rig each created set is exercised against. Recorded here rather than on the assets,
        /// because neither a clip nor a set names a rig — the pairing lives in an
        /// <c>ActorAuthoring</c>, and in a fixture this dictionary plays that part.
        /// </summary>
        private readonly Dictionary<ClipSetAsset, RigAsset> bindRigBySet =
            new Dictionary<ClipSetAsset, RigAsset>();

        /// <summary>Creates a rig with <paramref name="layerCount"/> layers and one target row per supplied id.</summary>
        internal RigAsset CreateRig(string assetName, ulong rigStableId, int layerCount, uint[] targetIds)
        {
            RigAsset rig = Create<RigAsset>(assetName);
            rig.stableId = rigStableId;
            rig.layers.Clear();
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                rig.layers.Add(new LayerDefinition
                {
                    displayName = "Layer" + layerIndex,
                    defaultActive = layerIndex == 0
                });
            }
            rig.targets.Clear();
            for (int targetIndex = 0; targetIndex < targetIds.Length; targetIndex++)
            {
                rig.targets.Add(new RigTargetDefinition
                {
                    displayName = "Target" + targetIndex,
                    stableId = targetIds[targetIndex],
                    kind = TargetKind.Quad,
                    boundsExtents = new float3(0.5f, 0.5f, 0.5f)
                });
            }
            return rig;
        }

        /// <summary>Creates a clip with an explicit stable id. A clip names no rig.</summary>
        internal ClipAsset CreateClip(string assetName, ulong clipStableId, float duration)
        {
            ClipAsset clip = Create<ClipAsset>(assetName);
            clip.stableId = clipStableId;
            clip.duration = duration;
            clip.defaultLoop = LoopMode.Loop;
            clip.defaultBlendIn = 0.1f;
            clip.defaultBlendOut = 0.2f;
            return clip;
        }

        /// <summary>
        /// Creates a clip set holding the supplied clips in the supplied order, and records
        /// <paramref name="rig"/> as the rig this fixture binds it to.
        /// </summary>
        /// <remarks>
        /// The rig is written nowhere on the set — a set names none. It is remembered here so
        /// <see cref="ValidateBindOf"/> and <see cref="BuildBindOf"/> can express the bind a fixture
        /// almost always wants, without every test restating it.
        /// </remarks>
        internal ClipSetAsset CreateSet(string assetName, RigAsset rig, ulong setStableId, params ClipAsset[] clips)
        {
            ClipSetAsset clipSet = Create<ClipSetAsset>(assetName);
            clipSet.stableId = setStableId;
            bindRigBySet[clipSet] = rig;
            clipSet.clips.Clear();
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                clipSet.clips.Add(clips[clipIndex]);
            }
            return clipSet;
        }

        /// <summary>The rig this fixture binds <paramref name="clipSet"/> to, or null.</summary>
        internal RigAsset RigBoundTo(ClipSetAsset clipSet)
        {
            RigAsset rig;
            if (clipSet != null && bindRigBySet.TryGetValue(clipSet, out rig))
            {
                return rig;
            }
            return null;
        }

        /// <summary>Validates one set as the bind this fixture created it in.</summary>
        internal List<ValidationMessage> ValidateBindOf(
            ClipSetAsset clipSet,
            ValidationStage stage = ValidationStage.Authoring,
            bool vatSourceHashRecomputed = false,
            ulong recomputedVatSourceHash = 0UL,
            TargetTagRegistry tagRegistry = null)
        {
            return ClipValidation.ValidateBind(
                RigBoundTo(clipSet),
                new ClipSetAsset[] { clipSet },
                stage,
                vatSourceHashRecomputed,
                recomputedVatSourceHash,
                tagRegistry);
        }

        /// <summary>Builds one set as the bind this fixture created it in.</summary>
        internal void BuildBindOf(
            ClipSetAsset clipSet,
            out Unity.Entities.BlobAssetReference<ClipRegistryBlob> registry,
            out Unity.Entities.Hash128 contentHash)
        {
            ClipRegistryBuilder.Build(
                RigBoundTo(clipSet), new ClipSetAsset[] { clipSet }, out registry, out contentHash);
        }

        /// <summary>Probes the dedup key of one set as the bind this fixture created it in.</summary>
        internal bool TryComputeBindHashOf(
            ClipSetAsset clipSet, out Unity.Entities.Hash128 contentHash)
        {
            return ClipRegistryBuilder.TryComputeContentHash(
                RigBoundTo(clipSet),
                clipSet != null ? new ClipSetAsset[] { clipSet } : null,
                out contentHash);
        }

        /// <summary>Creates a VAT texture set carrying the supplied addressing parameters.</summary>
        internal VatTextureSetAsset CreateVatTextureSet(string assetName, ulong vatSetKey)
        {
            VatTextureSetAsset vatTextureSet = Create<VatTextureSetAsset>(assetName);
            vatTextureSet.setKey = vatSetKey;
            vatTextureSet.flavor = VatFlavor.BoneMatrix;
            vatTextureSet.boneCount = 3;
            vatTextureSet.textureWidth = 9;
            vatTextureSet.rowsPerFrame = 1;
            vatTextureSet.sourceHash = 0xABCDEF0123456789UL;
            vatTextureSet.clipRanges.Clear();
            return vatTextureSet;
        }

        /// <summary>Creates and registers a ScriptableObject of the requested type for later cleanup.</summary>
        internal T Create<T>(string assetName) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = assetName;
            createdObjects.Add(asset);
            return asset;
        }

        /// <summary>Registers an already-created object so the fixture destroys it too.</summary>
        internal void Track(Object trackedObject)
        {
            createdObjects.Add(trackedObject);
        }

        /// <summary>Destroys every object this factory created. Safe to call more than once.</summary>
        internal void DestroyAll()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                Object createdObject = createdObjects[objectIndex];
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }
            createdObjects.Clear();
            bindRigBySet.Clear();
        }

        // -----------------------------------------------------------------------------------
        // Track / key / marker helpers.
        // -----------------------------------------------------------------------------------

        internal static TransformTrack AddTransformTrack(
            ClipAsset clip,
            uint targetId,
            TrackBlendOp blendOp,
            AnimatedChannels channels)
        {
            TransformTrack track = new TransformTrack
            {
                targetId = targetId,
                blendOp = blendOp,
                channels = channels
            };
            clip.transformTracks.Add(track);
            return track;
        }

        internal static void AddTransformKey(
            TransformTrack track,
            float normalizedTime,
            float3 position,
            float rotationDegrees,
            float3 scale,
            Interpolation interpolation)
        {
            track.keys.Add(new TransformKey
            {
                normalizedTime = normalizedTime,
                position = position,
                rotation = new float3(0f, 0f, rotationDegrees),
                scale = scale,
                interpolation = interpolation
            });
        }

        internal static SpriteTrack AddSpriteTrack(ClipAsset clip, uint targetId, SpriteFrameMode mode)
        {
            SpriteTrack track = new SpriteTrack
            {
                targetId = targetId,
                mode = mode
            };
            clip.spriteTracks.Add(track);
            return track;
        }

        internal static void AddSpriteKey(
            SpriteTrack track,
            float normalizedTime,
            int sliceIndex,
            float4 atlasRect)
        {
            track.keys.Add(new SpriteKey
            {
                normalizedTime = normalizedTime,
                sliceIndex = sliceIndex,
                atlasRect = atlasRect
            });
        }

        internal static void AddEvent(
            ClipAsset clip,
            float normalizedTime,
            uint eventKey,
            int intParam,
            float floatParam)
        {
            clip.events.Add(new EventMarker
            {
                normalizedTime = normalizedTime,
                eventKey = eventKey,
                intParam = intParam,
                floatParam = floatParam
            });
        }
    }
}
