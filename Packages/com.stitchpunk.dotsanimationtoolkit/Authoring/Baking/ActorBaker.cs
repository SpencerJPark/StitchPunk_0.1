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
                MarkBakeFailed();
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
                MarkBakeFailed();
                return;
            }

            if (!TryAcquireRegistry(authoring, clipSet, out BlobAssetReference<ClipRegistryBlob> registry))
            {
                MarkBakeFailed();
                return;
            }

            Entity actorEntity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(actorEntity, new ClipRegistry { Value = registry });
            AddSocketRegistry(actorEntity, rig, vatTextures);
            AddPlaybackLayers(actorEntity, authoring, rig, registry);
            AddBuffer<AnimationCommand>(actorEntity);
            AddComponent<AnimationCommandPending>(actorEntity);
            SetComponentEnabled<AnimationCommandPending>(actorEntity, false);
            AddBuffer<AnimEventOutput>(actorEntity);
            AddComponent<AnimEventsPending>(actorEntity);
            SetComponentEnabled<AnimEventsPending>(actorEntity, false);

            // Baked disabled for the same reason AnimEventsPending is: an actor standing still holds
            // no windows, and EventWindowSystem enables it the first frame one opens.
            AddComponent(actorEntity, new AnimEventMask { bits = 0UL });
            SetComponentEnabled<AnimEventMask>(actorEntity, false);

            AddBuffer<RigPartRef>(actorEntity);

            // Baked enabled: an ECB-instantiated copy starts enabled so that RigBindingSystem will
            // rebind it once C4 adds that system (section 5.3); a first-frame bounds write is
            // guaranteed (section 5.8); and everything animates until some provider says otherwise
            // (section 5.9).
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

            // Unconditional, unlike AnimLod below: nothing queries on it, so a conditional add
            // would split the root archetype in two to save four bytes (amendment A34).
            AddComponent(actorEntity, new AnimSampleState { sampledClipSignature = 0 });

            if (authoring.addDistanceLod)
            {
                AddComponent(actorEntity, new AnimLod { level = 0 });
            }

            AddBillboardRoots(actorEntity, authoring, rig);
        }

        // -----------------------------------------------------------------------------------
        // Billboarding (amendment A44).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Resolves the rig's billboard roots against this actor's hierarchy and bakes them into the
        /// actor's <see cref="BillboardRootElement"/> buffer, shallowest first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>All of an actor's billboard state is baked here, on this one entity.</strong> A
        /// billboard root may be any node of the prefab, including a bare grouping transform that
        /// carries no authoring component and so has no baker of its own. A baker may only write the
        /// entity it is baking, so collecting the roots into a buffer on the actor is what lets one
        /// baker resolve the whole hierarchy without a cross-entity structural change.
        /// </para>
        /// <para>
        /// <c>GetEntity</c> with <see cref="TransformUsageFlags.Dynamic"/> on each root node is not
        /// incidental: it is what guarantees a bare grouping node survives baking as a real
        /// transform entity rather than being stripped for having nothing to render.
        /// </para>
        /// <para>
        /// <strong><c>ActorAuthoring.billboardMode</c> is sugar for one root on the actor
        /// itself</strong> (A41's whole-actor billboard, kept working). It is folded in only when the
        /// rig does not already declare a root for the actor root, so an explicit rig row always
        /// wins over the checkbox — the rig is the shared, authoritative place and the component is
        /// the convenience.
        /// </para>
        /// </remarks>
        private void AddBillboardRoots(Entity actorEntity, ActorAuthoring authoring, RigAsset rig)
        {
            List<string> unresolvedRoots = new List<string>();
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, authoring.transform, unresolvedRoots);

            for (int messageIndex = 0; messageIndex < unresolvedRoots.Count; messageIndex++)
            {
                // Validation rule V21's path half. ClipValidation resolves target addresses against
                // the rig; only here is the prefab in hand, and only managed code can hand the user
                // a clickable object.
                Debug.LogError(
                    MessagePrefix + "Billboard root " + unresolvedRoots[messageIndex] +
                    " on rig '" + rig.name + "' matches no node under actor '" + authoring.name +
                    "'. That node will not billboard. Fix the address on the rig, or rename the " +
                    "object back.",
                    authoring);
            }

            bool actorRootAlreadyDeclared = false;
            for (int rootIndex = 0; rootIndex < resolvedRoots.Count; rootIndex++)
            {
                if (resolvedRoots[rootIndex].node == authoring.transform)
                {
                    actorRootAlreadyDeclared = true;
                    break;
                }
            }

            bool hasImplicitActorRoot =
                authoring.billboardMode != BillboardMode.Off && !actorRootAlreadyDeclared;

            if (resolvedRoots.Count == 0 && !hasImplicitActorRoot)
            {
                // Opt-in, like AnimLod and PartFacing: a rig that never billboards bakes no buffer
                // and its actors pay nothing.
                return;
            }

            DynamicBuffer<BillboardRootElement> rootElements =
                AddBuffer<BillboardRootElement>(actorEntity);

            if (hasImplicitActorRoot)
            {
                // Depth 0, so it belongs first — and it is added first for exactly that reason.
                rootElements.Add(new BillboardRootElement
                {
                    rootId = 0u,
                    node = actorEntity,
                    settings = new BillboardSettings
                    {
                        mode = authoring.billboardMode,
                        constraintAxis = new float3(0f, 1f, 0f),
                        frozenYaw = math.radians(authoring.frozenYawDegrees),
                        angleOffsetRadians = 0f,
                        blendWeight = 1f,
                        enabled = true,
                        snapSteps = 0,
                        snapPhaseRadians = 0f,
                        clampHalfArcRadians = -1f
                    },
                    resolvedRotation = quaternion.identity
                });
            }

            for (int rootIndex = 0; rootIndex < resolvedRoots.Count; rootIndex++)
            {
                ResolvedBillboardRoot resolvedRoot = resolvedRoots[rootIndex];
                rootElements.Add(new BillboardRootElement
                {
                    rootId = resolvedRoot.definition.stableId,
                    node = GetEntity(resolvedRoot.node.gameObject, TransformUsageFlags.Dynamic),
                    settings = BuildBillboardSettings(resolvedRoot.definition),
                    resolvedRotation = quaternion.identity
                });
            }
        }

        /// <summary>
        /// Converts one authored billboard root into the runtime parameter block.
        /// </summary>
        /// <remarks>
        /// Degrees become radians and the two opt-in booleans become sentinels, both once at bake:
        /// degrees are what an author types and radians are what trigonometry consumes, and a
        /// sentinel is one field rather than two to carry per actor per frame. The clamp arc is
        /// halved here for the same reason — every use of it is symmetric about the rest
        /// orientation.
        /// </remarks>
        private static BillboardSettings BuildBillboardSettings(BillboardRootDefinition definition)
        {
            return new BillboardSettings
            {
                mode = definition.mode,
                constraintAxis = math.normalizesafe(definition.constraintAxis),
                frozenYaw = 0f,
                angleOffsetRadians = math.radians(definition.angleOffsetDegrees),
                blendWeight = 1f,
                enabled = true,
                snapSteps = definition.snapEnabled ? math.max(2, definition.snapSteps) : 0,
                snapPhaseRadians = math.radians(definition.snapOffsetDegrees),
                clampHalfArcRadians = definition.clampEnabled
                    ? math.radians(definition.clampArcDegrees) * 0.5f
                    : -1f
            };
        }

        /// <summary>
        /// Records on the actor entity that this bake reported a failure and stopped, so
        /// <see cref="RigBindingBakingSystem"/> knows the parts under it are unbound for a reason
        /// that has already been printed (architecture section 4.1, amendment A22).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every early return above must call this. The binding pass suppresses its per-part
        /// complaint only when the tag is present, so an actor that loses its registry without one
        /// is reported rather than passed over in silence — which is the whole point of making the
        /// signal explicit instead of inferring it from "no registry".
        /// </para>
        /// <para>
        /// <c>TransformUsageFlags.None</c> deliberately: this baker needs the entity to exist so it
        /// can write the tag, and has no opinion left about how the actor should be transformed. A
        /// failed actor with parts still gets <c>Dynamic</c> from <see cref="RigTargetBaker"/>,
        /// which is the only remaining claim worth honouring.
        /// </para>
        /// </remarks>
        private void MarkBakeFailed()
        {
            AddComponent<ActorBakeFailed>(GetEntity(TransformUsageFlags.None));
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

        /// <summary>
        /// Adds the socket registry, when the rig declares sockets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Absent by design on rigs without sockets, so <c>SocketResolveSystem</c>'s query excludes
        /// them entirely rather than filtering them every frame — the same opt-in-component shape
        /// the rest of the toolkit uses.
        /// </para>
        /// <para>
        /// The blob is keyed on the rig and the texture set together. Socket motion is baked from
        /// the textures' source, so a rebake that moves a hand must produce a different key or
        /// every actor would keep the stale attachment path from the store.
        /// </para>
        /// </remarks>
        private void AddSocketRegistry(Entity actorEntity, RigAsset rig, VatTextureSetAsset vatTextures)
        {
            if (!SocketRegistryBuilder.HasSockets(rig))
            {
                return;
            }

            uint textureSetHash = vatTextures != null ? (uint)vatTextures.SetKey : 0u;
            Unity.Entities.Hash128 socketHash = new Unity.Entities.Hash128(
                (uint)(rig.StableId & 0xFFFFFFFFUL),
                (uint)(rig.StableId >> 32),
                textureSetHash,
                (uint)SocketRegistryBuilder.SchemaVersion);

            if (!TryGetBlobAssetReference(socketHash, out BlobAssetReference<SocketRegistryBlob> socketRegistry))
            {
                if (!SocketRegistryBuilder.TryBuild(rig, vatTextures, out socketRegistry))
                {
                    return;
                }
                AddBlobAssetWithCustomHash(ref socketRegistry, socketHash);
            }

            AddComponent(actorEntity, new SocketRegistry { Value = socketRegistry });
        }

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
                // Amendment A40: an explicitly seeded clip activates its layer, whatever the rig's
                // `defaultActive` says. Gating this on the rig meant an actor could name a clip for
                // a layer and have it silently never play — and there is no sensible authoring
                // intent behind "start this layer with this clip, but stopped". `defaultActive`
                // keeps its meaning for layers with no seeded clip, which is the case the warning
                // below already flags.
                playbackLayer.flags |= PlaybackFlags.Active;
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

            // Inactive children are included, and that is deliberate even though nothing here says
            // so: Baker.GetComponentsInChildren defaults to include-inactive, unlike the GameObject
            // method of the same name, and takes no bool to say otherwise. The default is the
            // behaviour this pass wants — RigBindingBakingSystem's queries carry
            // IncludeDisabledEntities, so a part on a disabled GameObject is still bound and still
            // animates, and leaving it out of the box would pop the actor the moment that part is
            // re-enabled at runtime. The two passes have to agree about which parts exist.
            RigTargetAuthoring[] parts = GetComponentsInChildren<RigTargetAuthoring>();
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                RigTargetAuthoring part = parts[partIndex];
                if (part == null)
                {
                    continue;
                }
                // Unknown targets are reported once, by RigTargetBaker, which can name the part and
                // the rig that does not declare its id (amendment A22); this pass stays silent
                // about them and simply leaves them out of the rest frame.
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
        /// from the same prefab do not sample on the same frames. The baked value is the runtime
        /// value today; <c>RigBindingSystem</c>, which build step C4 will add, is specified to
        /// re-derive it per instance at spawn.
        /// </summary>
        /// <remarks>
        /// Derived from the authoring hierarchy path, not from an instance id. An instance id is
        /// session-local, so the same prefab would bake to a different phase every session and
        /// subscene bakes would stop being reproducible — the property section 4.5 spends real
        /// effort guaranteeing for the blob. A path hash keeps two sibling actors on different
        /// phases while making the bake a function of the source rather than of the session.
        /// Renaming or reparenting an actor changes its phase, which is harmless: the phase only
        /// spreads sampling load and carries no visual meaning. Both of those edits retrigger the
        /// bake, because <see cref="AuthoringPathHash"/> reads names and the ancestor chain through
        /// the baker's dependency-taking API; reordering siblings does not, which is the one case
        /// where an incremental bake and a clean bake can disagree, and it changes only which frame
        /// the actor samples on.
        /// </remarks>
        private float ComputeSamplePhase(ActorAuthoring authoring)
        {
            // Bits 8–31, not 0–23: FNV-1a ends on a multiply, which propagates carries only upward,
            // so the low byte is the least mixed. Untestable by construction and unobservable in
            // this walk order — amendment A18 and open item A-4 in the architecture carry the why.
            uint pathHash = AuthoringPathHash.Of(this, authoring.transform);
            return (pathHash >> 8) * (1f / 16777216f);
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
