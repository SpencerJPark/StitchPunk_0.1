// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Read side of billboarding: resolves the world-space rotation a node inherits this frame.
    /// Local +Y is up in the billboard plane, +Z points away from the viewer, +X completes the pair.
    /// </summary>
    [BurstCompile]
    public static class BillboardApi
    {
        /// <param name="member">The node's billboard membership.</param>
        /// <param name="rootElementLookup">Lookup over actor <see cref="BillboardRootElement"/> buffers.</param>
        /// <param name="frameRotation">The resolved world-space billboard frame.</param>
        /// <returns>False when the actor is gone or the root was removed from the rig since this node was baked; caller should fall back to world space.</returns>
        [BurstCompile]
        public static bool TryGetFrame(
            in BillboardMember member,
            in BufferLookup<BillboardRootElement> rootElementLookup,
            out quaternion frameRotation)
        {
            frameRotation = quaternion.identity;
            if (!rootElementLookup.HasBuffer(member.actorRoot))
            {
                return false;
            }

            DynamicBuffer<BillboardRootElement> rootElements = rootElementLookup[member.actorRoot];
            for (int rootIndex = 0; rootIndex < rootElements.Length; rootIndex++)
            {
                if (rootElements[rootIndex].rootId != member.rootId)
                {
                    continue;
                }
                frameRotation = rootElements[rootIndex].resolvedRotation;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Expresses <paramref name="worldDirection"/> (such as gravity) in a billboard frame's local axes.
        /// </summary>
        /// <param name="frameRotation">A frame from <see cref="TryGetFrame"/>.</param>
        /// <param name="worldDirection">The world-space direction to express.</param>
        /// <param name="billboardDirection">The direction in the billboard frame's local axes.</param>
        // out param, not a return: a [BurstCompile] static is an external entry point and Burst
        // cannot return a struct across one (BC1064/BC1067).
        [BurstCompile]
        public static void ToBillboardSpace(
            in quaternion frameRotation, in float3 worldDirection, out float3 billboardDirection)
        {
            billboardDirection = math.mul(math.inverse(frameRotation), worldDirection);
        }
    }
}
