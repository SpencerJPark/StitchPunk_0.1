// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Which of the two rows in the clip editor's hierarchy an object came from.</summary>
    public enum ClipObjectKind : byte
    {
        /// <summary>A rig target the rig declares and no previewed node has claimed.</summary>
        RigTarget = 0,

        /// <summary>A node of the previewed prefab, addressed by name and by path.</summary>
        Bone = 1
    }

    /// <summary>
    /// The object a component stack belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong><see cref="kind"/> says where the row came from; <see cref="targetId"/> says what the
    /// object is.</strong> The two used to be the same question — a rig target had an id and a node
    /// had a name, and never both — which meant a plane in the prefab hierarchy could carry a bone
    /// track and nothing else, because a sprite track has only an id to bind by. A node that a rig
    /// target claims (<c>RigTargetDefinition.sourceNodePath</c>) now carries that id here as well as
    /// its name, so the part-bound components apply to it like any other part.
    /// </para>
    /// <para>
    /// <see cref="nodePath"/> is resolved by the caller, not looked up here: it is read against the
    /// previewed hierarchy, and only the window has one. Passing it in keeps this struct — and the
    /// model that reads it — free of the preview scene.
    /// </para>
    /// </remarks>
    public readonly struct ClipObjectRef : IEquatable<ClipObjectRef>
    {
        public readonly ClipObjectKind kind;

        /// <summary>
        /// The rig target this object is, or 0 when the rig declares none for it.
        /// </summary>
        /// <remarks>
        /// Set for a rig-target row always, and for a previewed node whenever some target claims
        /// that node's path. Zero is the sentinel the rig reserves, so it doubles as "not a part
        /// yet" without a second flag.
        /// </remarks>
        public readonly uint targetId;

        /// <summary>Set for a previewed node; empty for a rig-target row.</summary>
        public readonly string boneName;

        /// <summary>
        /// The node's path from the previewed prefab root, or empty when there is no hierarchy to
        /// read one against.
        /// </summary>
        /// <remarks>
        /// This is what a newly minted rig target records as its source, and what a billboard root
        /// on a node is addressed by. Empty is meaningfully different from "the root": with no
        /// previewed hierarchy an empty path would silently name the root rather than this node,
        /// which is what <see cref="billboardAddressable"/> guards.
        /// </remarks>
        public readonly string nodePath;

        /// <summary>
        /// The stable id of the billboard root this object declares, or 0 when it declares none.
        /// </summary>
        public readonly uint billboardRootId;

        /// <summary>
        /// How the rig would address this object as a billboard root, if it were made one.
        /// </summary>
        /// <remarks>
        /// Carried whether or not the object is a root yet, because that is what adding the
        /// Billboard component writes. Meaningful only when <see cref="billboardAddressable"/>.
        /// </remarks>
        public readonly BillboardNodeAddress billboardAddress;

        /// <summary>
        /// Whether that address can be resolved at all — false for a node with no previewed
        /// hierarchy to read a path against, where an empty path would silently mean the prefab
        /// root rather than "unknown".
        /// </summary>
        public readonly bool billboardAddressable;

        private ClipObjectRef(
            ClipObjectKind kind, uint targetId, string boneName, string nodePath,
            uint billboardRootId, BillboardNodeAddress billboardAddress, bool billboardAddressable)
        {
            this.kind = kind;
            this.targetId = targetId;
            this.boneName = boneName;
            this.nodePath = nodePath;
            this.billboardRootId = billboardRootId;
            this.billboardAddress = billboardAddress;
            this.billboardAddressable = billboardAddressable;
        }

        /// <summary>
        /// A rig target with no previewed node of its own, which is always addressable as a
        /// billboard root: it has a stable id, so unlike a node there is no path to resolve.
        /// </summary>
        public static ClipObjectRef RigTarget(uint targetId, uint billboardRootId)
        {
            BillboardNodeAddress address = new BillboardNodeAddress
            {
                kind = BillboardAddressKind.RigTarget,
                targetId = targetId
            };
            return new ClipObjectRef(
                ClipObjectKind.RigTarget, targetId, string.Empty, string.Empty, billboardRootId,
                address, true);
        }

        /// <summary>
        /// A node of the previewed prefab.
        /// </summary>
        /// <param name="targetId">
        /// The rig target claiming this node, or 0 when none does. A node with one is a part: its
        /// transform is keyed on a transform track and it can carry a flipbook. A node without one
        /// is keyed on a bone track, and minting a target is what adding a part-bound component to
        /// it does.
        /// </param>
        public static ClipObjectRef Bone(
            string boneName, uint targetId, uint billboardRootId,
            string hierarchyPath, bool billboardAddressable)
        {
            string resolvedPath = hierarchyPath ?? string.Empty;

            // A claimed node is still addressed by path for billboarding, not by its target id.
            // The id says which part it is; the path says where it sits, and a billboard root is a
            // fact about the node's place in the hierarchy that its descendants inherit.
            BillboardNodeAddress address = new BillboardNodeAddress
            {
                kind = BillboardAddressKind.HierarchyPath,
                hierarchyPath = resolvedPath
            };
            return new ClipObjectRef(
                ClipObjectKind.Bone, targetId, boneName ?? string.Empty, resolvedPath,
                billboardRootId, address, billboardAddressable);
        }

        /// <summary>
        /// The same object, now carrying the part the rig has just declared for it.
        /// </summary>
        /// <remarks>
        /// Promoting a node writes the rig, and every reference built before that call still says
        /// the node is not a part. Rebuilding the whole reference from the hierarchy is what the
        /// window does between edits; this is for the callers that promote several things in one
        /// pass — a paste, chiefly — and cannot afford a hierarchy rebuild between each one.
        /// </remarks>
        public ClipObjectRef WithRigTarget(uint newTargetId)
        {
            return new ClipObjectRef(
                kind, newTargetId, boneName, nodePath, billboardRootId, billboardAddress,
                billboardAddressable);
        }

        /// <summary>Whether this reference names something a track could actually be bound to.</summary>
        public bool IsValid
        {
            get
            {
                return kind == ClipObjectKind.RigTarget
                    ? targetId != 0u
                    : !string.IsNullOrEmpty(boneName);
            }
        }

        /// <summary>
        /// Whether the rig declares a part for this object, and so whether the part-bound
        /// components have an id to bind to without minting one first.
        /// </summary>
        public bool HasRigTarget
        {
            get { return targetId != 0u; }
        }

        /// <summary>The name a component minted for this object is called after.</summary>
        public string DisplayName
        {
            get { return kind == ClipObjectKind.RigTarget ? string.Empty : boneName; }
        }

        public bool Equals(ClipObjectRef other)
        {
            if (kind != other.kind)
            {
                return false;
            }
            return kind == ClipObjectKind.RigTarget
                ? targetId == other.targetId
                : string.Equals(boneName, other.boneName, StringComparison.Ordinal);
        }

        public override bool Equals(object other)
        {
            return other is ClipObjectRef && Equals((ClipObjectRef)other);
        }

        public override int GetHashCode()
        {
            return kind == ClipObjectKind.RigTarget
                ? targetId.GetHashCode()
                : (boneName == null ? 0 : boneName.GetHashCode());
        }
    }
}
