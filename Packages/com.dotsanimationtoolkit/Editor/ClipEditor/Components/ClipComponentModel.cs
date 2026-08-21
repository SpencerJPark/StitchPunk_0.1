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
    /// tested without a window, and it is where the rules belong: "a bone cannot carry a flipbook
    /// track" is a fact about the data model, not about a panel.
    /// </para>
    /// <para>
    /// <strong>Presence is derived, never stored.</strong> An object has a Transform component
    /// exactly when a transform track is bound to it. There is no second list to keep in step, so
    /// a clip hand-edited outside this window — or authored before the stack existed — reads back
    /// with precisely the components its tracks describe.
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
            ClipComponentKind.Socket
        };

        private static readonly List<ClipComponentInstance> presenceScratch =
            new List<ClipComponentInstance>();

        /// <summary>Every kind, in stack order — the Add Component menu's order too.</summary>
        public static IReadOnlyList<ClipComponentKind> AllKinds
        {
            get { return stackOrder; }
        }

        public static string DisplayName(ClipComponentKind kind)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform: return "Transform";
                case ClipComponentKind.BoneTransform: return "Bone Transform";
                case ClipComponentKind.Flipbook: return "Flipbook";
                case ClipComponentKind.Billboard: return "Billboard";
                default: return "Socket";
            }
        }

        public static ClipComponentScope Scope(ClipComponentKind kind)
        {
            return kind == ClipComponentKind.Socket
                ? ClipComponentScope.Rig
                : ClipComponentScope.Clip;
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
        /// The reason is returned rather than the kind simply being hidden, because a menu that
        /// silently omits what you are looking for reads as a bug. Billboard is the one that needs
        /// saying out loud: it is bound to a billboard <em>root</em>, which is rig structure, so the
        /// answer is "make this node a root first", not "billboards are unavailable".
        /// </remarks>
        public static bool AppliesTo(
            ClipComponentKind kind, ClipObjectRef objectRef, out string unavailableReason)
        {
            unavailableReason = string.Empty;
            switch (kind)
            {
                case ClipComponentKind.Transform:
                    if (objectRef.kind == ClipObjectKind.RigTarget)
                    {
                        return true;
                    }
                    unavailableReason =
                        "Only a rig target carries a transform track. A bone uses Bone Transform.";
                    return false;

                case ClipComponentKind.BoneTransform:
                    if (objectRef.kind == ClipObjectKind.Bone)
                    {
                        return true;
                    }
                    unavailableReason =
                        "Only a skeleton bone carries a bone track. A rig target uses Transform.";
                    return false;

                case ClipComponentKind.Flipbook:
                    if (objectRef.kind == ClipObjectKind.RigTarget)
                    {
                        return true;
                    }
                    unavailableReason =
                        "A flipbook drives a cutout part's frame index; a bone has no frames.";
                    return false;

                case ClipComponentKind.Billboard:
                    if (objectRef.billboardRootId != 0u)
                    {
                        return true;
                    }
                    unavailableReason =
                        "This node is not a billboard root. Make it one first — a billboard track "
                        + "animates a root the rig declares.";
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// The components this object has, in stack order.
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

            for (int orderIndex = 0; orderIndex < stackOrder.Length; orderIndex++)
            {
                ClipComponentKind kind = stackOrder[orderIndex];
                string unavailableReason;
                if (!AppliesTo(kind, objectRef, out unavailableReason))
                {
                    continue;
                }
                CollectInstancesOfKind(clip, rig, objectRef, kind, instances);
            }
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
            CollectInstancesOfKind(clip, rig, objectRef, kind, presenceScratch);
            return presenceScratch.Count > 0;
        }

        /// <summary>
        /// Whether Add Component should offer a kind, given what the object already has.
        /// </summary>
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
                unavailableReason = "The clip set has no rig, and this component is stored on it.";
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
        public static int KeyCount(ClipAsset clip, ClipComponentInstance instance)
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
                    BillboardTrack track = GetAt(clip == null ? null : clip.billboardTracks, instance.index);
                    return track == null || track.keys == null ? 0 : track.keys.Count;
                }
                default:
                    return 0;
            }
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
        /// A socket is minted with its stable id left to the caller's <c>EnsureStableIds</c>. Id 0
        /// is the sentinel for "no socket selected", so a socket that keeps it is not merely
        /// unidentified — it is unselectable and its marker unfindable.
        /// </para>
        /// </remarks>
        /// <returns>The instance created, or an index of −1 when nothing could be added.</returns>
        public static ClipComponentInstance Add(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            string socketDisplayName)
        {
            string unavailableReason;
            if (!CanAdd(clip, rig, objectRef, kind, out unavailableReason))
            {
                return new ClipComponentInstance(kind, -1);
            }

            switch (kind)
            {
                case ClipComponentKind.Transform:
                {
                    EnsureList(ref clip.transformTracks);
                    TransformTrack track = new TransformTrack();
                    track.targetId = objectRef.targetId;
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
                    track.targetId = objectRef.targetId;
                    clip.spriteTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.spriteTracks.Count - 1);
                }
                case ClipComponentKind.Billboard:
                {
                    EnsureList(ref clip.billboardTracks);
                    BillboardTrack track = new BillboardTrack();
                    track.rootStableId = objectRef.billboardRootId;
                    clip.billboardTracks.Add(track);
                    return new ClipComponentInstance(kind, clip.billboardTracks.Count - 1);
                }
                default:
                {
                    if (rig.sockets == null)
                    {
                        rig.sockets = new List<SocketDefinition>();
                    }
                    SocketDefinition socket = new SocketDefinition();
                    socket.displayName = socketDisplayName;
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
                    return RemoveAt(clip == null ? null : clip.billboardTracks, instance.index);
                default:
                    return RemoveAt(rig == null ? null : rig.sockets, instance.index);
            }
        }

        private static void CollectInstancesOfKind(
            ClipAsset clip, RigAsset rig, ClipObjectRef objectRef, ClipComponentKind kind,
            List<ClipComponentInstance> instances)
        {
            switch (kind)
            {
                case ClipComponentKind.Transform:
                {
                    if (clip == null || clip.transformTracks == null)
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
                    if (clip == null || clip.boneTracks == null)
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
                    if (clip == null || clip.spriteTracks == null)
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
                    if (clip == null || clip.billboardTracks == null || objectRef.billboardRootId == 0u)
                    {
                        return;
                    }
                    for (int trackIndex = 0; trackIndex < clip.billboardTracks.Count; trackIndex++)
                    {
                        BillboardTrack track = clip.billboardTracks[trackIndex];
                        if (track != null && track.rootStableId == objectRef.billboardRootId)
                        {
                            instances.Add(new ClipComponentInstance(kind, trackIndex));
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
