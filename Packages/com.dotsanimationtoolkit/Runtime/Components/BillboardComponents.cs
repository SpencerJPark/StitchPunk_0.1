// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// How a billboard faces the viewer (architecture section 6.1, amendments A39 and A44). The
    /// numeric values are shared with <c>_BillboardParams.x</c> so the CPU and shader paths cannot
    /// drift apart, and are therefore never renumbered.
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
        /// Align to the camera's forward vector rather than to its position, so every root takes
        /// the same rotation (amendment A39). The classic 2.5D look, and the default.
        /// </summary>
        ScreenAligned = 4,

        /// <summary>
        /// Turn about an arbitrary authored axis (amendment A44), the way <see cref="Upright"/>
        /// turns about world Y.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="Upright"/> is exactly this mode with the axis <c>(0, 1, 0)</c>, and the two
        /// share every line of the implementation. It keeps its own value because it shipped with
        /// one, and because a named mode reads better in an inspector than an axis a reader has to
        /// recognise.
        /// </para>
        /// <para>
        /// <strong>The shader path cannot honour this mode.</strong> <c>_BillboardParams</c> has no
        /// channel wide enough for an arbitrary axis, so <c>ToolkitBillboard.hlsl</c> treats it as
        /// <see cref="Upright"/>. A host that needs a non-vertical axis uses the CPU path, which is
        /// the right path for a layered cutout rig anyway (amendment A41).
        /// </para>
        /// </remarks>
        AxisConstrained = 5
    }

    /// <summary>
    /// One billboard root of an actor (amendment A44): a node that turns to face the viewer, and
    /// the pivot every node beneath it inherits unless one of them declares a root of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The whole of an actor's billboard state lives in this one buffer, on the actor
    /// root.</strong> A baker may only write components on the entity it is baking, and a billboard
    /// root can be any node of the prefab — including a bare grouping transform that carries no
    /// authoring component and therefore has no baker of its own. Collecting the roots into a buffer
    /// on the actor is what lets one baker resolve the whole hierarchy without a cross-entity
    /// structural change.
    /// </para>
    /// <para>
    /// <strong>Buffer order is hierarchy depth, shallowest first, and that is load-bearing.</strong>
    /// <c>BillboardResolveSystem</c> resolves the elements in order and reads each node's rest
    /// orientation by walking live <c>LocalTransform</c> values up the parent chain. An outer root
    /// has therefore already written its result by the time a nested root reads through it, which is
    /// exactly what stops the inner one composing on top of the outer one and turning twice. Reorder
    /// this buffer and nested billboards double-rotate.
    /// </para>
    /// </remarks>
    [InternalBufferCapacity(2)]
    public struct BillboardRootElement : IBufferElementData
    {
        /// <summary>
        /// The authored <c>BillboardRootId</c> this element came from. Clip billboard tracks and
        /// <see cref="BillboardMember"/> both address a root by this id rather than by buffer
        /// position, so neither is invalidated by a rig gaining or losing another root.
        /// </summary>
        public uint rootId;

        /// <summary>The entity whose rotation this root writes.</summary>
        public Entity node;

        /// <summary>This root's configuration, plus whatever a clip has keyed on top of it.</summary>
        public BillboardSettings settings;

        /// <summary>
        /// The root's resolved world-space orientation — the billboard frame, published for anything
        /// that needs it as a reference (the ragdoll's gravity frame, most of all).
        /// </summary>
        /// <remarks>
        /// Always meaningful. When the billboard refuses to resolve — mode off, root disabled, or a
        /// degenerate camera — this holds the node's unmodified world orientation rather than a
        /// stale value from whenever it last succeeded, so a consumer never has to ask whether the
        /// frame it just read is real.
        /// </remarks>
        public quaternion resolvedRotation;
    }

    /// <summary>
    /// Which billboard root a node inherits (amendment A44). Added only to nodes that actually sit
    /// under one, so a rig that never billboards carries nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is what makes the billboard frame an O(1) query instead of a hierarchy walk.</strong>
    /// The bake has already resolved which root is a node's nearest billboarded ancestor, so the
    /// ragdoll — which needs a gravity frame per body per physics step — reads it rather than
    /// climbing the parent chain to rediscover it.
    /// </para>
    /// <para>
    /// The root is named by id and the actor by entity, so the pair survives instantiation the way
    /// <c>RigPartBinding</c> does: the id is plain data, and the entity reference is patched by
    /// <c>Instantiate</c> for members of the actor's <c>LinkedEntityGroup</c> (amendment A35).
    /// </para>
    /// </remarks>
    public struct BillboardMember : IComponentData
    {
        /// <summary>The actor root whose <see cref="BillboardRootElement"/> buffer holds the root.</summary>
        public Entity actorRoot;

        /// <summary>The id of the nearest ancestor billboard root, inclusive of this node itself.</summary>
        public uint rootId;
    }
}
