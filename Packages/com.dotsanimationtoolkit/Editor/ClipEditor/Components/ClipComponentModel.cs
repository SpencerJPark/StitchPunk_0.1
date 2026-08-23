// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// What components an object has, what it could have, and what adding or removing one does to
    /// the asset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Pure over the assets, with no window state and no undo.</strong> The caller records
    /// undo on the right object — the clip for a clip-scoped component, the rig for a rig-scoped one
    /// — and marks it dirty; this decides only what changes. That split is what lets the rules be
    /// tested without a window, and it is where the rules belong: "keying this node writes a bone
    /// track, not a transform track" is a fact about the data model, not about a panel.
    /// </para>
    /// <para>
    /// <strong>Presence is derived, never stored.</strong> An object has a Flipbook component
    /// exactly when a sprite track is bound to it. There is no second list to keep in step, so
    /// a clip hand-edited outside this window — or authored before the stack existed — reads back
    /// with precisely the components its tracks describe.
    /// </para>
    /// <para>
    /// <strong>Transform is the one component nothing has to derive.</strong> Every object has one,
    /// so it is reported present on every object whether or not a track exists yet, and the track is
    /// minted by the first key. Which of the two transform kinds an object gets is decided by
    /// <see cref="TransformKindFor"/> and is not a choice the user makes.
    /// </para>
    /// </remarks>
    public static class ClipComponentModel
    {
        /// <summary>Kinds in the order the inspector stacks them, which is the order added here.</summary>
        private static readonly ClipComponentKind[] stackOrder =
        {
            ClipComponentKind.Transform,
            ClipComponentKind.BoneTransform,
            ClipComponentKind.Flipbook,
            ClipComponentKind.Billboard,
            ClipComponentKind.Socket,
            ClipComponentKind.Ragdoll
        };

        /// <summary>The add-ons, in stack order — everything the Add Component menu offers.</summary>
        private static readonly ClipComponentKind[] addableKinds =
        {
            ClipComponentKind.Flipbook,
            ClipComponentKind.Billboard,
            ClipComponentKind.Socket,
            ClipComponentKind.Ragdoll
        };

        private static readonly List<ClipComponentInstance> presenceScratch =
            new List<ClipComponentInstance>();

        /// <summary>Every kind, in stack order, intrinsic ones included.</summary>
        public static IReadOnlyList<ClipComponentKind> AllKinds
        {
            get { return stackOrder; }
        }

        /// <summary>
        /// The kinds Add Component offers, which is every kind that is not intrinsic.
        /// </summary>
        /// <remarks>
        /// The transform kinds are absent because they are on every object already. Offering "add
        /// Transform" would be offering to add a thing that is never missing, and the one time it
        /// looked missing — an object with no track yet — is exactly the case the stack now shows
        /// unkeyed rather than absent.
        /// </remarks>
        public static IReadOnlyList<ClipComponentKind> AddableKinds
        {
            get { return addableKinds; }
        }

        public static string DisplayName(ClipComponentKind kind)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform: return "Transform";
                case ClipComponentKind.BoneTransform: return "Bone Transform";
                case ClipComponentKind.Flipbook: return "Flipbook";
                case ClipComponentKind.Billboard: return "Billboard";
                case ClipComponentKind.Ragdoll: return "Ragdoll";
                default: return "Socket";
            }
        }

        /// <summary>
        /// What a kind does, in the words the picker shows when a row is hovered.
        /// </summary>
        /// <remarks>
        /// Held here rather than in the panel for the same reason the rules are: what a Flipbook is
        /// does not change with which window is asking. Written as one sentence about the thing
        /// itself, not about the button — a description that starts "click to…" is describing the
        /// menu rather than the component.
        /// </remarks>
        public static string Describe(ClipComponentKind kind)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform:
                    return "Where this part is, turned and scaled, at each key. Every object has "
                        + "one; keying it here writes the part's transform track.";

                case ClipComponentKind.BoneTransform:
                    return "Where this node sits in its parent's space at each key. Every object "
                        + "has one; on a skinned bone it moves the mesh.";

                case ClipComponentKind.Flipbook:
                    return "Swaps which sprite frame this part shows, keyed as a frame index. A "
                        + "part can carry several — a mouth and a pair of eyes drawn from the same "
                        + "texture array, each with its own base frame.";

                case ClipComponentKind.Billboard:
                    return "Turns this node to face the viewer, and its children with it. Stored on "
                        + "the rig, so it holds in every clip; the keys here animate how much.";

                case ClipComponentKind.Ragdoll:
                    return "A box collider this node falls with once the rig's ragdoll drops it — "
                        + "sized and placed in the viewport, and welded to whatever body is nearest "
                        + "above it. Works on a guiding part or a skinned bone alike, and holds for "
                        + "every clip since it is stored on the rig.";

                default:
                    return "An attachment point that follows this object — a hand's grip, a muzzle, "
                        + "a hat's peg. Stored on the rig, so anything spawned onto it finds it in "
                        + "every clip.";
            }
        }

        /// <summary>
        /// Which asset a kind is stored on, and therefore how far an edit to it reaches.
        /// </summary>
        /// <remarks>
        /// Billboard counts as rig-scoped because the component <em>is</em> the billboard root, which
        /// is rig structure: a node carrying it turns to face the viewer in every clip, whether or
        /// not this clip animates how much. Its keys are clip data hanging off that, the same way a
        /// socket's offset is rig data every clip shares. Ragdoll is rig-scoped for the same reason
        /// as Socket rather than Billboard's: its fields are not animatable at all (spec §3.3 has no
        /// key data, only authored tuning), so there is no clip-side half to distinguish from the rig
        /// structure — moving a box here moves it, full stop, in every clip that previews this rig.
        /// </remarks>
        public static ClipComponentScope Scope(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Socket || kind == ClipComponentKind.Billboard
                || kind == ClipComponentKind.Ragdoll
                ? ClipComponentScope.Rig
                : ClipComponentScope.Clip;
        }

        /// <summary>
        /// Whether a kind is on every object it applies to, rather than being added to it.
        /// </summary>
        /// <remarks>
        /// Only the transform kinds are. An intrinsic component is never offered by Add Component
        /// and never carries a remove button, and it is reported present before its track exists —
        /// the three things that together make "every object has a transform" true in the panel and
        /// not merely in the documentation.
        /// </remarks>
        public static bool IsIntrinsic(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Transform || kind == ClipComponentKind.BoneTransform;
        }

        /// <summary>
        /// Which of the two transform kinds an object carries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Decided by whether the rig declares a part for the object, not by which pane its row came
        /// from. A part is posed through a transform track bound to its id, and everything else
        /// through a bone track bound to its name — that is how the bake reads them, so it is how
        /// the panel has to write them.
        /// </para>
        /// <para>
        /// It follows that promoting a node to a part changes which track its poses live on, which
        /// is why <see cref="Add"/> migrates the keys rather than leaving them on a track nothing
        /// samples any more.
        /// </para>
        /// </remarks>
        public static ClipComponentKind TransformKindFor(ClipObjectRef objectRef)
        {
            return objectRef.HasRigTarget
                ? ClipComponentKind.Transform
                : ClipComponentKind.BoneTransform;
        }

        /// <summary>
        /// Whether a kind binds by rig-target id, and so needs the object to be a declared part.
        /// </summary>
        /// <remarks>
        /// A sprite track has one binding field and it is a target id, so a node the rig declares
        /// nothing for has nothing for it to point at. That used to be shown as "Flipbook is
        /// unavailable here", which is a true statement about the data model and a useless one to
        /// the person looking at a plane they want to animate. Adding the component now mints the
        /// part instead — see <see cref="PromoteToRigTarget"/>.
        /// </remarks>
        public static bool RequiresRigTarget(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Transform || kind == ClipComponentKind.Flipbook;
        }

        /// <summary>
        /// Whether an object can carry more than one of a kind.
        /// </summary>
        /// <remarks>
        /// Flipbook and Socket can. A part carries one flipbook track per independent feature set —
        /// a mouth based at 0 and eyes based at 32 driving the same part — and an object can hang
        /// several attachment points off itself. The rest are one to an object: two transform tracks
        /// on one part is a validation error whichever wins the bake.
        /// </remarks>
        public static bool AllowsMultiple(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Flipbook || kind == ClipComponentKind.Socket;
        }

        /// <summary>
        /// Whether a kind is offerable on an object at all, and why not when it is not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Every add-on applies to every object now.</strong> Flipbook used to be refused on
        /// anything but a declared part, which made "add a flipbook to this plane" impossible for
        /// the ordinary reason that the plane was not a part yet — a distinction the data model
        /// cares about and the person animating does not. Adding one promotes the node instead.
        /// </para>
        /// <para>
        /// <strong>Billboard applies everywhere too, and deliberately so.</strong> Put it on an
        /// object and that object turns to face the viewer; everything beneath it comes along,
        /// because everything beneath it is a transform child and rides on the parent it is already
        /// riding on. There is nothing further to decide, so there is nothing further to refuse —
        /// including on the prefab root, where the empty path is the address rather than a missing
        /// one and the whole actor turning is exactly what somebody meant by it.
        /// </para>
        /// <para>
        /// <strong>Which is a statement about kinds, not about right now.</strong> An add-on can
        /// still be unavailable because of the state around it — no clip open, no rig to store it
        /// on — and <see cref="CanAdd"/> is where those are answered. Keeping them apart is what
        /// stops a passing circumstance being recorded as a permanent fact about the component.
        /// </para>
        /// <para>
        /// The transform kinds are the only ones that can answer no, and they answer it about each
        /// other: an object has exactly one of them, chosen by
        /// <see cref="TransformKindFor"/>. The reason is still returned rather than the kind simply
        /// being hidden, because a stack that silently omits a component reads as a bug.
        /// </para>
        /// </remarks>
        public static bool AppliesTo(
            ClipComponentKind kind, ClipObjectRef objectRef, out string unavailableReason)
        {
            unavailableReason = string.Empty;
            if (!IsIntrinsic(kind))
            {
                return true;
            }

            ClipComponentKind transformKind = TransformKindFor(objectRef);
            if (kind == transformKind)
            {
                return true;
            }

            unavailableReason = kind == ClipComponentKind.Transform
                ? "The rig declares no part for this node, so its poses are keyed on a bone track. "
                    + "Adding a Flipbook makes it a part."
                : "This object is a part the rig declares, so its poses are keyed on its transform "
                    + "track rather than by bone name.";
            return false;
        }

        /// <summary>
        /// The components this object has, in stack order, its transform always first.
        /// </summary>
        /// <remarks>
        /// Cleared and refilled rather than returning a new list: the inspector rebuilds this on
        /// every selection change and every edit, and a panel is not a place to allocate per frame.
        /// </remarks>
        public static void CollectInstances(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef,
            List<ClipComponentInstance> instances)
        {
            if (instances == null)
            {
                return;
            }
            instances.Clear();
            if (!objectRef.IsValid)
            {
                return;
            }

            ClipComponentKind primaryTransform = TransformKindFor(objectRef);

            for (int orderIndex = 0; orderIndex < stackOrder.Length; orderIndex++)
            {
                ClipComponentKind kind = stackOrder[orderIndex];
                int countBefore = instances.Count;
                CollectInstancesOfKindInto(clip, rig, objectRef, kind, instances);

                if (!IsIntrinsic(kind))
                {
                    continue;
                }

                // The object's own transform kind is present whether or not its track is. Nothing
                // found means "not keyed yet", which is a state the object is genuinely in — not a
                // reason to leave a transform out of the stack.
                //
                // The other transform kind is shown only when a track for it actually exists, which
                // happens to a node promoted to a part while it was already posed as a bone and the
                // part it adopted was already keyed. Those keys cannot be carried across safely, so
                // the stack shows the track rather than leaving something that animates the object
                // with no way to see it.
                if (kind == primaryTransform && instances.Count == countBefore)
                {
                    instances.Add(
                        new ClipComponentInstance(kind, ClipComponentInstance.NoTrackIndex));
                }
            }
        }

        /// <summary>
        /// Whether a component is the object's own transform, as opposed to one left behind.
        /// </summary>
        /// <remarks>
        /// The panel asks this to decide whether to draw a remove button: an object's transform has
        /// none, because there is no state in which it has no transform, while a bone track stranded
        /// on a promoted node is a leftover and removing it is the whole point of showing it.
        /// </remarks>
        public static bool IsPrimaryTransform(ClipComponentKind kind, ClipObjectRef objectRef)
        {
            return IsIntrinsic(kind) && kind == TransformKindFor(objectRef);
        }

        /// <summary>Whether the object already carries at least one of a kind.</summary>
        /// <remarks>
        /// Answered through the shared scratch list rather than a fresh one. This runs once per
        /// kind per Add Component menu build, and the editor is single-threaded, so the list is
        /// reused for the same reason <see cref="CollectInstances"/> takes one from its caller.
        /// </remarks>
        public static bool HasAny(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind)
        {
            presenceScratch.Clear();
            CollectInstancesOfKindInto(clip, rig, objectRef, kind, presenceScratch);
            return presenceScratch.Count > 0;
        }

        /// <summary>
        /// Whether Add Component should offer a kind, given what the object already has.
        /// </summary>
        /// <remarks>
        /// Everything refused here is refused by circumstance rather than by kind — no clip
        /// selected, no rig to store it on, one of this kind already there — which is why these
        /// live here and not in <see cref="AppliesTo"/>. Each one is a sentence the picker can show
        /// on the row's hover card, and each one names something the author can go and change.
        /// </remarks>
        public static bool CanAdd(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            out string unavailableReason)
        {
            if (!AppliesTo(kind, objectRef, out unavailableReason))
            {
                return false;
            }
            if (Scope(kind) == ClipComponentScope.Clip && clip == null)
            {
                unavailableReason = "Select a clip first — this component is stored on the clip.";
                return false;
            }
            if (Scope(kind) == ClipComponentScope.Rig && rig == null)
            {
                unavailableReason = "This clip set's Rig field is empty, and this component is "
                    + "stored on the rig. Assign a RigAsset there in the Inspector — the toolbar's "
                    + "Bone Source field is a different thing, the rigged prefab for bone tracks.";
                return false;
            }

            // A part-bound component on an unclaimed node mints the part, and the part is a row on
            // the rig. So the rig has to be there even for a kind the clip stores.
            if (RequiresRigTarget(kind) && !objectRef.HasRigTarget && rig == null)
            {
                unavailableReason = "This clip set's Rig field is empty, so there is nothing to "
                    + "declare this node a part on. Assign a RigAsset there in the Inspector.";
                return false;
            }
            if (RequiresRigTarget(kind) && !objectRef.HasRigTarget
                && objectRef.kind != ClipObjectKind.Bone)
            {
                unavailableReason = "This row is not a node the rig can declare a part for.";
                return false;
            }

            if (!AllowsMultiple(kind) && HasAny(clip, rig, objectRef, kind))
            {
                unavailableReason = DisplayName(kind) + " is already on this object.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// How many keys a component holds — what makes removing it destructive.
        /// </summary>
        /// <remarks>
        /// A socket answers 0 because it has no keys at all, not because it is empty. The caller
        /// confirms its removal on different grounds: it is rig structure, and something in a scene
        /// may be attached to it.
        /// </remarks>
        public static int KeyCount(
            ClipAsset clip, ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            switch (instance.kind)
            {
                case ClipComponentKind.Transform:
                {
                    TransformTrack track = GetAt(clip == null ? null : clip.transformTracks, instance.index);
                    return track == null || track.keys == null ? 0 : track.keys.Count;
                }
                case ClipComponentKind.BoneTransform:
                {
                    BoneTrack track = GetAt(clip == null ? null : clip.boneTracks, instance.index);
                    return track == null || track.keys == null ? 0 : track.keys.Count;
                }
                case ClipComponentKind.Flipbook:
                {
                    SpriteTrack track = GetAt(clip == null ? null : clip.spriteTracks, instance.index);
                    return track == null || track.keys == null ? 0 : track.keys.Count;
                }
                case ClipComponentKind.Billboard:
                {
                    // Summed over the root's tracks rather than read from one: the instance
                    // addresses the rig's root list, and the keys are in the clip.
                    if (clip == null || clip.billboardTracks == null
                        || objectRef.billboardRootId == 0u)
                    {
                        return 0;
                    }
                    int billboardKeyCount = 0;
                    for (int trackIndex = 0; trackIndex < clip.billboardTracks.Count; trackIndex++)
                    {
                        BillboardTrack track = clip.billboardTracks[trackIndex];
                        if (track == null || track.keys == null
                            || track.rootStableId != objectRef.billboardRootId)
                        {
                            continue;
                        }
                        billboardKeyCount += track.keys.Count;
                    }
                    return billboardKeyCount;
                }
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Declares a rig target for a previewed node, so part-bound components have an id to bind
        /// to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>An existing target is adopted before a new one is minted.</strong> A rig authored
        /// before nodes could be promoted binds its parts to scene objects by
        /// <c>RigTargetAuthoring</c>, and the editor's own path resolution has always matched a
        /// target to a node by display name. Minting a second target for a node that already has one
        /// would split one part in two — one baked and one not — which is worse than the problem
        /// this solves.
        /// </para>
        /// <para>
        /// <strong>Bone keys move with the node.</strong> A promoted node is posed on a transform
        /// track from here on (<see cref="TransformKindFor"/>), so any bone track it already had
        /// would stop being sampled. Its keys are carried across rather than dropped: the two key
        /// types hold the same pose, differing only in whether the rotation is stored as a
        /// quaternion or as the Euler degrees a person types.
        /// </para>
        /// <para>
        /// The caller mints the stable id through <c>RigAsset.EnsureStableIds</c>, as it does for a
        /// socket, and records undo on the rig. Id 0 is the sentinel for "no target", so a row that
        /// keeps it is not merely unidentified — it is a part no track can address.
        /// </para>
        /// </remarks>
        /// <returns>The definition the node is now bound to, or null when it could not be made.</returns>
        public static RigTargetDefinition PromoteToRigTarget(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef)
        {
            if (rig == null || objectRef.kind != ClipObjectKind.Bone || !objectRef.IsValid)
            {
                return null;
            }
            if (rig.targets == null)
            {
                rig.targets = new List<RigTargetDefinition>();
            }

            RigTargetDefinition existing = FindTargetForNode(rig, objectRef);
            if (existing != null)
            {
                // Stamping the path is what makes the adoption stick: next time the window builds
                // the object it resolves this target by path rather than by the name coincidence
                // that found it here. An empty path is not stamped — a caller that never had a
                // previewed hierarchy to read one against would otherwise erase the binding a
                // caller that did had already recorded.
                if (!string.IsNullOrEmpty(objectRef.nodePath))
                {
                    existing.sourceNodePath = objectRef.nodePath;
                }
                MigrateBoneTrackToTransform(clip, objectRef.boneName, existing);
                return existing;
            }

            RigTargetDefinition minted = new RigTargetDefinition();
            minted.displayName = objectRef.boneName;
            minted.sourceNodePath = objectRef.nodePath;
            rig.targets.Add(minted);
            rig.EnsureStableIds();
            MigrateBoneTrackToTransform(clip, objectRef.boneName, minted);
            return minted;
        }

        /// <summary>
        /// The target already standing for a node — by recorded path first, then by name.
        /// </summary>
        /// <remarks>
        /// The name fallback exists for rigs authored before <c>sourceNodePath</c> did, where the
        /// only link between a part and its node is that they are called the same thing. It is
        /// consulted only when promoting, never when reading the stack, so an accidental name
        /// collision cannot silently rebind a part that is working.
        /// </remarks>
        public static RigTargetDefinition FindTargetForNode(RigAsset rig, ClipObjectRef objectRef)
        {
            if (rig == null || rig.targets == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(objectRef.nodePath))
            {
                for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition target = rig.targets[targetIndex];
                    if (target != null
                        && string.Equals(
                            target.sourceNodePath, objectRef.nodePath, StringComparison.Ordinal))
                    {
                        return target;
                    }
                }
            }

            if (string.IsNullOrEmpty(objectRef.boneName))
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null
                    && string.IsNullOrEmpty(target.sourceNodePath)
                    && string.Equals(target.displayName, objectRef.boneName, StringComparison.Ordinal))
                {
                    return target;
                }
            }
            return null;
        }

        /// <summary>
        /// The target claiming a node's path, or 0 when none does.
        /// </summary>
        /// <remarks>
        /// By path only, which is the whole difference between this and
        /// <see cref="FindTargetForNode"/>. This one runs on every hierarchy rebuild and decides
        /// which components a row shows, so it answers from what the rig recorded rather than from
        /// a name that happens to match.
        /// </remarks>
        public static uint ResolveTargetIdForNode(RigAsset rig, string nodePath)
        {
            if (rig == null || rig.targets == null || string.IsNullOrEmpty(nodePath))
            {
                return 0u;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null
                    && string.Equals(target.sourceNodePath, nodePath, StringComparison.Ordinal))
                {
                    return target.Id.Value;
                }
            }
            return 0u;
        }

        /// <summary>
        /// Creates the track or socket a kind stands for, bound to the object.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The new track is empty, and an empty track is valid: every rule that walks keys
        /// short-circuits on zero of them, the bake writes a zero-length blob, and every sampler
        /// path already answers "no keys" without touching the array. So "added, not yet keyed" is a
        /// state the asset can genuinely hold — which is what lets adding a component be a decision
        /// separate from making the first key.
        /// </para>
        /// <para>
        /// A part-bound kind on an unclaimed node promotes it first, so the object this returns
        /// against may be a part that did not exist when the call was made. The caller rebuilds its
        /// object reference from the hierarchy afterwards rather than being handed a new one — the
        /// rig is the record of what happened, and a second copy of that answer could disagree with
        /// it.
        /// </para>
        /// <para>
        /// A socket is minted with its stable id left to the caller's <c>EnsureStableIds</c>. Id 0
        /// is the sentinel for "no socket selected", so a socket that keeps it is not merely
        /// unidentified — it is unselectable and its marker unfindable.
        /// </para>
        /// </remarks>
        /// <param name="newComponentName">
        /// Cosmetic label for the rig-scoped kinds, which are the ones a person names: a socket and
        /// a billboard root both appear in lists of their own. Ignored by the track kinds, whose
        /// identity is the object they are bound to.
        /// </param>
        /// <returns>The instance created, or an index of −1 when nothing could be added.</returns>
        public static ClipComponentInstance Add(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            string newComponentName)
        {
            string unavailableReason;
            if (!CanAdd(clip, rig, objectRef, kind, out unavailableReason))
            {
                return new ClipComponentInstance(kind, ClipComponentInstance.NoTrackIndex);
            }

            uint targetId = objectRef.targetId;
            if (RequiresRigTarget(kind) && targetId == 0u)
            {
                RigTargetDefinition promoted = PromoteToRigTarget(clip, rig, objectRef);
                if (promoted == null)
                {
                    return new ClipComponentInstance(kind, ClipComponentInstance.NoTrackIndex);
                }
                targetId = promoted.Id.Value;
            }

            switch (kind)
            {
                case ClipComponentKind.Transform:
                {
                    EnsureList(ref clip.transformTracks);
                    TransformTrack track = new TransformTrack();
                    track.targetId = targetId;
                    clip.transformTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.transformTracks.Count - 1);
                }
                case ClipComponentKind.BoneTransform:
                {
                    EnsureList(ref clip.boneTracks);
                    BoneTrack track = new BoneTrack();
                    track.boneName = objectRef.boneName;
                    clip.boneTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.boneTracks.Count - 1);
                }
                case ClipComponentKind.Flipbook:
                {
                    EnsureList(ref clip.spriteTracks);
                    SpriteTrack track = new SpriteTrack();
                    track.targetId = targetId;
                    clip.spriteTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.spriteTracks.Count - 1);
                }
                case ClipComponentKind.Billboard:
                {
                    if (rig.billboardRoots == null)
                    {
                        rig.billboardRoots = new List<BillboardRootDefinition>();
                    }
                    BillboardRootDefinition definition = new BillboardRootDefinition();
                    definition.displayName = newComponentName;
                    definition.address = objectRef.billboardAddress;
                    rig.billboardRoots.Add(definition);

                    // The caller mints the id, as it does for a socket: a root saved with id 0 is
                    // one no billboard track could ever address.
                    return new ClipComponentInstance(kind, rig.billboardRoots.Count - 1);
                }
                case ClipComponentKind.Ragdoll:
                {
                    if (rig.ragdollBodies == null)
                    {
                        rig.ragdollBodies = new List<RagdollBodyDefinition>();
                    }
                    RagdollBodyDefinition body = new RagdollBodyDefinition();
                    body.displayName = newComponentName;
                    body.address = objectRef.ragdollAddress;
                    rig.ragdollBodies.Add(body);

                    // Box, mass, damping and limits are left at the definition's own field
                    // initializers — a unit box at the node's origin, 1kg, light damping, a ±45°
                    // hinge. The window sizes the box from the node's renderer immediately after
                    // this call when it has one (spec §8.1); the model has no viewport to measure
                    // against, only the assets.
                    return new ClipComponentInstance(kind, rig.ragdollBodies.Count - 1);
                }
                default:
                {
                    if (rig.sockets == null)
                    {
                        rig.sockets = new List<SocketDefinition>();
                    }
                    SocketDefinition socket = new SocketDefinition();
                    socket.displayName = newComponentName;
                    if (objectRef.kind == ClipObjectKind.RigTarget)
                    {
                        socket.mode = SocketAttachMode.RigTarget;
                        socket.targetId = objectRef.targetId;
                    }
                    else
                    {
                        socket.mode = SocketAttachMode.Bone;
                        socket.boneName = objectRef.boneName;
                    }
                    rig.sockets.Add(socket);
                    return new ClipComponentInstance(kind, rig.sockets.Count - 1);
                }
            }
        }

        /// <summary>Deletes the track or socket a component stands for.</summary>
        /// <returns>Whether anything was removed.</returns>
        public static bool Remove(ClipAsset clip, RigAsset rig, ClipComponentInstance instance)
        {
            switch (instance.kind)
            {
                case ClipComponentKind.Transform:
                    return RemoveAt(clip == null ? null : clip.transformTracks, instance.index);
                case ClipComponentKind.BoneTransform:
                    return RemoveAt(clip == null ? null : clip.boneTracks, instance.index);
                case ClipComponentKind.Flipbook:
                    return RemoveAt(clip == null ? null : clip.spriteTracks, instance.index);
                case ClipComponentKind.Billboard:
                {
                    // The tracks go with the root. A track bound to a root the rig no longer
                    // declares is a validation error (V24) that animates nothing, so leaving one
                    // behind would be a broken clip with no visible cause.
                    BillboardRootDefinition definition =
                        GetAt(rig == null ? null : rig.billboardRoots, instance.index);
                    if (definition == null)
                    {
                        return false;
                    }
                    uint rootStableId = definition.Id.Value;
                    if (clip != null && clip.billboardTracks != null)
                    {
                        for (int trackIndex = clip.billboardTracks.Count - 1; trackIndex >= 0; trackIndex--)
                        {
                            BillboardTrack track = clip.billboardTracks[trackIndex];
                            if (track != null && track.rootStableId == rootStableId)
                            {
                                clip.billboardTracks.RemoveAt(trackIndex);
                            }
                        }
                    }
                    return RemoveAt(rig.billboardRoots, instance.index);
                }
                case ClipComponentKind.Ragdoll:
                    // No clip-side data to take with it — a ragdoll body carries no keys (spec §3.3
                    // has no animatable field on it at all), so unlike Billboard there is nothing
                    // here for the caller to warn about beyond the body itself going away.
                    return RemoveAt(rig == null ? null : rig.ragdollBodies, instance.index);
                default:
                    return RemoveAt(rig == null ? null : rig.sockets, instance.index);
            }
        }

        /// <summary>
        /// Moves a node's bone keys onto a transform track bound to the part it just became.
        /// </summary>
        /// <remarks>
        /// Silent when there is nothing to move, which is the ordinary case. When there is, the bone
        /// track is deleted afterwards: two tracks posing the same node is not a merge the bake can
        /// make sense of, and the bone one is the copy nothing samples once the node is a part.
        /// </remarks>
        private static void MigrateBoneTrackToTransform(
            ClipAsset clip, string boneName, RigTargetDefinition target)
        {
            if (clip == null || clip.boneTracks == null || target == null
                || string.IsNullOrEmpty(boneName))
            {
                return;
            }

            int boneTrackIndex = -1;
            for (int trackIndex = 0; trackIndex < clip.boneTracks.Count; trackIndex++)
            {
                BoneTrack candidate = clip.boneTracks[trackIndex];
                if (candidate != null
                    && string.Equals(candidate.boneName, boneName, StringComparison.Ordinal))
                {
                    boneTrackIndex = trackIndex;
                    break;
                }
            }
            if (boneTrackIndex < 0)
            {
                return;
            }

            BoneTrack boneTrack = clip.boneTracks[boneTrackIndex];
            uint targetId = target.Id.Value;
            if (targetId == 0u)
            {
                return;
            }
            if (boneTrack.keys == null || boneTrack.keys.Count == 0)
            {
                // An empty track is not poses, it is a leftover binding. The node is posed as a
                // part from here on, so nothing is lost by dropping it.
                clip.boneTracks.RemoveAt(boneTrackIndex);
                return;
            }

            EnsureList(ref clip.transformTracks);
            TransformTrack transformTrack = null;
            for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
            {
                TransformTrack candidate = clip.transformTracks[trackIndex];
                if (candidate != null && candidate.targetId == targetId)
                {
                    transformTrack = candidate;
                    break;
                }
            }

            // Refused onto a part that is already keyed. Those poses were authored against this
            // part, and appending a second object's keys to them would interleave two animations
            // into one unreadable track. The bone track is then left exactly where it is: deleting
            // authored keys to tidy up a binding is not a trade this is entitled to make, and the
            // stack shows the stranded track so it can be dealt with deliberately.
            if (transformTrack != null && transformTrack.keys != null
                && transformTrack.keys.Count > 0)
            {
                return;
            }

            if (transformTrack == null)
            {
                transformTrack = new TransformTrack();
                transformTrack.targetId = targetId;
                clip.transformTracks.Add(transformTrack);
            }

            transformTrack.keys = new List<TransformKey>(boneTrack.keys.Count);
            for (int keyIndex = 0; keyIndex < boneTrack.keys.Count; keyIndex++)
            {
                transformTrack.keys.Add(ClipKeyConversion.ToTransformKey(boneTrack.keys[keyIndex]));
            }
            clip.boneTracks.RemoveAt(boneTrackIndex);
        }

        /// <summary>
        /// The instances of one kind on one object, in the order the owning list holds them.
        /// </summary>
        /// <remarks>
        /// Public because a caller sometimes wants one kind rather than the whole stack — a paste
        /// asking "does this object have a third flipbook yet" has no use for its sockets. Unlike
        /// <see cref="CollectInstances"/> this never invents the placeholder an unkeyed intrinsic
        /// component carries: the question here is what exists, not what the panel shows.
        /// </remarks>
        public static void CollectInstancesOfKind(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            List<ClipComponentInstance> instances)
        {
            if (instances == null)
            {
                return;
            }
            instances.Clear();
            CollectInstancesOfKindInto(clip, rig, objectRef, kind, instances);
        }

        private static void CollectInstancesOfKindInto(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            List<ClipComponentInstance> instances)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform:
                {
                    if (clip == null || clip.transformTracks == null || objectRef.targetId == 0u)
                    {
                        return;
                    }
                    for (int trackIndex = 0; trackIndex < clip.transformTracks.Count; trackIndex++)
                    {
                        TransformTrack track = clip.transformTracks[trackIndex];
                        if (track != null && track.targetId == objectRef.targetId)
                        {
                            instances.Add(new ClipComponentInstance(kind, trackIndex));
                        }
                    }
                    return;
                }
                case ClipComponentKind.BoneTransform:
                {
                    // The empty name is refused rather than matched. A rig-target row has no node
                    // name at all, and a hand-edited clip can hold a track whose name is blank
                    // (validation rule V15) — matching the two would hang that track off every part
                    // in the rig.
                    if (clip == null || clip.boneTracks == null
                        || string.IsNullOrEmpty(objectRef.boneName))
                    {
                        return;
                    }
                    for (int trackIndex = 0; trackIndex < clip.boneTracks.Count; trackIndex++)
                    {
                        BoneTrack track = clip.boneTracks[trackIndex];
                        if (track != null
                            && string.Equals(track.boneName, objectRef.boneName, StringComparison.Ordinal))
                        {
                            instances.Add(new ClipComponentInstance(kind, trackIndex));
                        }
                    }
                    return;
                }
                case ClipComponentKind.Flipbook:
                {
                    if (clip == null || clip.spriteTracks == null || objectRef.targetId == 0u)
                    {
                        return;
                    }
                    for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                    {
                        SpriteTrack track = clip.spriteTracks[trackIndex];
                        if (track != null && track.targetId == objectRef.targetId)
                        {
                            instances.Add(new ClipComponentInstance(kind, trackIndex));
                        }
                    }
                    return;
                }
                case ClipComponentKind.Billboard:
                {
                    // The component is the root the rig declares, so its index addresses the rig's
                    // root list. The clip's tracks hang off it and may not exist yet: a node can
                    // billboard without any clip animating how much.
                    if (rig == null || rig.billboardRoots == null || objectRef.billboardRootId == 0u)
                    {
                        return;
                    }
                    for (int rootIndex = 0; rootIndex < rig.billboardRoots.Count; rootIndex++)
                    {
                        BillboardRootDefinition definition = rig.billboardRoots[rootIndex];
                        if (definition != null && definition.Id.Value == objectRef.billboardRootId)
                        {
                            instances.Add(new ClipComponentInstance(kind, rootIndex));
                            return;
                        }
                    }
                    return;
                }
                case ClipComponentKind.Ragdoll:
                {
                    // The component is the body the rig declares, addressed exactly as a billboard
                    // root is: the window has already resolved which body (if any) welds to this
                    // node and carries its id on the reference, because only the window holds the
                    // previewed hierarchy the address is read against.
                    if (rig == null || rig.ragdollBodies == null || objectRef.ragdollBodyId == 0u)
                    {
                        return;
                    }
                    for (int bodyIndex = 0; bodyIndex < rig.ragdollBodies.Count; bodyIndex++)
                    {
                        RagdollBodyDefinition definition = rig.ragdollBodies[bodyIndex];
                        if (definition != null && definition.Id.Value == objectRef.ragdollBodyId)
                        {
                            instances.Add(new ClipComponentInstance(kind, bodyIndex));
                            return;
                        }
                    }
                    return;
                }
                default:
                {
                    if (rig == null || rig.sockets == null)
                    {
                        return;
                    }
                    for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
                    {
                        SocketDefinition socket = rig.sockets[socketIndex];
                        if (socket == null || !FollowsObject(socket, objectRef))
                        {
                            continue;
                        }
                        instances.Add(new ClipComponentInstance(kind, socketIndex));
                    }
                    return;
                }
            }
        }

        /// <summary>Whether a socket hangs off this object — the object being its source.</summary>
        private static bool FollowsObject(SocketDefinition socket, ClipObjectRef objectRef)
        {
            if (objectRef.kind == ClipObjectKind.RigTarget)
            {
                return socket.mode == SocketAttachMode.RigTarget
                    && socket.targetId == objectRef.targetId;
            }
            return socket.mode == SocketAttachMode.Bone
                && string.Equals(socket.boneName, objectRef.boneName, StringComparison.Ordinal);
        }

        private static TItem GetAt<TItem>(List<TItem> list, int index) where TItem : class
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                return null;
            }
            return list[index];
        }

        private static bool RemoveAt<TItem>(List<TItem> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                return false;
            }
            list.RemoveAt(index);
            return true;
        }

        private static void EnsureList<TItem>(ref List<TItem> list)
        {
            if (list == null)
            {
                list = new List<TItem>();
            }
        }
    }
}
