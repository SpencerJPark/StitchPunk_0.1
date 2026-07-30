// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// Bakes an <see cref="ActorAuthoring"/> into the actor-root archetype of architecture
    /// section 5.2: the shared registry blob, the seeded playback layers, the command and event
    /// channels, the binding/visibility/bounds enableables, and the actor-space
    /// <see cref="ActorRestBounds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry blob is built <em>here</em> rather than in a baking system (architecture
    /// section 4.1's explicit decision): it is a pure function of ScriptableObject data with no
    /// cross-entity input, and <c>DependsOn</c> plus <c>AddBlobAssetWithCustomHash</c> buy
    /// incremental-rebake correctness and store-level deduplication for free. The store owns the
    /// blob from the moment it is handed over — nothing in this package disposes a registry blob by
    /// hand.
    /// </para>
    /// <para>
    /// Cross-entity work stays out of this baker on purpose. A baker may only write components on
    /// the entity it is baking, so the parts' dense indices, the <see cref="RigPartRef"/> buffer and
    /// each part's <c>actorRoot</c> are completed by <see cref="RigBindingBakingSystem"/> in
    /// <c>PostBakingSystemGroup</c>.
    /// </para>
    /// </remarks>
    public sealed class ActorBaker : Baker<ActorAuthoring>
    {
        private const string MessagePrefix = "[DOTS Animation Toolkit] ";

        /// <inheritdoc />
        public override void Bake(ActorAuthoring authoring)
        {
            ClipSetAsset clipSet = DependsOn(authoring.clipSet);
            if (clipSet == null)
            {
                Debug.LogError(
                    MessagePrefix + "Actor '" + authoring.name +
                    "' has no clip set assigned, so it cannot be baked. Assign a Clip Set Asset.",
                    authoring);
                return;
            }

            RigAsset rig = DependsOn(clipSet.rig);
            DependsOnClips(clipSet);
            VatTextureSetAsset vatTextures = DependsOn(clipSet.vatTextures);
            DependsOnVatTextures(vatTextures);
            DependsOnStartingClips(authoring);

            if (rig == null)
            {
                Debug.LogError(
                    MessagePrefix + "Actor '" + authoring.name + "' references clip set '" +
                    clipSet.name + "', which has no rig assigned. Assign a Rig Asset to the set.",
                    authoring);
                return;
            }

            if (!TryAcquireRegistry(authoring, clipSet, out BlobAssetReference<ClipRegistryBlob> registry))
            {
                return;
            }

            Entity actorEntity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(actorEntity, new ClipRegistry { Value = registry });
            AddPlaybackLayers(actorEntity, authoring, rig, registry);
            AddBuffer<AnimationCommand>(actorEntity);
            AddComponent<AnimationCommandPending>(actorEntity);
            SetComponentEnabled<AnimationCommandPending>(actorEntity, false);
            AddBuffer<AnimEventOutput>(actorEntity);
            AddComponent<AnimEventsPending>(actorEntity);
            SetComponentEnabled<AnimEventsPending>(actorEntity, false);
            AddBuffer<RigPartRef>(actorEntity);

            // Baked enabled: an ECB-instantiated copy starts enabled and RigBindingSystem rebinds it
            // (section 5.3); a first-frame bounds write is guaranteed (section 5.8); and everything
            // animates until some provider says otherwise (section 5.9).
            AddComponent<RigBindingUninitialized>(actorEntity);
            AddComponent<AnimVisible>(actorEntity);
            AddComponent<BoundsDirty>(actorEntity);

            AddComponent(actorEntity, new ActorRestBounds
            {
                value = ComputeActorRestBounds(authoring, registry)
            });
            AddComponent(actorEntity, new SampleSettings
            {
                rateHz = math.max(0f, authoring.sampleOverride.rateHz),
                phase01 = ComputeSamplePhase(authoring)
            });
            AddComponent(actorEntity, BuildVatTextureBinding(vatTextures));

            if (authoring.addDistanceLod)
            {
                AddComponent(actorEntity, new AnimLod { level = 0 });
            }
        }

        // -----------------------------------------------------------------------------------
        // Dependencies. Every asset the blob is a function of must retrigger this bake when edited.
        // -----------------------------------------------------------------------------------

        private void DependsOnClips(ClipSetAsset clipSet)
        {
            List<ClipAsset> clips = clipSet.clips;
            if (clips == null)
            {
                return;
            }
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                DependsOn(clips[clipIndex]);
            }
        }

        private void DependsOnStartingClips(ActorAuthoring authoring)
        {
            List<StartingLayerState> startingLayers = authoring.startingLayers;
            if (startingLayers == null)
            {
                return;
            }
            for (int entryIndex = 0; entryIndex < startingLayers.Count; entryIndex++)
            {
                StartingLayerState startingLayer = startingLayers[entryIndex];
                if (startingLayer != null)
                {
                    DependsOn(startingLayer.clip);
                }
            }
        }

        private void DependsOnVatTextures(VatTextureSetAsset vatTextures)
        {
            if (vatTextures == null)
            {
                return;
            }
            DependsOn(vatTextures.boneTexture);
            DependsOn(vatTextures.positionTexture);
            DependsOn(vatTextures.normalTexture);
        }

        // -----------------------------------------------------------------------------------
        // Registry blob: the canonical probe / store-hit / build / register pattern (section 4.5).
        // -----------------------------------------------------------------------------------

        private bool TryAcquireRegistry(
            ActorAuthoring authoring,
            ClipSetAsset clipSet,
            out BlobAssetReference<ClipRegistryBlob> registry)
        {
            // The probe computes the dedup key without handing back a blob to own, so a store hit
            // costs no persistent allocation and leaves nothing to dispose.
            if (ClipRegistryBuilder.TryComputeContentHash(clipSet, out Unity.Entities.Hash128 contentHash) &&
                TryGetBlobAssetReference(contentHash, out registry))
            {
                return true;
            }

            try
            {
                ClipRegistryBuilder.Build(clipSet, out registry, out contentHash);
            }
            catch (ClipValidationException validationException)
            {
                Debug.LogError(
                    DescribeValidationFailure(clipSet, validationException),
                    authoring);
                registry = default;
                return false;
            }

            // Ownership of the freshly built blob passes to the store here, and stays there.
            AddBlobAssetWithCustomHash(ref registry, contentHash);
            return true;
        }

        private static string DescribeValidationFailure(
            ClipSetAsset clipSet,
            ClipValidationException validationException)
        {
            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.Append(MessagePrefix);
            messageBuilder.Append("Clip set '");
            messageBuilder.Append(clipSet.name);
            messageBuilder.Append("' cannot be baked because it has validation errors:");
            IReadOnlyList<ValidationMessage> validationMessages = validationException.Messages;
            for (int messageIndex = 0; messageIndex < validationMessages.Count; messageIndex++)
            {
                ValidationMessage validationMessage = validationMessages[messageIndex];
                if (!validationMessage.IsError)
                {
                    continue;
                }
                messageBuilder.Append("\n  ");
                messageBuilder.Append(validationMessage.ToString());
            }
            return messageBuilder.ToString();
        }

        // -----------------------------------------------------------------------------------
        // Playback layers.
        // -----------------------------------------------------------------------------------

        private void AddPlaybackLayers(
            Entity actorEntity,
            ActorAuthoring authoring,
            RigAsset rig,
            BlobAssetReference<ClipRegistryBlob> registry)
        {
            int layerCount = registry.Value.layerCount;
            DynamicBuffer<PlaybackLayer> playbackLayers = AddBuffer<PlaybackLayer>(actorEntity);
            playbackLayers.ResizeUninitialized(layerCount);
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                playbackLayers[layerIndex] = new PlaybackLayer
                {
                    clipIndex = -1,
                    previousClipIndex = -1,
                    speed = 1f,
                    previousSpeed = 1f,
                    loop = LoopMode.UseClipDefault,
                    previousLoop = LoopMode.UseClipDefault,
                    flags = PlaybackFlags.None
                };
            }

            SeedStartingLayers(authoring, rig, registry, playbackLayers);
        }

        private static void SeedStartingLayers(
            ActorAuthoring authoring,
            RigAsset rig,
            BlobAssetReference<ClipRegistryBlob> registry,
            DynamicBuffer<PlaybackLayer> playbackLayers)
        {
            List<StartingLayerState> startingLayers = authoring.startingLayers;
            if (startingLayers == null)
            {
                return;
            }

            for (int entryIndex = 0; entryIndex < startingLayers.Count; entryIndex++)
            {
                StartingLayerState startingLayer = startingLayers[entryIndex];
                if (startingLayer == null)
                {
                    continue;
                }
                if (startingLayer.layerIndex < 0 || startingLayer.layerIndex >= playbackLayers.Length)
                {
                    Debug.LogError(
                        MessagePrefix + "Actor '" + authoring.name + "' seeds layer " +
                        startingLayer.layerIndex.ToString() + ", but its rig defines only " +
                        playbackLayers.Length.ToString() + " layers. The entry is ignored.",
                        authoring);
                    continue;
                }
                if (startingLayer.clip == null)
                {
                    continue;
                }
                if (!ClipRegistryUtil.TryResolveClip(
                        ref registry.Value,
                        startingLayer.clip.Id,
                        out int clipIndex))
                {
                    Debug.LogError(
                        MessagePrefix + "Actor '" + authoring.name + "' seeds layer " +
                        startingLayer.layerIndex.ToString() + " with clip '" +
                        startingLayer.clip.name + "', which is not a member of clip set '" +
                        authoring.clipSet.name + "'. The entry is ignored.",
                        authoring);
                    continue;
                }

                PlaybackLayer playbackLayer = playbackLayers[startingLayer.layerIndex];
                playbackLayer.clip = startingLayer.clip.Id;
                playbackLayer.clipIndex = clipIndex;
                playbackLayer.time = 0f;
                playbackLayer.speed = startingLayer.speed;
                playbackLayer.loop = startingLayer.loop;
                if (IsLayerActiveByDefault(rig, startingLayer.layerIndex))
                {
                    playbackLayer.flags |= PlaybackFlags.Active;
                }
                playbackLayers[startingLayer.layerIndex] = playbackLayer;
            }

            WarnAboutActiveLayersWithoutAClip(authoring, rig, playbackLayers);
        }

        private static bool IsLayerActiveByDefault(RigAsset rig, int layerIndex)
        {
            if (rig.layers == null || layerIndex >= rig.layers.Count)
            {
                return false;
            }
            LayerDefinition layerDefinition = rig.layers[layerIndex];
            return layerDefinition != null && layerDefinition.defaultActive;
        }

        private static void WarnAboutActiveLayersWithoutAClip(
            ActorAuthoring authoring,
            RigAsset rig,
            DynamicBuffer<PlaybackLayer> playbackLayers)
        {
            for (int layerIndex = 0; layerIndex < playbackLayers.Length; layerIndex++)
            {
                if (playbackLayers[layerIndex].clipIndex >= 0 || !IsLayerActiveByDefault(rig, layerIndex))
                {
                    continue;
                }
                Debug.LogWarning(
                    MessagePrefix + "Rig '" + rig.name + "' marks layer " + layerIndex.ToString() +
                    " as active by default, but actor '" + authoring.name +
                    "' seeds no starting clip for it, so the layer starts stopped.",
                    authoring);
            }
        }

        // -----------------------------------------------------------------------------------
        // Actor-space rest bounds (section 4.6, amendment A13).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Unions, in <strong>actor space</strong>, the box each bound part occupies at its rest
        /// pose: the part's rest position relative to the actor root, inflated by the target's
        /// authored half-extents scaled the same conservative way architecture section 4.6 scales
        /// them for keys.
        /// </summary>
        /// <remarks>
        /// This is the half of the bounds problem <c>ClipRegistryBuilder</c> structurally cannot
        /// solve: <see cref="ClipBlob.offsetBounds"/> is offset space, centred on the origin, because
        /// the builder sees only a <see cref="ClipSetAsset"/> graph and never the prefab that holds
        /// the rest poses. This baker does see the prefab, so it measures the rest frame here and
        /// section 5.8 combines the two at runtime. A rig whose parts sit far from the actor origin
        /// gets a correct silhouette only because of this component.
        /// </remarks>
        private AABB ComputeActorRestBounds(
            ActorAuthoring authoring,
            BlobAssetReference<ClipRegistryBlob> registry)
        {
            MinMaxAABB restBounds = MinMaxAABB.Empty;
            bool anyPartBounded = false;

            RigTargetAuthoring[] parts = GetComponentsInChildren<RigTargetAuthoring>();
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                RigTargetAuthoring part = parts[partIndex];
                if (part == null)
                {
                    continue;
                }
                // Unknown targets are reported once, by RigBindingBakingSystem; this pass stays
                // silent about them and simply leaves them out of the rest frame.
                if (!ClipRegistryUtil.ResolveTargetIndex(
                        ref registry.Value,
                        new TargetId(part.targetStableId),
                        out int denseTargetIndex))
                {
                    continue;
                }
                if (!TryGetRestPoseInActorSpace(
                        authoring,
                        part,
                        out float3 restPosition,
                        out float2 restScale))
                {
                    continue;
                }

                float scaleFactor = math.max(
                    math.max(math.abs(restScale.x), math.abs(restScale.y)),
                    1f);
                float3 halfExtents = registry.Value.targetBoundsExtents[denseTargetIndex] * scaleFactor;
                restBounds.Encapsulate(restPosition - halfExtents);
                restBounds.Encapsulate(restPosition + halfExtents);
                anyPartBounded = true;
            }

            if (!anyPartBounded)
            {
                // MinMaxAABB.Empty is an inverted box; converting it would produce nonsense extents.
                return new AABB { Center = float3.zero, Extents = float3.zero };
            }
            return restBounds;
        }

        /// <summary>
        /// Accumulates the local transforms between a part and its actor root, taking a bake
        /// dependency on every transform it multiplies so that moving any intermediate pivot
        /// retriggers the bake.
        /// </summary>
        /// <remarks>
        /// The chain is multiplied out rather than read from world matrices on purpose: the result
        /// must not change when the same prefab is placed somewhere else in the scene, and a
        /// world-space round trip would make the baked bounds depend on the actor's own placement.
        /// </remarks>
        /// <returns>False when the part is not under this actor, or is under a nested actor.</returns>
        private bool TryGetRestPoseInActorSpace(
            ActorAuthoring authoring,
            RigTargetAuthoring part,
            out float3 restPosition,
            out float2 restScale)
        {
            restPosition = float3.zero;
            restScale = new float2(1f, 1f);

            Transform actorTransform = GetComponent<Transform>(authoring);
            Transform currentTransform = GetComponent<Transform>(part);
            if (actorTransform == null || currentTransform == null)
            {
                return false;
            }

            float4x4 partToActor = float4x4.identity;
            while (currentTransform != actorTransform)
            {
                if (currentTransform != part.transform &&
                    GetComponent<ActorAuthoring>(currentTransform) != null)
                {
                    // A nested actor owns this part, not us.
                    return false;
                }
                partToActor = math.mul(ReadLocalMatrix(currentTransform), partToActor);
                Transform parentTransform = currentTransform.parent;
                if (parentTransform == null)
                {
                    return false;
                }
                currentTransform = GetComponent<Transform>(parentTransform);
                if (currentTransform == null)
                {
                    return false;
                }
            }

            restPosition = partToActor.c3.xyz;
            restScale = new float2(
                math.length(partToActor.c0.xyz),
                math.length(partToActor.c1.xyz));
            return true;
        }

        private static float4x4 ReadLocalMatrix(Transform transform)
        {
            Vector3 localPosition = transform.localPosition;
            Quaternion localRotation = transform.localRotation;
            Vector3 localScale = transform.localScale;
            return float4x4.TRS(
                new float3(localPosition.x, localPosition.y, localPosition.z),
                new quaternion(localRotation.x, localRotation.y, localRotation.z, localRotation.w),
                new float3(localScale.x, localScale.y, localScale.z));
        }

        // -----------------------------------------------------------------------------------
        // Remaining root components.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Derives the actor's crowd-sampling phase (architecture section 5.6) so two actors baked
        /// from the same prefab do not sample on the same frames. <c>RigBindingSystem</c> re-derives
        /// it per instance at spawn.
        /// </summary>
        /// <remarks>
        /// Derived from the authoring hierarchy path, not from an instance id. An instance id is
        /// session-local, so the same prefab would bake to a different phase every session and
        /// subscene bakes would stop being reproducible — the property section 4.5 spends real
        /// effort guaranteeing for the blob. A path hash keeps two sibling actors on different
        /// phases while making the bake a pure function of the source. Renaming or reparenting an
        /// actor changes its phase, which is harmless: the phase only spreads sampling load and
        /// carries no visual meaning.
        /// </remarks>
        private static float ComputeSamplePhase(ActorAuthoring authoring)
        {
            uint pathHash = AuthoringPathHash.Of(authoring.transform);
            return (pathHash & 0x00FFFFFFu) * (1f / 16777216f);
        }

        private static VatTextureBinding BuildVatTextureBinding(VatTextureSetAsset vatTextures)
        {
            if (vatTextures == null)
            {
                return default;
            }
            return new VatTextureBinding
            {
                setKey = vatTextures.SetKey,
                boneOrPositionTexture = vatTextures.flavor == VatFlavor.BoneMatrix
                    ? vatTextures.boneTexture
                    : vatTextures.positionTexture,
                normalTexture = vatTextures.normalTexture
            };
        }
    }
}
