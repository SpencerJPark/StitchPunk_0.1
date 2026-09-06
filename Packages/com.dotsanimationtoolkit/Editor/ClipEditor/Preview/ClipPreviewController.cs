// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Drives the clip preview: transient registry blob in, rendered texture out. Visual parity is
    /// by construction, not effort — the pose comes from <see cref="ClipSampler"/> and a registry
    /// built by <see cref="ClipRegistryBuilder"/>, the same functions the runtime and baker use.
    /// <see cref="Render"/> never depends on selection: it draws whatever the scene holds, at
    /// minimum the reference grid, whether or not a clip is selected.
    /// </summary>
    public sealed class ClipPreviewController : IDisposable
    {
        /// <summary>Used only when there is no geometry to frame, so nothing tells us how far back to be.</summary>
        private const float DefaultOrbitDistance = 6f;

        private const float MinimumOrbitDistance = 1f;
        private const float MaximumOrbitDistance = 60f;

        /// <summary>Degrees a pixel of drag is worth, orbiting or looking around.</summary>
        private const float DegreesPerDragPixel = 0.4f;

        /// <summary>How fast RMB + WASD flies, in world units a second, before Shift.</summary>
        private const float FlySpeedUnitsPerSecond = 4f;

        /// <summary>Shift's multiplier on the fly speed, matching the Scene view's own accelerator.</summary>
        private const float FlyFastMultiplier = 4f;

        // What fraction of the current distance one pixel of Alt + RMB drag closes. A fraction
        // rather than a fixed step, so the same drag reads as the same gesture near and far.
        private const float DollyFractionPerPixel = 0.005f;

        /// <summary>How much bigger than a joint marker the frame around a bone is.</summary>
        private const float BonelessFrameRadiusMultiplier = 4f;

        /// <summary>Must match the camera's own field of view, or framing overshoots or crops.</summary>
        private const float FrameFieldOfViewDegrees = 45f;

        /// <summary>Margin around the framed rig, so it is not flush against the viewport edge.</summary>
        private const float FramePadding = 1.25f;

        /// <summary>Keeps a degenerate rig — one flat quad, or nothing but a socket marker — framable.</summary>
        private const float MinimumFrameRadius = 0.25f;

        // How high above the floor the camera aims, at the least, in world units. Aiming at the
        // origin points the camera at the floor and hands the bottom half of the frame to empty ground.
        private const float MinimumFocusHeight = 1f;

        private PreviewRenderUtility renderUtility;
        private readonly PreviewRigMirror rigMirror = new PreviewRigMirror();

        // Socket markers and their preview attachments. Its own object rather than part of either
        // mirror, since a socket may follow a rig-target part or a posed skeleton bone.
        private readonly PreviewSocketMarkers socketMarkers = new PreviewSocketMarkers();
        private bool socketRootAdded;

        /// <summary>The rig the part quads were built from, so an edit does not rebuild them.</summary>
        private RigAsset mirrorRig;

        // Whether the mirror's current root has joined the preview scene. Tracked separately from
        // the utility's lifetime: the mirror rebuilds whenever the rig changes, so a flag tied to
        // "the utility exists" would leave every root after the first outside the scene.
        private bool mirrorRootAdded;

        private readonly PreviewSkeletonMirror skeletonMirror = new PreviewSkeletonMirror();
        private GameObject skinnedSourcePrefab;

        // Tracked separately from mirrorRootAdded for the same reason: the skeleton instance
        // rebuilds whenever the source changes.
        private bool skeletonRootAdded;

        // The grid and the selection marker. Built once and never rebuilt, so unlike the mirrors
        // these join the preview scene a single time.
        private readonly PreviewSceneGizmos sceneGizmos = new PreviewSceneGizmos();
        private bool gizmosAdded;

        // Joint markers for the skinned source. Rebuilt with the skeleton, so it joins the preview
        // scene again each time — tracked by its own flag for the same reason.
        private readonly PreviewBoneHandles boneHandles = new PreviewBoneHandles();
        private bool boneHandlesAdded;

        private readonly PreviewTransformGizmo transformGizmo = new PreviewTransformGizmo();
        private bool transformGizmoAdded;
        private bool hasGizmo;
        private GizmoMode gizmoMode = GizmoMode.Move;
        private Vector3 gizmoPivot;
        private GizmoHandle activeGizmoHandle;

        // The Ragdoll toolbar toggle's own simulation — the physics, not PreviewRagdollBoxHandles'
        // authoring gizmo.
        private readonly RagdollPreviewSimulation ragdollSimulation = new RagdollPreviewSimulation();
        private bool ragdollPreviewEnabled;

        /// <summary>The viewport's ragdoll box wireframes and the selected body's grab handles.</summary>
        private readonly PreviewRagdollBoxHandles ragdollBoxHandles = new PreviewRagdollBoxHandles();
        private bool ragdollBoxHandlesAdded;
        private uint selectedRagdollBodyId;
        private RagdollBoxHandle activeRagdollBoxHandle;

        // When the ragdoll last stepped, so Render can advance it by real elapsed time measured
        // directly rather than a fixed guess.
        private double lastRagdollTickTime;

        // What is selected, as an index into the skeleton mirror's depth-first transform list. -1
        // is nothing. Not a Transform reference, since the instance is destroyed and rebuilt
        // whenever the rig changes.
        private int selectedHierarchyIndex = -1;

        // The rig target the outline follows when a part is selected instead of a bone, or 0 for
        // none. Separate from selectedHierarchyIndex: a target lives in the rig, a bone in the previewed prefab.
        private uint selectedTargetId;

        /// <summary>The socket the outline follows, or 0. A third selectable kind, hence a third field.</summary>
        private uint selectedSocketId;

        // The one manually-owned blob in the toolkit, and only in the editor: built Persistent
        // because it must outlive the call that made it, so Dispose is not optional.
        private BlobAssetReference<ClipRegistryBlob> registry;
        private ClipSetAsset boundClipSet;

        // The rig the bound set is being previewed on. Supplied by the window rather than read off
        // the set, since a set names no rig.
        private RigAsset boundRig;

        // What the last SamplePose was for, so the billboard pass can read the same clip's keyed
        // channels at the same instant the pose came from.
        private ulong lastSampledClipId;
        private float lastSampledNormalizedTime;
        private bool hasSampledClip;

        // Whether the viewport shows billboarding. On by default, since a preview that silently
        // differs from the game is worse than no preview; switchable because a billboarded rig
        // always faces the camera, making the authored pose impossible to inspect from any other angle.
        public bool BillboardPreviewEnabled { get; set; } = true;
        private string statusMessage = "No clip set assigned.";

        /// <summary>Whether the ragdoll toggle is currently dropping the previewed rig.</summary>
        public bool RagdollPreviewEnabled
        {
            get { return ragdollPreviewEnabled; }
        }

        /// <summary>Whether the active ragdoll has settled — nothing to show once every body sleeps.</summary>
        public bool RagdollPreviewSleeping
        {
            get { return ragdollSimulation.Sleeping; }
        }

        // Off to on: captures whatever pose is currently on screen, builds the body array against
        // it, and starts stepping. Refuses, leaving the toggle unchanged, when the rig has no
        // ragdoll bodies or none resolve in this preview, reporting why.
        public bool TryEnableRagdollPreview(out string refusalReason)
        {
            refusalReason = string.Empty;
            if (ragdollPreviewEnabled)
            {
                return true;
            }
            if (mirrorRig == null)
            {
                refusalReason = "No rig loaded.";
                return false;
            }
            if (!ragdollSimulation.TryBuild(mirrorRig, this, out refusalReason))
            {
                return false;
            }
            ragdollPreviewEnabled = true;
            lastRagdollTickTime = 0d;
            return true;
        }

        // On to off: restores every simulated node's pre-drop pose, then discards the simulation.
        // The restore is explicit rather than left to the next SamplePose, since a node the current
        // clip does not key is never touched by a resample and would stay wherever the ragdoll
        // dropped it. Restoring first, disposing second: Dispose drops the captured poses too.
        public void DisableRagdollPreview()
        {
            if (!ragdollPreviewEnabled)
            {
                return;
            }
            ragdollPreviewEnabled = false;
            ragdollSimulation.RestoreCapturedPose();
            ragdollSimulation.Dispose();
        }

        private float orbitYaw = 0f;
        private float orbitPitch = 0f;
        private float orbitDistance = DefaultOrbitDistance;

        // Where the camera is looking from, in degrees. Read/write so a caller that needs a fixed
        // angle (the 2D Direction Sets viewer) can take one and give it back — the two views share
        // one controller, and a setter alone would discard whatever angle the author had orbited to.
        public float OrbitYaw
        {
            get { return orbitYaw; }
            set { orbitYaw = value; }
        }

        /// <inheritdoc cref="OrbitYaw"/>
        public float OrbitPitch
        {
            get { return orbitPitch; }
            set { orbitPitch = value; }
        }

        /// <summary>The point the camera orbits and looks at — the middle of the rig, not the origin.</summary>
        private Vector3 orbitFocus = Vector3.zero;

        // Whether the camera should reframe on the next render. Deferred rather than framed the
        // moment the rig changes, since the clip set and the prefab field arrive separately and a
        // render is the first moment both are known to be in place.
        private bool framePending = true;

        /// <summary>Why the preview is empty, or an empty string when it is fine.</summary>
        public string StatusMessage
        {
            get { return statusMessage; }
        }

        // Overwrites the status line, for feedback that belongs to one moment rather than the
        // preview's ongoing state. Persists until the next Refresh or SamplePose call has its own thing to say.
        public void ReportTransientStatus(string message)
        {
            statusMessage = message ?? string.Empty;
        }

        /// <summary>Whether a registry is currently built and sampleable.</summary>
        public bool HasRegistry
        {
            get { return registry.IsCreated; }
        }

        // Whether the built registry holds a clip id — "would SamplePose find this". The same
        // linear scan SamplePose runs, against the same array, so the two cannot disagree.
        public bool IsClipInRegistry(ulong clipId)
        {
            if (!registry.IsCreated)
            {
                return false;
            }
            ref ClipRegistryBlob registryBlob = ref registry.Value;
            for (int index = 0; index < registryBlob.sortedClipIds.Length; index++)
            {
                if (registryBlob.sortedClipIds[index] == clipId)
                {
                    return true;
                }
            }
            return false;
        }

        // Rebuilds the transient registry for clipSet and the mirror for its rig. Validation
        // failures are caught rather than propagated, since an authoring window that dies on an
        // invalid clip is useless precisely while the clip is being fixed.
        public void SetClipSet(ClipSetAsset clipSet)
        {
            boundClipSet = clipSet;
            Refresh();
        }

        /// <summary>
        /// Sets the rig the bound set is previewed on. Independent of <see cref="SetClipSet"/>:
        /// either can change without the other, exactly as the two toolbar pickers can.
        /// </summary>
        public void SetRig(RigAsset rig)
        {
            boundRig = rig;
            Refresh();
        }

        // Rebuilds the part quads, but only when the rig they were built from has actually changed.
        // The guard is the point: without it, every clip edit destroyed and recreated all 30-odd
        // part objects, and a fresh quad at the origin made an unrelated edit visibly jump the rig.
        private void RebuildMirrorIfRigChanged(RigAsset rig)
        {
            if (mirrorRig == rig && rigMirror.PartCount > 0)
            {
                return;
            }

            // A rig swap invalidates every node the simulation is holding onto: the old mirror is
            // about to be disposed out from under it.
            DisableRagdollPreview();

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

        // Puts every part at its rest pose, with no clip applied. Without this a new mirror is a
        // heap of unit quads on the origin until a clip is selected and sampled.
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

        // Builds the preview registry, or records why it could not be built. A validation failure
        // is named here, not listed: ClipValidationException.Message is every offending rule on its
        // own line, which would squeeze the 3D preview out of a status label meant for one sentence
        // — ValidationBadgeElement is the surface for the full list. Anything else thrown reports in
        // full, since an unexpected build failure has no other surface here.
        private void RebuildRegistry(ClipSetAsset clipSet)
        {
            try
            {
                Unity.Entities.Hash128 contentHash;
                // The preview binds the one set the window has open to the rig the window is
                // showing — the same shape an actor's bind has, with a list of one.
                ClipRegistryBuilder.Build(
                    boundRig,
                    new ClipSetAsset[] { clipSet },
                    out registry,
                    out contentHash);
            }
            catch (ArgumentNullException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                statusMessage = "Assign a rig to the toolbar's Rig field.";
            }
            catch (ClipValidationException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                statusMessage = "Clip set has validation errors — open the error list in the top bar.";
            }
            catch (Exception buildException)
            {
                registry = default(BlobAssetReference<ClipRegistryBlob>);
                statusMessage = buildException.Message;
            }
        }

        // Assigns the rigged prefab whose skeleton authored bone tracks pose. Optional: null
        // returns the preview to quads-only. Must be the same prefab the VAT bake samples.
        public void SetSkinnedSource(GameObject prefab)
        {
            if (skinnedSourcePrefab == prefab)
            {
                return;
            }

            // The skeleton instance every Bone-kind and HierarchyPath-kind ragdoll body resolves
            // against is about to be destroyed and rebuilt.
            DisableRagdollPreview();

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

        // The previewed rig's root, which the hierarchy pane lists. Null when none. The window
        // builds its tree from this live instance rather than the prefab asset, so a picked
        // transform is literally a node of the tree's own source.
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

        // Whether a hierarchy index names an imported skinned-mesh bone, as opposed to an authored
        // guiding transform. A ragdoll body addresses the two differently: a bone's path below the
        // prefab root is not stable the way a bare transform's is, so it is addressed by name instead.
        public bool IsSkinnedBone(int hierarchyIndex)
        {
            Transform node = skeletonMirror.GetTransformByIndex(hierarchyIndex);
            return node != null && IsSkinnedBone(node);
        }

        // A hierarchy node's own renderer bounds, local to its transform, or false when it carries
        // no renderer to measure. What a freshly added Ragdoll component sizes its box from. Built
        // on TryGetLocalBounds, the same local-space bounds the selection outline uses, since a
        // world-axis-aligned box would swell and swing as the rig turns.
        public bool TryGetLocalRendererBounds(int hierarchyIndex, out Vector3 center, out Vector3 size)
        {
            center = Vector3.zero;
            size = Vector3.one;

            Transform node = skeletonMirror.GetTransformByIndex(hierarchyIndex);
            if (node == null)
            {
                return false;
            }

            Bounds localBounds;
            if (!TryGetLocalBounds(node, out localBounds))
            {
                return false;
            }
            center = localBounds.center;
            size = localBounds.size;
            return true;
        }

        // Resolves a RigNodeAddress to the preview transform it names — the reverse of node→address.
        // Shared by RagdollPreviewSimulation and PreviewRagdollBoxHandles so neither invents its own
        // address→node lookup. RigTarget resolves against rigMirror by id; Bone resolves against
        // skeletonMirror by name; HierarchyPath resolves against skeletonMirror's instance, the only
        // preview surface with the prefab's real nested structure — resolving to nothing when no
        // skinned source is assigned.
        public Transform ResolveRagdollNode(in RigNodeAddress address)
        {
            switch (address.kind)
            {
                case RigNodeAddressKind.RigTarget:
                    return rigMirror.GetPartTransform(address.targetId);

                case RigNodeAddressKind.Bone:
                {
                    Transform boneTransform;
                    return skeletonMirror.TryGetBone(address.boneName, out boneTransform)
                        ? boneTransform
                        : null;
                }

                default:
                {
                    Transform skeletonRoot = HierarchyRoot;
                    if (skeletonRoot == null)
                    {
                        return null;
                    }
                    return string.IsNullOrEmpty(address.hierarchyPath)
                        ? skeletonRoot
                        : skeletonRoot.Find(address.hierarchyPath);
                }
            }
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

        // The transform a socket currently follows, or null when its binding resolves to nothing.
        // Exposed so the window can invert the marker's composition when a gizmo drag ends.
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

        // Re-places the socket markers without rebuilding them. What an offset edit actually needs:
        // RebuildSockets destroys and recreates every marker object, which is heavy to do on each
        // mouse move of a drag and unnecessary since a marker is placed from the socket's numbers each update.
        public void RefreshSocketPlacement()
        {
            socketMarkers.UpdateMarkers(rigMirror, skeletonMirror);
        }

        // Rebuilds socket markers after the rig's socket list itself has changed. Separate from the
        // rig-mirror rebuild, which is guarded on the rig asset changing and would not notice a
        // socket added to the rig it already holds.
        public void RebuildSockets()
        {
            socketMarkers.Rebuild(mirrorRig, AssetDatabase
                .GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));
            socketRootAdded = false;
            socketMarkers.UpdateMarkers(rigMirror, skeletonMirror);
        }

        // Every transform name in the loaded prefab, for checking which bindings still resolve. An
        // empty set means no prefab is loaded, which callers must read as "cannot tell" rather than
        // "everything is broken".
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

        /// <summary>Sets what the selection outline follows, by hierarchy index. -1 for nothing.</summary>
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

        // Sets the outline to follow a rig target's mirrored part instead of a prefab transform.
        // Mutually exclusive with the hierarchy-index selection: clearing the other here stops a
        // stale bone index outlining a joint while the inspector talks about a part.
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

        // A world ray through a viewport point, for gizmo picking and dragging. Poses the camera
        // first, since a drag is handled outside the render loop and a stale camera pose would
        // track nothing the user can see.
        public Ray BuildViewportRay(Vector2 viewportPoint, float aspect)
        {
            EnsureRenderUtility();
            ApplyCameraPose();
            return PreviewScenePicker.BuildRay(
                renderUtility.camera.transform, renderUtility.camera.fieldOfView, aspect, viewportPoint);
        }

        /// <summary>The preview camera's current forward direction — the free-drag plane a ragdoll box's centre handle moves within.</summary>
        public Vector3 CameraForward
        {
            get
            {
                EnsureRenderUtility();
                ApplyCameraPose();
                return renderUtility.camera.transform.forward;
            }
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

        // Points the ragdoll box handles at one body, or none. Separate from
        // SetSelectedSocketId/SetSelectedTargetId rather than a fourth branch of the same field: a
        // Ragdoll selection does not move the ordinary selection outline.
        public void SetSelectedRagdollBodyId(uint bodyId)
        {
            selectedRagdollBodyId = bodyId;
            activeRagdollBoxHandle = RagdollBoxHandle.None;
        }

        /// <summary>Which ragdoll box handle, if any, is mid-drag — for highlighting only; the drag itself is driven by the caller.</summary>
        public void SetActiveRagdollBoxHandle(RagdollBoxHandle handle)
        {
            activeRagdollBoxHandle = handle;
        }

        /// <summary>The selected ragdoll body's box in world space, or false when nothing is selected or it does not resolve.</summary>
        public bool TryGetSelectedRagdollBoxVisual(out RagdollBoxVisual box)
        {
            box = default(RagdollBoxVisual);
            if (selectedRagdollBodyId == 0u || mirrorRig == null || mirrorRig.ragdollBodies == null)
            {
                return false;
            }
            for (int index = 0; index < mirrorRig.ragdollBodies.Count; index++)
            {
                RagdollBodyDefinition definition = mirrorRig.ragdollBodies[index];
                if (definition != null && definition.Id.Value == selectedRagdollBodyId)
                {
                    return TryBuildRagdollBoxVisual(definition, out box);
                }
            }
            return false;
        }

        /// <summary>The selected ragdoll body's grab handle under a viewport point, or none.</summary>
        public RagdollBoxHandle PickRagdollBoxHandle(Vector2 viewportPoint, float aspect)
        {
            RagdollBoxVisual box;
            if (mirrorRig == null || !TryGetSelectedRagdollBoxVisual(out box))
            {
                return RagdollBoxHandle.None;
            }
            Ray ray = BuildViewportRay(viewportPoint, aspect);
            return PreviewRagdollBoxHandles.Pick(ray, in box, mirrorRig.ragdollSettings.space, GizmoHandleLength);
        }

        /// <summary>Every ragdoll body currently resolved in the preview, in world space.</summary>
        private List<RagdollBoxVisual> BuildRagdollBoxVisuals(RigAsset rig)
        {
            List<RagdollBoxVisual> boxes = new List<RagdollBoxVisual>();
            if (rig == null || rig.ragdollBodies == null)
            {
                return boxes;
            }
            for (int index = 0; index < rig.ragdollBodies.Count; index++)
            {
                RagdollBodyDefinition definition = rig.ragdollBodies[index];
                RagdollBoxVisual box;
                if (definition != null && TryBuildRagdollBoxVisual(definition, out box))
                {
                    boxes.Add(box);
                }
            }
            return boxes;
        }

        private bool TryBuildRagdollBoxVisual(RagdollBodyDefinition definition, out RagdollBoxVisual box)
        {
            box = default(RagdollBoxVisual);
            Transform node = ResolveRagdollNode(definition.address);
            if (node == null)
            {
                return false;
            }

            Vector3 localCenter = new Vector3(definition.boxCenter.x, definition.boxCenter.y, definition.boxCenter.z);
            Vector3 localEuler = new Vector3(
                definition.boxEulerAngles.x, definition.boxEulerAngles.y, definition.boxEulerAngles.z);

            box = new RagdollBoxVisual
            {
                bodyId = definition.Id.Value,
                center = node.position + node.rotation * localCenter,
                rotation = node.rotation * Quaternion.Euler(localEuler),
                size = new Vector3(definition.boxSize.x, definition.boxSize.y, definition.boxSize.z)
            };
            return true;
        }

        /// <summary>Rebuilds every body's wireframe and the selected body's grab handles for this render.</summary>
        private void UpdateRagdollBoxHandles()
        {
            List<RagdollBoxVisual> boxes = BuildRagdollBoxVisuals(mirrorRig);
            RagdollSpace space = mirrorRig != null ? mirrorRig.ragdollSettings.space : RagdollSpace.Planar2D;
            ragdollBoxHandles.Rebuild(boxes, selectedRagdollBodyId, space, activeRagdollBoxHandle, GizmoHandleLength);
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

        /// <summary>Describes what a hierarchy item is, for the inspector's subtitle.</summary>
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

        // Hit-tests the previewed rig under a viewport point, nearest first. The camera is posed
        // here as well as in Render, since a click is handled outside the render loop and picking
        // against a stale camera pose would select something not under the cursor.
        /// <param name="viewportPoint">Pointer position, (0,0) bottom-left to (1,1) top-right.</param>
        /// <param name="aspect">Width over height of the rect the viewport is drawn into.</param>
        /// <param name="hits">Filled with what is under the pointer. Cleared first.</param>
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

        // How big a joint marker is, in world units — drawn and clicked from this one property so
        // the click target cannot drift from the marker the user is aiming at.
        private float BoneHandleRadius
        {
            get { return Mathf.Clamp(orbitDistance * 0.018f, 0.005f, 0.6f); }
        }

        // Finds the authoring clip behind a baked clip id. Bone tracks are authoring-only data —
        // they never reach the blob, so posing the skeleton needs the ClipAsset itself.
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

        // Rebuilds the registry against the currently bound set — call after an edit. An edit
        // changes the clip's data, so only the registry is stale; the part quads are built from the
        // rig and are left standing.
        public void Refresh()
        {
            ReleaseRegistry();
            statusMessage = string.Empty;

            if (boundRig == null)
            {
                DisposeMirrors();
                statusMessage = "Assign a rig in the toolbar's Rig field.";
                return;
            }

            RebuildMirrorIfRigChanged(boundRig);
            if (rigMirror.PartCount == 0)
            {
                statusMessage = "Rig '" + boundRig.name + "' declares no targets.";
                return;
            }
            if (boundClipSet == null)
            {
                // A rig with no set is a legitimate half-state, and a useful one: the hierarchy and
                // the rest pose are the rig's, so the viewport still shows the character standing
                // there with nothing to play.
                statusMessage = "No clip set assigned.";
                return;
            }

            RebuildRegistry(boundClipSet);
        }

        private void DisposeMirrors()
        {
            rigMirror.Dispose();
            mirrorRootAdded = false;
            socketMarkers.Dispose();
            socketRootAdded = false;
            mirrorRig = null;
        }

        /// <summary>
        /// Poses the mirror for <paramref name="clipId"/> at <paramref name="normalizedTime"/>.
        /// </summary>
        /// <returns>False when the clip is not in the registry.</returns>
        public bool SamplePose(ulong clipId, float normalizedTime)
        {
            // Undo the previous tick's billboard before this tick's pose is written. The billboard
            // is a transient overwrite layered on top of the authored pose, so it has to come off
            // before a fresh pose goes on — otherwise the node a clip does not drive is never
            // rewritten, and the next ApplyBillboards records the already-billboarded rotation as
            // if it were the authored one. One tick of that and the recorded "original" is a
            // billboarded pose, which is why restoring it appeared to do nothing at all.
            RestoreBillboardedNodes();

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

        // Poses one target from a value that is not in the registry yet — what makes an unkeyed
        // edit visible while auto-key is off. Composed the way ClipSampler.ApplyClipToPose composes
        // an Override transform track: position and rotation added to rest, scale multiplying it.
        /// <param name="targetId">The part being held.</param>
        /// <param name="localPosition">Held position offset from the rest pose.</param>
        /// <param name="rotationDegrees">Held rotation offset, in the degrees the editor authors in.</param>
        /// <param name="scale">Held scale factor against the rest scale.</param>
        public void ApplyHeldTargetPose(
            uint targetId, in float3 localPosition, in float3 rotationDegrees, in float3 scale)
        {
            if (targetId == 0u)
            {
                return;
            }

            RebuildRestPosesIfNeeded();
            TargetRestPose rest = ResolveRestPose(targetId);

            TargetPose pose;
            ClipSampler.RestToPose(in rest, out pose);
            pose.localPosition = rest.localPosition + localPosition;
            pose.rotation = rest.rotation + math.radians(rotationDegrees);
            pose.scale = rest.scale * scale;

            // No-ops for a target the mirror does not hold, which is the right answer for an id the
            // rig no longer has.
            rigMirror.ApplyPose(targetId, in pose);

            // Same reason SamplePose updates them last: a marker left on the previous pose reads as
            // the socket lagging the part it follows.
            socketMarkers.UpdateMarkers(rigMirror, skeletonMirror);
        }

        // The rest pose every part is animated from, taken from the loaded prefab (not the origin):
        // position and rotation are additive against it, scale multiplicative, matching how
        // TransformApplySystem composes at runtime. Matched by name against the prefab, since a rig
        // target's displayName is the only thing it and a prefab transform share; unmatched falls
        // back to identity. Measured relative to the prefab root, not the transform's own parent,
        // since the mirror flattens every part under one root regardless of the prefab's real nesting.
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

            if (boundRig == null)
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

            List<RigTargetDefinition> targets = boundRig.targets;
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

                // Degrees to radians because the blob's rotations are radians, and this value is
                // added to them before ApplyPose converts the sum back for the Transform.
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
            orbitYaw += pixelDelta.x * DegreesPerDragPixel;
            orbitPitch = Mathf.Clamp(
                orbitPitch + pixelDelta.y * DegreesPerDragPixel, -85f, 85f);
        }

        /// <summary>Zooms the preview camera. Positive zooms out.</summary>
        public void Zoom(float amount)
        {
            orbitDistance = Mathf.Clamp(
                orbitDistance + amount, MinimumOrbitDistance, MaximumOrbitDistance);
        }

        // Slides the camera sideways and up without turning it — the Scene view's middle-drag pan.
        // The viewport's height has to come from the caller, since a pan only tracks the pointer if
        // a pixel of drag is worth exactly the world distance a pixel spans at the focus.
        public void Pan(Vector2 pixelDelta, float viewportHeightPixels)
        {
            if (viewportHeightPixels < 1f)
            {
                return;
            }

            float halfFieldOfViewRadians = FrameFieldOfViewDegrees * 0.5f * Mathf.Deg2Rad;
            float worldUnitsPerPixel =
                2f * orbitDistance * Mathf.Tan(halfFieldOfViewRadians) / viewportHeightPixels;

            // Negated in x and not in y because the world moves with the pointer while UI Toolkit's
            // y runs down the screen: dragging right pushes the scene right, so the camera goes left.
            orbitFocus += OrbitRotation * new Vector3(
                -pixelDelta.x * worldUnitsPerPixel, pixelDelta.y * worldUnitsPerPixel, 0f);
        }

        // Turns the camera about its own position rather than about the rig — the Scene view's
        // right-drag look. This class has no camera position of its own to store — it is derived
        // from the focus — so a look holds the position still and moves the focus to match.
        public void LookAround(Vector2 pixelDelta)
        {
            Vector3 heldCameraPosition = CameraOrbitPosition;

            orbitYaw += pixelDelta.x * DegreesPerDragPixel;
            orbitPitch = Mathf.Clamp(
                orbitPitch + pixelDelta.y * DegreesPerDragPixel, -85f, 85f);

            orbitFocus = heldCameraPosition + OrbitRotation * new Vector3(0f, 0f, orbitDistance);
        }

        /// <summary>
        /// Dollies in and out by a drag rather than by the wheel — the Scene view's Alt + right-drag.
        /// Dragging right or up moves in.
        /// </summary>
        public void Dolly(Vector2 pixelDelta)
        {
            float pixelsTowardsSubject = pixelDelta.x - pixelDelta.y;
            Zoom(-pixelsTowardsSubject * orbitDistance * DollyFractionPerPixel);
        }

        /// <summary>
        /// Flies the camera through the scene along its own axes, keeping its direction — the WASD /
        /// QE half of fly mode.
        /// </summary>
        /// <param name="localDirection">
        /// Movement in camera space: +Z forward, +X right, +Y up. Length is ignored; it is
        /// normalised so a diagonal is not faster than a straight line.
        /// </param>
        // Moves the focus, which carries the camera with it; the distance between them is untouched
        // on purpose, so the orbit pivot stays the same way ahead of the camera after a flight.
        public void Fly(Vector3 localDirection, float deltaSeconds, bool fast)
        {
            if (localDirection.sqrMagnitude < Mathf.Epsilon || deltaSeconds <= 0f)
            {
                return;
            }

            float speed = FlySpeedUnitsPerSecond * (fast ? FlyFastMultiplier : 1f);
            orbitFocus += OrbitRotation * localDirection.normalized * speed * deltaSeconds;
        }

        /// <summary>Returns the camera to the pose the window opens with: head-on, framing the rig.</summary>
        public void ResetView()
        {
            orbitYaw = 0f;
            orbitPitch = 0f;
            FrameRig();
        }

        // Frames whatever is selected, or the whole rig when nothing is — the F key. No
        // MinimumFocusHeight lift here: that exists for an unposed rig laid out around the origin,
        // and applying it to a deliberate pick would aim a metre above a selected foot.
        public void FrameSelection()
        {
            Transform selectedTransform = ResolveSelectedTransform();
            if (selectedTransform == null)
            {
                FrameRig();
                return;
            }

            Bounds selectionBounds = new Bounds(selectedTransform.position, Vector3.zero);
            bool hasAny = false;
            EncapsulateRenderers(selectedTransform.gameObject, ref selectionBounds, ref hasAny);
            if (!hasAny)
            {
                // A bone or a bare grouping transform draws no geometry. Framing "nothing" would
                // slam to the minimum distance, so frame the joint marker that was clicked instead.
                float markerRadius = BoneHandleRadius * BonelessFrameRadiusMultiplier;
                selectionBounds = new Bounds(
                    selectedTransform.position,
                    new Vector3(markerRadius, markerRadius, markerRadius));
            }

            framePending = false;
            orbitFocus = selectionBounds.center;
            orbitDistance = DistanceThatFrames(
                Mathf.Max(selectionBounds.extents.magnitude, MinimumFrameRadius));
        }

        // Points the camera at the rig and backs off far enough to hold all of it. Placement and
        // framing are separate questions: the rig is built at the origin, but a character stands on
        // the floor, so this aims at the middle of what is actually there, never lower than MinimumFocusHeight.
        public void FrameRig()
        {
            framePending = false;

            Bounds rigBounds;
            if (!TryComputeRigBounds(out rigBounds))
            {
                orbitFocus = new Vector3(0f, MinimumFocusHeight, 0f);
                orbitDistance = DefaultOrbitDistance;
                return;
            }

            Vector3 framedFocus = rigBounds.center;
            framedFocus.y = Mathf.Max(framedFocus.y, MinimumFocusHeight);
            orbitFocus = framedFocus;

            // Measured from where the camera is aimed, not from the middle of the rig. Raising the
            // aim moves the rig down the frame, and a radius that still described a sphere around
            // the bounds centre would crop whatever the lift pushed past the bottom edge.
            float radius = Mathf.Max(
                rigBounds.extents.magnitude + Vector3.Distance(rigBounds.center, framedFocus),
                MinimumFrameRadius);
            orbitDistance = DistanceThatFrames(radius);
        }

        /// <summary>
        /// How far back a sphere of <paramref name="radius"/> has to be seen from to fit the frame,
        /// clamped to the range <see cref="Zoom"/> allows — framing must never put the camera
        /// somewhere the user cannot zoom back out of.
        /// </summary>
        private static float DistanceThatFrames(float radius)
        {
            float halfFieldOfViewRadians = FrameFieldOfViewDegrees * 0.5f * Mathf.Deg2Rad;
            return Mathf.Clamp(
                radius / Mathf.Tan(halfFieldOfViewRadians) * FramePadding,
                MinimumOrbitDistance,
                MaximumOrbitDistance);
        }

        // The world bounds of everything the preview draws as the rig. Both mirrors count: a rigged
        // character's targets are a handful of quads at rest, so framing those alone would zoom in
        // on nothing.
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

        // Renders the preview scene and returns the resulting texture. The texture is owned by the
        // render utility — never destroy it here. Returns null only for a degenerate size or a
        // render utility that could not be created; an empty scene still renders the grid.
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

            UpdateRagdollBoxHandles();

            ApplyCameraPose();

            // After the camera, because billboarding is defined against it; after the pose, because
            // the pose is the billboard's rest orientation. That is the runtime's order exactly
            // (TransformSampleSystem, TransformApplySystem, BillboardResolveSystem), and it has to
            // be, or the viewport would answer a different question from the game.
            ApplyBillboards();

            // After billboarding: a ragdolling body's node overwrites whatever ApplyBillboards just
            // wrote, exactly as RagdollApplySystem overwrites BillboardResolveSystem's write at runtime.
            StepRagdollPreview();

            // NEVER read-and-restore GUIUtility.hotControl around this render. Amendment A54 wrapped
            // these three lines in exactly that, as "free insurance" against the render disturbing an
            // unrelated drag, and it is what broke every button and every drag in the window instead.
            // hotControl is not an int field with an accessor: assigning it takes or RELEASES the
            // mouse capture, and UI Toolkit's own pointer capture is synced through it (which is why
            // UIElements uses SetHotControlWithoutSendingEvents internally rather than this setter).
            // This method runs on an EditorApplication.update tick 30 times a second, so restoring
            // the pre-render value — 0, whenever the gesture in flight is a UI Toolkit one — released
            // the captured pointer within ~33ms of any gesture starting. A Button's Clickable holds
            // the pointer from PointerDown to PointerUp and fires `clicked` only if it still has it,
            // so buttons stopped opening their pickers; a slider dragger lost the pointer the moment
            // it grabbed it, so drags died on the spot.
            renderUtility.BeginPreview(new Rect(0f, 0f, pixelWidth, pixelHeight), GUIStyle.none);
            renderUtility.camera.Render();
            return renderUtility.EndPreview();
        }

        // Nodes this preview has billboarded, and the local rotation (and position) each had
        // immediately before the billboard overwrote it. Turning the toggle off or deleting the
        // root only stops future writes — it does not restore the node, so this is what
        // RestoreBillboardedNodes uses to un-write a billboard. Local rather than world rotation, so
        // restoring is order-independent (a parent restored after its child would drag it off its restored pose).
        private readonly List<Transform> billboardedNodes = new List<Transform>();
        private readonly List<Quaternion> billboardedNodeLocalRotations = new List<Quaternion>();
        private readonly List<Vector3> billboardedNodeLocalPositions = new List<Vector3>();

        /// <summary>Puts every billboarded node back and forgets them all.</summary>
        private void RestoreBillboardedNodes()
        {
            for (int index = 0; index < billboardedNodes.Count; index++)
            {
                Transform node = billboardedNodes[index];
                if (node != null)
                {
                    node.localRotation = billboardedNodeLocalRotations[index];
                    node.localPosition = billboardedNodeLocalPositions[index];
                }
            }
            billboardedNodes.Clear();
            billboardedNodeLocalRotations.Clear();
            billboardedNodeLocalPositions.Clear();
        }

        /// <summary>
        /// Restores and forgets any recorded node that is not among <paramref name="resolvedRoots"/>
        /// — the case where a billboard root was deleted or re-addressed while the toggle stayed on.
        /// </summary>
        private void RetireBillboardedNodesNotIn(List<ResolvedBillboardRoot> resolvedRoots)
        {
            for (int index = billboardedNodes.Count - 1; index >= 0; index--)
            {
                Transform recordedNode = billboardedNodes[index];
                bool stillBillboarded = false;
                for (int rootIndex = 0; rootIndex < resolvedRoots.Count; rootIndex++)
                {
                    if (resolvedRoots[rootIndex].node == recordedNode)
                    {
                        stillBillboarded = true;
                        break;
                    }
                }
                if (stillBillboarded)
                {
                    continue;
                }
                if (recordedNode != null)
                {
                    recordedNode.localRotation = billboardedNodeLocalRotations[index];
                    recordedNode.localPosition = billboardedNodeLocalPositions[index];
                }
                billboardedNodes.RemoveAt(index);
                billboardedNodeLocalRotations.RemoveAt(index);
                billboardedNodeLocalPositions.RemoveAt(index);
            }
        }

        // Records a node's pre-billboard local rotation, once. Deliberately does not refresh an
        // existing record: a node the current clip does not key is never rewritten by SamplePose, so
        // re-recording on a later tick would store the already-billboarded rotation as the "restore" value.
        private void RecordBillboardedNode(Transform node)
        {
            for (int index = 0; index < billboardedNodes.Count; index++)
            {
                if (billboardedNodes[index] == node)
                {
                    return;
                }
            }
            billboardedNodes.Add(node);
            billboardedNodeLocalRotations.Add(node.localRotation);
            billboardedNodeLocalPositions.Add(node.localPosition);
        }

        // Turns the preview's billboard roots to face the preview camera. Every number comes from
        // BillboardMath, none re-derived: the viewport feeds it this camera and these transforms
        // instead of the game's, so the two cannot disagree about facing, snapping, clamping or
        // blending. Written shallowest first, in world rotations, so a nested root reading its own
        // world rotation after its ancestor was written already sees the ancestor's billboard.
        private void ApplyBillboards()
        {
            Transform previewRoot = HierarchyRoot;

            if (!BillboardPreviewEnabled || mirrorRig == null || renderUtility == null
                || previewRoot == null)
            {
                RestoreBillboardedNodes();
                return;
            }

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(mirrorRig, previewRoot, null);
            if (resolvedRoots.Count == 0)
            {
                RestoreBillboardedNodes();
                return;
            }

            // Everything billboarded last tick that is not billboarded this tick goes back to its
            // authored rotation before anything new is written — see RestoreBillboardedNodes.
            RetireBillboardedNodesNotIn(resolvedRoots);

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

                // Recorded before the write, every tick: the value being preserved is the freshly
                // sampled authored rotation, which moves as the playhead does, so a stale first-tick
                // capture would restore the wrong pose after a scrub.
                RecordBillboardedNode(node);

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

        // The runtime parameter block for one authored root, with the selected clip's keyed
        // channels folded in at the playhead. Mirrors ActorBaker.BuildBillboardSettings and
        // BillboardResolveSystem.ApplyKeyedChannels together, since the preview has neither a bake
        // nor playback layers to go through.
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

        // Advances the Ragdoll toggle's simulation by real elapsed time. Ticked from here rather
        // than a per-frame hook on the window, since ApplyBillboards just ran and the ragdoll's
        // gravity frame is still cheap to read straight off the transform it was written onto.
        private void StepRagdollPreview()
        {
            if (!ragdollPreviewEnabled || !ragdollSimulation.IsBuilt || mirrorRig == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float realDeltaTime = lastRagdollTickTime > 0d ? (float)(now - lastRagdollTickTime) : 0f;
            lastRagdollTickTime = now;

            quaternion frameRotation = ResolveRagdollFrameRotation(mirrorRig);
            ragdollSimulation.Step(
                mirrorRig, in frameRotation, RagdollPreviewScenery.instance.Props, realDeltaTime);
        }

        // This step's gravity frame for RagdollSpace.Planar2D — identity for Spatial3D or when the
        // ragdoll's root body inherits no billboard root. Reads a transform ApplyBillboards just
        // wrote rather than resolving billboarding a second time, the same cache-read shape the
        // runtime's SolveRagdollJob uses against BillboardResolveSystem's earlier write.
        private quaternion ResolveRagdollFrameRotation(RigAsset rig)
        {
            if (rig.ragdollSettings.space != RagdollSpace.Planar2D)
            {
                return quaternion.identity;
            }

            Transform skeletonRoot = HierarchyRoot;
            Transform rootBodyNode = ragdollSimulation.RootNode;
            if (skeletonRoot == null || rootBodyNode == null)
            {
                return quaternion.identity;
            }

            List<ResolvedBillboardRoot> resolvedRoots = BillboardRootResolver.Resolve(rig, skeletonRoot, null);
            int rootIndex = BillboardRootResolver.FindNearestRootIndex(resolvedRoots, rootBodyNode, skeletonRoot);
            return rootIndex < 0 ? quaternion.identity : resolvedRoots[rootIndex].node.rotation;
        }

        /// <summary>
        /// The camera's orientation for the current yaw and pitch. The one place the orbit angles
        /// become a rotation, so posing, panning, looking and flying cannot disagree about which way
        /// the camera is pointing.
        /// </summary>
        private Quaternion OrbitRotation
        {
            get { return Quaternion.Euler(orbitPitch, orbitYaw, 0f); }
        }

        /// <summary>Where the orbit rig puts the camera — the focus, backed off along the view.</summary>
        private Vector3 CameraOrbitPosition
        {
            get { return orbitFocus + OrbitRotation * new Vector3(0f, 0f, -orbitDistance); }
        }

        // Poses the camera on its orbit around orbitFocus, a field rather than the origin: the rig
        // is placed at the origin but a character stands on the floor, so its mass sits above y = 0.
        private void ApplyCameraPose()
        {
            renderUtility.camera.transform.position = CameraOrbitPosition;
            renderUtility.camera.transform.rotation = OrbitRotation;
        }

        // Adds whatever exists but has not yet joined the preview scene. Each root is tracked by
        // its own flag rather than one "scene is populated" flag, since the mirrors rebuild
        // whenever the set or rig changes and a shared flag would leave every rebuilt root outside the scene.
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

            ragdollBoxHandles.EnsureBuilt();
            if (ragdollBoxHandles.HandlesObject != null && !ragdollBoxHandlesAdded)
            {
                renderUtility.AddSingleGO(ragdollBoxHandles.HandlesObject);
                ragdollBoxHandlesAdded = true;
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

        // The preview transform the current selection points at, or null. Shared by the outline and
        // by FrameSelection, so the F key always frames the thing that is outlined.
        private Transform ResolveSelectedTransform()
        {
            if (selectedSocketId != 0u)
            {
                return socketMarkers.GetMarker(selectedSocketId);
            }
            if (selectedTargetId != 0u)
            {
                return rigMirror.GetPartTransform(selectedTargetId);
            }
            return skeletonMirror.GetTransformByIndex(selectedHierarchyIndex);
        }

        // Outlines the selected transform, or hides it when nothing resolves. Resolved every frame
        // rather than cached, since the outline has to follow the posed skeleton as the playhead scrubs.
        private void UpdateSelectionMarker()
        {
            Transform selectedTransform = ResolveSelectedTransform();
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

        // The object's own bounds in its local space, so the outline can be an oriented box.
        // Renderer.bounds is deliberately not used: it is world-axis-aligned, so an outline built
        // from it would swell and swing as the rig turns.
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
            ragdollSimulation.Dispose();
            ragdollPreviewEnabled = false;

            // Before Cleanup: these live in the render utility's scene, and cleaning that up first
            // would leave the references pointing at objects Unity has already destroyed.
            sceneGizmos.Dispose();
            boneHandles.Dispose();
            transformGizmo.Dispose();
            ragdollBoxHandles.Dispose();

            mirrorRootAdded = false;
            skeletonRootAdded = false;
            gizmosAdded = false;
            boneHandlesAdded = false;
            transformGizmoAdded = false;
            ragdollBoxHandlesAdded = false;
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
