// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Bakes an <see cref="ActorAuthoring"/> into the actor-root archetype: the shared registry and
    /// profile blobs, the seeded playback layers, the command and event channels, the
    /// binding/visibility/bounds enableables, and the actor-space <see cref="ActorRestBounds"/>.
    /// </summary>
    // The registry and profile blobs are built here, not in a baking system: both are pure
    // functions of ScriptableObject data with no cross-entity input, so DependsOn plus
    // AddBlobAssetWithCustomHash buy incremental-rebake correctness and store dedup for free.
    // Cross-entity work stays out of this baker on purpose — a baker may only write components on
    // the entity it is baking, so the parts' dense indices, the RigPartRef buffer and each part's
    // actorRoot are completed by RigBindingBakingSystem in PostBakingSystemGroup instead.
    public sealed class ActorBaker : Baker<ActorAuthoring>
    {
        private const string MessagePrefix = "[DOTS Animation Toolkit] ";

        /// <inheritdoc />
        public override void Bake(ActorAuthoring authoring)
        {
            ActorProfileAsset profile = DependsOn(authoring.profile);
            if (profile == null)
            {
                Debug.LogError(
                    MessagePrefix + "Actor '" + authoring.name +
                    "' has no profile assigned, so it cannot be baked. Assign an Actor Profile Asset.",
                    authoring);
                MarkBakeFailed();
                return;
            }

            RigAsset rig = DependsOn(profile.rig);
            List<ClipSetAsset> clipSets = DependsOnClipSets(profile);
            VatTextureSetAsset vatTextures = DependsOnClipSetContents(clipSets);
            DependsOnProfileAnimationClips(profile);

            if (rig == null)
            {
                Debug.LogError(
                    MessagePrefix + "Actor '" + authoring.name + "' profile '" + profile.name +
                    "' has no rig assigned, so it cannot be baked. Assign a Rig Asset to the profile.",
                    authoring);
                MarkBakeFailed();
                return;
            }

            if (clipSets.Count == 0)
            {
                Debug.LogError(
                    MessagePrefix + "Actor '" + authoring.name + "' profile '" + profile.name +
                    "' has no clip sets assigned, so it cannot be baked. Assign at least one Clip " +
                    "Set Asset to the profile.",
                    authoring);
                MarkBakeFailed();
                return;
            }

            if (!TryAcquireRegistry(
                    authoring, rig, clipSets, out BlobAssetReference<ClipRegistryBlob> registry))
            {
                MarkBakeFailed();
                return;
            }

            if (!TryAcquireProfileBlob(
                    authoring, profile, out BlobAssetReference<ActorProfileBlob> profileBlob))
            {
                MarkBakeFailed();
                return;
            }

            Entity actorEntity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(actorEntity, new ClipRegistry { Value = registry });
            AddComponent(actorEntity, new ActorProfile { Value = profileBlob });
            AddSocketRegistry(actorEntity, rig, vatTextures);
            AddPlaybackLayers(actorEntity, authoring, profile, registry, profileBlob);
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

            // Baked enabled: an ECB-instantiated copy starts enabled so RigBindingSystem rebinds it,
            // a first-frame bounds write is guaranteed, and everything animates until some provider says otherwise.
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
            // would split the root archetype in two to save four bytes.
            AddComponent(actorEntity, new AnimSampleState { sampledClipSignature = 0 });

            if (authoring.addDistanceLod)
            {
                AddComponent(actorEntity, new AnimLod { level = 0 });
            }

            // facing is host-written and never derived here; appliedFacing starts equal to it so
            // ActorFacingRepickSystem has nothing to reconcile on the first frame.
            AddComponent(actorEntity, new ActorFacing
            {
                facing = Direction.SouthEast,
                appliedFacing = Direction.SouthEast
            });

            // Baked disabled: only a Play/PlayAnimation command or an at-event trigger ever sets one,
            // and RagdollActor (opt-in, rig content) may not even be present to honour it.
            AddComponent<ActorRagdollRequest>(actorEntity);
            SetComponentEnabled<ActorRagdollRequest>(actorEntity, false);

            AddBillboardRoots(actorEntity, authoring, rig);
            AddRagdollBodies(actorEntity, authoring, rig);
        }

        // -----------------------------------------------------------------------------------
        // Billboarding.
        // -----------------------------------------------------------------------------------

        // All of an actor's billboard state is baked here, on this one entity: a billboard root may
        // be any node of the prefab, including a bare grouping transform with no baker of its own,
        // and a baker may only write the entity it is baking — so collecting roots into a buffer on
        // the actor is what lets one baker resolve the whole hierarchy without a cross-entity change.
        // GetEntity(..., Dynamic) on each root node is what guarantees a bare grouping node survives
        // baking as a real transform entity rather than being stripped. ActorAuthoring.billboardMode
        // is sugar for one root on the actor itself, folded in only when the rig does not already
        // declare a root there, so an explicit rig row always wins over the checkbox.
        /// <summary>Resolves the rig's billboard roots against this actor's hierarchy and bakes them into the actor's <see cref="BillboardRootElement"/> buffer, shallowest first.</summary>
        private void AddBillboardRoots(Entity actorEntity, ActorAuthoring authoring, RigAsset rig)
        {
            List<string> unresolvedRoots = new List<string>();
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, authoring.transform, unresolvedRoots);

            for (int messageIndex = 0; messageIndex < unresolvedRoots.Count; messageIndex++)
            {
                // V21's path half: only here is the prefab in hand, and only managed code can hand
                // the user a clickable object.
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

        // Degrees become radians and the two opt-in booleans become sentinels, both once at bake, so
        // nothing per actor per frame carries an extra field. The clamp arc is halved here since
        // every use of it is symmetric about the rest orientation.
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

        // -----------------------------------------------------------------------------------
        // Ragdoll bodies.
        // -----------------------------------------------------------------------------------

        // Structured the same way as AddBillboardRoots, for the same reason: a ragdoll body can be
        // any node of the prefab, including a bare grouping transform with no baker of its own.
        // Opt-in twice over: a rig with no ragdollBodies bakes nothing, and so does a rig whose
        // bodies exist but never resolve to a runtime node — either way there is nothing to
        // simulate, and archetype-splitting every actor for a component with zero elements is what
        // the opt-in components elsewhere in this package already refuse to do.
        /// <summary>
        /// Resolves the rig's ragdoll bodies against this actor's hierarchy and bakes them into the
        /// actor's <see cref="RagdollBody"/>/<see cref="RagdollRestPose"/> buffers, plus the
        /// <see cref="RagdollActor"/> toggle, <see cref="RagdollState"/> and
        /// <see cref="RagdollWorldContact"/> buffer every ragdoll needs alongside them.
        /// </summary>
        private void AddRagdollBodies(Entity actorEntity, ActorAuthoring authoring, RigAsset rig)
        {
            List<string> unresolvedBodies = new List<string>();
            List<string> boneOnlyBodies = new List<string>();
            List<ResolvedRagdollBody> resolvedBodies =
                RagdollBodyResolver.Resolve(rig, authoring.transform, unresolvedBodies, boneOnlyBodies);

            for (int messageIndex = 0; messageIndex < unresolvedBodies.Count; messageIndex++)
            {
                // V26's runtime half, the ragdoll counterpart to V21's billboard half above: only
                // here, with the prefab in hand, can an unresolved address be reported against a
                // clickable object.
                Debug.LogError(
                    MessagePrefix + "Ragdoll body " + unresolvedBodies[messageIndex] +
                    " on rig '" + rig.name + "' matches no node under actor '" + authoring.name +
                    "'. That body will not simulate. Fix the address on the rig, or rename the " +
                    "object back.",
                    authoring);
            }

            if (boneOnlyBodies.Count > 0)
            {
                // Not an error and not a warning: a skinned bone has no GameObject under a VAT
                // actor's runtime prefab at all, so these bodies author and editor-preview
                // completely but never gain a runtime node to simulate. Logged at the informational
                // tier so a rig deliberately mixing guiding-part and skinned-bone bodies does not
                // read as broken.
                StringBuilder boneListBuilder = new StringBuilder();
                for (int boneIndex = 0; boneIndex < boneOnlyBodies.Count; boneIndex++)
                {
                    if (boneIndex > 0)
                    {
                        boneListBuilder.Append(", ");
                    }
                    boneListBuilder.Append(boneOnlyBodies[boneIndex]);
                }
                Debug.Log(
                    MessagePrefix + "Actor '" + authoring.name + "' has " +
                    boneOnlyBodies.Count.ToString() + " ragdoll body(ies) addressing a skinned bone " +
                    "(" + boneListBuilder.ToString() + "): these author and editor-preview but do " +
                    "not simulate at runtime, because a VAT actor has no bone entity for them to " +
                    "move.",
                    authoring);
            }

            if (resolvedBodies.Count == 0)
            {
                return;
            }

            WarnIfDisconnected(authoring, rig, resolvedBodies);

            AddComponent<RagdollActor>(actorEntity);
            SetComponentEnabled<RagdollActor>(actorEntity, false);

            DynamicBuffer<RagdollBody> bodyElements = AddBuffer<RagdollBody>(actorEntity);
            DynamicBuffer<RagdollRestPose> restPoseElements = AddBuffer<RagdollRestPose>(actorEntity);
            RagdollRigSettings rigSettings = rig.ragdollSettings;

            for (int bodyIndex = 0; bodyIndex < resolvedBodies.Count; bodyIndex++)
            {
                ResolvedRagdollBody resolvedBody = resolvedBodies[bodyIndex];
                bodyElements.Add(new RagdollBody
                {
                    bodyId = resolvedBody.definition.Id,
                    node = GetEntity(resolvedBody.node.gameObject, TransformUsageFlags.Dynamic),
                    parentBodyIndex = resolvedBody.parentBodyIndex,
                    parameters = BuildBodyParams(resolvedBody, rigSettings),
                    state = default
                });

                // Identity, never read until RagdollCaptureSystem writes a real value into this slot.
                restPoseElements.Add(new RagdollRestPose
                {
                    localTransform = LocalTransform.Identity,
                    postTransformMatrix = new PostTransformMatrix { Value = float4x4.identity }
                });
            }

            AddComponent(actorEntity, new RagdollState
            {
                frameRotation = quaternion.identity,
                planeNormal = new float3(0f, 0f, 1f),
                // Never read until RagdollCaptureSystem seeds a real value on the first switch-on.
                planeOrigin = float3.zero,
                substepAccumulator = 0f,
                sleepTimer = 0f,
                // An actor is baked with RagdollActor disabled, so its very first enable must always
                // capture — there is no earlier drop to have already seeded RagdollRestPose from.
                flags = RagdollStateFlags.CaptureNeeded
            });

            AddBuffer<RagdollWorldContact>(actorEntity);

            // RagdollRigSettings' rig-wide solver knobs have nowhere else to land at runtime, so
            // they are baked once here rather than re-read from RigAsset every step.
            AddComponent(actorEntity, new RagdollRigConfig
            {
                space = rigSettings.space,
                gravityScale = rigSettings.gravityScale,
                jointStiffness = rigSettings.jointStiffness,
                jointDamping = rigSettings.jointDamping,
                solverIterations = rigSettings.solverIterations,
                substepDeltaTime = rigSettings.substepHz > 0f ? 1f / rigSettings.substepHz : 1f / 120f
            });
        }

        // Made authoritative here, not only at authoring time, because this pass already walks the
        // resolved hierarchy to compute parentBodyIndex — counting how many come back -1 is free.
        /// <summary>V31's runtime counterpart: more than one resolved body with no ragdolled ancestor is a disconnected ragdoll. A warning, since two articulations on one rig is odd but simulable.</summary>
        private static void WarnIfDisconnected(
            ActorAuthoring authoring, RigAsset rig, List<ResolvedRagdollBody> resolvedBodies)
        {
            int disconnectedRootCount = 0;
            for (int bodyIndex = 0; bodyIndex < resolvedBodies.Count; bodyIndex++)
            {
                if (resolvedBodies[bodyIndex].parentBodyIndex < 0)
                {
                    disconnectedRootCount++;
                }
            }
            if (disconnectedRootCount <= 1)
            {
                return;
            }
            Debug.LogWarning(
                MessagePrefix + "Rig '" + rig.name + "' resolves " +
                disconnectedRootCount.ToString() + " ragdoll bodies with no ragdolled ancestor " +
                "under actor '" + authoring.name + "'. A ragdoll is meant to be a single connected " +
                "tree (rule V-R6); the extra roots will simulate as separate, disconnected " +
                "articulations.",
                authoring);
        }

        // Degrees become radians, the box's full size becomes half-extents, mass and inertia are
        // inverted, and the -1 damping sentinels are resolved — all through the same shared
        // functions the editor preview builder calls too, so neither side can drift from the other.
        private static RagdollBodyParams BuildBodyParams(
            ResolvedRagdollBody resolvedBody, RagdollRigSettings rigSettings)
        {
            RagdollBodyDefinition definition = resolvedBody.definition;
            float3 boxHalfExtents = math.max(definition.boxSize, float3.zero) * 0.5f;

            RagdollSolver.ComputeBoxInverseInertia(
                definition.mass, in boxHalfExtents, out float invMass, out float3 invInertiaDiagonal);
            RagdollSolver.ResolveDampingSentinel(
                definition.linearDamping, rigSettings.defaultLinearDamping, out float resolvedLinearDamping);
            RagdollSolver.ResolveDampingSentinel(
                definition.angularDamping, rigSettings.defaultAngularDamping, out float resolvedAngularDamping);

            bool isRoot = resolvedBody.parentBodyIndex < 0;
            RagdollBodyFlags flags = RagdollBodyFlags.None;
            if (definition.collidesWithWorld)
            {
                flags |= RagdollBodyFlags.CollidesWithWorld;
            }
            if (isRoot)
            {
                flags |= RagdollBodyFlags.IsRoot;
            }

            return new RagdollBodyParams
            {
                boxCenter = definition.boxCenter,
                boxHalfExtents = boxHalfExtents,
                boxRotation = quaternion.Euler(math.radians(definition.boxEulerAngles)),
                invMass = invMass,
                invInertiaDiagonal = invInertiaDiagonal,
                linearDamping = resolvedLinearDamping,
                angularDamping = resolvedAngularDamping,
                restitution = definition.restitution,
                friction = definition.friction,
                limitMin = math.radians(definition.limitMinDegrees),
                limitMax = math.radians(definition.limitMaxDegrees),
                swingLimit = math.radians(definition.swingLimitDegrees),
                twistLimit = math.radians(definition.twistLimitDegrees),
                restRelativeRotation = resolvedBody.restRelativeRotation,
                parentAnchorOffset = resolvedBody.parentAnchorOffset,
                parentBodyIndex = resolvedBody.parentBodyIndex,
                selfGroup = definition.selfGroup,
                selfCollidesWith = definition.selfCollidesWith,
                flags = flags
            };
        }

        // Every early return above must call this — the binding pass suppresses its per-part
        // complaint only when the tag is present, so a registry loss without this tag would be
        // silently unreported instead of already-explained.
        // TransformUsageFlags.None deliberately: this baker only needs the entity to exist to write
        // the tag and has no opinion on how the actor should be transformed.
        /// <summary>Records on the actor entity that this bake reported a failure and stopped, so <see cref="RigBindingBakingSystem"/> knows why its parts are unbound.</summary>
        private void MarkBakeFailed()
        {
            AddComponent<ActorBakeFailed>(GetEntity(TransformUsageFlags.None));
        }

        // -----------------------------------------------------------------------------------
        // Dependencies. Every asset the blob is a function of must retrigger this bake when edited.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Registers a dependency on every set the profile names and returns them in canonical bind
        /// order, nulls and repeats dropped — the same list the builder will see.
        /// </summary>
        private List<ClipSetAsset> DependsOnClipSets(ActorProfileAsset profile)
        {
            List<ClipSetAsset> authoredClipSets = profile.clipSets;
            if (authoredClipSets != null)
            {
                for (int setIndex = 0; setIndex < authoredClipSets.Count; setIndex++)
                {
                    DependsOn(authoredClipSets[setIndex]);
                }
            }
            return ClipRegistryBuilder.BuildCanonicalClipSets(authoredClipSets);
        }

        /// <summary>
        /// Registers a dependency on every clip and VAT texture the bound sets reach, and returns
        /// the one texture set the bind addresses (a second one is an error).
        /// </summary>
        private VatTextureSetAsset DependsOnClipSetContents(List<ClipSetAsset> clipSets)
        {
            VatTextureSetAsset bindVatTextures = null;
            for (int setIndex = 0; setIndex < clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = clipSets[setIndex];
                List<ClipAsset> clips = clipSet.clips;
                if (clips != null)
                {
                    for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                    {
                        DependsOn(clips[clipIndex]);
                    }
                }

                VatTextureSetAsset vatTextures = DependsOn(clipSet.vatTextures);
                DependsOnVatTextures(vatTextures);
                if (bindVatTextures == null)
                {
                    bindVatTextures = vatTextures;
                }
            }
            return bindVatTextures;
        }

        /// <summary>Registers a dependency on every clip an animation entry names directly, since those references live outside the bound clip sets' own lists.</summary>
        private void DependsOnProfileAnimationClips(ActorProfileAsset profile)
        {
            List<ActorLayerDefinition> layers = profile.layers;
            if (layers == null)
            {
                return;
            }
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                ActorLayerDefinition layer = layers[layerIndex];
                if (layer == null || layer.animations == null)
                {
                    continue;
                }
                for (int animationIndex = 0; animationIndex < layer.animations.Count; animationIndex++)
                {
                    ActorAnimationDefinition animation = layer.animations[animationIndex];
                    if (animation == null)
                    {
                        continue;
                    }
                    if (animation.hasDirections)
                    {
                        DependsOnDirectionSlotClips(animation.directionSlots);
                    }
                    else
                    {
                        DependsOn(animation.clip);
                    }
                }
            }
        }

        private void DependsOnDirectionSlotClips(DirectionSlots directionSlots)
        {
            if (directionSlots == null)
            {
                return;
            }
            DependsOn(directionSlots.southEast);
            DependsOn(directionSlots.northEast);
            DependsOn(directionSlots.south);
            DependsOn(directionSlots.north);
            DependsOn(directionSlots.east);
        }

        private void DependsOnVatTextures(VatTextureSetAsset vatTextures)
        {
            if (vatTextures == null || vatTextures.parts == null)
            {
                return;
            }
            for (int partIndex = 0; partIndex < vatTextures.parts.Count; partIndex++)
            {
                VatPartTextures part = vatTextures.parts[partIndex];
                if (part == null)
                {
                    continue;
                }
                DependsOn(part.boneTexture);
                DependsOn(part.positionTexture);
                DependsOn(part.normalTexture);
            }
        }

        // -----------------------------------------------------------------------------------
        // Registry blob: the canonical probe / store-hit / build / register pattern.
        // -----------------------------------------------------------------------------------

        // Absent by design on rigs without sockets, so SocketResolveSystem's query excludes them
        // entirely rather than filtering every frame. Keyed on the rig and the texture set
        // together: socket motion is baked from the textures' source, so a rebake that moves a hand
        // must produce a different key or every actor keeps the stale attachment path from the store.
        /// <summary>Adds the socket registry, when the rig declares sockets.</summary>
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
            RigAsset rig,
            List<ClipSetAsset> clipSets,
            out BlobAssetReference<ClipRegistryBlob> registry)
        {
            // The probe computes the dedup key without handing back a blob to own, so a store hit
            // costs no persistent allocation and leaves nothing to dispose.
            if (ClipRegistryBuilder.TryComputeContentHash(
                    rig, clipSets, out Unity.Entities.Hash128 contentHash) &&
                TryGetBlobAssetReference(contentHash, out registry))
            {
                return true;
            }

            try
            {
                ClipRegistryBuilder.Build(rig, clipSets, out registry, out contentHash);
            }
            catch (ClipValidationException validationException)
            {
                Debug.LogError(
                    DescribeValidationFailure(rig, clipSets, validationException),
                    authoring);
                registry = default;
                return false;
            }

            // Ownership of the freshly built blob passes to the store here, and stays there.
            AddBlobAssetWithCustomHash(ref registry, contentHash);
            return true;
        }

        private static string DescribeValidationFailure(
            RigAsset rig,
            List<ClipSetAsset> clipSets,
            ClipValidationException validationException)
        {
            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.Append(MessagePrefix);
            messageBuilder.Append("Rig '");
            messageBuilder.Append(rig.name);
            messageBuilder.Append("' bound to ");
            messageBuilder.Append(DescribeClipSets(clipSets));
            messageBuilder.Append(" cannot be baked because it has validation errors:");
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
        // Profile blob: mirrors TryAcquireRegistry's probe / build / register pattern.
        // -----------------------------------------------------------------------------------

        private bool TryAcquireProfileBlob(
            ActorAuthoring authoring,
            ActorProfileAsset profile,
            out BlobAssetReference<ActorProfileBlob> profileBlob)
        {
            ulong contentHash64 = ActorProfileBuilder.ComputeContentHash(profile);
            Unity.Entities.Hash128 contentHash = new Unity.Entities.Hash128(
                (uint)contentHash64,
                (uint)(contentHash64 >> 32),
                (uint)ActorProfileBuilder.SchemaVersion,
                (uint)profile.StableId ^ (uint)(profile.StableId >> 32));

            if (contentHash64 != 0UL && TryGetBlobAssetReference(contentHash, out profileBlob))
            {
                return true;
            }

            try
            {
                profileBlob = ActorProfileBuilder.Build(profile, Allocator.Persistent);
            }
            catch (ClipValidationException validationException)
            {
                Debug.LogError(DescribeProfileValidationFailure(profile, validationException), authoring);
                profileBlob = default;
                return false;
            }

            // Ownership of the freshly built blob passes to the store here, and stays there.
            AddBlobAssetWithCustomHash(ref profileBlob, contentHash);
            return true;
        }

        private static string DescribeProfileValidationFailure(
            ActorProfileAsset profile,
            ClipValidationException validationException)
        {
            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.Append(MessagePrefix);
            messageBuilder.Append("Profile '");
            messageBuilder.Append(profile.name);
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

        /// <summary>Renders a bind's sets as <c>clip sets 'Walks', 'Reactions'</c> for a message.</summary>
        private static string DescribeClipSets(List<ClipSetAsset> clipSets)
        {
            if (clipSets.Count == 0)
            {
                return "no clip sets";
            }
            StringBuilder messageBuilder = new StringBuilder();
            messageBuilder.Append(clipSets.Count == 1 ? "clip set " : "clip sets ");
            for (int setIndex = 0; setIndex < clipSets.Count; setIndex++)
            {
                if (setIndex > 0)
                {
                    messageBuilder.Append(", ");
                }
                messageBuilder.Append('\'');
                messageBuilder.Append(clipSets[setIndex].name);
                messageBuilder.Append('\'');
            }
            return messageBuilder.ToString();
        }

        // -----------------------------------------------------------------------------------
        // Playback layers.
        // -----------------------------------------------------------------------------------

        private void AddPlaybackLayers(
            Entity actorEntity,
            ActorAuthoring authoring,
            ActorProfileAsset profile,
            BlobAssetReference<ClipRegistryBlob> registry,
            BlobAssetReference<ActorProfileBlob> profileBlob)
        {
            int layerCount = profile.layers == null ? 0 : profile.layers.Count;
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

            SeedStartingLayers(authoring, profile, registry, profileBlob, playbackLayers);
        }

        // A layer's startingAnimationKey seeds whichever layer the named entry actually lives on
        // (ActorProfileApi.TryResolve's own answer), not necessarily the layer that named the key —
        // the same routing PlaybackApi.PlayAnimation uses at runtime. P7 warns at validation when
        // the two disagree; this is where that disagreement actually plays out.
        private static void SeedStartingLayers(
            ActorAuthoring authoring,
            ActorProfileAsset profile,
            BlobAssetReference<ClipRegistryBlob> registry,
            BlobAssetReference<ActorProfileBlob> profileBlob,
            DynamicBuffer<PlaybackLayer> playbackLayers)
        {
            List<ActorLayerDefinition> layers = profile.layers;
            if (layers == null)
            {
                return;
            }

            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                ActorLayerDefinition layer = layers[layerIndex];
                if (layer == null || layer.startingAnimationKey == 0u)
                {
                    continue;
                }

                if (!ActorProfileApi.TryResolve(
                        ref profileBlob.Value,
                        layer.startingAnimationKey,
                        Direction.SouthEast,
                        out byte resolvedLayerIndex,
                        out ClipId resolvedClip,
                        out int animationIndex))
                {
                    Debug.LogError(
                        MessagePrefix + "Actor '" + authoring.name + "' profile '" + profile.name +
                        "' layer " + layerIndex.ToString() + " names starting animation id " +
                        layer.startingAnimationKey.ToString() +
                        ", which does not resolve. The entry is ignored.",
                        authoring);
                    continue;
                }

                if (resolvedLayerIndex >= playbackLayers.Length)
                {
                    continue;
                }

                if (!ClipRegistryApi.TryResolveClip(ref registry.Value, resolvedClip, out int clipIndex))
                {
                    Debug.LogError(
                        MessagePrefix + "Actor '" + authoring.name + "' profile '" + profile.name +
                        "' layer " + layerIndex.ToString() + " names starting animation id " +
                        layer.startingAnimationKey.ToString() +
                        ", whose clip is not a member of this actor's clip sets. The entry is ignored.",
                        authoring);
                    continue;
                }

                ref ActorAnimationBlob animationBlob = ref profileBlob.Value.animations[animationIndex];

                PlaybackLayer playbackLayer = playbackLayers[resolvedLayerIndex];
                playbackLayer.clip = resolvedClip;
                playbackLayer.clipIndex = clipIndex;
                playbackLayer.time = 0f;
                playbackLayer.speed = animationBlob.speed;
                playbackLayer.loop = animationBlob.loop;
                // A seeded layer is playing a named animation, so IsAnimationPlaying answers for it.
                playbackLayer.animationKey = layer.startingAnimationKey;
                // An explicitly seeded clip activates its layer, whatever the profile's defaultActive
                // says — otherwise an actor could name a starting animation and have it silently
                // never play. defaultActive keeps its meaning for layers with no seeded animation.
                playbackLayer.flags |= PlaybackFlags.Active;
                playbackLayers[resolvedLayerIndex] = playbackLayer;
            }

            WarnAboutActiveLayersWithoutAClip(authoring, profile, playbackLayers);
        }

        private static bool IsLayerActiveByDefault(ActorProfileAsset profile, int layerIndex)
        {
            if (profile.layers == null || layerIndex >= profile.layers.Count)
            {
                return false;
            }
            ActorLayerDefinition layerDefinition = profile.layers[layerIndex];
            return layerDefinition != null && layerDefinition.defaultActive;
        }

        private static void WarnAboutActiveLayersWithoutAClip(
            ActorAuthoring authoring,
            ActorProfileAsset profile,
            DynamicBuffer<PlaybackLayer> playbackLayers)
        {
            for (int layerIndex = 0; layerIndex < playbackLayers.Length; layerIndex++)
            {
                if (playbackLayers[layerIndex].clipIndex >= 0 || !IsLayerActiveByDefault(profile, layerIndex))
                {
                    continue;
                }
                Debug.LogWarning(
                    MessagePrefix + "Profile '" + profile.name + "' marks layer " + layerIndex.ToString() +
                    " as active by default, but actor '" + authoring.name +
                    "' seeds no starting animation for it, so the layer starts stopped.",
                    authoring);
            }
        }

        // -----------------------------------------------------------------------------------
        // Actor-space rest bounds.
        // -----------------------------------------------------------------------------------

        // This is the half of the bounds problem ClipRegistryBuilder structurally cannot solve:
        // its offset-space bounds are centred on the origin, because the builder sees only a
        // ClipSetAsset graph and never the prefab holding the rest poses. This baker does see the
        // prefab, so it measures the rest frame here and the runtime combines the two.
        /// <summary>Unions, in actor space, the box each bound part occupies at its rest pose.</summary>
        private AABB ComputeActorRestBounds(
            ActorAuthoring authoring,
            BlobAssetReference<ClipRegistryBlob> registry)
        {
            MinMaxAABB restBounds = MinMaxAABB.Empty;
            bool anyPartBounded = false;

            // Inactive children included deliberately: Baker.GetComponentsInChildren defaults to
            // include-inactive (unlike the GameObject method of the same name), which matches
            // RigBindingBakingSystem's IncludeDisabledEntities queries — the two passes must agree
            // on which parts exist, or the actor pops the moment a disabled part is re-enabled.
            RigTargetAuthoring[] parts = GetComponentsInChildren<RigTargetAuthoring>();
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                RigTargetAuthoring part = parts[partIndex];
                if (part == null)
                {
                    continue;
                }
                // Unknown targets are reported once, by RigTargetBaker; this pass stays silent and
                // simply leaves them out of the rest frame.
                if (!ClipRegistryApi.TryResolveTarget(
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

        // Multiplied out from local transforms rather than read from world matrices: the result
        // must not change when the same prefab is placed elsewhere in the scene.
        /// <summary>Accumulates the local transforms between a part and its actor root, taking a bake dependency on each one so moving any intermediate pivot retriggers the bake.</summary>
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

        // Derived from the authoring hierarchy path, not an instance id: an instance id is
        // session-local, so the same prefab would bake to a different phase every session and
        // subscene bakes would stop being reproducible. Renaming or reparenting an actor changes
        // its phase, which is harmless — the phase only spreads sampling load.
        /// <summary>Derives the actor's crowd-sampling phase, so two actors baked from the same prefab do not sample on the same frames.</summary>
        private float ComputeSamplePhase(ActorAuthoring authoring)
        {
            // Bits 8-31, not 0-23: FNV-1a ends on a multiply, which propagates carries only upward,
            // so the low byte is the least mixed.
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
                setKey = vatTextures.SetKey
            };
        }
    }
}
