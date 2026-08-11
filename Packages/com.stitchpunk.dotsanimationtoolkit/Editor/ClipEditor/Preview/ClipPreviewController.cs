// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// Drives the clip preview: transient registry blob in, rendered texture out
    /// (architecture section 7.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Visual parity is by construction, not by effort.</strong> The pose comes from
    /// <see cref="ClipSampler"/> — the runtime's own functions — sampled out of a registry blob
    /// built by <see cref="ClipRegistryBuilder"/>, the same builder the baker uses. There is no
    /// second, editor-only sampler to drift from the real one, which is the failure the host's old
    /// preview had.
    /// </para>
    /// <para>
    /// <strong>This blob is the one manually-owned blob in the toolkit</strong>, and only in the
    /// editor. Everything else is owned by a <c>BlobAssetStore</c> and freed with it; this one is
    /// built with <see cref="Allocator.Persistent"/> because it must outlive the call that made it,
    /// so <see cref="Dispose"/> is not optional. Every path that replaces it disposes the old one
    /// first.
    /// </para>
    /// <para>
    /// <strong>Rest poses are identity here.</strong> <c>ClipRegistryBuilder</c> sees only a
    /// <c>ClipSetAsset</c> graph and never the actor prefab that carries the real rest poses — the
    /// same reason section 4.6's offset bounds are origin-centred. Authored keys are offsets *from*
    /// rest, so an identity rest shows the authored motion faithfully; it just shows it about the
    /// origin rather than about where the part sits on a built actor.
    /// </para>
    /// </remarks>
    public sealed class ClipPreviewController : IDisposable
    {
        private PreviewRenderUtility renderUtility;
        private readonly PreviewRigMirror rigMirror = new PreviewRigMirror();

        /// <summary>
        /// Whether the mirror's <em>current</em> root has joined the preview scene. Tracked
        /// separately from the utility's own lifetime because the mirror is rebuilt whenever the rig
        /// changes: a flag tied to "the utility exists" would leave every root after the first one
        /// outside the scene, rendering an empty preview that looks exactly like a broken clip.
        /// </summary>
        private bool mirrorRootAdded;

        private readonly PreviewSkeletonMirror skeletonMirror = new PreviewSkeletonMirror();
        private GameObject skinnedSourcePrefab;

        /// <summary>
        /// Tracked separately from <see cref="mirrorRootAdded"/> for the same reason it exists: the
        /// skeleton instance is rebuilt whenever the source changes, and a flag tied to "the render
        /// utility exists" would leave every instance after the first outside the preview scene —
        /// an empty preview that looks exactly like a broken clip.
        /// </summary>
        private bool skeletonRootAdded;

        private BlobAssetReference<ClipRegistryBlob> registry;
        private ClipSetAsset boundClipSet;
        private string statusMessage = string.Empty;

        private float orbitYaw = 0f;
        private float orbitPitch = 0f;
        private float orbitDistance = 6f;

        /// <summary>Why the preview is empty, or an empty string when it is fine.</summary>
        public string StatusMessage
        {
            get { return statusMessage; }
        }

        /// <summary>Whether a registry is currently built and sampleable.</summary>
        public bool HasRegistry
        {
            get { return registry.IsCreated; }
        }

        /// <summary>
        /// Rebuilds the transient registry for <paramref name="clipSet"/> and the mirror for its rig.
        /// </summary>
        /// <remarks>
        /// Validation failures are caught and surfaced through <see cref="StatusMessage"/> rather
        /// than propagated. <c>ClipRegistryBuilder.Build</c> throws on any error-severity rule, and
        /// an authoring window that dies on an invalid clip is useless precisely when it is most
        /// needed — while the clip is being fixed. The side effect is that the preview doubles as a
        /// validation surface for free.
        /// </remarks>
        public void SetClipSet(ClipSetAsset clipSet)
        {
            ReleaseRegistry();
            boundClipSet = clipSet;
            statusMessage = string.Empty;

            if (clipSet == null)
            {
                rigMirror.Dispose();
                statusMessage = "No clip set.";
                return;
            }

            rigMirror.Rebuild(clipSet.rig);
            mirrorRootAdded = false;
            if (rigMirror.PartCount == 0)
            {
                statusMessage = "Clip set's rig declares no targets.";
                return;
            }

            try
            {
                Unity.Entities.Hash128 contentHash;
                ClipRegistryBuilder.Build(clipSet, out registry, out contentHash);
            }
            catch (Exception buildException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                statusMessage = buildException.Message;
            }
        }

        /// <summary>
        /// Assigns the rigged prefab whose skeleton authored bone tracks pose (amendment A42, B4).
        /// </summary>
        /// <remarks>
        /// Optional. Passing null returns the preview to quads-only, which is exactly right for a
        /// cutout clip set — that workflow must not pay for a feature it does not use. This is the
        /// same prefab the VAT bake samples; using a different one would preview motion against a
        /// skeleton the bake never sees.
        /// </remarks>
        public void SetSkinnedSource(GameObject prefab)
        {
            if (skinnedSourcePrefab == prefab)
            {
                return;
            }
            skinnedSourcePrefab = prefab;
            skeletonMirror.Rebuild(prefab);
            skeletonRootAdded = false;
        }

        /// <summary>
        /// Finds the authoring clip behind a baked clip id.
        /// </summary>
        /// <remarks>
        /// Bone tracks are authoring-only data — amendment A42's correction: they never reach the
        /// blob, because nothing at runtime samples a bone. So posing the skeleton needs the
        /// <c>ClipAsset</c> itself, which the blob's id is the only handle back to.
        /// </remarks>
        private List<BoneTrack> FindClipById(ulong clipId)
        {
            if (boundClipSet == null || boundClipSet.clips == null)
            {
                return null;
            }
            for (int clipIndex = 0; clipIndex < boundClipSet.clips.Count; clipIndex++)
            {
                ClipAsset candidate = boundClipSet.clips[clipIndex];
                if (candidate != null && candidate.Id.Value == clipId)
                {
                    return candidate.boneTracks;
                }
            }
            return null;
        }

        /// <summary>Rebuilds against the currently bound set — call after an edit.</summary>
        public void Refresh()
        {
            SetClipSet(boundClipSet);
        }

        /// <summary>
        /// Poses the mirror for <paramref name="clipId"/> at <paramref name="normalizedTime"/>.
        /// </summary>
        /// <returns>False when the clip is not in the registry.</returns>
        public bool SamplePose(ulong clipId, float normalizedTime)
        {
            if (!registry.IsCreated)
            {
                return false;
            }

            ref ClipRegistryBlob registryBlob = ref registry.Value;
            int clipIndex = -1;
            for (int index = 0; index < registryBlob.sortedClipIds.Length; index++)
            {
                if (registryBlob.sortedClipIds[index] == clipId)
                {
                    clipIndex = index;
                    break;
                }
            }
            if (clipIndex < 0)
            {
                return false;
            }

            ref ClipBlob clipBlob = ref registryBlob.clips[clipIndex];
            TargetRestPose identityRest = new TargetRestPose
            {
                localPosition = Unity.Mathematics.float3.zero,
                rotationZ = 0f,
                scale = new Unity.Mathematics.float2(1f, 1f),
                restSliceIndex = 0
            };

            for (int targetIndex = 0; targetIndex < registryBlob.sortedTargetIds.Length; targetIndex++)
            {
                TargetPose pose;
                ClipSampler.SamplePose(ref clipBlob, targetIndex, normalizedTime, in identityRest, out pose);
                rigMirror.ApplyPose(registryBlob.sortedTargetIds[targetIndex], in pose);
            }

            // After the whole pose, never inside the loop: a marker placed before its part is posed
            // shows the previous frame and reads as the socket lagging the rig.
            rigMirror.UpdateSocketMarkers();

            // Posed after the parts so one scrub shows both at the same instant, which is the
            // entire point of authoring bone and cutout rows on one timeline.
            skeletonMirror.ApplyBoneTracks(FindClipById(clipId), normalizedTime);
            if (skeletonMirror.UnresolvedBoneNames.Count > 0)
            {
                statusMessage = "Bone name(s) not in the skinned source: "
                    + string.Join(", ", skeletonMirror.UnresolvedBoneNames);
            }
            return true;
        }

        /// <summary>Orbits the preview camera by a pointer delta, in pixels.</summary>
        public void Orbit(Vector2 pixelDelta)
        {
            orbitYaw += pixelDelta.x * 0.4f;
            orbitPitch = Mathf.Clamp(orbitPitch + pixelDelta.y * 0.4f, -85f, 85f);
        }

        /// <summary>Zooms the preview camera. Positive zooms out.</summary>
        public void Zoom(float amount)
        {
            orbitDistance = Mathf.Clamp(orbitDistance + amount, 1f, 60f);
        }

        /// <summary>
        /// Renders the mirror and returns the resulting texture, or null when there is nothing to
        /// show. The texture is owned by the render utility — never destroy it here.
        /// </summary>
        public Texture Render(int pixelWidth, int pixelHeight)
        {
            if (rigMirror.RootObject == null || pixelWidth <= 0 || pixelHeight <= 0)
            {
                return null;
            }

            EnsureRenderUtility();
            if (skeletonMirror.InstanceRoot != null && !skeletonRootAdded)
            {
                renderUtility.AddSingleGO(skeletonMirror.InstanceRoot);
                skeletonRootAdded = true;
            }
            if (!mirrorRootAdded)
            {
                renderUtility.AddSingleGO(rigMirror.RootObject);
                mirrorRootAdded = true;
            }

            Quaternion orbitRotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            renderUtility.camera.transform.position = orbitRotation * new Vector3(0f, 0f, -orbitDistance);
            renderUtility.camera.transform.rotation = orbitRotation;

            renderUtility.BeginPreview(new Rect(0f, 0f, pixelWidth, pixelHeight), GUIStyle.none);
            renderUtility.camera.Render();
            return renderUtility.EndPreview();
        }

        private void EnsureRenderUtility()
        {
            if (renderUtility != null)
            {
                return;
            }

            renderUtility = new PreviewRenderUtility();
            renderUtility.camera.fieldOfView = 45f;
            renderUtility.camera.nearClipPlane = 0.1f;
            renderUtility.camera.farClipPlane = 200f;
            renderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            renderUtility.camera.backgroundColor = new Color(0.17f, 0.17f, 0.18f, 1f);
            renderUtility.ambientColor = new Color(0.45f, 0.45f, 0.45f, 1f);

            renderUtility.lights[0].intensity = 1.1f;
            renderUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            renderUtility.lights[1].intensity = 0.5f;
            renderUtility.lights[1].transform.rotation = Quaternion.Euler(-20f, -110f, 0f);
        }

        private void ReleaseRegistry()
        {
            if (registry.IsCreated)
            {
                registry.Dispose();
            }
            registry = default(BlobAssetReference<ClipRegistryBlob>);
        }

        public void Dispose()
        {
            ReleaseRegistry();
            rigMirror.Dispose();
            skeletonMirror.Dispose();
            mirrorRootAdded = false;
            skeletonRootAdded = false;
            if (renderUtility != null)
            {
                renderUtility.Cleanup();
                renderUtility = null;
            }
        }
    }
}
