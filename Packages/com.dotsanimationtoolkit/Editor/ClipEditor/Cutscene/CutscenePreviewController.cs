// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Non-destructive Scene-view preview and gizmo keying: clip blocks, seam crossfades, root
    /// motion and part-track overrides, posed onto the real bound actors (Phase G decision G-D1,
    /// amendment A58 §3.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Poses real scene GameObjects, never a mirror.</strong> Spec §3 makes Unity's own Scene
    /// view the viewport, so unlike the Clip Editor's <c>ClipPreviewController</c> (a
    /// <c>PreviewRenderUtility</c> instance over its own mirrored hierarchy) this writes straight
    /// onto the bound actors — which is also what makes Unity's built-in Move/Rotate/Scale gizmos
    /// work on them for free the moment one is selected; nothing here draws a custom gizmo.
    /// </para>
    /// <para>
    /// <strong>Entering capture, leaving restore, exactly.</strong> <see cref="EnterPreview"/>
    /// snapshots every bound GameObject's local transform, every bound part's, and every bound
    /// part renderer's material property block, before this controller ever writes to them;
    /// <see cref="ExitPreview"/> writes every snapshot back unconditionally. Nothing here uses Undo
    /// for the pose write/restore cycle — Undo is for authored changes (a keyed value), and a scrub
    /// is not one.
    /// </para>
    /// <para>
    /// <strong>The clip lane is sampled through the runtime's own sampler.</strong> Each actor slot
    /// builds the <c>ClipRegistryBlob</c> its (rig, clip sets) bind would bake
    /// (<see cref="CutsceneSlotClipPreview"/>) and every part goes through
    /// <c>ClipSampler.SamplePose</c> against a rest pose captured by the same
    /// <see cref="RestPoseCapture"/> the bake uses. Block phase and seam weight come from
    /// <see cref="CutsceneBlockTiming"/>, the one copy the runtime player also reads (A58-D1), so
    /// there is no second animation pipeline to drift.
    /// </para>
    /// </remarks>
    internal sealed class CutscenePreviewController
    {
        private static readonly int ImageIndexPropertyId = Shader.PropertyToID("_ImageIndex");
        private static readonly int AtlasFramePropertyId = Shader.PropertyToID("_AtlasFrame");

        private sealed class TransformSnapshot
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;

            public static TransformSnapshot Capture(Transform transform)
            {
                return new TransformSnapshot
                {
                    localPosition = transform.localPosition,
                    localRotation = transform.localRotation,
                    localScale = transform.localScale
                };
            }

            public void RestoreTo(Transform transform)
            {
                transform.localPosition = localPosition;
                transform.localRotation = localRotation;
                transform.localScale = localScale;
            }
        }

        /// <summary>One bound rig-target child: what to pose, what it looked like, what it rests at.</summary>
        /// <summary>
        /// One renderer's <c>enabled</c> flag as it was before preview touched it. Captured for
        /// every bound object, not only the ones a cutscene hides — whether a slot is hidden depends
        /// on the playhead, and the snapshot has to predate any scrub.
        /// </summary>
        private struct RendererVisibilitySnapshot
        {
            public Renderer renderer;
            public bool wasEnabled;
        }

        private sealed class PartBinding
        {
            public Transform partTransform;
            public Renderer partRenderer;
            public TransformSnapshot transformSnapshot;
            public MaterialPropertyBlock capturedPropertyBlock;
            public bool hadPropertyBlock;
            public TargetRestPose restPose;
        }

        private readonly Dictionary<uint, GameObject> boundObjects = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, TransformSnapshot> rootSnapshots = new Dictionary<uint, TransformSnapshot>();
        private readonly Dictionary<uint, List<RendererVisibilitySnapshot>> rendererSnapshots =
            new Dictionary<uint, List<RendererVisibilitySnapshot>>();
        private readonly Dictionary<uint, Dictionary<uint, PartBinding>> partsBySlot =
            new Dictionary<uint, Dictionary<uint, PartBinding>>();
        private readonly Dictionary<uint, CutsceneSlotClipPreview> clipPreviewsBySlot =
            new Dictionary<uint, CutsceneSlotClipPreview>();

        // Reused every tick: the risk note in A58 §6 is that a 30s vignette must not churn the
        // editor with a fresh allocation per part per frame.
        private readonly Dictionary<uint, TargetPose> composedPoses = new Dictionary<uint, TargetPose>();
        private readonly MaterialPropertyBlock scratchPropertyBlock = new MaterialPropertyBlock();

        public bool IsActive { get; private set; }

        /// <summary>
        /// Extra seconds of clip phase beyond the playhead, accumulated while the transport is
        /// paused on a hold marker (A58 §2.4: "loops keep cycling, camera holds"). The cutscene
        /// clock stops at a hold; the actors' own playback does not.
        /// </summary>
        public float HoldClipPhaseSeconds { get; set; }

        /// <summary>Why a slot cannot preview its clips, or empty when it can. Null for an unknown slot.</summary>
        public string GetClipPreviewStatus(uint slotId)
        {
            CutsceneSlotClipPreview clipPreview;
            return clipPreviewsBySlot.TryGetValue(slotId, out clipPreview) ? clipPreview.StatusMessage : null;
        }

        /// <summary>Captures every bound GameObject's (and bound part's) current state, then marks preview active.</summary>
        public void EnterPreview(CutsceneAsset cutscene, string sceneGuid)
        {
            if (IsActive || cutscene == null || cutscene.slots == null || string.IsNullOrEmpty(sceneGuid))
            {
                return;
            }

            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }

                CutsceneSlotBindingEntry binding =
                    CutsceneSceneBindingUtility.FindBinding(cutscene, sceneGuid, slot.SlotId);
                if (binding == null)
                {
                    continue;
                }
                GameObject boundObject = CutsceneSceneBindingUtility.ResolveGameObject(binding.globalObjectId);
                if (boundObject == null)
                {
                    continue;
                }

                boundObjects[slot.SlotId] = boundObject;
                rootSnapshots[slot.SlotId] = TransformSnapshot.Capture(boundObject.transform);
                rendererSnapshots[slot.SlotId] = CaptureRendererVisibility(boundObject);

                if (slot.kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }

                partsBySlot[slot.SlotId] = CapturePartBindings(boundObject);
            }

            IsActive = true;
        }

        private static List<RendererVisibilitySnapshot> CaptureRendererVisibility(GameObject boundObject)
        {
            Renderer[] renderers = boundObject.GetComponentsInChildren<Renderer>(true);
            List<RendererVisibilitySnapshot> snapshots = new List<RendererVisibilitySnapshot>(renderers.Length);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                snapshots.Add(new RendererVisibilitySnapshot
                {
                    renderer = renderers[rendererIndex],
                    wasEnabled = renderers[rendererIndex].enabled
                });
            }
            return snapshots;
        }

        private static Dictionary<uint, PartBinding> CapturePartBindings(GameObject boundObject)
        {
            Dictionary<uint, PartBinding> parts = new Dictionary<uint, PartBinding>();
            RigTargetAuthoring[] boundParts = boundObject.GetComponentsInChildren<RigTargetAuthoring>(true);
            for (int partIndex = 0; partIndex < boundParts.Length; partIndex++)
            {
                RigTargetAuthoring part = boundParts[partIndex];
                Renderer partRenderer = part.GetComponent<Renderer>();

                PartBinding partBinding = new PartBinding
                {
                    partTransform = part.transform,
                    partRenderer = partRenderer,
                    transformSnapshot = TransformSnapshot.Capture(part.transform),
                    restPose = RestPoseCapture.FromTransform(part.transform, part.restSliceIndex)
                };

                if (partRenderer != null)
                {
                    partBinding.hadPropertyBlock = partRenderer.HasPropertyBlock();
                    if (partBinding.hadPropertyBlock)
                    {
                        partBinding.capturedPropertyBlock = new MaterialPropertyBlock();
                        partRenderer.GetPropertyBlock(partBinding.capturedPropertyBlock);
                    }
                }

                parts[part.targetStableId] = partBinding;
            }
            return parts;
        }

        /// <summary>Restores every captured transform and property block exactly, and drops the preview registries (G-D1, A58-D2).</summary>
        public void ExitPreview()
        {
            if (!IsActive)
            {
                DisposeClipPreviews();
                return;
            }

            foreach (KeyValuePair<uint, GameObject> boundObjectEntry in boundObjects)
            {
                if (boundObjectEntry.Value == null)
                {
                    continue;
                }
                TransformSnapshot rootSnapshot;
                if (rootSnapshots.TryGetValue(boundObjectEntry.Key, out rootSnapshot))
                {
                    rootSnapshot.RestoreTo(boundObjectEntry.Value.transform);
                }

                List<RendererVisibilitySnapshot> renderers;
                if (rendererSnapshots.TryGetValue(boundObjectEntry.Key, out renderers))
                {
                    for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
                    {
                        if (renderers[rendererIndex].renderer != null)
                        {
                            renderers[rendererIndex].renderer.enabled = renderers[rendererIndex].wasEnabled;
                        }
                    }
                }

                Dictionary<uint, PartBinding> parts;
                if (!partsBySlot.TryGetValue(boundObjectEntry.Key, out parts))
                {
                    continue;
                }
                foreach (KeyValuePair<uint, PartBinding> partEntry in parts)
                {
                    RestorePart(partEntry.Value);
                }
            }

            boundObjects.Clear();
            rootSnapshots.Clear();
            rendererSnapshots.Clear();
            partsBySlot.Clear();
            DisposeClipPreviews();
            HoldClipPhaseSeconds = 0f;
            IsActive = false;
        }

        private static void RestorePart(PartBinding partBinding)
        {
            if (partBinding.partTransform != null)
            {
                partBinding.transformSnapshot.RestoreTo(partBinding.partTransform);
            }
            if (partBinding.partRenderer == null)
            {
                return;
            }
            // A part that had no block before must not be left holding one this controller invented,
            // or its material's own frame stays overridden by the last previewed instant.
            if (partBinding.hadPropertyBlock)
            {
                partBinding.partRenderer.SetPropertyBlock(partBinding.capturedPropertyBlock);
            }
            else
            {
                partBinding.partRenderer.SetPropertyBlock(null);
            }
        }

        private void DisposeClipPreviews()
        {
            foreach (KeyValuePair<uint, CutsceneSlotClipPreview> previewEntry in clipPreviewsBySlot)
            {
                previewEntry.Value.Dispose();
            }
            clipPreviewsBySlot.Clear();
        }

        /// <summary>The live bound GameObject for a slot, or null when unbound or preview is inactive.</summary>
        public GameObject GetBoundObject(uint slotId)
        {
            GameObject boundObject;
            return boundObjects.TryGetValue(slotId, out boundObject) ? boundObject : null;
        }

        /// <summary>The live child transform a part track's tag resolves to, or null when unresolved or preview is inactive.</summary>
        public Transform GetBoundPartTransform(uint slotId, RigAsset rig, uint tagId)
        {
            uint targetStableId;
            if (rig == null || !TryResolveTagToTargetId(rig, tagId, out targetStableId))
            {
                return null;
            }
            PartBinding partBinding;
            return TryGetPart(slotId, targetStableId, out partBinding) ? partBinding.partTransform : null;
        }

        /// <summary>
        /// The live child transform a raw target id resolves to. A socket names its target by id
        /// rather than by tag, so it cannot go through <see cref="GetBoundPartTransform"/>.
        /// </summary>
        public Transform GetBoundPartTransformByTargetId(uint slotId, uint targetStableId)
        {
            PartBinding partBinding;
            return TryGetPart(slotId, targetStableId, out partBinding) ? partBinding.partTransform : null;
        }

        private bool TryGetPart(uint slotId, uint targetStableId, out PartBinding partBinding)
        {
            partBinding = null;
            Dictionary<uint, PartBinding> parts;
            return partsBySlot.TryGetValue(slotId, out parts)
                && parts.TryGetValue(targetStableId, out partBinding)
                && partBinding.partTransform != null;
        }

        // -----------------------------------------------------------------------------------
        // Posing.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Poses every bound actor/prop at <paramref name="timeSeconds"/>: root motion, then each
        /// actor's clip lane (with seam crossfade), then part-track overrides on top.
        /// </summary>
        public void ApplyPose(CutsceneAsset cutscene, float timeSeconds)
        {
            if (!IsActive || cutscene == null || cutscene.slots == null)
            {
                return;
            }

            ResolveAttachmentsAt(cutscene, timeSeconds);

            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                GameObject boundObject;
                if (slot == null || !boundObjects.TryGetValue(slot.SlotId, out boundObject) || boundObject == null)
                {
                    continue;
                }

                float3 position;
                float3 eulerDegrees;
                float3 scale;
                // An attached slot's root lane is ignored exactly as it is at run time (§3.1) — the
                // host owns the transform, and the placement pass below writes it.
                // The merged lane, not the authored one (decision A64-D2): a mark IS a root key, and
                // rehearsing the walk here is the whole reason the merge is shared with the builder.
                if (!resolvedAttachments[slotIndex].isAttached
                    && CutsceneKeySampler.TrySampleTransform(
                        CutsceneMarkMerge.BuildEffectiveRootKeys(slot), timeSeconds, out position, out eulerDegrees, out scale))
                {
                    boundObject.transform.localPosition = new Vector3(position.x, position.y, position.z);
                    boundObject.transform.localRotation = Quaternion.Euler(eulerDegrees.x, eulerDegrees.y, eulerDegrees.z);
                    boundObject.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                }
                // No root keys authored for this slot (amendment A62 defect 2): leave the bound
                // GameObject's captured rest transform alone rather than snapping it to the origin.

                if (slot.kind != CutsceneSlotKind.Actor)
                {
                    continue;
                }
                ApplyActorParts(slot, timeSeconds);
            }

            PlaceAttachedSlots(cutscene);
            ApplyAttachmentVisibility(cutscene);
        }

        // -----------------------------------------------------------------------------------
        // Attach lane preview (amendment A63 §3.4). Mirrors the runtime's composition rather than
        // approximating it: preview and playback disagreeing is the defect this whole tool exists
        // to avoid.
        // -----------------------------------------------------------------------------------

        private struct ResolvedAttachment
        {
            public bool isAttached;
            public bool isPlaced;
            public int hostSlotIndex;
            public uint socketId;
            public Vector3 localOffset;
            public Vector3 localEulerDegrees;
            public bool hide;
        }

        // Reused every tick, like composedPoses — a 30s vignette must not allocate per slot per frame.
        private readonly List<ResolvedAttachment> resolvedAttachments = new List<ResolvedAttachment>();

        /// <summary>The last attach marker at or before the playhead decides each slot's state.</summary>
        private void ResolveAttachmentsAt(CutsceneAsset cutscene, float timeSeconds)
        {
            resolvedAttachments.Clear();
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                ResolvedAttachment resolved = new ResolvedAttachment { hostSlotIndex = -1 };
                if (slot != null && slot.attachMarkers != null)
                {
                    CutsceneAttachMarker activeMarker = null;
                    for (int markerIndex = 0; markerIndex < slot.attachMarkers.Count; markerIndex++)
                    {
                        CutsceneAttachMarker marker = slot.attachMarkers[markerIndex];
                        if (marker != null && marker.time <= timeSeconds
                            && (activeMarker == null || marker.time >= activeMarker.time))
                        {
                            activeMarker = marker;
                        }
                    }

                    if (activeMarker != null && activeMarker.kind == CutsceneAttachKind.Attach)
                    {
                        int hostSlotIndex = FindSlotIndexById(cutscene, activeMarker.hostSlotId);
                        if (hostSlotIndex >= 0 && hostSlotIndex != slotIndex)
                        {
                            resolved.isAttached = true;
                            resolved.hostSlotIndex = hostSlotIndex;
                            resolved.socketId = activeMarker.socketId;
                            resolved.localOffset = new Vector3(
                                activeMarker.localOffset.x, activeMarker.localOffset.y, activeMarker.localOffset.z);
                            resolved.localEulerDegrees = new Vector3(
                                activeMarker.localEulerDegrees.x, activeMarker.localEulerDegrees.y,
                                activeMarker.localEulerDegrees.z);
                            resolved.hide = activeMarker.hideWhileAttached;
                        }
                    }
                }
                resolvedAttachments.Add(resolved);
            }
        }

        /// <summary>
        /// Places every attached slot on its host, hosts first. Repeated rather than done in one
        /// sweep because attachments chain — a crate in a hand on an actor riding a cart — and a
        /// rider read before its own host was placed would trail a frame behind it.
        /// </summary>
        private void PlaceAttachedSlots(CutsceneAsset cutscene)
        {
            for (int pass = 0; pass < resolvedAttachments.Count; pass++)
            {
                bool placedAnyThisPass = false;
                for (int slotIndex = 0; slotIndex < resolvedAttachments.Count; slotIndex++)
                {
                    ResolvedAttachment resolved = resolvedAttachments[slotIndex];
                    if (!resolved.isAttached || resolved.isPlaced)
                    {
                        continue;
                    }
                    if (resolvedAttachments[resolved.hostSlotIndex].isAttached
                        && !resolvedAttachments[resolved.hostSlotIndex].isPlaced)
                    {
                        continue;
                    }

                    PlaceOneAttachedSlot(cutscene, slotIndex, resolved);
                    resolved.isPlaced = true;
                    resolvedAttachments[slotIndex] = resolved;
                    placedAnyThisPass = true;
                }
                if (!placedAnyThisPass)
                {
                    return;
                }
            }
        }

        private void PlaceOneAttachedSlot(CutsceneAsset cutscene, int slotIndex, in ResolvedAttachment resolved)
        {
            GameObject boundObject = GetBoundObject(cutscene.slots[slotIndex].SlotId);
            CutsceneSlot hostSlot = cutscene.slots[resolved.hostSlotIndex];
            GameObject hostObject = hostSlot == null ? null : GetBoundObject(hostSlot.SlotId);
            if (boundObject == null || hostObject == null)
            {
                return;
            }

            SocketDefinition socket = FindSocket(hostSlot.rig, resolved.socketId);
            Transform socketTransform = null;
            if (socket != null && socket.mode == SocketAttachMode.RigTarget)
            {
                socketTransform = GetBoundPartTransformByTargetId(hostSlot.SlotId, socket.targetId);
            }
            // A Bone socket's motion lives in a VAT texture the editor never samples, so it previews
            // at the host root — a recorded limitation the inspector says out loud (§3.4).

            if (socketTransform != null)
            {
                Quaternion socketLocalRotation = Quaternion.Euler(socket.localEulerAngles);
                boundObject.transform.position = socketTransform.position
                    + socketTransform.rotation * (socket.localPosition + resolved.localOffset);
                boundObject.transform.rotation = socketTransform.rotation * socketLocalRotation;
                return;
            }

            boundObject.transform.position = hostObject.transform.TransformPoint(resolved.localOffset);
            boundObject.transform.rotation =
                hostObject.transform.rotation * Quaternion.Euler(resolved.localEulerDegrees);
        }

        private void ApplyAttachmentVisibility(CutsceneAsset cutscene)
        {
            for (int slotIndex = 0; slotIndex < resolvedAttachments.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }
                List<RendererVisibilitySnapshot> renderers;
                if (!rendererSnapshots.TryGetValue(slot.SlotId, out renderers))
                {
                    continue;
                }

                bool hide = resolvedAttachments[slotIndex].isAttached && resolvedAttachments[slotIndex].hide;
                for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex].renderer;
                    if (renderer == null)
                    {
                        continue;
                    }
                    // Restores to what was captured rather than to true: a renderer the scene had
                    // already disabled must not be switched on by scrubbing past a detach.
                    renderer.enabled = hide ? false : renderers[rendererIndex].wasEnabled;
                }
            }
        }

        private static int FindSlotIndexById(CutsceneAsset cutscene, uint slotId)
        {
            if (slotId == 0u)
            {
                return -1;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                if (cutscene.slots[slotIndex] != null && cutscene.slots[slotIndex].SlotId == slotId)
                {
                    return slotIndex;
                }
            }
            return -1;
        }

        private static SocketDefinition FindSocket(RigAsset rig, uint socketId)
        {
            if (socketId == 0u || rig == null || rig.sockets == null)
            {
                return null;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                if (rig.sockets[socketIndex] != null && rig.sockets[socketIndex].Id.Value == socketId)
                {
                    return rig.sockets[socketIndex];
                }
            }
            return null;
        }

        private void ApplyActorParts(CutsceneSlot slot, float timeSeconds)
        {
            Dictionary<uint, PartBinding> parts;
            if (!partsBySlot.TryGetValue(slot.SlotId, out parts) || parts.Count == 0)
            {
                return;
            }

            composedPoses.Clear();
            foreach (KeyValuePair<uint, PartBinding> partEntry in parts)
            {
                TargetPose restAsPose;
                ClipSampler.RestToPose(in partEntry.Value.restPose, out restAsPose);
                composedPoses[partEntry.Key] = restAsPose;
            }

            // Clip lane, then facing, then the cutscene's own overrides — the order the shipped
            // systems run in (TransformSampleSystem applies facing after composition;
            // CutscenePartOverrideSystem then runs after that one). An override key is the last
            // word on the channels it owns, in the preview exactly as in play.
            SlotFacing facing = ResolveSlotFacing(slot, timeSeconds);
            ComposeClipLane(slot, parts, timeSeconds, in facing);
            ComposeFacingMirror(slot, in facing);
            ComposePartTrackOverrides(slot, timeSeconds);

            foreach (KeyValuePair<uint, PartBinding> partEntry in parts)
            {
                TargetPose pose;
                if (composedPoses.TryGetValue(partEntry.Key, out pose))
                {
                    WritePose(partEntry.Value, in pose);
                }
            }
        }

        /// <summary>
        /// Samples whichever clip block the slot's lane is playing, cross-fading the one before it
        /// while their overlap lasts, into <see cref="composedPoses"/>.
        /// </summary>
        private void ComposeClipLane(
            CutsceneSlot slot, Dictionary<uint, PartBinding> parts, float timeSeconds, in SlotFacing facing)
        {
            if (slot.clipBlocks == null || slot.clipBlocks.Count == 0)
            {
                return;
            }

            CutsceneSlotClipPreview clipPreview = EnsureClipPreview(slot);
            if (!clipPreview.HasRegistry)
            {
                return;
            }

            int activeBlockIndex = ResolveActiveBlockIndex(slot.clipBlocks, timeSeconds);
            if (activeBlockIndex < 0)
            {
                // Nothing has started yet: parts stay at rest, exactly as an actor does before its
                // first Play command reaches it.
                return;
            }

            CutsceneClipBlock activeBlock = slot.clipBlocks[activeBlockIndex];
            int activeClipIndex;
            if (!clipPreview.TryGetClipIndex(
                    ResolveFacingVariantClipId(slot, in facing, activeBlock.clipId), out activeClipIndex))
            {
                return;
            }

            float activeClipTime = CutsceneBlockTiming.ClipTimeInBlock(activeBlock.start, timeSeconds)
                + HoldClipPhaseSeconds;
            float activePhase = CutsceneBlockTiming.LoopPhaseNormalized(
                activeClipTime, clipPreview.GetClipDuration(activeClipIndex), activeBlock.loop);

            int previousClipIndex = -1;
            float previousPhase = 0f;
            float blendWeight = 1f;
            if (activeBlockIndex > 0)
            {
                CutsceneClipBlock previousBlock = slot.clipBlocks[activeBlockIndex - 1];
                float blendDuration = CutsceneBlockTiming.SeamBlendDuration(
                    previousBlock.start, previousBlock.duration, activeBlock.start);
                blendWeight = CutsceneBlockTiming.SeamBlendWeight(
                    activeBlock.start, blendDuration, timeSeconds);
                if (blendWeight < 1f && clipPreview.TryGetClipIndex(
                        ResolveFacingVariantClipId(slot, in facing, previousBlock.clipId), out previousClipIndex))
                {
                    // The outgoing clip keeps running on its own clock while the weight climbs —
                    // PlaybackTimeSystem.AdvanceBlend's behaviour, not a frozen last frame.
                    float previousClipTime =
                        CutsceneBlockTiming.ClipTimeInBlock(previousBlock.start, timeSeconds)
                        + HoldClipPhaseSeconds;
                    previousPhase = CutsceneBlockTiming.LoopPhaseNormalized(
                        previousClipTime, clipPreview.GetClipDuration(previousClipIndex), previousBlock.loop);
                }
            }

            int targetCount = clipPreview.TargetCount;
            for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
            {
                uint targetId = clipPreview.GetTargetId(targetIndex);
                PartBinding partBinding;
                if (!parts.TryGetValue(targetId, out partBinding))
                {
                    continue;
                }

                TargetPose pose;
                clipPreview.SamplePose(
                    activeClipIndex, targetIndex, activePhase, in partBinding.restPose, out pose);

                if (previousClipIndex >= 0)
                {
                    TargetPose previousPose;
                    clipPreview.SamplePose(
                        previousClipIndex, targetIndex, previousPhase, in partBinding.restPose, out previousPose);
                    ClipSampler.LerpPose(in previousPose, in pose, blendWeight, out pose);
                }

                composedPoses[targetId] = pose;
            }
        }

        /// <summary>
        /// The block a lane is playing at <paramref name="timeSeconds"/>: the last one to have
        /// started. −1 before the lane's first block.
        /// </summary>
        /// <remarks>
        /// A block's <c>duration</c> deliberately does not end it — see
        /// <see cref="CutsceneBlockTiming"/>. Scanned rather than tracked with the runtime player's
        /// forward-only <c>nextClipBlockIndex</c> cursor, because a scrub jumps backwards.
        /// </remarks>
        private static int ResolveActiveBlockIndex(List<CutsceneClipBlock> clipBlocks, float timeSeconds)
        {
            int activeIndex = -1;
            for (int blockIndex = 0; blockIndex < clipBlocks.Count; blockIndex++)
            {
                CutsceneClipBlock block = clipBlocks[blockIndex];
                if (block != null && block.start <= timeSeconds)
                {
                    activeIndex = blockIndex;
                }
            }
            return activeIndex;
        }

        // -----------------------------------------------------------------------------------
        // Facing (A58 §3.1, T4): the angle is resolved, run through the runtime's own resolver,
        // and applied — not merely displayed as a number.
        // -----------------------------------------------------------------------------------

        /// <summary>Which authored-side clip a slot's facing calls for at the playhead, and whether it mirrors.</summary>
        private struct SlotFacing
        {
            public bool isResolved;
            public Direction clipFacing;
            public bool mirrorX;
        }

        /// <summary>
        /// Walks the runtime facing path for a slot's angle at <paramref name="timeSeconds"/>, the
        /// way the Direction Sets pane does: angle to a facing vector,
        /// <see cref="FacingResolver.FromMovement"/> at the set's own turn granularity,
        /// <see cref="FacingResolver.Snap"/> into the coverage the filled slots actually give, then
        /// <see cref="FacingResolver.ToAuthoredSide"/>. Unresolved without a direction set — there is
        /// then nothing that says which art serves which angle.
        /// </summary>
        private static SlotFacing ResolveSlotFacing(CutsceneSlot slot, float timeSeconds)
        {
            SlotFacing facing = new SlotFacing();
            if (slot.directionSet == null)
            {
                return facing;
            }

            float angleDegrees;
            CutsceneKeySampler.TryResolveFacingAngle(
                slot.facingKeys, CutsceneMarkMerge.BuildEffectiveRootKeys(slot), timeSeconds, out angleDegrees);

            float angleRadians = Mathf.Deg2Rad * angleDegrees;
            float2 facingVector = new float2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));
            // No hysteresis seed: the angle is authored rather than sampled from noisy movement, so
            // the same playhead must always resolve to the same facing however it was scrubbed to.
            Direction memberFacing = FacingResolver.FromMovement(
                in facingVector, slot.directionSet.targetDirections, Direction.SouthEast);

            AnimationDirections coverage;
            slot.directionSet.TryGetEffectiveDirections(out coverage);
            Direction foldedFacing = FacingResolver.Snap(memberFacing, coverage);

            Direction clipFacing;
            bool mirrorX;
            FacingResolver.ToAuthoredSide(foldedFacing, out clipFacing, out mirrorX);

            facing.isResolved = true;
            facing.clipFacing = clipFacing;
            facing.mirrorX = mirrorX;
            return facing;
        }

        /// <summary>
        /// The clip a block actually plays once facing has had its say: the direction set's sibling
        /// for the resolved side.
        /// </summary>
        /// <remarks>
        /// <strong>Substituted only when the block already names a member of the set</strong>
        /// (decision A58-D5). A block naming the set's SouthEast walk is asking for "the walk",
        /// and turning the actor should re-pick the variant; a block naming a one-off clip the set
        /// has never heard of — a wave, a stumble — is asking for that clip exactly, and swapping it
        /// out for a walk because the actor happens to face north-east would be silent nonsense.
        /// </remarks>
        private static ulong ResolveFacingVariantClipId(
            CutsceneSlot slot, in SlotFacing facing, ulong authoredClipId)
        {
            if (!facing.isResolved || authoredClipId == 0UL || !IsDirectionSetMember(slot.directionSet, authoredClipId))
            {
                return authoredClipId;
            }
            ClipAsset variantClip = slot.directionSet.GetSlot(facing.clipFacing);
            return variantClip != null ? variantClip.Id.Value : authoredClipId;
        }

        private static bool IsDirectionSetMember(DirectionSetAsset directionSet, ulong clipId)
        {
            for (int slotIndex = 0; slotIndex < DirectionSlotOrder.Length; slotIndex++)
            {
                ClipAsset slotClip = directionSet.GetSlot(DirectionSlotOrder[slotIndex]);
                if (slotClip != null && slotClip.Id.Value == clipId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>What the slot's facing resolves to at a time, for the slot inspector's readout.</summary>
        public static string DescribeResolvedFacing(CutsceneSlot slot, float timeSeconds)
        {
            SlotFacing facing = ResolveSlotFacing(slot, timeSeconds);
            if (!facing.isResolved)
            {
                return "no direction set";
            }
            return "plays the " + facing.clipFacing + " variant" + (facing.mirrorX ? ", mirrored" : string.Empty);
        }

        /// <summary>The five east-side slots a direction set authors, in the order the queue lists them.</summary>
        private static readonly Direction[] DirectionSlotOrder =
        {
            Direction.South, Direction.SouthEast, Direction.East, Direction.NorthEast, Direction.North
        };

        /// <summary>
        /// Reflects every facing part about the actor's vertical axis when the resolved facing is
        /// served by a mirrored clip — the same three negations
        /// <c>TransformSampleSystem</c> applies for <c>PartFacing.mirrorX</c>, so the preview flips
        /// what play flips rather than merely scaling the whole actor by −1.
        /// </summary>
        private void ComposeFacingMirror(CutsceneSlot slot, in SlotFacing facing)
        {
            if (!facing.isResolved || !facing.mirrorX || slot.rig == null || slot.rig.targets == null)
            {
                return;
            }

            for (int targetIndex = 0; targetIndex < slot.rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = slot.rig.targets[targetIndex];
                if (target == null || !target.facesDirection)
                {
                    continue;
                }
                TargetPose pose;
                if (!composedPoses.TryGetValue(target.stableId, out pose))
                {
                    continue;
                }
                pose.localPosition.x = -pose.localPosition.x;
                pose.rotation.y = -pose.rotation.y;
                pose.rotation.z = -pose.rotation.z;
                pose.scale.x = -pose.scale.x;
                composedPoses[target.stableId] = pose;
            }
        }

        /// <summary>
        /// Layers each part track's masked channels over the composited clip pose — the Override
        /// layer, identical in rule to <c>CutscenePartOverrideSystem</c>: an unmasked channel keeps
        /// whatever the clip lane just decided, never the rest pose.
        /// </summary>
        private void ComposePartTrackOverrides(CutsceneSlot slot, float timeSeconds)
        {
            if (slot.rig == null || slot.partTracks == null)
            {
                return;
            }

            for (int trackIndex = 0; trackIndex < slot.partTracks.Count; trackIndex++)
            {
                CutsceneKeyedTrack track = slot.partTracks[trackIndex];
                if (track == null || track.keys.Count == 0)
                {
                    continue;
                }
                uint targetStableId;
                if (!TryResolveTagToTargetId(slot.rig, track.tagId, out targetStableId))
                {
                    continue;
                }
                TargetPose pose;
                if (!composedPoses.TryGetValue(targetStableId, out pose))
                {
                    continue;
                }

                float3 sampledPosition;
                float3 sampledEulerDegrees;
                float3 sampledScale;
                CutsceneKeySampler.TrySampleTransform(
                    track.keys, timeSeconds, out sampledPosition, out sampledEulerDegrees, out sampledScale);

                if ((track.channels & AnimatedChannels.PositionXY) != 0)
                {
                    pose.localPosition.x = sampledPosition.x;
                    pose.localPosition.y = sampledPosition.y;
                }
                if ((track.channels & AnimatedChannels.PositionZ) != 0)
                {
                    pose.localPosition.z = sampledPosition.z;
                }
                if ((track.channels & AnimatedChannels.Rotation) != 0)
                {
                    // The key authors degrees; a pose carries radians, like TargetPose everywhere.
                    pose.rotation = math.radians(sampledEulerDegrees);
                }
                if ((track.channels & AnimatedChannels.Scale) != 0)
                {
                    pose.scale = sampledScale;
                }

                composedPoses[targetStableId] = pose;
            }
        }

        private void WritePose(PartBinding partBinding, in TargetPose pose)
        {
            if (partBinding.partTransform == null)
            {
                return;
            }

            partBinding.partTransform.localPosition =
                new Vector3(pose.localPosition.x, pose.localPosition.y, pose.localPosition.z);
            // Radians to the degrees a Transform authors in, converted here and only here — the
            // GameObject-side mirror of TransformApplySystem's own quaternion.Euler(pose.rotation).
            float3 rotationDegrees = math.degrees(pose.rotation);
            partBinding.partTransform.localRotation =
                Quaternion.Euler(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z);
            partBinding.partTransform.localScale = new Vector3(pose.scale.x, pose.scale.y, pose.scale.z);

            if (partBinding.partRenderer == null)
            {
                return;
            }
            // The same two per-instance properties SpriteMaterialSystem publishes at run time, so a
            // flipbook part shows the frame its sprite track keyed rather than its rest frame.
            partBinding.partRenderer.GetPropertyBlock(scratchPropertyBlock);
            scratchPropertyBlock.SetFloat(ImageIndexPropertyId, pose.sliceIndex);
            scratchPropertyBlock.SetVector(
                AtlasFramePropertyId,
                new Vector4(pose.atlasRect.x, pose.atlasRect.y, pose.atlasRect.z, pose.atlasRect.w));
            partBinding.partRenderer.SetPropertyBlock(scratchPropertyBlock);
        }

        private CutsceneSlotClipPreview EnsureClipPreview(CutsceneSlot slot)
        {
            CutsceneSlotClipPreview clipPreview;
            if (!clipPreviewsBySlot.TryGetValue(slot.SlotId, out clipPreview))
            {
                clipPreview = new CutsceneSlotClipPreview();
                clipPreviewsBySlot[slot.SlotId] = clipPreview;
            }
            clipPreview.RebuildIfBindChanged(slot.rig, slot.clipSets);
            return clipPreview;
        }

        private static bool TryResolveTagToTargetId(RigAsset rig, uint tagId, out uint targetStableId)
        {
            targetStableId = 0u;
            if (rig.targets == null)
            {
                return false;
            }
            for (int i = 0; i < rig.targets.Count; i++)
            {
                RigTargetDefinition target = rig.targets[i];
                if (target != null && target.tagId == tagId)
                {
                    targetStableId = target.stableId;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pushes the camera lane's pose at <paramref name="timeSeconds"/> onto the last active
        /// Scene view (spec §4, G4: "scrub preview of the shot"), respecting cut markers (G-D7).
        /// A no-op with no camera keys authored yet, or no Scene view to drive.
        /// </summary>
        /// <remarks>
        /// <strong>Placed by solving for the pivot a desired camera <em>position</em> implies,
        /// not by aiming at it.</strong> <c>SceneView.LookAt</c> takes an orbit pivot and a distance
        /// (<c>size</c>), not a camera position — the relationship, confirmed empirically against
        /// this Editor version, is <c>cameraDistance = size / sin(fov · 0.5)</c>, then
        /// <c>pivot = position + rotation · forward · cameraDistance</c>. <c>size</c> itself is
        /// arbitrary (chosen as 1) because only the ratio matters once <c>cameraDistance</c> is
        /// solved for; any positive value reproduces the same camera position and rotation.
        /// </remarks>
        public void ApplyCameraPose(CutsceneAsset cutscene, float timeSeconds)
        {
            if (!IsActive || cutscene?.cameraLane?.keys == null || cutscene.cameraLane.keys.Count == 0)
            {
                return;
            }
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                return;
            }

            float3 sampledPosition;
            float3 sampledEulerDegrees;
            float fieldOfView;
            bool isCut;
            CutsceneKeySampler.SampleCameraWithCuts(
                cutscene.cameraLane.keys, cutscene.cameraLane.cutMarkers, timeSeconds,
                out sampledPosition, out sampledEulerDegrees, out fieldOfView, out isCut);
            Vector3 position = new Vector3(sampledPosition.x, sampledPosition.y, sampledPosition.z);
            Quaternion rotation = Quaternion.Euler(sampledEulerDegrees.x, sampledEulerDegrees.y, sampledEulerDegrees.z);

            const float ChosenSize = 1f;
            float cameraDistance = ChosenSize / Mathf.Sin(Mathf.Max(1f, fieldOfView) * 0.5f * Mathf.Deg2Rad);
            Vector3 pivot = position + rotation * Vector3.forward * cameraDistance;

            sceneView.orthographic = false;
            sceneView.LookAt(pivot, rotation, ChosenSize, false, true);

            SceneView.CameraSettings cameraSettings = sceneView.cameraSettings;
            cameraSettings.fieldOfView = fieldOfView;
            sceneView.cameraSettings = cameraSettings;

            // LookAt updates pivot/rotation/size immediately but the camera transform itself is
            // recomputed on the view's own repaint — without forcing one here, a caller reading
            // sceneView.camera.transform straight back (or a scrub that never yields a frame) can
            // observe the pose from before this call rather than the one just requested.
            sceneView.Repaint();
        }

        // -----------------------------------------------------------------------------------
        // Gizmo keying (spec §3): move the actor or a part with Unity's own transform tool,
        // then press Key — the same interaction family Rig Edit and Unity Timeline recording use.
        // -----------------------------------------------------------------------------------

        /// <summary>Keys the currently live root pose of <paramref name="slot"/> at <paramref name="timeSeconds"/>.</summary>
        public bool TryKeyRoot(
            SerializedObject serializedObject, SerializedProperty transformKeysProperty,
            CutsceneSlot slot, float timeSeconds)
        {
            GameObject boundObject;
            if (!boundObjects.TryGetValue(slot.SlotId, out boundObject) || boundObject == null)
            {
                return false;
            }
            UpsertTransformKey(
                transformKeysProperty, timeSeconds,
                boundObject.transform.localPosition,
                boundObject.transform.localRotation.eulerAngles,
                boundObject.transform.localScale);
            serializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>Keys the currently live pose of one part-track's bound child at <paramref name="timeSeconds"/>.</summary>
        public bool TryKeyPartTrack(
            SerializedObject serializedObject, SerializedProperty keysProperty,
            CutsceneSlot slot, CutsceneKeyedTrack track, float timeSeconds)
        {
            if (slot.rig == null)
            {
                return false;
            }
            uint targetStableId;
            if (!TryResolveTagToTargetId(slot.rig, track.tagId, out targetStableId))
            {
                return false;
            }
            PartBinding partBinding;
            if (!TryGetPart(slot.SlotId, targetStableId, out partBinding))
            {
                return false;
            }

            Transform partTransform = partBinding.partTransform;
            UpsertTransformKey(
                keysProperty, timeSeconds,
                partTransform.localPosition, partTransform.localRotation.eulerAngles, partTransform.localScale);
            serializedObject.ApplyModifiedProperties();
            return true;
        }

        /// <summary>Time epsilon within which a re-key overwrites an existing key rather than adding a duplicate.</summary>
        private const float KeyTimeEpsilon = 1f / 120f;

        private static void UpsertTransformKey(
            SerializedProperty listProperty, float timeSeconds, Vector3 position, Vector3 rotationEuler, Vector3 scale)
        {
            int existingIndex = -1;
            for (int i = 0; i < listProperty.arraySize; i++)
            {
                if (Mathf.Abs(listProperty.GetArrayElementAtIndex(i).FindPropertyRelative("time").floatValue - timeSeconds)
                    <= KeyTimeEpsilon)
                {
                    existingIndex = i;
                    break;
                }
            }

            int index = existingIndex;
            if (index < 0)
            {
                index = listProperty.arraySize;
                listProperty.InsertArrayElementAtIndex(index);
            }

            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = timeSeconds;
            WriteFloat3(element.FindPropertyRelative("position"), position);
            WriteFloat3(element.FindPropertyRelative("rotation"), rotationEuler);
            WriteFloat3(element.FindPropertyRelative("scale"), scale);
            if (existingIndex < 0)
            {
                element.FindPropertyRelative("interpolation").enumValueIndex = (int)Interpolation.Linear;
                SerializedProperty startHandle = element.FindPropertyRelative("bezierStartHandle");
                startHandle.FindPropertyRelative("x").floatValue = 0f;
                startHandle.FindPropertyRelative("y").floatValue = 0f;
                SerializedProperty endHandle = element.FindPropertyRelative("bezierEndHandle");
                endHandle.FindPropertyRelative("x").floatValue = 0f;
                endHandle.FindPropertyRelative("y").floatValue = 0f;
            }

            if (existingIndex < 0)
            {
                SortByTime(listProperty);
            }
        }

        private static void WriteFloat3(SerializedProperty float3Property, Vector3 value)
        {
            float3Property.FindPropertyRelative("x").floatValue = value.x;
            float3Property.FindPropertyRelative("y").floatValue = value.y;
            float3Property.FindPropertyRelative("z").floatValue = value.z;
        }

        private static void SortByTime(SerializedProperty listProperty)
        {
            int count = listProperty.arraySize;
            for (int i = 1; i < count; i++)
            {
                int j = i;
                while (j > 0 &&
                    listProperty.GetArrayElementAtIndex(j - 1).FindPropertyRelative("time").floatValue >
                    listProperty.GetArrayElementAtIndex(j).FindPropertyRelative("time").floatValue)
                {
                    listProperty.MoveArrayElement(j, j - 1);
                    j--;
                }
            }
        }
    }
}
