// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// How a billboard faces the viewer. The numeric values are shared with
    /// <c>_BillboardParams.x</c> so the CPU and shader paths cannot drift apart — never renumber these.
    /// </summary>
    public enum BillboardMode : byte
    {
        /// <summary>No billboarding; the node keeps its animated orientation.</summary>
        Off = 0,

        /// <summary>Face the camera on every axis.</summary>
        Full = 1,

        /// <summary>Face the camera but stay upright — rotation about world Y only.</summary>
        Upright = 2,

        /// <summary>
        /// Hold an authored yaw while pitch still follows the camera. The corpse case: a dead body
        /// should not pirouette to follow the viewer, but it should still be seen rather than
        /// edge-on.
        /// </summary>
        FrozenYaw = 3,

        /// <summary>
        /// Align to the camera's forward vector rather than its position, so every root takes the
        /// same rotation. The classic 2.5D look, and the default.
        /// </summary>
        ScreenAligned = 4,

        /// <summary>Turn about an arbitrary authored axis, the way <see cref="Upright"/> turns about world Y.</summary>
        // The shader path cannot honour an arbitrary axis (_BillboardParams has no channel wide
        // enough), so ToolkitBillboard.hlsl treats this as Upright; a host needing a non-vertical
        // axis must use the CPU path.
        AxisConstrained = 5
    }

    /// <summary>One billboard root of an actor: a node that turns to face the viewer, and the pivot every node beneath it inherits unless one declares its own root.</summary>
    // Buffer order is hierarchy depth, shallowest first, and that is load-bearing:
    // BillboardResolveSystem resolves in order and reads each node's rest orientation up the
    // parent chain, so an outer root has already written its result before a nested root reads
    // through it. Reorder this buffer and nested billboards double-rotate.
    [InternalBufferCapacity(2)]
    public struct BillboardRootElement : IBufferElementData
    {
        public uint rootId; // addressed by id, not buffer position, so it survives another root being added/removed

        public Entity node; // entity whose rotation this root writes

        public BillboardSettings settings; // this root's configuration, plus whatever a clip keyed on top

        public quaternion resolvedRotation; // always meaningful: holds the node's unmodified world orientation when billboarding doesn't resolve, never a stale value
    }

    /// <summary>Which billboard root a node inherits. Added only to nodes that actually sit under one.</summary>
    public struct BillboardMember : IComponentData
    {
        public Entity actorRoot; // whose BillboardRootElement buffer holds the root

        public uint rootId; // nearest ancestor billboard root, inclusive of this node
    }
}
