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
    /// <para>
    /// <strong>Rendering does not depend on selection.</strong> <see cref="Render"/> draws whatever
    /// the preview scene currently holds — at minimum the reference grid — and returns a texture
    /// whether or not a clip is selected, a set is assigned, or a registry could be built. Selection
    /// decides what is <em>in</em> the scene and where the selection marker sits; it never decides
    /// whether there is a picture. The window relied on the opposite for a long time, and the result
    /// was a viewport that looked broken until something was clicked.
    /// </para>
    /// </remarks>
    public sealed class ClipPreviewController : IDisposable
    {
        private const float DefaultOrbitDistance = 6f;

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

        /// <summary>
        /// The grid and the selection marker. Built once and never rebuilt, so unlike the mirrors
        /// these join the preview scene a single time.
        /// </summary>
        private readonly PreviewSceneGizmos sceneGizmos = new PreviewSceneGizmos();
        private bool gizmosAdded;

        /// <summary>
        /// Joint markers for the skinned source. Rebuilt with the skeleton, so it joins the preview
        /// scene again each time — tracked by its own flag for the reason above.
        /// </summary>
        private readonly PreviewBoneHandles boneHandles = new PreviewBoneHandles();
        private bool boneHandlesAdded;

        /// <summary>
        /// What is selected, as an index into the skeleton mirror's depth-first transform list.
        /// -1 is nothing. Not a <c>Transform</c> reference, because the instance is destroyed and
        /// rebuilt whenever the rig changes and a held reference would be a destroyed object.
        /// </summary>
        private int selectedHierarchyIndex = -1;

        private BlobAssetReference<ClipRegistryBlob> registry;
        private ClipSetAsset boundClipSet;
        private string statusMessage = "No clip set assigned.";

        private float orbitYaw = 0f;
        private float orbitPitch = 0f;
        private float orbitDistance = DefaultOrbitDistance;

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
                mirrorRootAdded = false;
                statusMessage = "No clip set assigned.";
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

            boneHandles.Rebuild(skeletonMirror.InstanceRoot);
            boneHandlesAdded = false;

            // The old instance's transforms are gone, so an index into them means nothing now.
            selectedHierarchyIndex = -1;
            sceneGizmos.HideSelection();
        }

        /// <summary>The previewed rig's root, which the hierarchy pane lists. Null when none.</summary>
        /// <remarks>
        /// The window builds its tree from this live instance rather than from the prefab asset, so
        /// a picked transform is literally a node of the tree's own source. Two walks of two
        /// hierarchies would have to agree about ordering forever; one hierarchy cannot disagree
        /// with itself.
        /// </remarks>
        public Transform HierarchyRoot
        {
            get
            {
                return skeletonMirror.InstanceRoot != null
                    ? skeletonMirror.InstanceRoot.transform
                    : null;
            }
        }

        /// <summary>
        /// Sets what the selection outline follows, by hierarchy index. -1 for nothing.
        /// </summary>
        /// <remarks>
        /// This is the whole of what selection does to the viewport. Nothing here affects whether
        /// the preview renders, what is in the scene, or where the camera is — an index that no
        /// longer resolves hides the outline and changes nothing else.
        /// </remarks>
        public void SetSelectedHierarchyIndex(int hierarchyIndex)
        {
            selectedHierarchyIndex = hierarchyIndex;
            if (hierarchyIndex < 0)
            {
                sceneGizmos.HideSelection();
            }
        }

        /// <summary>The hierarchy index of a transform, or -1 when it is not in the previewed rig.</summary>
        public int GetHierarchyIndex(Transform node)
        {
            return skeletonMirror.GetIndex(node);
        }

        /// <summary>The hierarchy index of the first transform with this name, or -1.</summary>
        public int FindHierarchyIndexByName(string boneName)
        {
            return skeletonMirror.FindIndexByName(boneName);
        }

        /// <summary>
        /// Describes what a hierarchy item is, for the inspector's subtitle.
        /// </summary>
        /// <remarks>
        /// Worth saying out loud because the hierarchy lists <em>every</em> transform, and what you
        /// can usefully do with one depends on which kind it is: only a skinned bone moves the mesh
        /// when a bone track drives it.
        /// </remarks>
        public string DescribeHierarchyItem(int hierarchyIndex)
        {
            Transform node = skeletonMirror.GetTransformByIndex(hierarchyIndex);
            if (node == null)
            {
                return string.Empty;
            }

            if (IsSkinnedBone(node))
            {
                return "Skinned bone — a bone track on this name moves the mesh.";
            }
            if (node.GetComponent<SkinnedMeshRenderer>() != null)
            {
                return "Skinned mesh renderer.";
            }
            if (node.GetComponent<Renderer>() != null)
            {
                return "Renderer.";
            }
            return "Transform with no renderer of its own.";
        }

        private bool IsSkinnedBone(Transform node)
        {
            IReadOnlyList<Transform> bones = boneHandles.Bones;
            for (int boneIndex = 0; boneIndex < bones.Count; boneIndex++)
            {
                if (bones[boneIndex] == node)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Hit-tests the previewed rig under a viewport point, nearest first.
        /// </summary>
        /// <param name="viewportPoint">Pointer position, (0,0) bottom-left to (1,1) top-right.</param>
        /// <param name="aspect">Width over height of the rect the viewport is drawn into.</param>
        /// <param name="hits">Filled with what is under the pointer. Cleared first.</param>
        /// <remarks>
        /// <para>
        /// Only the previewed rig is pickable. The grid, the selection outline and the cutout
        /// mirror's quads are deliberately not: the first two are furniture, and the quads have no
        /// row in the hierarchy pane to select, so a click on one could only select something the
        /// user cannot see selected.
        /// </para>
        /// <para>
        /// The camera is posed here as well as in <see cref="Render"/>. A click is handled outside
        /// the render loop, and picking against a camera left wherever the last frame put it is a
        /// whole class of "it selected something that is not under my cursor" bug.
        /// </para>
        /// </remarks>
        public void CollectPickHits(Vector2 viewportPoint, float aspect, List<PreviewPickHit> hits)
        {
            hits.Clear();
            if (HierarchyRoot == null)
            {
                return;
            }

            EnsureRenderUtility();
            ApplyCameraPose();

            Ray pickRay = PreviewScenePicker.BuildRay(
                renderUtility.camera.transform,
                renderUtility.camera.fieldOfView,
                aspect,
                viewportPoint);

            PreviewScenePicker.CollectHits(
                HierarchyRoot, boneHandles.Bones, BoneHandleRadius, pickRay, hits);
        }

        /// <summary>
        /// How big a joint marker is, in world units — drawn <em>and</em> clicked.
        /// </summary>
        /// <remarks>
        /// Scaled by camera distance so it holds roughly the same size on screen at any zoom, and
        /// read from this one property by both the drawing and the picking so the click target
        /// cannot drift away from the marker the user is aiming at.
        /// </remarks>
        private float BoneHandleRadius
        {
            get { return Mathf.Clamp(orbitDistance * 0.018f, 0.005f, 0.6f); }
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
        /// Returns the camera to the pose the window opens with: head-on, framing the origin.
        /// </summary>
        /// <remarks>
        /// Needed precisely because the viewport now lives independently of selection. An orbit that
        /// wandered off the rig used to be fixed by selecting something else and forcing a rebuild;
        /// with the camera persisting across every selection change, there has to be a way back.
        /// </remarks>
        public void ResetView()
        {
            orbitYaw = 0f;
            orbitPitch = 0f;
            orbitDistance = DefaultOrbitDistance;
        }

        /// <summary>
        /// Renders the preview scene and returns the resulting texture. The texture is owned by the
        /// render utility — never destroy it here.
        /// </summary>
        /// <remarks>
        /// Returns null only for a degenerate size or a render utility that could not be created.
        /// An empty scene is not a failure: with no clip set, no rig and no selection, this still
        /// renders the grid, which is what the window shows on open.
        /// </remarks>
        public Texture Render(int pixelWidth, int pixelHeight)
        {
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                return null;
            }

            EnsureRenderUtility();
            PopulatePreviewScene();

            // Bone handles are rewritten every frame because the bones move as the clip scrubs, and
            // the markers are also the click targets — stale markers would be a viewport where the
            // handles and the hit tests disagree about where the skeleton is.
            boneHandles.UpdateGeometry(BoneHandleRadius);
            UpdateSelectionMarker();
            ApplyCameraPose();

            renderUtility.BeginPreview(new Rect(0f, 0f, pixelWidth, pixelHeight), GUIStyle.none);
            renderUtility.camera.Render();
            return renderUtility.EndPreview();
        }

        private void ApplyCameraPose()
        {
            Quaternion orbitRotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            renderUtility.camera.transform.position = orbitRotation * new Vector3(0f, 0f, -orbitDistance);
            renderUtility.camera.transform.rotation = orbitRotation;
        }

        /// <summary>
        /// Adds whatever exists but has not yet joined the preview scene.
        /// </summary>
        /// <remarks>
        /// Each root is tracked by its own flag rather than by one "scene is populated" flag: the
        /// mirrors are rebuilt whenever the set or the rig changes, and a shared flag would leave
        /// every rebuilt root outside the scene — an empty preview that looks exactly like a broken
        /// clip.
        /// </remarks>
        private void PopulatePreviewScene()
        {
            sceneGizmos.EnsureBuilt();
            if (!gizmosAdded && sceneGizmos.GridObject != null && sceneGizmos.SelectionObject != null)
            {
                renderUtility.AddSingleGO(sceneGizmos.GridObject);
                renderUtility.AddSingleGO(sceneGizmos.SelectionObject);
                gizmosAdded = true;
            }

            if (skeletonMirror.InstanceRoot != null && !skeletonRootAdded)
            {
                renderUtility.AddSingleGO(skeletonMirror.InstanceRoot);
                skeletonRootAdded = true;
            }

            if (boneHandles.HandlesObject != null && !boneHandlesAdded)
            {
                renderUtility.AddSingleGO(boneHandles.HandlesObject);
                boneHandlesAdded = true;
            }

            if (rigMirror.RootObject != null && !mirrorRootAdded)
            {
                renderUtility.AddSingleGO(rigMirror.RootObject);
                mirrorRootAdded = true;
            }
        }

        /// <summary>
        /// Outlines the selected transform, or hides the outline when nothing resolves.
        /// </summary>
        /// <remarks>
        /// Resolved every frame rather than cached on selection, because the transform moves: the
        /// outline has to follow the posed skeleton as the playhead scrubs, not sit where the object
        /// was when it was clicked.
        /// </remarks>
        private void UpdateSelectionMarker()
        {
            Transform selectedTransform = skeletonMirror.GetTransformByIndex(selectedHierarchyIndex);
            if (selectedTransform == null)
            {
                sceneGizmos.HideSelection();
                return;
            }

            Bounds localBounds;
            if (TryGetLocalBounds(selectedTransform, out localBounds))
            {
                sceneGizmos.ShowSelection(
                    selectedTransform.TransformPoint(localBounds.center),
                    selectedTransform.rotation,
                    Vector3.Scale(selectedTransform.lossyScale, localBounds.size));
                return;
            }

            // No geometry to outline — a bone, or an empty. A fixed screen-relative box is the only
            // honest thing to draw, and it matches the joint marker the click targeted.
            float markerSize = BoneHandleRadius * 2f;
            sceneGizmos.ShowSelection(
                selectedTransform.position,
                selectedTransform.rotation,
                new Vector3(markerSize, markerSize, markerSize));
        }

        /// <summary>
        /// The object's own bounds in its local space, so the outline can be an oriented box.
        /// </summary>
        /// <remarks>
        /// <c>Renderer.bounds</c> is deliberately not used: it is a world-axis-aligned box, so an
        /// outline built from it would swell and swing as the rig turns instead of hugging the
        /// object. <c>localBounds</c> and the mesh's own bounds are in the renderer's space, which
        /// is what makes the highlight follow the object's rotation.
        /// </remarks>
        private static bool TryGetLocalBounds(Transform node, out Bounds localBounds)
        {
            localBounds = default(Bounds);

            SkinnedMeshRenderer skinnedRenderer = node.GetComponent<SkinnedMeshRenderer>();
            if (skinnedRenderer != null)
            {
                localBounds = skinnedRenderer.localBounds;
                return true;
            }

            MeshFilter meshFilter = node.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                localBounds = meshFilter.sharedMesh.bounds;
                return true;
            }

            return false;
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

            // Before Cleanup: these live in the render utility's scene, and cleaning that up first
            // would leave the references pointing at objects Unity has already destroyed.
            sceneGizmos.Dispose();
            boneHandles.Dispose();

            mirrorRootAdded = false;
            skeletonRootAdded = false;
            gizmosAdded = false;
            boneHandlesAdded = false;
            selectedHierarchyIndex = -1;
            if (renderUtility != null)
            {
                renderUtility.Cleanup();
                renderUtility = null;
            }
        }
    }
}
