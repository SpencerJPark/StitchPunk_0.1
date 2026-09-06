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
    /// The object a component stack belongs to. <see cref="kind"/> says where the row came from;
    /// <see cref="targetId"/> says what the object is — a claimed node carries both, so part-bound
    /// components apply to it like any other part.
    /// </summary>
    public readonly struct ClipObjectRef : IEquatable<ClipObjectRef>
    {
        public readonly ClipObjectKind kind;

        /// <summary>The rig target this object is, or 0 when the rig declares none for it.</summary>
        public readonly uint targetId;

        /// <summary>Set for a previewed node; empty for a rig-target row.</summary>
        public readonly string boneName;

        // The node's path from the previewed prefab root, or empty when there is no hierarchy to
        // read one against. An empty path is an address, not a missing one — it names the prefab root.
        public readonly string nodePath;

        /// <summary>
        /// The stable id of the billboard root this object declares, or 0 when it declares none.
        /// </summary>
        public readonly uint billboardRootId;

        // How the rig would address this object as a billboard root, if it were made one. Carried
        // whether or not the object is a root yet, since that is what adding Billboard writes.
        public readonly RigNodeAddress billboardAddress;

        /// <summary>The stable id of the ragdoll body welded to this object, or 0 when none is.</summary>
        public readonly uint ragdollBodyId;

        // How the rig would address this object as a ragdoll body, if one were added. Unlike
        // billboardAddress this can be a Bone address: a ragdoll body welds cleanly to a skinned
        // bone, whose path below the prefab root is not a stable handle the way a transform's is.
        public readonly RigNodeAddress ragdollAddress;

        private ClipObjectRef(
            ClipObjectKind kind, uint targetId, string boneName, string nodePath,
            uint billboardRootId, RigNodeAddress billboardAddress, uint ragdollBodyId,
            RigNodeAddress ragdollAddress)
        {
            this.kind = kind;
            this.targetId = targetId;
            this.boneName = boneName;
            this.nodePath = nodePath;
            this.billboardRootId = billboardRootId;
            this.billboardAddress = billboardAddress;
            this.ragdollBodyId = ragdollBodyId;
            this.ragdollAddress = ragdollAddress;
        }

        /// <summary>
        /// A rig target with no previewed node of its own, addressed as a billboard root or a
        /// ragdoll body by its stable id — unlike a node, there is no path to resolve.
        /// </summary>
        public static ClipObjectRef RigTarget(uint targetId, uint billboardRootId, uint ragdollBodyId = 0u)
        {
            RigNodeAddress address = new RigNodeAddress
            {
                kind = RigNodeAddressKind.RigTarget,
                targetId = targetId
            };
            return new ClipObjectRef(
                ClipObjectKind.RigTarget, targetId, string.Empty, string.Empty, billboardRootId,
                address, ragdollBodyId, address);
        }

        /// <summary>A node of the previewed prefab.</summary>
        /// <param name="targetId">The rig target claiming this node, or 0 when none does.</param>
        /// <param name="isSkinnedBone">
        /// Whether this node is an imported skinned-mesh bone rather than an authored guiding
        /// transform. Decides only <see cref="ragdollAddress"/>'s kind; <see cref="billboardAddress"/>
        /// always addresses a node by path regardless.
        /// </param>
        public static ClipObjectRef Bone(
            string boneName, uint targetId, uint billboardRootId, string hierarchyPath,
            uint ragdollBodyId = 0u, bool isSkinnedBone = false)
        {
            string resolvedPath = hierarchyPath ?? string.Empty;
            string resolvedBoneName = boneName ?? string.Empty;

            // A claimed node is still addressed by path for billboarding, not by its target id.
            // The id says which part it is; the path says where it sits, and a billboard root is a
            // fact about the node's place in the hierarchy that its descendants inherit.
            RigNodeAddress billboardAddress = new RigNodeAddress
            {
                kind = RigNodeAddressKind.HierarchyPath,
                hierarchyPath = resolvedPath
            };

            // A ragdoll body takes the same path address as billboarding, unless the node is a
            // skinned bone: a bone's path below the prefab root moves whenever an artist reparents
            // inside the armature, so a bone is addressed by name instead.
            RigNodeAddress ragdollAddress = isSkinnedBone
                ? new RigNodeAddress { kind = RigNodeAddressKind.Bone, boneName = resolvedBoneName }
                : new RigNodeAddress
                {
                    kind = RigNodeAddressKind.HierarchyPath,
                    hierarchyPath = resolvedPath
                };

            return new ClipObjectRef(
                ClipObjectKind.Bone, targetId, resolvedBoneName, resolvedPath,
                billboardRootId, billboardAddress, ragdollBodyId, ragdollAddress);
        }

        // The same object, now carrying the part the rig has just declared for it — for callers
        // that promote several things in one pass and cannot afford a hierarchy rebuild between each.
        public ClipObjectRef WithRigTarget(uint newTargetId)
        {
            return new ClipObjectRef(
                kind, newTargetId, boneName, nodePath, billboardRootId, billboardAddress,
                ragdollBodyId, ragdollAddress);
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
