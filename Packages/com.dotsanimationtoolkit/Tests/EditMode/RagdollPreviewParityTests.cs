// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The Clip Editor's ragdoll preview and the runtime's <c>RagdollSolveSystem</c> must produce
    /// the same state from the same input (Phase D6, spec §10) — the obligation
    /// <c>SocketPreviewParityTests</c> and <c>BillboardPreviewParityTests</c> already establish for
    /// their own features, made for the ragdoll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>What this can and cannot check.</strong> Both paths step through the literal same
    /// <see cref="RagdollSolver.Step"/> — spec §6.0's whole point — so they cannot disagree about
    /// the XPBD arithmetic itself; nothing here re-derives that. What they <em>could</em> disagree
    /// about is the plumbing on either side of it: <c>RagdollPreviewSimulation</c> captures and
    /// applies state against a live <see cref="Transform"/> hierarchy and lets Unity do the
    /// parent-space conversion, while <c>RagdollCaptureSystem</c>/<c>RagdollApplySystem</c> do that
    /// conversion by hand against baked local transforms and a parent lookup. This fixture drives
    /// both mechanisms from the same starting pose and the same <see cref="RagdollBodyParams"/>,
    /// and asserts they land on the same world pose after identical fixed-step accumulation —
    /// exactly the shape <c>BillboardPreviewParityTests</c> uses for its own two write mechanisms.
    /// </para>
    /// <para>
    /// <strong>A two-body chain, not one.</strong> A single root body never exercises the joint or
    /// limit constraints, which is where a parent-chain conversion mistake is most likely to hide
    /// (a wrong sign compounds through the joint pin every iteration). Root + child is the smallest
    /// rig that puts <see cref="RagdollBodyParams.parentAnchorOffset"/> and
    /// <see cref="RagdollBodyParams.restRelativeRotation"/> in play at all.
    /// </para>
    /// </remarks>
    public sealed class RagdollPreviewParityTests
    {
        private const float Tolerance = 5e-4f;

        private static RagdollBodyParams[] BuildBodyParams()
        {
            RagdollBodyParams rootParams = new RagdollBodyParams
            {
                boxCenter = float3.zero,
                boxHalfExtents = new float3(0.25f, 0.35f, 0.2f),
                boxRotation = quaternion.identity,
                linearDamping = 0.05f,
                angularDamping = 0.05f,
                restitution = 0.1f,
                friction = 0.6f,
                limitMin = math.radians(-45f),
                limitMax = math.radians(45f),
                swingLimit = math.radians(45f),
                twistLimit = math.radians(45f),
                restRelativeRotation = quaternion.identity,
                parentAnchorOffset = float3.zero,
                parentBodyIndex = -1,
                selfGroup = 0,
                selfCollidesWith = 0xFF,
                flags = RagdollBodyFlags.CollidesWithWorld | RagdollBodyFlags.IsRoot
            };
            RagdollSolver.ComputeBoxInverseInertia(
                2f, in rootParams.boxHalfExtents, out rootParams.invMass, out rootParams.invInertiaDiagonal);

            // The joint anchor and rest relation a real bake would derive from the child node
            // sitting 0.4 units below the root's own centre, unrotated at rest — an arbitrary but
            // ordinary "limb hanging off a torso" shape.
            RagdollBodyParams childParams = new RagdollBodyParams
            {
                boxCenter = float3.zero,
                boxHalfExtents = new float3(0.15f, 0.3f, 0.15f),
                boxRotation = quaternion.identity,
                linearDamping = 0.05f,
                angularDamping = 0.05f,
                restitution = 0.1f,
                friction = 0.6f,
                limitMin = math.radians(-60f),
                limitMax = math.radians(60f),
                swingLimit = math.radians(60f),
                twistLimit = math.radians(60f),
                restRelativeRotation = quaternion.identity,
                parentAnchorOffset = new float3(0f, -0.4f, 0f),
                parentBodyIndex = 0,
                selfGroup = 1,
                selfCollidesWith = 0xFF,
                flags = RagdollBodyFlags.CollidesWithWorld
            };
            RagdollSolver.ComputeBoxInverseInertia(
                1f, in childParams.boxHalfExtents, out childParams.invMass, out childParams.invInertiaDiagonal);

            return new RagdollBodyParams[] { rootParams, childParams };
        }

        private static RagdollSolverSettings BuildSettings(float substepDeltaTime)
        {
            return new RagdollSolverSettings
            {
                space = RagdollSpace.Planar2D,
                worldGravity = new float3(0f, -9.81f, 0f),
                planeOrigin = new float3(0f, 1.4f, 0f),
                gravityScale = 1f,
                frameRotation = quaternion.identity,
                solverIterations = 6,
                substepDeltaTime = substepDeltaTime,
                jointStiffness = 1f,
                jointDamping = 0.5f,
                sleepLinearSpeed = 0.05f,
                sleepAngularSpeed = 0.05f
            };
        }

        // -----------------------------------------------------------------------------------
        // The preview's mechanism: a live Transform hierarchy, captured and applied through
        // world-space reads/writes with Unity performing the parent-space conversion — the exact
        // shape RagdollPreviewSimulation uses.
        // -----------------------------------------------------------------------------------

        private static void SimulateThePreviewWay(
            RagdollBodyParams[] bodyParams, RagdollSolverSettings settings, int stepCount,
            out float3 rootWorldPosition, out quaternion rootWorldRotation,
            out float3 childWorldPosition, out quaternion childWorldRotation)
        {
            GameObject rootObject = new GameObject("PreviewRoot");
            GameObject childObject = new GameObject("PreviewChild");
            try
            {
                childObject.transform.SetParent(rootObject.transform, false);
                rootObject.transform.SetPositionAndRotation(new Vector3(0f, 1.4f, 0f), Quaternion.identity);
                childObject.transform.SetPositionAndRotation(new Vector3(0f, 1.0f, 0f), Quaternion.identity);

                NativeArray<RagdollBodyParams> paramsArray =
                    new NativeArray<RagdollBodyParams>(bodyParams, Allocator.Temp);
                NativeArray<RagdollBodyState> stateArray = new NativeArray<RagdollBodyState>(2, Allocator.Temp);
                NativeArray<RagdollContact> contacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

                Transform[] nodes = new Transform[] { rootObject.transform, childObject.transform };
                for (int index = 0; index < nodes.Length; index++)
                {
                    quaternion nodeWorldRotation = nodes[index].rotation;
                    float3 nodeWorldPosition = nodes[index].position;
                    stateArray[index] = new RagdollBodyState
                    {
                        position = nodeWorldPosition + math.mul(nodeWorldRotation, paramsArray[index].boxCenter),
                        orientation = nodeWorldRotation,
                        linearVelocity = float3.zero,
                        angularVelocity = float3.zero
                    };
                }

                for (int step = 0; step < stepCount; step++)
                {
                    RagdollSolver.Step(
                        in settings, in paramsArray, ref stateArray, in contacts, out bool belowSleepThreshold);
                }

                for (int index = 0; index < nodes.Length; index++)
                {
                    RagdollBodyState state = stateArray[index];
                    RagdollBodyParams parameters = paramsArray[index];
                    nodes[index].rotation = state.orientation;
                    nodes[index].position = state.position - math.mul(state.orientation, parameters.boxCenter);
                }

                rootWorldPosition = rootObject.transform.position;
                rootWorldRotation = rootObject.transform.rotation;
                childWorldPosition = childObject.transform.position;
                childWorldRotation = childObject.transform.rotation;

                paramsArray.Dispose();
                stateArray.Dispose();
                contacts.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        // -----------------------------------------------------------------------------------
        // The runtime's mechanism: local transforms and a manual parent-chain composition, with no
        // Transform and no World — the exact shape RagdollCaptureSystem/RagdollApplySystem use
        // against LocalTransform and a ComponentLookup.
        // -----------------------------------------------------------------------------------

        private static void SimulateTheRuntimeWay(
            RagdollBodyParams[] bodyParams, RagdollSolverSettings settings, int stepCount,
            out float3 rootWorldPosition, out quaternion rootWorldRotation,
            out float3 childWorldPosition, out quaternion childWorldRotation)
        {
            // Baked local transforms, matching the Transform-based scene above exactly: the root at
            // (0, 1.4, 0) and the child 0.4 units below it in the root's local space.
            float3 rootLocalPosition = new float3(0f, 1.4f, 0f);
            quaternion rootLocalRotation = quaternion.identity;
            float3 childLocalPosition = new float3(0f, -0.4f, 0f);
            quaternion childLocalRotation = quaternion.identity;

            float3 rootStartWorldPosition = rootLocalPosition;
            quaternion rootStartWorldRotation = rootLocalRotation;
            float3 childStartWorldPosition =
                rootStartWorldPosition + math.mul(rootStartWorldRotation, childLocalPosition);
            quaternion childStartWorldRotation = math.mul(rootStartWorldRotation, childLocalRotation);

            NativeArray<RagdollBodyParams> paramsArray =
                new NativeArray<RagdollBodyParams>(bodyParams, Allocator.Temp);
            NativeArray<RagdollBodyState> stateArray = new NativeArray<RagdollBodyState>(2, Allocator.Temp);
            NativeArray<RagdollContact> contacts = new NativeArray<RagdollContact>(0, Allocator.Temp);

            stateArray[0] = new RagdollBodyState
            {
                position = rootStartWorldPosition + math.mul(rootStartWorldRotation, paramsArray[0].boxCenter),
                orientation = rootStartWorldRotation,
                linearVelocity = float3.zero,
                angularVelocity = float3.zero
            };
            stateArray[1] = new RagdollBodyState
            {
                position = childStartWorldPosition + math.mul(childStartWorldRotation, paramsArray[1].boxCenter),
                orientation = childStartWorldRotation,
                linearVelocity = float3.zero,
                angularVelocity = float3.zero
            };

            for (int step = 0; step < stepCount; step++)
            {
                RagdollSolver.Step(
                    in settings, in paramsArray, ref stateArray, in contacts, out bool belowSleepThreshold);
            }

            // RagdollApplySystem's own inversion: recover each node's world pose, then convert
            // world -> local against the parent's *current* world transform (the root's, freshly
            // solved, for the child) rather than the pose it started the frame at.
            float3 rootNodeWorldPosition =
                stateArray[0].position - math.mul(stateArray[0].orientation, paramsArray[0].boxCenter);
            quaternion rootNodeWorldRotation = stateArray[0].orientation;

            float3 childNodeWorldPosition =
                stateArray[1].position - math.mul(stateArray[1].orientation, paramsArray[1].boxCenter);
            quaternion childNodeWorldRotation = stateArray[1].orientation;

            quaternion inverseRootRotation = math.inverse(rootNodeWorldRotation);
            childLocalPosition = math.mul(inverseRootRotation, childNodeWorldPosition - rootNodeWorldPosition);
            childLocalRotation = math.mul(inverseRootRotation, childNodeWorldRotation);
            rootLocalPosition = rootNodeWorldPosition;
            rootLocalRotation = rootNodeWorldRotation;

            paramsArray.Dispose();
            stateArray.Dispose();
            contacts.Dispose();

            // Re-derive the final world pose from the baked locals, the way any later reader (or
            // this assertion) would.
            rootWorldPosition = rootLocalPosition;
            rootWorldRotation = rootLocalRotation;
            childWorldPosition = rootLocalPosition + math.mul(rootLocalRotation, childLocalPosition);
            childWorldRotation = math.mul(rootLocalRotation, childLocalRotation);
        }

        [Test]
        public void CaptureAndStepAndApply_AgreeBetweenTransformBasedAndLocalBasedMechanisms()
        {
            RagdollBodyParams[] bodyParams = BuildBodyParams();
            RagdollSolverSettings settings = BuildSettings(1f / 120f);

            // A few frames' worth of substeps: enough for the joint and limit constraints to have
            // done real work (a single substep leaves gravity barely integrated), short enough that
            // any drift between the two mechanisms has not yet been swamped by the child settling
            // against its own limit.
            const int stepCount = 30;

            float3 previewRootPosition;
            quaternion previewRootRotation;
            float3 previewChildPosition;
            quaternion previewChildRotation;
            SimulateThePreviewWay(
                bodyParams, settings, stepCount,
                out previewRootPosition, out previewRootRotation,
                out previewChildPosition, out previewChildRotation);

            float3 runtimeRootPosition;
            quaternion runtimeRootRotation;
            float3 runtimeChildPosition;
            quaternion runtimeChildRotation;
            SimulateTheRuntimeWay(
                bodyParams, settings, stepCount,
                out runtimeRootPosition, out runtimeRootRotation,
                out runtimeChildPosition, out runtimeChildRotation);

            AssertPositionsEqual(previewRootPosition, runtimeRootPosition, "root position");
            AssertRotationsEqual(previewRootRotation, runtimeRootRotation, "root rotation");
            AssertPositionsEqual(previewChildPosition, runtimeChildPosition, "child position");
            AssertRotationsEqual(previewChildRotation, runtimeChildRotation, "child rotation");

            // And the whole point of dropping something: it must actually have fallen, or this
            // fixture would pass just as happily comparing two mechanisms that both do nothing.
            Assert.Less(
                previewRootPosition.y, 1.4f - 0.01f,
                "The root must have fallen at all for this comparison to mean anything.");
        }

        private static void AssertPositionsEqual(float3 expected, float3 actual, string because)
        {
            Assert.Less(
                math.length(expected - actual), Tolerance,
                "Preview and runtime mechanisms must land on the same " + because + ".");
        }

        private static void AssertRotationsEqual(quaternion expected, quaternion actual, string because)
        {
            float4 expectedValue = math.normalize(expected.value);
            float4 actualValue = math.normalize(actual.value);
            if (math.dot(expectedValue, actualValue) < 0f)
            {
                actualValue = -actualValue;
            }
            Assert.Less(
                math.length(expectedValue - actualValue), Tolerance,
                "Preview and runtime mechanisms must land on the same " + because + ".");
        }
    }
}
