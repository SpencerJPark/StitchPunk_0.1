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
    /// chain, rather than reading <c>LocalToWorld</c> (which is a frame stale relative to what this
    /// same presentation group already wrote — an ancestor billboard root's fresh rotation would not
    /// be visible yet). Shared by <c>RagdollCaptureSystem</c> and <c>RagdollApplySystem</c>.
    /// </summary>
    [BurstCompile]
    internal static class RagdollTransformMath
    {
        /// <param name="node">The node to resolve; must carry <c>LocalTransform</c>.</param>
        // Does not fold in an ancestor's PostTransformMatrix (matching BillboardResolveSystem's own
        // walk). A ragdoll body under a non-uniformly scaled or mirrored ancestor could drift
        // slightly as a result; no fixture here exercises that combination.
        [BurstCompile]
        public static void ComputeWorldTransform(
            in Entity node,
            in ComponentLookup<LocalTransform> localTransformLookup,
            in ComponentLookup<Parent> parentLookup,
            out LocalTransform worldTransform)
        {
            LocalTransform accumulated = localTransformLookup[node];
            Entity walker = node;

            // Bounded by the rig's depth, which is small. A cycle is impossible: the Entities parent
            // hierarchy is a tree by construction.
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

        /// <summary>The world transform of <paramref name="node"/>'s parent — identity when it has none, the "no parent" reading a root-body node needs.</summary>
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
