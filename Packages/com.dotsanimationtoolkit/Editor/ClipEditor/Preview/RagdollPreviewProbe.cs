// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The editor preview's <see cref="RagdollContact"/> provider: the always-present ground plane
    /// at <see cref="RagdollPreviewScenery.GroundHeight"/>, plus whatever drop-in props
    /// <see cref="RagdollPreviewScenery"/> currently holds. Shaped exactly like
    /// <c>RagdollProbeFallbackSystem</c>'s ground pass so the preview floor agrees with the
    /// runtime's. A ramp is simply a box prop whose rotation is not identity, and each prop's plane
    /// is bounded to its own footprint (inflated by the body's diagonal) rather than infinite.
    /// </summary>
    public static class RagdollPreviewProbe
    {
        private static readonly float3 GroundNormal = new float3(0f, 1f, 0f);

        // Rebuilds contacts for this frame — every body's ground contact, plus one contact per
        // enabled prop whose footprint the body currently sits within. Called once per frame, not
        // once per fixed substep, matching RagdollProbeFallbackSystem.
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
