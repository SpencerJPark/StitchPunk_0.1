// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The read side of billboarding (amendment A44): how a game asks which way a node is actually
    /// facing this frame, without recomputing any of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This exists so the ragdoll does not have to reinvent facing.</strong> Billboard-space
    /// physics needs a reference frame to call "down", and the only correct answer is the frame the
    /// animation system just resolved. A second implementation would agree with the first until the
    /// moment either gained a snap wheel or an arc limit, and then would disagree silently.
    /// </para>
    /// <para>
    /// The frame is a <em>world-space rotation</em>. Its axes are the billboard's basis: local +Y is
    /// up within the billboard plane, local +Z points away from the viewer, and local +X completes
    /// the pair. Gravity in billboard space is therefore the frame's own down, and a planar
    /// constraint is the plane spanned by its X and Y.
    /// </para>
    /// <para>
    /// Every method is Burst-compatible and takes lookups directly, so a physics job can ask without
    /// a main-thread hop.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class BillboardQuery
    {
        /// <summary>
        /// The billboard frame a node inherits, resolved this frame.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One hop, not a hierarchy walk. The bake already worked out which root is a node's nearest
        /// billboarded ancestor and recorded it on the node, so this reads that answer rather than
        /// climbing the parent chain to rediscover it — which matters when the caller is asking once
        /// per body per physics step.
        /// </para>
        /// <para>
        /// The buffer scan is linear because a rig has a handful of roots and because addressing by
        /// id rather than by buffer position is what keeps a member valid when the rig gains or
        /// loses an unrelated root.
        /// </para>
        /// </remarks>
        /// <param name="member">The node's billboard membership.</param>
        /// <param name="rootElementLookup">Lookup over actor <see cref="BillboardRootElement"/> buffers.</param>
        /// <param name="frameRotation">The resolved world-space billboard frame.</param>
        /// <returns>
        /// False when the actor is gone or the root has been removed from the rig since this node
        /// was baked, in which case the caller should fall back to world space.
        /// </returns>
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
        /// A world-space direction expressed in a billboard frame — most usefully, gravity.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A ragdoll simulating in billboard space wants "down" to mean down <em>as the viewer sees
        /// it</em>, not down in the world. Passing world gravity through the frame gives exactly
        /// that, and it keeps rotating correctly as the camera orbits, because the frame does.
        /// </para>
        /// <para>
        /// The inverse rotation, not the rotation: this maps a world vector <em>into</em> the
        /// frame's local axes. Applying the frame itself would map the other way and produce a
        /// gravity that leans further from vertical the more the billboard turns.
        /// </para>
        /// </remarks>
        /// <param name="frameRotation">A frame from <see cref="TryGetFrame"/>.</param>
        /// <param name="worldDirection">The world-space direction to express, such as gravity.</param>
        /// <param name="billboardDirection">The direction in the billboard frame's local axes.</param>
        /// <remarks>
        /// The result is an <c>out</c> parameter rather than a return value because a
        /// <c>[BurstCompile]</c> static is an external entry point, and Burst can neither pass nor
        /// return a struct across one — a returned <c>float3</c> raises BC1064 and BC1067 and fails
        /// the whole Runtime assembly, not just this method.
        /// </remarks>
        [BurstCompile]
        public static void ToBillboardSpace(
            in quaternion frameRotation, in float3 worldDirection, out float3 billboardDirection)
        {
            billboardDirection = math.mul(math.inverse(frameRotation), worldDirection);
        }
    }
}
