// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Composes a node's world transform by walking live <c>LocalTransform</c> values up the parent
    /// chain (Phase D, amendment A50) — shared by <c>RagdollCaptureSystem</c> and
    /// <c>RagdollApplySystem</c>, which both need it: capture to seed a body's world position and
    /// orientation from the current hierarchy, apply to convert a solved world pose back into the
    /// node's local space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Mirrors <c>BillboardResolveSystem.ComputeWorldTransform</c> deliberately, rather than
    /// reading <c>LocalToWorld</c>.</strong> <c>LocalToWorld</c> is a frame stale relative to
    /// whatever this same presentation group already wrote this frame — in particular, an ancestor
    /// billboard root's freshly resolved rotation would not be visible yet — and the ragdoll group
    /// runs immediately after <c>BillboardResolveSystem</c> for exactly that reason (§7). Composing
    /// from live local values is what keeps a ragdoll body's world pose agreeing with the frame the
    /// billboard system just resolved, instead of lagging it by one frame.
    /// </para>
    /// <para>
    /// <strong>This composition does not fold in an ancestor's <c>PostTransformMatrix</c>,
    /// matching the same simplification <c>BillboardResolveSystem</c>'s own walk already makes.</strong>
    /// Unity's own <c>LocalToWorldSystem</c> does multiply a parent's <c>PostTransformMatrix</c> into
    /// the matrix its children are composed against, so a ragdoll body whose ancestor is both
    /// non-uniformly scaled (or mirrored, amendment A37) and carries a body beneath it could see a
    /// small drift here. No fixture in this phase's obligations exercises that combination, and
    /// fixing it means deciding whether the billboard walk should change too — a call this phase
    /// declines to make quietly. Flagged in <c>RagdollApplySystem</c>'s remarks as a carried
    /// limitation rather than left implicit.
    /// </para>
    /// </remarks>
    [BurstCompile]
    internal static class RagdollTransformUtil
    {
        /// <summary>
        /// The world-space position, rotation and (uniform) scale of <paramref name="node"/>, walked
        /// from its own <c>LocalTransform</c> up through every ancestor's.
        /// </summary>
        /// <param name="node">The node to resolve; must carry <c>LocalTransform</c>.</param>
        /// <param name="localTransformLookup">Read-only lookup over every node's <c>LocalTransform</c>.</param>
        /// <param name="parentLookup">Read-only lookup over every node's <c>Parent</c>.</param>
        /// <param name="worldTransform">The composed result.</param>
        [BurstCompile]
        public static void ComputeWorldTransform(
            in Entity node,
            in ComponentLookup<LocalTransform> localTransformLookup,
            in ComponentLookup<Parent> parentLookup,
            out LocalTransform worldTransform)
        {
            LocalTransform accumulated = localTransformLookup[node];
            Entity walker = node;

            // Bounded by the rig's depth, which is small. A cycle is impossible: Entities' parent
            // hierarchy is a tree by construction — the same guarantee BillboardResolveSystem's own
            // walk relies on.
            while (parentLookup.HasComponent(walker))
            {
                Entity parent = parentLookup[walker].Value;
                if (!localTransformLookup.HasComponent(parent))
                {
                    break;
                }

                LocalTransform parentTransform = localTransformLookup[parent];
                accumulated = new LocalTransform
                {
                    Position = parentTransform.Position
                        + math.mul(parentTransform.Rotation, accumulated.Position * parentTransform.Scale),
                    Rotation = math.mul(parentTransform.Rotation, accumulated.Rotation),
                    Scale = parentTransform.Scale * accumulated.Scale
                };
                walker = parent;
            }
            worldTransform = accumulated;
        }

        /// <summary>
        /// The world-space position, rotation and scale of <paramref name="node"/>'s <em>parent</em>
        /// — identity when <paramref name="node"/> has none, exactly the "no parent" reading
        /// <c>RagdollApplySystem</c> needs for a root-body node that sits directly under the actor.
        /// </summary>
        [BurstCompile]
        public static void ComputeParentWorldTransform(
            in Entity node,
            in ComponentLookup<LocalTransform> localTransformLookup,
            in ComponentLookup<Parent> parentLookup,
            out LocalTransform parentWorldTransform)
        {
            if (!parentLookup.HasComponent(node))
            {
                parentWorldTransform = LocalTransform.Identity;
                return;
            }
            Entity parent = parentLookup[node].Value;
            if (!localTransformLookup.HasComponent(parent))
            {
                parentWorldTransform = LocalTransform.Identity;
                return;
            }
            ComputeWorldTransform(in parent, in localTransformLookup, in parentLookup, out parentWorldTransform);
        }
    }
}
