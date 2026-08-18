// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
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
    /// <strong>Rest poses come from the prefab in the toolbar's rig field.</strong> Each target
    /// binds by name to a transform of that prefab and takes its root-relative position, rotation
    /// and scale; authored keys are offsets *from* that, which is exactly how the runtime composes.
    /// They used to be identity, which showed the authored motion faithfully but showed it about
    /// the origin rather than about where the part sits on a built actor — every part a unit quad
    /// in a heap. A target with no matching transform still falls back to identity, which is what a
    /// set with no prefab loaded gets.
    /// </para>
    /// <para>
    /// <strong>The rig is built at the origin and the camera is aimed at the rig.</strong> Those are
    /// separate questions and conflating them is why the view used to look at the ground between a
    /// character's feet: the origin is where the rig is *placed* — it is what the floor grid is
    /// drawn for — but a character stands on the floor, so none of it is near 0,0,0.
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
        /// <summary>Used only when there is no geometry to frame, so nothing tells us how far back to be.</summary>
        private const float DefaultOrbitDistance = 6f;

        private const float MinimumOrbitDistance = 1f;
        private const float MaximumOrbitDistance = 60f;

        /// <summary>Must match the camera's own field of view, or framing overshoots or crops.</summary>
        private const float FrameFieldOfViewDegrees = 45f;

        /// <summary>Margin around the framed rig, so it is not flush against the viewport edge.</summary>
        private const float FramePadding = 1.25f;

        /// <summary>Keeps a degenerate rig — one flat quad, or nothing but a socket marker — framable.</summary>
        private const float MinimumFrameRadius = 0.25f;

        private PreviewRenderUtility renderUtility;
        private readonly PreviewRigMirror rigMirror = new PreviewRigMirror();

        /// <summary>
        /// Socket markers and their preview attachments.
        /// </summary>
        /// <remarks>
        /// Its own object rather than part of either mirror, because a socket may follow either
        /// one -- a rig-target part or a posed skeleton bone -- and living inside one of them would
        /// have made the other kind the awkward case forever.
        /// </remarks>
        private readonly PreviewSocketMarkers socketMarkers = new PreviewSocketMarkers();
        private bool socketRootAdded;

        /// <summary>The rig the part quads were built from, so an edit does not rebuild them.</summary>
        private RigAsset mirrorRig;

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

        private readonly PreviewTransformGizmo transformGizmo = new PreviewTransformGizmo();
        private bool transformGizmoAdded;
        private bool hasGizmo;
        private GizmoMode gizmoMode = GizmoMode.Move;
        private Vector3 gizmoPivot;
        private GizmoHandle activeGizmoHandle;

        /// <summary>
        /// What is selected, as an index into the skeleton mirror's depth-first transform list.
        /// -1 is nothing. Not a <c>Transform</c> reference, because the instance is destroyed and
        /// rebuilt whenever the rig changes and a held reference would be a destroyed object.
        /// </summary>
        private int selectedHierarchyIndex = -1;

        /// <summary>
        /// The rig target the outline follows when a part is selected instead of a bone, or 0 for
        /// none. Separate from <see cref="selectedHierarchyIndex"/> because the two name things in
        /// different hierarchies — a target lives in the rig, a bone in the previewed prefab.
        /// </summary>
        private uint selectedTargetId;

        /// <summary>The socket the outline follows, or 0. A third selectable kind, hence a third field.</summary>
        private uint selectedSocketId;

        private BlobAssetReference<ClipRegistryBlob> registry;
        private ClipSetAsset boundClipSet;

        /// <summary>
        /// What the last <see cref="SamplePose"/> was for, so the billboard pass can read the same
        /// clip's keyed channels at the same instant the pose came from.
        /// </summary>
        private ulong lastSampledClipId;
        private float lastSampledNormalizedTime;
        private bool hasSampledClip;

        /// <summary>
        /// Whether the viewport shows billboarding (amendment A44). On by default, because a preview
        /// that silently differs from the game is worse than no preview.
        /// </summary>
        /// <remarks>
        /// Switchable because a billboarded rig always faces the camera, which makes the authored
        /// pose impossible to inspect from any other angle - orbiting shows you the same view. An
        /// author placing parts needs to be able to turn it off and see what they actually authored.
        /// </remarks>
        public bool BillboardPreviewEnabled { get; set; } = true;
        private string statusMessage = "No clip set assigned.";

        private float orbitYaw = 0f;
        private float orbitPitch = 0f;
        private float orbitDistance = DefaultOrbitDistance;

        /// <summary>The point the camera orbits and looks at — the middle of the rig, not the origin.</summary>
        private Vector3 orbitFocus = Vector3.zero;

        /// <summary>
        /// Whether the camera should reframe on the next render.
        /// </summary>
        /// <remarks>
        /// Deferred rather than framed at the moment the rig changes, because the two halves of a
        /// rig arrive separately: the clip set brings the part quads and the toolbar's prefab field
        /// brings the mesh. Framing on whichever landed first would aim the camera at half a
        /// character. A render is the first moment both are known to be in place.
        /// </remarks>
        private bool framePending = true;

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
                socketMarkers.Dispose();
                socketRootAdded = false;
                mirrorRig = null;
                statusMessage = "No clip set assigned.";
                return;
            }

            RebuildMirrorIfRigChanged(clipSet.rig);
            if (rigMirror.PartCount == 0)
            {
                statusMessage = "Clip set's rig declares no targets.";
                return;
            }

            RebuildRegistry(clipSet);
        }

        /// <summary>
        /// Rebuilds the part quads, but only when the rig they were built from has actually changed.
        /// </summary>
        /// <remarks>
        /// <strong>The guard is the point.</strong> Every clip edit refreshes the preview, and this
        /// used to destroy and recreate all 30-odd part objects each time. A fresh quad is a unit
        /// quad at the origin until the next pose lands on it, so an edit that had nothing to do
        /// with transforms — keying a flipbook index, say — made the whole rig visibly jump. Parts
        /// are a function of the rig, not of the clip being edited, so they should survive an edit
        /// to the clip.
        /// </remarks>
        private void RebuildMirrorIfRigChanged(RigAsset rig)
        {
            if (mirrorRig == rig && rigMirror.PartCount > 0)
            {
                return;
            }
            mirrorRig = rig;
            rigMirror.Rebuild(rig);
            mirrorRootAdded = false;
            socketMarkers.Rebuild(rig, UnityEditor.AssetDatabase
                .GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));
            socketRootAdded = false;
            restPosesDirty = true;
            framePending = true;
            ApplyRestPoses();
        }

        /// <summary>
        /// Puts every part at its rest pose, with no clip applied.
        /// </summary>
        /// <remarks>
        /// What a freshly built mirror should look like: the character standing as the prefab has
        /// it. Without this a new mirror is a heap of unit quads on the origin until a clip is
        /// selected and sampled, which reads as a broken rig rather than as an unposed one.
        /// </remarks>
        private void ApplyRestPoses()
        {
            if (mirrorRig == null || mirrorRig.targets == null)
            {
                return;
            }
            RebuildRestPosesIfNeeded();

            for (int targetIndex = 0; targetIndex < mirrorRig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = mirrorRig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }
                uint targetId = target.Id.Value;
                TargetRestPose rest = ResolveRestPose(targetId);
                TargetPose pose = new TargetPose
                {
                    localPosition = rest.localPosition,
                    rotation = rest.rotation,
                    scale = rest.scale,
                    sliceIndex = rest.restSliceIndex,
                    atlasRect = ClipSampler.IdentityAtlasRect
                };
                rigMirror.ApplyPose(targetId, in pose);
            }
            socketMarkers.UpdateMarkers(rigMirror, skeletonMirror);
        }

        private void RebuildRegistry(ClipSetAsset clipSet)
        {
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

            // The names the rest poses were bound to belong to the old instance, so they are rebound
            // and re-applied here rather than waiting for the next clip sample — with no clip
            // selected there may not be one.
            restPosesDirty = true;
            framePending = true;
            ApplyRestPoses();

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

        /// <summary>The transform at a hierarchy index, or null when the index names nothing.</summary>
        public Transform GetTransformByIndex(int hierarchyIndex)
        {
            return skeletonMirror.GetTransformByIndex(hierarchyIndex);
        }

        /// <summary>The socket a picked transform stands for, or false when it is not a socket.</summary>
        public bool TryGetSocketIdForTransform(Transform picked, out uint socketId)
        {
            return socketMarkers.TryGetSocketId(picked, out socketId);
        }

        /// <summary>The marker transform for a socket, or null when it has none.</summary>
        public Transform GetSocketMarker(uint socketId)
        {
            return socketMarkers.GetMarker(socketId);
        }

        /// <summary>
        /// The transform a socket currently follows, or null when its binding resolves to nothing.
        /// </summary>
        /// <remarks>
        /// Exposed so the window can invert the marker's composition when a gizmo drag ends —
        /// turning a dragged world-ish pose back into the local offset a socket actually stores.
        /// </remarks>
        public Transform GetSocketFollowedTransform(SocketDefinition socket)
        {
            return socketMarkers.GetFollowedTransform(socket, rigMirror, skeletonMirror);
        }

        /// <summary>Whether a socket's binding resolves to something the preview is showing.</summary>
        public bool IsSocketResolved(SocketDefinition socket)
        {
            return socketMarkers.IsResolved(socket, rigMirror, skeletonMirror);
        }

        /// <summary>Re-instantiates socket preview attachments after one has been reassigned.</summary>
        public void RefreshSocketAttachments()
        {
            socketMarkers.RebuildAttachments();
        }

        /// <summary>
        /// Rebuilds socket markers after the rig's socket list itself has changed.
        /// </summary>
        /// <remarks>
        /// Separate from the rig-mirror rebuild, which is guarded on the rig <em>asset</em> changing
        /// and so would not notice a socket being added to the rig it already holds.
        /// </remarks>
        public void RebuildSockets()
        {
            socketMarkers.Rebuild(mirrorRig, AssetDatabase
                .GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));
            socketRootAdded = false;
            socketMarkers.UpdateMarkers(rigMirror, skeletonMirror);
        }

        /// <summary>
        /// Every transform name in the loaded prefab, for checking which bindings still resolve.
        /// </summary>
        /// <remarks>
        /// Names rather than transforms because that is the shape every name-based binding in the
        /// toolkit is checked against — a bone track's <c>boneName</c>, a bone socket's, and a rig
        /// target's <c>displayName</c>. An empty set means no prefab is loaded, which callers must
        /// read as "cannot tell" rather than "everything is broken".
        /// </remarks>
        public void CollectHierarchyNames(HashSet<string> names)
        {
            names.Clear();
            IReadOnlyList<Transform> transforms = skeletonMirror.TransformsByIndex;
            for (int index = 0; index < transforms.Count; index++)
            {
                Transform node = transforms[index];
                if (node != null)
                {
                    names.Add(node.name);
                }
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
            selectedTargetId = 0u;
            selectedSocketId = 0u;
            if (hierarchyIndex < 0)
            {
                sceneGizmos.HideSelection();
            }
        }

        /// <summary>
        /// Sets the outline to follow a rig target's mirrored part instead of a prefab transform.
        /// </summary>
        /// <remarks>
        /// The two selections are mutually exclusive because there is one outline and one inspector.
        /// Clearing the other here is what stops a stale bone index outlining a joint while the
        /// inspector talks about a part.
        /// </remarks>
        public void SetSelectedTargetId(uint targetId)
        {
            selectedTargetId = targetId;
            selectedHierarchyIndex = -1;
            selectedSocketId = 0u;
            if (targetId == 0u)
            {
                sceneGizmos.HideSelection();
            }
        }

        /// <summary>Points the selection outline at a socket marker. 0 for nothing.</summary>
        public void SetSelectedSocketId(uint socketId)
        {
            selectedSocketId = socketId;
            selectedTargetId = 0u;
            selectedHierarchyIndex = -1;
            if (socketId == 0u)
            {
                sceneGizmos.HideSelection();
            }
        }

        /// <summary>
        /// How long a gizmo handle is in world units — scaled by camera distance so it holds its
        /// size on screen, and read by the drawing and the picking alike.
        /// </summary>
        public float GizmoHandleLength
        {
            get { return Mathf.Clamp(orbitDistance * 0.16f, 0.05f, 4f); }
        }

        /// <summary>Places the transform gizmo, or hides it.</summary>
        public void SetGizmo(bool visible, GizmoMode mode, Vector3 pivot, GizmoHandle activeHandle)
        {
            hasGizmo = visible;
            gizmoMode = mode;
            gizmoPivot = pivot;
            activeGizmoHandle = activeHandle;
            if (!visible)
            {
                transformGizmo.Hide();
            }
        }

        /// <summary>A world ray through a viewport point, for gizmo picking and dragging.</summary>
        /// <remarks>
        /// Poses the camera first for the same reason <see cref="CollectPickHits"/> does: a drag is
        /// handled outside the render loop, and a stale camera turns a gizmo drag into a value that
        /// tracks nothing the user can see.
        /// </remarks>
        public Ray BuildViewportRay(Vector2 viewportPoint, float aspect)
        {
            EnsureRenderUtility();
            ApplyCameraPose();
            return PreviewScenePicker.BuildRay(
                renderUtility.camera.transform, renderUtility.camera.fieldOfView, aspect, viewportPoint);
        }

        /// <summary>The gizmo handle under a viewport point, or none.</summary>
        public GizmoHandle PickGizmoHandle(Vector2 viewportPoint, float aspect)
        {
            if (!hasGizmo)
            {
                return GizmoHandle.None;
            }
            return PreviewGizmoMath.PickHandle(
                BuildViewportRay(viewportPoint, aspect), gizmoMode, gizmoPivot, GizmoHandleLength);
        }

        /// <summary>The rig target a picked transform stands for, or false when it is not a part.</summary>
        public bool TryGetTargetIdForTransform(Transform pickedTransform, out uint targetId)
        {
            return rigMirror.TryGetTargetId(pickedTransform, out targetId);
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

            EnsureRenderUtility();
            ApplyCameraPose();

            Ray pickRay = PreviewScenePicker.BuildRay(
                renderUtility.camera.transform,
                renderUtility.camera.fieldOfView,
                aspect,
                viewportPoint);

            if (HierarchyRoot != null)
            {
                PreviewScenePicker.CollectHits(
                    HierarchyRoot, boneHandles.Bones, BoneHandleRadius, pickRay, hits);
            }

            // The cutout parts are pickable too, now that rig targets have rows in the hierarchy
            // pane to be selected into. They were excluded while they had none: a click that
            // selected something the user could not see selected is worse than a click that does
            // nothing.
            if (rigMirror.RootObject != null)
            {
                List<PreviewPickHit> partHits = new List<PreviewPickHit>();
                PreviewScenePicker.CollectHits(
                    rigMirror.RootObject.transform, null, 0f, pickRay, partHits);
                for (int hitIndex = 0; hitIndex < partHits.Count; hitIndex++)
                {
                    uint hitTargetId;
                    if (rigMirror.TryGetTargetId(partHits[hitIndex].pickedTransform, out hitTargetId))
                    {
                        hits.Add(partHits[hitIndex]);
                    }
                }
            }

            // Sockets last, which puts them first among equals: hits are ordered nearest-first
            // afterwards, and a socket sits *inside* the hand it is attached to. Adding them at all
            // is what makes a socket clickable rather than reachable only through the tree.
            if (socketMarkers.RootObject != null)
            {
                PreviewScenePicker.CollectHits(
                    socketMarkers.RootObject.transform, null, 0f, pickRay, hits);
            }
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

        /// <summary>
        /// Rebuilds the registry against the currently bound set — call after an edit.
        /// </summary>
        /// <remarks>
        /// An edit changes the clip's <em>data</em>, so only the registry built from it is stale.
        /// The part quads are built from the rig and are left standing, which is what stops an edit
        /// to one track from making every part blink through the origin on its way back.
        /// </remarks>
        public void Refresh()
        {
            if (boundClipSet == null)
            {
                SetClipSet(null);
                return;
            }

            ReleaseRegistry();
            statusMessage = string.Empty;

            RebuildMirrorIfRigChanged(boundClipSet.rig);
            if (rigMirror.PartCount == 0)
            {
                statusMessage = "Clip set's rig declares no targets.";
                return;
            }

            RebuildRegistry(boundClipSet);
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
            RebuildRestPosesIfNeeded();

            lastSampledClipId = clipId;
            lastSampledNormalizedTime = normalizedTime;
            hasSampledClip = true;

            for (int targetIndex = 0; targetIndex < registryBlob.sortedTargetIds.Length; targetIndex++)
            {
                uint targetId = registryBlob.sortedTargetIds[targetIndex];
                TargetRestPose rest = ResolveRestPose(targetId);

                TargetPose pose;
                ClipSampler.SamplePose(ref clipBlob, targetIndex, normalizedTime, in rest, out pose);
                rigMirror.ApplyPose(targetId, in pose);
            }

            // After the whole pose, never inside the loop: a marker placed before its part is posed
            // shows the previous frame and reads as the socket lagging the rig.
            socketMarkers.UpdateMarkers(rigMirror, skeletonMirror);

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

        /// <summary>
        /// The rest pose every part is animated <em>from</em>, taken from the loaded prefab.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A part's rest pose is where the prefab puts it, not the origin.</strong> This
        /// used to be a hard-coded identity, which made every part a unit quad stacked on the origin
        /// and meant the preview showed the authored offsets rather than the character. Worse, it
        /// made the preview disagree with the runtime for no reason: <c>TransformApplySystem</c>
        /// composes against the entity's real rest pose, so a clip that looked right here would not
        /// look right in play.
        /// </para>
        /// <para>
        /// The composition rules are what make this the correct fix rather than a cosmetic one.
        /// Position and rotation are <em>additive</em> against the rest pose and scale is
        /// <em>multiplicative</em> (§5.11), so a part with no track, or a track authored at zero
        /// offset and unit scale, now sits exactly where the prefab has it. Nothing about the
        /// authored data changes; a key of "no offset" finally means no offset.
        /// </para>
        /// <para>
        /// Matched by name, because a rig target's <c>displayName</c> is the only thing it and a
        /// prefab transform have in common — a target carries a stable id the prefab has never heard
        /// of. An unmatched target falls back to identity, which is what a cutout set with no prefab
        /// loaded gets, and is the old behaviour exactly.
        /// </para>
        /// <para>
        /// <strong>Measured relative to the prefab root, not to the transform's own parent.</strong>
        /// The mirror parents every part under one flat root, so a part's <c>localPosition</c> is
        /// meaningless here — a cutout prefab nests deeply (pelvis → torso → neck → head → eyes),
        /// and taking the local offset of each would pile the whole character back onto the origin
        /// one link at a time. The root-relative transform is what "where this part sits in the
        /// character" actually means once the hierarchy is flattened.
        /// </para>
        /// </remarks>
        private readonly Dictionary<uint, TargetRestPose> targetRestPoses =
            new Dictionary<uint, TargetRestPose>();

        /// <summary>Set whenever the rig or the loaded prefab changes, either of which rebinds names.</summary>
        private bool restPosesDirty = true;

        private static readonly TargetRestPose IdentityRestPose = new TargetRestPose
        {
            localPosition = Unity.Mathematics.float3.zero,
            rotation = Unity.Mathematics.float3.zero,
            scale = new Unity.Mathematics.float3(1f, 1f, 1f),
            restSliceIndex = 0
        };

        private TargetRestPose ResolveRestPose(uint targetId)
        {
            TargetRestPose rest;
            return targetRestPoses.TryGetValue(targetId, out rest) ? rest : IdentityRestPose;
        }

        private void RebuildRestPosesIfNeeded()
        {
            if (!restPosesDirty)
            {
                return;
            }
            restPosesDirty = false;
            targetRestPoses.Clear();

            if (boundClipSet == null || boundClipSet.rig == null)
            {
                return;
            }

            Transform instanceRoot = skeletonMirror.InstanceRoot != null
                ? skeletonMirror.InstanceRoot.transform
                : null;
            if (instanceRoot == null)
            {
                return;
            }

            List<RigTargetDefinition> targets = boundClipSet.rig.targets;
            for (int targetIndex = 0; targets != null && targetIndex < targets.Count; targetIndex++)
            {
                RigTargetDefinition target = targets[targetIndex];
                if (target == null || string.IsNullOrEmpty(target.displayName))
                {
                    continue;
                }

                Transform sourceTransform;
                if (!skeletonMirror.TryGetBone(target.displayName, out sourceTransform)
                    || sourceTransform == null)
                {
                    continue;
                }

                // The part's transform expressed in the prefab root's space, which is the space the
                // flat mirror root stands in. Composing the two matrices is the only way to get it
                // that survives an arbitrary nesting depth.
                Matrix4x4 rootRelative =
                    instanceRoot.worldToLocalMatrix * sourceTransform.localToWorldMatrix;
                Vector3 relativePosition = rootRelative.GetPosition();
                Vector3 relativeEuler = rootRelative.rotation.eulerAngles;
                Vector3 relativeScale = rootRelative.lossyScale;

                // Degrees to radians because the blob's rotations are radians (§4.5) and this value
                // is added to them before ApplyPose converts the sum back for the Transform.
                targetRestPoses[target.Id.Value] = new TargetRestPose
                {
                    localPosition = new Unity.Mathematics.float3(
                        relativePosition.x, relativePosition.y, relativePosition.z),
                    rotation = new Unity.Mathematics.float3(
                        relativeEuler.x * Mathf.Deg2Rad,
                        relativeEuler.y * Mathf.Deg2Rad,
                        relativeEuler.z * Mathf.Deg2Rad),
                    scale = new Unity.Mathematics.float3(
                        relativeScale.x, relativeScale.y, relativeScale.z),
                    restSliceIndex = 0
                };
            }
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
            orbitDistance = Mathf.Clamp(
                orbitDistance + amount, MinimumOrbitDistance, MaximumOrbitDistance);
        }

        /// <summary>
        /// Returns the camera to the pose the window opens with: head-on, framing the rig.
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
            FrameRig();
        }

        /// <summary>
        /// Points the camera at the rig and backs off far enough to hold all of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Placement and framing are separate questions.</strong> The rig is built at the
        /// origin — that is where it belongs in the space, and the floor grid is drawn for it there.
        /// But a character stands <em>on</em> the floor, so none of it is near 0,0,0; a camera aimed
        /// at the origin looks at the ground between its feet. This aims at the middle of what is
        /// actually there.
        /// </para>
        /// <para>
        /// Distance comes from the bounding sphere and the vertical field of view, so it fits a
        /// two-metre character and a twenty-metre vehicle without either being guesswork. The
        /// padding leaves a margin so the rig is not flush against the viewport edge, and the clamp
        /// is the same one <see cref="Zoom"/> uses — framing must not put the camera somewhere the
        /// user cannot zoom back out of.
        /// </para>
        /// </remarks>
        public void FrameRig()
        {
            framePending = false;

            Bounds rigBounds;
            if (!TryComputeRigBounds(out rigBounds))
            {
                orbitFocus = Vector3.zero;
                orbitDistance = DefaultOrbitDistance;
                return;
            }

            orbitFocus = rigBounds.center;

            float radius = Mathf.Max(rigBounds.extents.magnitude, MinimumFrameRadius);
            float halfFieldOfViewRadians = FrameFieldOfViewDegrees * 0.5f * Mathf.Deg2Rad;
            orbitDistance = Mathf.Clamp(
                radius / Mathf.Tan(halfFieldOfViewRadians) * FramePadding,
                MinimumOrbitDistance,
                MaximumOrbitDistance);
        }

        /// <summary>
        /// The world bounds of everything the preview draws as the rig.
        /// </summary>
        /// <remarks>
        /// Both mirrors count. The cutout parts are the rig for a paper-doll set, and the
        /// instantiated prefab is the rig for a skinned one — a rigged character's targets are a
        /// handful of quads at rest, so framing those alone would zoom in on nothing. Renderers are
        /// taken as they are, which means an extra in the prefab that is not really part of the
        /// character (a health bar above its head, say) widens the frame a little. That is the right
        /// trade: guessing which children "count" by name would be wrong in ways nobody could
        /// predict.
        /// </remarks>
        private bool TryComputeRigBounds(out Bounds rigBounds)
        {
            rigBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasAny = false;

            EncapsulateRenderers(rigMirror.RootObject, ref rigBounds, ref hasAny);
            EncapsulateRenderers(skeletonMirror.InstanceRoot, ref rigBounds, ref hasAny);
            return hasAny;
        }

        private static void EncapsulateRenderers(
            GameObject root, ref Bounds rigBounds, ref bool hasAny)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasAny)
                {
                    rigBounds = renderer.bounds;
                    hasAny = true;
                    continue;
                }
                rigBounds.Encapsulate(renderer.bounds);
            }
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

            // Framed here rather than when the rig changed, because a rig arrives in two pieces —
            // the clip set's part quads and the toolbar prefab's mesh — and only at a render are
            // both known to be standing. Once only: after this the camera is the user's, and
            // reframing on any later render would fight every orbit they make.
            if (framePending)
            {
                FrameRig();
            }

            // Bone handles are rewritten every frame because the bones move as the clip scrubs, and
            // the markers are also the click targets — stale markers would be a viewport where the
            // handles and the hit tests disagree about where the skeleton is.
            boneHandles.UpdateGeometry(BoneHandleRadius);
            UpdateSelectionMarker();

            if (hasGizmo)
            {
                transformGizmo.Rebuild(gizmoMode, gizmoPivot, GizmoHandleLength, activeGizmoHandle);
            }
            else
            {
                transformGizmo.Hide();
            }

            ApplyCameraPose();

            // After the camera, because billboarding is defined against it; after the pose, because
            // the pose is the billboard's rest orientation. That is the runtime's order exactly
            // (TransformSampleSystem, TransformApplySystem, BillboardResolveSystem), and it has to
            // be, or the viewport would answer a different question from the game.
            ApplyBillboards();

            renderUtility.BeginPreview(new Rect(0f, 0f, pixelWidth, pixelHeight), GUIStyle.none);
            renderUtility.camera.Render();
            return renderUtility.EndPreview();
        }

        /// <summary>
        /// Poses the camera on its orbit around <see cref="orbitFocus"/>.
        /// </summary>
        /// <remarks>
        /// The focus is a field rather than the origin because the rig is <em>placed</em> at the
        /// origin but does not sit centred on it — a character stands on the floor, so its mass is
        /// entirely above y = 0. Orbiting the origin put it in the top half of the frame and
        /// swung it around a point below its feet.
        /// </remarks>
        /// <summary>
        /// Turns the preview's billboard roots to face the preview camera (amendment A44).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Every number here comes from <c>BillboardMath</c>, and none of it is
        /// re-derived.</strong> The viewport's only job is to feed that function the same inputs the
        /// runtime job feeds it - this camera instead of the game's, these transforms instead of
        /// those entities - so the two cannot disagree about facing, snapping, clamping or blending.
        /// A preview with its own copy of the arithmetic would agree until either gained a feature
        /// and then diverge silently, which is the failure <c>SocketPreviewParityTests</c> exists to
        /// prevent for sockets.
        /// </para>
        /// <para>
        /// <strong>Shallowest first, writing world rotations.</strong> Unity's <c>Transform</c>
        /// converts a world rotation into the parent's space for us, so setting
        /// <c>node.rotation</c> does what the runtime's inverse-parent multiply does by hand - and
        /// because the hierarchy updates immediately, a nested root reading its own world rotation
        /// after its ancestor was written already sees the ancestor's billboard. Same mechanism,
        /// reached more cheaply, and it is why a held item does not turn twice here either.
        /// </para>
        /// </remarks>
        private void ApplyBillboards()
        {
            if (!BillboardPreviewEnabled || mirrorRig == null || renderUtility == null)
            {
                return;
            }

            Transform previewRoot = HierarchyRoot;
            if (previewRoot == null)
            {
                return;
            }

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(mirrorRig, previewRoot, null);
            if (resolvedRoots.Count == 0)
            {
                return;
            }

            Transform cameraTransform = renderUtility.camera.transform;
            float3 cameraPosition = cameraTransform.position;
            float3 cameraForward = cameraTransform.forward;

            for (int rootIndex = 0; rootIndex < resolvedRoots.Count; rootIndex++)
            {
                ResolvedBillboardRoot resolvedRoot = resolvedRoots[rootIndex];
                Transform node = resolvedRoot.node;
                if (node == null)
                {
                    continue;
                }

                BillboardSettings settings = BuildPreviewSettings(resolvedRoot.definition);

                quaternion resolvedRotation;
                if (BillboardMath.TryResolve(
                        settings,
                        node.position,
                        cameraPosition,
                        cameraForward,
                        node.rotation,
                        out resolvedRotation))
                {
                    node.rotation = resolvedRotation;
                }
            }
        }

        /// <summary>
        /// The runtime parameter block for one authored root, with the selected clip's keyed
        /// channels folded in at the playhead.
        /// </summary>
        /// <remarks>
        /// Mirrors <c>ActorBaker.BuildBillboardSettings</c> and
        /// <c>BillboardResolveSystem.ApplyKeyedChannels</c> together, because the preview has neither
        /// a bake nor playback layers to go through. The conversions - degrees to radians, the two
        /// opt-in booleans to sentinels, the arc halved - are the one thing this pass repeats rather
        /// than calls. If a third caller ever needs them they belong on
        /// <c>BillboardRootDefinition</c> itself.
        /// </remarks>
        private BillboardSettings BuildPreviewSettings(BillboardRootDefinition definition)
        {
            BillboardSettings settings = new BillboardSettings
            {
                mode = definition.mode,
                constraintAxis = math.normalizesafe(definition.constraintAxis),
                frozenYaw = 0f,
                angleOffsetRadians = math.radians(definition.angleOffsetDegrees),
                blendWeight = 1f,
                enabled = true,
                snapSteps = definition.snapEnabled ? Mathf.Max(2, definition.snapSteps) : 0,
                snapPhaseRadians = math.radians(definition.snapOffsetDegrees),
                clampHalfArcRadians = definition.clampEnabled
                    ? math.radians(definition.clampArcDegrees) * 0.5f
                    : -1f
            };

            if (!hasSampledClip || !registry.IsCreated)
            {
                return settings;
            }

            ref ClipRegistryBlob registryBlob = ref registry.Value;
            for (int clipIndex = 0; clipIndex < registryBlob.sortedClipIds.Length; clipIndex++)
            {
                if (registryBlob.sortedClipIds[clipIndex] != lastSampledClipId)
                {
                    continue;
                }

                ref ClipBlob clipBlob = ref registryBlob.clips[clipIndex];
                for (int trackIndex = 0; trackIndex < clipBlob.billboardTracks.Length; trackIndex++)
                {
                    if (clipBlob.billboardTracks[trackIndex].rootId != definition.stableId)
                    {
                        continue;
                    }

                    float keyedAngleOffset;
                    float keyedBlendWeight;
                    bool keyedEnabled;
                    ClipSampler.SampleBillboardTrack(
                        ref clipBlob.billboardTracks[trackIndex],
                        lastSampledNormalizedTime,
                        out keyedAngleOffset,
                        out keyedBlendWeight,
                        out keyedEnabled);

                    settings.angleOffsetRadians += keyedAngleOffset;
                    settings.blendWeight = keyedBlendWeight;
                    settings.enabled = keyedEnabled;
                    return settings;
                }
                break;
            }
            return settings;
        }

        private void ApplyCameraPose()
        {
            Quaternion orbitRotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            renderUtility.camera.transform.position =
                orbitFocus + orbitRotation * new Vector3(0f, 0f, -orbitDistance);
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

            transformGizmo.EnsureBuilt();
            if (transformGizmo.GizmoObject != null && !transformGizmoAdded)
            {
                renderUtility.AddSingleGO(transformGizmo.GizmoObject);
                transformGizmoAdded = true;
            }

            if (rigMirror.RootObject != null && !mirrorRootAdded)
            {
                renderUtility.AddSingleGO(rigMirror.RootObject);
                mirrorRootAdded = true;
            }

            if (socketMarkers.RootObject != null && !socketRootAdded)
            {
                renderUtility.AddSingleGO(socketMarkers.RootObject);
                socketRootAdded = true;
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
            Transform selectedTransform;
            if (selectedSocketId != 0u)
            {
                selectedTransform = socketMarkers.GetMarker(selectedSocketId);
            }
            else if (selectedTargetId != 0u)
            {
                selectedTransform = rigMirror.GetPartTransform(selectedTargetId);
            }
            else
            {
                selectedTransform = skeletonMirror.GetTransformByIndex(selectedHierarchyIndex);
            }
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
            renderUtility.camera.fieldOfView = FrameFieldOfViewDegrees;
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
            socketMarkers.Dispose();
            skeletonMirror.Dispose();

            // Before Cleanup: these live in the render utility's scene, and cleaning that up first
            // would leave the references pointing at objects Unity has already destroyed.
            sceneGizmos.Dispose();
            boneHandles.Dispose();
            transformGizmo.Dispose();

            mirrorRootAdded = false;
            skeletonRootAdded = false;
            gizmosAdded = false;
            boneHandlesAdded = false;
            transformGizmoAdded = false;
            hasGizmo = false;
            selectedHierarchyIndex = -1;
            if (renderUtility != null)
            {
                renderUtility.Cleanup();
                renderUtility = null;
            }
        }
    }
}
