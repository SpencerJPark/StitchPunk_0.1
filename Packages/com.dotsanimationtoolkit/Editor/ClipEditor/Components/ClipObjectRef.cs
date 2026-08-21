// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Which of the two addressable things in a rig an object is.</summary>
    public enum ClipObjectKind : byte
    {
        /// <summary>A rig target — a cutout part the rig declares and gives a stable id.</summary>
        RigTarget = 0,

        /// <summary>A node of the imported skeleton, addressed by name.</summary>
        Bone = 1
    }

    /// <summary>
    /// The object a component stack belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rig targets carry an id and bones carry a name, because a bone lives in an imported hierarchy
    /// this package does not own and cannot assign a stable id to — the same asymmetry
    /// <c>BoneTrack.boneName</c> and <c>SocketDefinition.boneName</c> already carry.
    /// </para>
    /// <para>
    /// <strong><see cref="billboardRootId"/> and <see cref="billboardAddress"/> are resolved by the
    /// caller, not looked up here.</strong> A billboard root is addressed by hierarchy path for a
    /// bone, and only the window knows the previewed hierarchy that path is read against. Passing
    /// both in keeps this struct — and the model that reads it — free of the preview scene.
    /// </para>
    /// </remarks>
    public readonly struct ClipObjectRef : IEquatable<ClipObjectRef>
    {
        public readonly ClipObjectKind kind;

        /// <summary>Set for a rig target; 0 for a bone.</summary>
        public readonly uint targetId;

        /// <summary>Set for a bone; null or empty for a rig target.</summary>
        public readonly string boneName;

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
        /// Whether that address can be resolved at all — false for a bone with no previewed
        /// hierarchy to read a path against, where an empty path would silently mean the prefab
        /// root rather than "unknown".
        /// </summary>
        public readonly bool billboardAddressable;

        private ClipObjectRef(
            ClipObjectKind kind, uint targetId, string boneName, uint billboardRootId,
            BillboardNodeAddress billboardAddress, bool billboardAddressable)
        {
            this.kind = kind;
            this.targetId = targetId;
            this.boneName = boneName;
            this.billboardRootId = billboardRootId;
            this.billboardAddress = billboardAddress;
            this.billboardAddressable = billboardAddressable;
        }

        /// <summary>
        /// A rig target, which is always addressable as a billboard root: it has a stable id, so
        /// unlike a bone there is no hierarchy to read a path against.
        /// </summary>
        public static ClipObjectRef RigTarget(uint targetId, uint billboardRootId)
        {
            BillboardNodeAddress address = new BillboardNodeAddress
            {
                kind = BillboardAddressKind.RigTarget,
                targetId = targetId
            };
            return new ClipObjectRef(
                ClipObjectKind.RigTarget, targetId, string.Empty, billboardRootId, address, true);
        }

        public static ClipObjectRef Bone(
            string boneName, uint billboardRootId,
            string billboardHierarchyPath, bool billboardAddressable)
        {
            BillboardNodeAddress address = new BillboardNodeAddress
            {
                kind = BillboardAddressKind.HierarchyPath,
                hierarchyPath = billboardHierarchyPath ?? string.Empty
            };
            return new ClipObjectRef(
                ClipObjectKind.Bone, 0u, boneName ?? string.Empty, billboardRootId,
                address, billboardAddressable);
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
