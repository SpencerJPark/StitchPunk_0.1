// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// §6.4's determinism guarantee: identical launches produce bit-identical state. Mirrors
    /// <c>ClipRegistryDeterminismTests</c>' shape — run the same fixed-substep simulation twice
    /// from scratch and require every float, not merely "close enough", to agree, because the
    /// whole point of a fixed substep rate and a fixed iteration count (§6.4) is that there is
    /// nothing left in the pipeline that a second run could compute differently.
    /// </summary>
    public sealed class RagdollSolverDeterminismTests
    {
        [Test]
        public void IdenticalLaunches_ProduceBitIdenticalState()
        {
            NativeArray<RagdollBodyState> firstResult = RunLaunch();
            NativeArray<RagdollBodyState> secondResult = RunLaunch();

            Assert.AreEqual(firstResult.Length, secondResult.Length);
            for (int bodyIndex = 0; bodyIndex < firstResult.Length; bodyIndex++)
            {
                RagdollBodyState first = firstResult[bodyIndex];
                RagdollBodyState second = secondResult[bodyIndex];

                Assert.IsTrue(
                    math.all(first.position == second.position),
                    $"Body {bodyIndex} position diverged between two identical launches.");
                Assert.IsTrue(
                    math.all(first.orientation.value == second.orientation.value),
                    $"Body {bodyIndex} orientation diverged between two identical launches.");
                Assert.IsTrue(
                    math.all(first.linearVelocity == second.linearVelocity),
                    $"Body {bodyIndex} linear velocity diverged between two identical launches.");
                Assert.IsTrue(
                    math.all(first.angularVelocity == second.angularVelocity),
                    $"Body {bodyIndex} angular velocity diverged between two identical launches.");
            }

            firstResult.Dispose();
            secondResult.Dispose();
        }

        /// <summary>
        /// A three-body Planar2D chain with gravity, a hinge limit, and one world contact —
        /// exercising every constraint category <see cref="RagdollSolver.Step"/> touches in a
        /// single launch, run for enough substeps to settle. Returns a freshly allocated array the
        /// caller owns and must dispose.
        /// </summary>
        private static NativeArray<RagdollBodyState> RunLaunch()
        {
            RagdollSolverSettings settings = RagdollTestFactory.DefaultSettings(RagdollSpace.Planar2D);
            settings.frameRotation = math.mul(quaternion.RotateY(0.5f), quaternion.RotateZ(0.2f));
            settings.worldGravity = new float3(0f, -9.81f, 0f);

            RagdollBodyParams rootParams = RagdollTestFactory.DefaultBodyParams(-1);

            RagdollBodyParams middleParams = RagdollTestFactory.DefaultBodyParams(0);
            middleParams.parentAnchorOffset = new float3(0f, -0.5f, 0f);
            middleParams.limitMin = -0.8f;
            middleParams.limitMax = 0.8f;

            RagdollBodyParams tipParams = RagdollTestFactory.DefaultBodyParams(1);
            tipParams.parentAnchorOffset = new float3(0f, -0.5f, 0f);
            tipParams.limitMin = -0.8f;
            tipParams.limitMax = 0.8f;

            NativeArray<RagdollBodyParams> paramsArray = new NativeArray<RagdollBodyParams>(3, Allocator.Temp);
            paramsArray[0] = rootParams;
            paramsArray[1] = middleParams;
            paramsArray[2] = tipParams;

            NativeArray<RagdollBodyState> statesArray = new NativeArray<RagdollBodyState>(3, Allocator.Temp);
            statesArray[0] = RagdollTestFactory.DefaultBodyState(new float3(0f, 3f, 0f));
            statesArray[1] = RagdollTestFactory.DefaultBodyState(new float3(0.3f, 2.4f, 0f));
            statesArray[2] = RagdollTestFactory.DefaultBodyState(new float3(-0.2f, 1.8f, 0f));

            NativeArray<RagdollContact> worldContacts = new NativeArray<RagdollContact>(1, Allocator.Temp);
            worldContacts[0] = new RagdollContact
            {
                bodyIndex = 2,
                point = new float3(-0.2f, 0f, 0f),
                normal = new float3(0f, 1f, 0f),
                distance = 0.1f,
                restitution = 0.3f,
                friction = 0.4f
            };

            for (int step = 0; step < 30; step++)
            {
                RagdollSolver.Step(settings, paramsArray, ref statesArray, worldContacts, out bool _);
            }

            NativeArray<RagdollBodyState> result = new NativeArray<RagdollBodyState>(statesArray.Length, Allocator.Temp);
            statesArray.CopyTo(result);

            paramsArray.Dispose();
            statesArray.Dispose();
            worldContacts.Dispose();

            return result;
        }
    }
}
