// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// §6.2's exact Planar2D invariant: after any number of steps, every body's position lies in
    /// the frame plane and every orientation is a pure rotation about the frame normal. Exercised
    /// against a <strong>rotated, off-origin</strong> frame — the case that would expose a bug an
    /// axis-aligned, origin-centered frame could hide (an implementation that silently assumed
    /// world Z or world-origin math would still pass an identity-frame test).
    /// </summary>
    public sealed class RagdollPlanarConstraintTests
    {
        private const float PositionTolerance = 1e-4f;
        private const float OrientationTolerance = 1e-3f;

        [Test]
        public void AfterEveryStep_AllBodiesStayExactlyInARotatedOffOriginFramePlane()
        {
            quaternion frameRotation = math.mul(quaternion.RotateY(0.7f), quaternion.RotateX(0.4f));
            RagdollSolver.ComputePlaneNormal(frameRotation, out float3 planeNormal);
            float3 planeOrigin = new float3(1f, 2f, -3f);

            RagdollSolverSettings settings = RagdollTestFactory.DefaultSettings(RagdollSpace.Planar2D);
            settings.frameRotation = frameRotation;
            settings.planeOrigin = planeOrigin;
            settings.worldGravity = new float3(0f, -9.81f, 0f);

            RagdollBodyParams rootParams = RagdollTestFactory.DefaultBodyParams(-1);
            RagdollBodyParams childParams = RagdollTestFactory.DefaultBodyParams(0);
            childParams.parentAnchorOffset = new float3(0.4f, -0.6f, 0f);
            childParams.limitMin = -1.2f;
            childParams.limitMax = 1.2f;

            NativeArray<RagdollBodyParams> paramsArray = new NativeArray<RagdollBodyParams>(2, Allocator.Temp);
            paramsArray[0] = rootParams;
            paramsArray[1] = childParams;

            NativeArray<RagdollBodyState> statesArray = new NativeArray<RagdollBodyState>(2, Allocator.Temp);
            RagdollBodyState rootState = RagdollTestFactory.DefaultBodyState(planeOrigin + new float3(0.2f, 0.1f, 0f));
            rootState.angularVelocity = math.mul(frameRotation, new float3(0f, 0f, 1f)) * 2f;
            statesArray[0] = rootState;

            RagdollBodyState childState = RagdollTestFactory.DefaultBodyState(planeOrigin + new float3(-0.5f, 0.3f, 0f));
            childState.angularVelocity = math.mul(frameRotation, new float3(0f, 0f, 1f)) * -1.5f;
            statesArray[1] = childState;

            NativeArray<RagdollContact> worldContacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

            for (int step = 0; step < 40; step++)
            {
                RagdollSolver.Step(settings, paramsArray, ref statesArray, worldContacts, out bool _);

                for (int bodyIndex = 0; bodyIndex < statesArray.Length; bodyIndex++)
                {
                    RagdollBodyState state = statesArray[bodyIndex];

                    float distanceFromPlane = math.dot(state.position - planeOrigin, planeNormal);
                    Assert.AreEqual(
                        0f, distanceFromPlane, PositionTolerance,
                        $"Body {bodyIndex} left the frame plane at step {step}.");

                    float3 bodyLocalZWorldSpace = math.mul(state.orientation, new float3(0f, 0f, 1f));
                    Assert.AreEqual(
                        1f, math.dot(bodyLocalZWorldSpace, planeNormal), OrientationTolerance,
                        $"Body {bodyIndex}'s local +Z stopped mapping onto the plane normal at step {step} -- its orientation is no longer a pure rotation about the normal.");
                }
            }

            paramsArray.Dispose();
            statesArray.Dispose();
            worldContacts.Dispose();
        }
    }
}
