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

        /// <summary>
        /// How high above the floor the camera aims, at the least, in world units.
        /// </summary>
        /// <remarks>
        /// Aiming at the rig's centre is right when the rig has one; aiming at the origin, which is
        /// what happens when the parts are laid out around it, points the camera at the floor and
        /// hands the bottom half of the frame to empty ground. A unit up is roughly chest height on
        /// a two-unit character, so the space above the floor — the space anything is animated in —
        /// is the space the viewport shows.
        /// </remarks>
        private const float MinimumFocusHeight = 1f;

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
        /// The Ragdoll toolbar toggle's own simulation (Phase D6, spec §8.4, §8.5). Not the box
        /// handles' authoring gizmo (<see cref="PreviewRagdollBoxHandles"/>) — this is the physics.
        /// </summary>
        private readonly RagdollPreviewSimulation ragdollSimulation = new RagdollPreviewSimulation();
        private bool ragdollPreviewEnabled;

        /// <summary>The viewport's ragdoll box wireframes and the selected body's grab handles (Phase D6, spec §8.3).</summary>
        private readonly PreviewRagdollBoxHandles ragdollBoxHandles = new PreviewRagdollBoxHandles();
        private bool ragdollBoxHandlesAdded;
        private uint selectedRagdollBodyId;
        private RagdollBoxHandle activeRagdollBoxHandle;

        /// <summary>
        /// When the ragdoll last stepped, so <see cref="Render"/> can advance it by real elapsed
        /// time rather than a fixed guess — the "editor delta time is jittery" problem spec §8.5
        /// names, solved by measuring it directly rather than trusting a caller's estimate.
        /// </summary>
        private double lastRagdollTickTime;

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
        /// The rig the bound set is being previewed on. Supplied by the window rather than read off
        /// the set, because a set names no rig — the pairing is the window's, and at run time an
        /// actor's.
        /// </summary>
        private RigAsset boundRig;

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

        /// <summary>Whether the ragdoll toggle is currently dropping the previewed rig (spec §8.4).</summary>
        public bool RagdollPreviewEnabled
        {
            get { return ragdollPreviewEnabled; }
        }

        /// <summary>Whether the active ragdoll has settled — nothing to show once every body sleeps.</summary>
        public bool RagdollPreviewSleeping
        {
            get { return ragdollSimulation.Sleeping; }
        }

        /// <summary>
        /// Off → On (spec §8.4): captures whatever pose is currently on screen, builds the body
        /// array against it, and starts stepping. Refuses — leaving the toggle unchanged — when the
        /// rig has no ragdoll bodies or none of them resolve in this preview, reporting why.
        /// </summary>
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

        /// <summary>
        /// On → Off (spec §8.4): restores every simulated node's pre-drop pose, then discards the
        /// simulation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The restore is explicit, and an earlier version's assumption that it need not be
        /// was wrong.</strong> That version reasoned that the playhead never moves while ragdolling,
        /// so the caller's next <see cref="SamplePose"/> at the same unchanged time would reproduce
        /// the pre-drop pose on its own. That holds only for nodes the current clip actually drives.
        /// A bone with no bone track in this clip, or a part that has never been keyed, is not
        /// touched by a resample at all — it simply stays wherever the ragdoll dropped it, and the
        /// toggle visibly fails to put the character back.
        /// </para>
        /// <para>
        /// Restoring first and disposing second, because <see cref="RagdollPreviewSimulation.Dispose"/>
        /// drops the captured poses along with everything else.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Overwrites the status line, for feedback that belongs to one moment rather than to the
        /// preview's ongoing state (spec §8.4: "the toggle refuses to engage, and the status line
        /// says why"). Persists until the next <see cref="Refresh"/> or <see cref="SamplePose"/>
        /// call has its own, more current thing to say — the same lifetime every other reason this
        /// field is set already has.
        /// </summary>
        public void ReportTransientStatus(string message)
        {
            statusMessage = message ?? string.Empty;
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
        /// Validation failures are caught rather than propagated. <c>ClipRegistryBuilder.Build</c>
        /// throws on any error-severity rule, and an authoring window that dies on an invalid clip
        /// is useless precisely when it is most needed — while the clip is being fixed. What it
        /// does <em>not</em> do is report them: see <see cref="RebuildRegistry"/>.
        /// </remarks>
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

            // A rig swap invalidates every node the simulation is holding onto (Phase D6): the old
            // mirror is about to be disposed out from under it.
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

        /// <summary>
        /// Builds the preview registry, or records why it could not be built.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A validation failure is named here, not listed here.</strong>
        /// <see cref="ClipValidationException.Message"/> is every offending rule on its own line,
        /// and <see cref="StatusMessage"/> ends up in a wrapping label directly above the 3D
        /// preview — so putting it there turned each finding into two or three lines of pane the
        /// rig no longer had, at exactly the moment the rig was what you were looking at. It also
        /// made this the window's second renderer of one rule set, disagreeing in wording and order
        /// with <c>ValidationBadgeElement</c>, which is the one that can be switched off and the one
        /// whose findings are clickable. One sentence pointing at it is the whole job here.
        /// </para>
        /// <para>
        /// Anything else thrown still reports in full. An unexpected build failure has no other
        /// surface in this window, and a message nobody planned for is worth more than a category.
        /// </para>
        /// </remarks>
        private void RebuildRegistry(ClipSetAsset clipSet)
        {
            try
            {
                Unity.Entities.Hash128 contentHash;
                // The preview binds the one set the window has open to the rig the window is
                // showing — the same shape an actor's bind has, with a list of one (Phase F §5).
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

            // The skeleton instance every Bone-kind and HierarchyPath-kind ragdoll body resolves
            // against (Phase D6) is about to be destroyed and rebuilt.
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

        /// <summary>
        /// Whether a hierarchy index names an imported skinned-mesh bone, as opposed to an authored
        /// guiding transform (Phase D5, spec §2). A ragdoll body addresses the two differently: a
        /// bone's path below the prefab root is not stable the way a bare transform's is, so it is
        /// addressed by name instead — <see cref="RigNodeAddressKind.Bone"/>, generalised from
        /// <c>SocketDefinition.boneName</c>'s precedent.
        /// </summary>
        public bool IsSkinnedBone(int hierarchyIndex)
        {
            Transform node = skeletonMirror.GetTransformByIndex(hierarchyIndex);
            return node != null && IsSkinnedBone(node);
        }

        /// <summary>
        /// A hierarchy node's own renderer bounds, local to its transform, or false when it carries
        /// no renderer to measure.
        /// </summary>
        /// <remarks>
        /// What a freshly added Ragdoll component sizes its box from (Phase D5, spec §8.1): a node
        /// with geometry gets a box that hugs it, and a bare grouping transform keeps the
        /// <c>RagdollBodyDefinition</c> field initializer's unit-box default instead. Built on
        /// <see cref="TryGetLocalBounds"/>, the same local-space bounds the selection outline already
        /// measures, for the same reason that one avoids <c>Renderer.bounds</c>: a world-axis-aligned
        /// box would swell and swing as the rig turns rather than hugging the node that owns it.
        /// </remarks>
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

        /// <summary>
        /// Resolves a <see cref="RigNodeAddress"/> to the preview transform it names — the reverse
        /// of what D5's <c>ClipEditorWindow.BuildRagdollAddressFor</c> already does (node → address).
        /// Shared by <c>RagdollPreviewSimulation</c> and <c>PreviewRagdollBoxHandles</c> (Phase D6,
        /// spec §8.3, §8.5) so neither invents its own address→node lookup that could disagree with
        /// the other's.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Two mirrors, three address kinds, two live trees.</strong>
        /// <see cref="RigNodeAddressKind.RigTarget"/> resolves against <see cref="rigMirror"/> by a
        /// plain id lookup — the mirror's quads are flat, so this is the only address kind that
        /// never needs a hierarchy. <see cref="RigNodeAddressKind.Bone"/> resolves against
        /// <see cref="skeletonMirror"/> by name. <see cref="RigNodeAddressKind.HierarchyPath"/>
        /// names a bare grouping transform of the <em>real</em> authoring prefab — not a row this
        /// package owns — and the only preview surface that is a literal instantiation of that
        /// prefab, carrying its real nested structure, is <see cref="skeletonMirror"/>'s instance
        /// (<see cref="HierarchyRoot"/>, exactly as <see cref="ApplyBillboards"/> already assumes for
        /// the same address kind). With no skinned source assigned there is no such tree to search,
        /// so a <see cref="RigNodeAddressKind.HierarchyPath"/> body on a pure-cutout rig resolves to
        /// nothing here — the same pre-existing gap a <see cref="RigNodeAddressKind.HierarchyPath"/>
        /// billboard root already has on a pure-cutout preview, not a new one this method introduces.
        /// </para>
        /// </remarks>
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
        /// <summary>Re-places the socket markers without rebuilding them.</summary>
        /// <remarks>
        /// What an offset edit actually needs. <see cref="RebuildSockets"/> destroys and recreates
        /// every marker object and re-fetches their material, which is a heavy thing to do on each
        /// mouse move of a drag and none of which an offset change invalidates — a marker is placed
        /// from the socket's numbers every time it is updated, so moving it is the whole job.
        /// </remarks>
        public void RefreshSocketPlacement()
        {
            socketMarkers.UpdateMarkers(rigMirror, skeletonMirror);
        }

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

        /// <summary>The preview camera's current forward direction — the free-drag plane a ragdoll box's centre handle moves within (spec §8.3).</summary>
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

        /// <summary>
        /// Points the ragdoll box handles at one body, or none (spec §8.3). Separate from
        /// <see cref="SetSelectedSocketId"/>/<see cref="SetSelectedTargetId"/> rather than a fourth
        /// branch of the same field: a Ragdoll component selection does not move the ordinary
        /// selection outline (a body's node may itself be the outlined part), so the two must be
        /// able to disagree.
        /// </summary>
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

        /// <summary>
        /// Poses one target from a value that is not in the registry yet.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>This is what makes an unkeyed edit visible.</strong> The registry is built from
        /// committed keys, so with auto-key off there is nothing in it to sample: dragging a number
        /// field moved the numbers and left the character standing still, which reads as the field
        /// being broken rather than as the edit being held.
        /// </para>
        /// <para>
        /// Layered on top of <see cref="SamplePose"/> rather than folded into it, and composed the
        /// way <c>ClipSampler.ApplyClipToPose</c> composes an Override transform track — position
        /// and rotation added to the rest pose, scale multiplying it (section 5.11) — so the held
        /// value lands exactly where the same value would once it is keyed.
        /// </para>
        /// </remarks>
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
        /// actually there, and never lower than <see cref="MinimumFocusHeight"/> — a paper-doll rig
        /// whose parts are laid out around the origin has its middle on the floor, which is the
        /// same low aim by another route.
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

            UpdateRagdollBoxHandles();

            ApplyCameraPose();

            // After the camera, because billboarding is defined against it; after the pose, because
            // the pose is the billboard's rest orientation. That is the runtime's order exactly
            // (TransformSampleSystem, TransformApplySystem, BillboardResolveSystem), and it has to
            // be, or the viewport would answer a different question from the game.
            ApplyBillboards();

            // After billboarding, matching AnimationToolkitRagdollSystemGroup's own
            // [UpdateAfter(BillboardResolveSystem)] edge (spec §7): a ragdolling body's node
            // overwrites whatever ApplyBillboards just wrote it, exactly as RagdollApplySystem
            // overwrites BillboardResolveSystem's write at runtime (§9 G1's own shape).
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
        /// <summary>
        /// Nodes this preview has billboarded, and the local rotation each had immediately before
        /// the billboard overwrote it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A preview that writes a pose owes a way to un-write it.</strong>
        /// <see cref="ApplyBillboards"/> assigns <c>node.rotation</c> outright. Turning the toggle
        /// off, or deleting the billboard root, merely stops that assignment happening — it does not
        /// put the node back, so the node keeps the last rotation the billboard gave it and the
        /// authored pose is unreachable until the scene is rebuilt. Only nodes the current clip
        /// actually keys are rescued by the next resample, which is why this was visible on some
        /// nodes and not others.
        /// </para>
        /// <para>
        /// Local rather than world rotation, so restoring is order-independent: a parent restored
        /// after its child would otherwise drag the child back off its restored world pose.
        /// </para>
        /// </remarks>
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

        /// <summary>Records a node's pre-billboard local rotation, once.</summary>
        /// <remarks>
        /// <strong>Deliberately does not refresh an existing record.</strong> Re-recording on every
        /// render tick looks harmless and is not: a node the current clip does not key is never
        /// rewritten by <see cref="SamplePose"/>, so on the second tick its local rotation is already
        /// the billboarded one, and refreshing would store that as the value to "restore" to. The
        /// record is invalidated by <see cref="SamplePose"/> instead, which is the only thing that
        /// legitimately changes the authored pose underneath it.
        /// </remarks>
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

        /// <summary>
        /// Advances the Ragdoll toggle's simulation by real elapsed time (spec §8.5).
        /// </summary>
        /// <remarks>
        /// Ticked from here rather than from <c>ClipEditorWindow</c>'s own per-frame hook, because
        /// this is where <see cref="ApplyBillboards"/> just ran and where the ragdoll's own gravity
        /// frame — the billboard root's freshly-written world rotation — is still cheap to read
        /// straight off the transform it was written onto (see <see cref="ResolveRagdollFrameRotation"/>).
        /// </remarks>
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

        /// <summary>
        /// This step's gravity frame for <see cref="RagdollSpace.Planar2D"/> (spec §6.2) — identity
        /// for <see cref="RagdollSpace.Spatial3D"/> or when the ragdoll's root body inherits no
        /// billboard root.
        /// </summary>
        /// <remarks>
        /// <strong>Reads a transform <see cref="ApplyBillboards"/> just wrote; does not resolve
        /// billboarding a second time.</strong> The runtime's own <c>SolveRagdollJob</c> calls
        /// <c>BillboardQuery.TryGetFrame</c> against the baked <c>BillboardRootElement</c> buffer
        /// <c>BillboardResolveSystem</c> filled earlier the same frame — a cache read, not a second
        /// resolve. This is the preview's equivalent: <see cref="ApplyBillboards"/> already ran this
        /// call and already wrote the nearest billboard root's resolved world rotation onto its
        /// transform, so reading that transform's current <c>rotation</c> is the cache read.
        /// </remarks>
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
