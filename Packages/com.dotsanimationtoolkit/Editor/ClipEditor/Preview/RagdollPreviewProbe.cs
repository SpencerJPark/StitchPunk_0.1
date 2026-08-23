// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The editor preview's <see cref="RagdollContact"/> provider (Phase D6, spec §7.5, §8.6): the
    /// always-present ground plane at <see cref="RagdollPreviewScenery.GroundHeight"/>, plus whatever
    /// drop-in props <see cref="RagdollPreviewScenery"/> currently holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Shaped exactly like <c>RagdollProbeFallbackSystem</c>'s ground pass</strong> — same
    /// <see cref="RagdollSolver.ComputeBoxProjectedRadius"/> call, same "the reported distance
    /// accounts for the box's own extent, not just its centre" reasoning, same
    /// <c>CollidesWithWorld</c> opt-out — because the two are meant to agree about what a ground
    /// contact means; a preview whose floor behaved differently from the runtime's would make a
    /// drop that reads fine here read wrong in play.
    /// </para>
    /// <para>
    /// <strong>A prop is one plane contact, not a box collider.</strong> The runtime's own solver
    /// resolves world contacts as single non-penetration planes (spec §6.1: "resolve each
    /// <c>RagdollWorldContact</c> as a non-penetration constraint"), the same shape the ground plane
    /// already uses. A ramp is not a different <em>kind</em> of collider from a box here — spec §8.6
    /// lists "box / ramp" as two entries of one prop list precisely because both are a platform with
    /// one contact face; a ramp is simply a box prop whose rotation is not identity, so its one
    /// contact plane is not horizontal. Building a second, box-vs-box SAT path for props would be
    /// re-deriving the self-collision machinery <see cref="RagdollSolver"/> already keeps private for
    /// a feature that is explicitly test scenery, not authored geometry (spec §12 row 2 already scopes
    /// self-collision itself to box-vs-box only; a third collider shape for drop-test props is not
    /// asked for anywhere in §8.6).
    /// </para>
    /// <para>
    /// <strong>The plane is bounded to the prop's own footprint.</strong> Treating a prop as a
    /// literal infinite plane would land a body standing well clear of a small test box the instant
    /// it merely shared that box's height — which is not what "drop a rig onto a box" is meant to
    /// show. A body is tested against a prop only while its own horizontal position, measured in the
    /// prop's local axes, falls within the prop's footprint (inflated by the body's own diagonal, so
    /// a wide body does not clip through the very edge it is visibly resting on).
    /// </para>
    /// </remarks>
    public static class RagdollPreviewProbe
    {
        private static readonly float3 GroundNormal = new float3(0f, 1f, 0f);

        /// <summary>
        /// Rebuilds <paramref name="contacts"/> for this frame — every body's ground contact, plus
        /// one contact per enabled prop whose footprint the body currently sits within.
        /// </summary>
        /// <remarks>
        /// Called once per frame, not once per fixed substep, exactly as
        /// <c>RagdollProbeFallbackSystem</c> runs once before <c>RagdollSolveSystem</c>'s job
        /// consumes the same buffer across however many substeps that frame runs (spec §7.2).
        /// </remarks>
        public static void BuildContacts(
            in NativeArray<RagdollBodyParams> bodyParams,
            in NativeArray<RagdollBodyState> bodyStates,
            List<RagdollPreviewPropDefinition> props,
            float contactProbeRadius,
            ref NativeList<RagdollContact> contacts)
        {
            contacts.Clear();
            int bodyCount = bodyStates.Length;
            for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
            {
                RagdollBodyParams parameters = bodyParams[bodyIndex];
                if (!parameters.CollidesWithWorld)
                {
                    continue;
                }
                RagdollBodyState state = bodyStates[bodyIndex];

                AddGroundContact(bodyIndex, in parameters, in state, contactProbeRadius, ref contacts);

                if (props == null)
                {
                    continue;
                }
                for (int propIndex = 0; propIndex < props.Count; propIndex++)
                {
                    RagdollPreviewPropDefinition prop = props[propIndex];
                    if (prop == null || !prop.enabled)
                    {
                        continue;
                    }
                    TryAddPropContact(bodyIndex, in parameters, in state, prop, contactProbeRadius, ref contacts);
                }
            }
        }

        private static void AddGroundContact(
            int bodyIndex,
            in RagdollBodyParams parameters,
            in RagdollBodyState state,
            float contactProbeRadius,
            ref NativeList<RagdollContact> contacts)
        {
            quaternion boxWorldOrientation = math.mul(state.orientation, parameters.boxRotation);
            RagdollSolver.ComputeBoxProjectedRadius(
                in parameters.boxHalfExtents, in boxWorldOrientation, in GroundNormal, out float projectedRadius);

            float distance = state.position.y - RagdollPreviewScenery.GroundHeight
                - projectedRadius - contactProbeRadius;

            contacts.Add(new RagdollContact
            {
                bodyIndex = bodyIndex,
                point = new float3(state.position.x, RagdollPreviewScenery.GroundHeight, state.position.z),
                normal = GroundNormal,
                distance = distance,
                referencePosition = state.position,
                restitution = parameters.restitution,
                friction = parameters.friction
            });
        }

        private static void TryAddPropContact(
            int bodyIndex,
            in RagdollBodyParams parameters,
            in RagdollBodyState state,
            RagdollPreviewPropDefinition prop,
            float contactProbeRadius,
            ref NativeList<RagdollContact> contacts)
        {
            quaternion propRotation = quaternion.Euler(math.radians(prop.eulerAngles));
            quaternion inversePropRotation = math.inverse(propRotation);
            float3 halfSize = prop.size * 0.5f;

            // The body's horizontal position in the prop's own local axes, so the footprint test
            // below reads as an ordinary axis-aligned box test regardless of how the prop is rotated.
            float3 localPosition = math.mul(inversePropRotation, state.position - prop.position);

            quaternion boxWorldOrientation = math.mul(state.orientation, parameters.boxRotation);
            float3 propUpNormal = math.mul(propRotation, new float3(0f, 1f, 0f));
            RagdollSolver.ComputeBoxProjectedRadius(
                in parameters.boxHalfExtents, in boxWorldOrientation, in propUpNormal, out float projectedRadius);

            // A generous footprint slack (the body's own diagonal) rather than its projected radius:
            // the body may be tumbling, so the edge a moment ago is not necessarily the edge now, and
            // erring toward "still counts as on the platform" reads better for test scenery than a
            // body visibly resting on a corner it has technically rolled just past.
            float footprintSlack = math.length(parameters.boxHalfExtents);
            if (math.abs(localPosition.x) > halfSize.x + footprintSlack
                || math.abs(localPosition.z) > halfSize.z + footprintSlack)
            {
                return;
            }

            float3 upFaceCenter = prop.position + math.mul(propRotation, new float3(0f, halfSize.y, 0f));
            float distance = math.dot(state.position - upFaceCenter, propUpNormal)
                - projectedRadius - contactProbeRadius;

            contacts.Add(new RagdollContact
            {
                bodyIndex = bodyIndex,
                point = state.position - propUpNormal * (distance + projectedRadius),
                normal = propUpNormal,
                distance = distance,
                referencePosition = state.position,
                restitution = parameters.restitution,
                friction = parameters.friction
            });
        }
    }
}
