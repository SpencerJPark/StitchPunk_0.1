// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Drops the previewed rig as an active ragdoll (Phase D6, spec §8.4, §8.5): builds
    /// <see cref="RagdollBodyParams"/>/<see cref="RagdollBodyState"/> from the rig exactly as
    /// <c>ActorBaker</c> would, and steps them through the identical <see cref="RagdollSolver"/>
    /// entry points the runtime's <c>RagdollSolveSystem</c> job calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>No parallel solver, no parallel struct (spec §6.0).</strong> Every substep is one call
    /// to <see cref="RagdollSolver.Step"/> against the same <see cref="RagdollBodyParams"/> and
    /// <see cref="RagdollBodyState"/> arrays a baked <c>RagdollBody</c> buffer composes. A preview
    /// that redeclared any of that arithmetic would drift the first time either side gained a field —
    /// the exact failure <c>RagdollPreviewParityTests</c> exists to catch.
    /// </para>
    /// <para>
    /// <strong>Body → node resolution goes through <see cref="ClipPreviewController.ResolveRagdollNode"/>,
    /// once, here.</strong> D5 solved node → address (<c>ClipEditorWindow.BuildRagdollAddressFor</c>);
    /// this is the reverse, and <see cref="PreviewRagdollBoxHandles"/> calls the very same controller
    /// method rather than this class owning a second copy of it.
    /// </para>
    /// <para>
    /// <strong>Parentage is derived from authored <em>paths</em>, not from a live Transform walk.</strong>
    /// <c>RagdollBodyResolver</c> (D3) walks <c>Transform.parent</c> because the real actor prefab is
    /// one hierarchy. The preview is not: a <see cref="RigNodeAddressKind.RigTarget"/> body's node is
    /// a flat quad under <c>PreviewRigMirror</c>'s root, with no nesting that reflects the rig's real
    /// structure, while a <see cref="RigNodeAddressKind.Bone"/> body's node <em>does</em> sit in a
    /// real, nested tree — the skinned source instance. Rather than special-case each address kind's
    /// notion of "ancestor" separately, every resolved body is given a path key — a rig target's
    /// <c>RigTargetDefinition.sourceNodePath</c>, a <see cref="RigNodeAddressKind.HierarchyPath"/>
    /// body's own <c>hierarchyPath</c>, or (for a bone) the name path walked live from the skinned
    /// instance up to its root — and "nearest ragdolled ancestor" becomes an ordinary string-prefix
    /// question over those keys. This is provably the same answer a live walk would give whenever the
    /// keys really are paths in one tree (which is exactly what
    /// <c>RigTargetDefinition.sourceNodePath</c>'s own doc comment says it is: "the path, from the
    /// previewed prefab's root, of the node this target stands for"), and it is the only approach that
    /// answers the same question for a flat quad and a live bone alike.
    /// </para>
    /// <para>
    /// <strong>Known divergence from the baker: <c>restRelativeRotation</c>/<c>parentAnchorOffset</c>
    /// come from whatever pose is on screen when the toggle switches on, not from the rig's authored
    /// rest pose.</strong> <c>ActorBaker</c> always measures a joint's "zero" from the prefab's rest
    /// pose, baked once, forever. Reproducing that here would need a second, dedicated rest-pose
    /// sample of the skinned instance's bones (the rig-target case already has one —
    /// <c>ClipPreviewController</c>'s <c>targetRestPoses</c> — but bones do not), which is more
    /// machinery than this phase's preview loop justifies. The practical effect: a rig captured
    /// mid-animation (an elbow bent past its authored limit, say) previews its limit as measured from
    /// <em>that</em> bend rather than from rest, so a first-frame correction that would not fire in
    /// the runtime can fire here. Documented rather than silently accepted — see the Phase D6 report
    /// for the full reasoning.
    /// </para>
    /// </remarks>
    public sealed class RagdollPreviewSimulation
    {
        /// <summary>Runtime's own default (<c>ConfigBootstrapSystem</c>) — mirrored here since the editor preview has no <c>RagdollConfig</c> singleton to read.</summary>
        private const int MaxSubstepsPerFrame = 4;

        private const float ContactProbeRadius = 0.02f;
        private const float SleepLinearSpeed = 0.05f;
        private const float SleepAngularSpeed = 0.05f;
        private const float SleepDelaySeconds = 0.5f;

        private static readonly float3 WorldGravity = new float3(0f, -9.81f, 0f);

        private NativeArray<RagdollBodyParams> bodyParams;
        private NativeArray<RagdollBodyState> bodyStates;
        private NativeList<RagdollContact> contacts;
        private List<Transform> nodes = new List<Transform>();

        /// <summary>
        /// Each node's local TRS at the moment the drop started — the preview's
        /// <c>RagdollRestPose</c> (spec §5.3), and the whole of "turning it off restores the pose".
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Captured, not re-derived.</strong> The first version of this class stored nothing
        /// and assumed the caller's next <c>SamplePose</c> at the unchanged playhead time would put
        /// everything back on its own. That is true only for nodes the current clip actually drives:
        /// a bone with no bone track in this clip, or a part that has never been keyed, is never
        /// re-posed by a resample and simply stays wherever the ragdoll dropped it. Since a ragdoll
        /// is most useful on exactly the rigs where many nodes are unkeyed, the assumption failed in
        /// the common case rather than an exotic one.
        /// </para>
        /// <para>
        /// Local rather than world TRS, so restoring is a plain assignment that cannot be disturbed
        /// by the order nodes are written in — a parent restored after its child would otherwise
        /// drag the child back off its restored world pose.
        /// </para>
        /// </remarks>
        private readonly List<PreviewRestPose> restPoses = new List<PreviewRestPose>();

        /// <summary>One node's pre-drop local TRS.</summary>
        private struct PreviewRestPose
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        private bool built;
        private bool sleeping;
        private float substepAccumulator;
        private float sleepTimer;
        private float3 planeOrigin;

        /// <summary>Whether <see cref="TryBuild"/> has produced a live body array.</summary>
        public bool IsBuilt
        {
            get { return built; }
        }

        /// <summary>Whether every body has settled quiet for long enough to stop integrating.</summary>
        public bool Sleeping
        {
            get { return sleeping; }
        }

        /// <summary>How many bodies are simulating.</summary>
        public int BodyCount
        {
            get { return nodes.Count; }
        }

        /// <summary>
        /// The root body's node (buffer index 0), or null when nothing is built. Body 0 is always
        /// <em>a</em> root after parent-before-child sorting, matching <c>SolveRagdollJob</c>'s own
        /// "just use the first body" reading of the gravity frame's source node when a rig resolves
        /// more than one disconnected root (spec §9's V-R6 case).
        /// </summary>
        public Transform RootNode
        {
            get { return nodes.Count > 0 ? nodes[0] : null; }
        }

        /// <summary>
        /// Resolves the rig's ragdoll bodies against the current preview and captures their starting
        /// state from whatever pose is on screen right now (spec §8.4's "capture the current preview
        /// pose").
        /// </summary>
        /// <param name="refusalReason">
        /// Why nothing was built, for the toolbar's status line (spec §8.4's "the toggle refuses to
        /// engage, and the status line says why").
        /// </param>
        public bool TryBuild(RigAsset rig, ClipPreviewController controller, out string refusalReason)
        {
            Dispose();
            refusalReason = string.Empty;

            if (rig == null || rig.ragdollBodies == null || rig.ragdollBodies.Count == 0)
            {
                refusalReason = "This rig declares no ragdoll bodies.";
                return false;
            }

            List<string> unresolvedNames = new List<string>();
            List<ResolvedPreviewBody> resolvedBodies = ResolveBodies(rig, controller, unresolvedNames);
            if (resolvedBodies.Count == 0)
            {
                refusalReason = unresolvedNames.Count > 0
                    ? "None of this rig's ragdoll bodies resolve in the current preview: "
                        + string.Join(", ", unresolvedNames)
                    : "This rig declares no ragdoll bodies.";
                return false;
            }

            int bodyCount = resolvedBodies.Count;
            bodyParams = new NativeArray<RagdollBodyParams>(bodyCount, Allocator.Persistent);
            bodyStates = new NativeArray<RagdollBodyState>(bodyCount, Allocator.Persistent);
            contacts = new NativeList<RagdollContact>(bodyCount, Allocator.Persistent);
            nodes = new List<Transform>(bodyCount);
            restPoses.Clear();

            RagdollRigSettings rigSettings = rig.ragdollSettings;
            for (int index = 0; index < bodyCount; index++)
            {
                ResolvedPreviewBody body = resolvedBodies[index];
                RagdollBodyParams parameters = BuildBodyParams(body, rigSettings);
                bodyParams[index] = parameters;
                nodes.Add(body.node);
                restPoses.Add(new PreviewRestPose
                {
                    localPosition = body.node.localPosition,
                    localRotation = body.node.localRotation,
                    localScale = body.node.localScale
                });

                // Capture, mirroring RagdollCaptureSystem exactly (§7.1, RagdollBodyParams' own
                // remarks): the body's own centre of mass, not the node's origin.
                quaternion nodeWorldRotation = body.node.rotation;
                float3 nodeWorldPosition = body.node.position;
                bodyStates[index] = new RagdollBodyState
                {
                    position = nodeWorldPosition + math.mul(nodeWorldRotation, parameters.boxCenter),
                    orientation = nodeWorldRotation,
                    linearVelocity = float3.zero,
                    angularVelocity = float3.zero
                };
            }

            // §6.2: captured once here and never revisited — see RagdollState.planeOrigin's remarks.
            planeOrigin = bodyStates[0].position - math.mul(bodyStates[0].orientation, bodyParams[0].boxCenter);

            substepAccumulator = 0f;
            sleepTimer = 0f;
            sleeping = false;
            built = true;
            return true;
        }

        /// <summary>
        /// Advances the fixed-step accumulator by <paramref name="realDeltaTime"/> and writes
        /// whatever whole substeps that buys onto the resolved nodes.
        /// </summary>
        /// <param name="frameRotation">
        /// This step's gravity frame (spec §6.2) — identity for <see cref="RagdollSpace.Spatial3D"/>
        /// or when the rig declares no billboard root. The caller resolves this, never this class:
        /// see <c>ClipPreviewController.ApplyRagdollPreview</c> for why reading the billboard root's
        /// already-written world rotation is the right source rather than a second resolve.
        /// </param>
        public void Step(
            RigAsset rig, in quaternion frameRotation, List<RagdollPreviewPropDefinition> props,
            float realDeltaTime)
        {
            if (!built || rig == null)
            {
                return;
            }

            RagdollRigSettings rigSettings = rig.ragdollSettings;
            float substepDeltaTime = rigSettings.substepHz > 0f ? 1f / rigSettings.substepHz : 1f / 120f;
            float maxAccumulator = substepDeltaTime * MaxSubstepsPerFrame;
            substepAccumulator = math.min(substepAccumulator + math.max(realDeltaTime, 0f), maxAccumulator);

            int stepsToRun = (int)math.floor(substepAccumulator / substepDeltaTime);
            if (stepsToRun <= 0)
            {
                WriteToTransforms();
                return;
            }

            if (sleeping)
            {
                // §9 G1's preview counterpart: a sleeping ragdoll still owns its nodes every tick
                // (WriteToTransforms below never stops running); it simply stops integrating.
                substepAccumulator -= stepsToRun * substepDeltaTime;
                WriteToTransforms();
                return;
            }

            RagdollPreviewProbe.BuildContacts(in bodyParams, in bodyStates, props, ContactProbeRadius, ref contacts);

            RagdollSolverSettings settings = new RagdollSolverSettings
            {
                space = rigSettings.space,
                worldGravity = WorldGravity,
                planeOrigin = planeOrigin,
                gravityScale = rigSettings.gravityScale,
                frameRotation = frameRotation,
                solverIterations = rigSettings.solverIterations > 0 ? rigSettings.solverIterations : (byte)6,
                substepDeltaTime = substepDeltaTime,
                jointStiffness = rigSettings.jointStiffness,
                jointDamping = rigSettings.jointDamping,
                sleepLinearSpeed = SleepLinearSpeed,
                sleepAngularSpeed = SleepAngularSpeed
            };

            NativeArray<RagdollContact> contactsView = contacts.AsArray();
            bool belowSleepThreshold = true;
            for (int step = 0; step < stepsToRun; step++)
            {
                RagdollSolver.Step(
                    in settings, in bodyParams, ref bodyStates, in contactsView, out belowSleepThreshold);
                substepAccumulator -= substepDeltaTime;
            }

            float substepTimeRun = stepsToRun * substepDeltaTime;
            if (belowSleepThreshold)
            {
                sleepTimer += substepTimeRun;
                if (sleepTimer >= SleepDelaySeconds)
                {
                    sleeping = true;
                }
            }
            else
            {
                sleepTimer = 0f;
            }

            WriteToTransforms();
        }

        /// <summary>Inverts <see cref="RagdollBodyParams.boxCenter"/> to recover each node's world pose, exactly as <c>RagdollApplySystem</c> does.</summary>
        private void WriteToTransforms()
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                Transform node = nodes[index];
                if (node == null)
                {
                    continue;
                }
                RagdollBodyState state = bodyStates[index];
                RagdollBodyParams parameters = bodyParams[index];
                node.rotation = state.orientation;
                node.position = state.position - math.mul(state.orientation, parameters.boxCenter);
            }
        }

        /// <summary>Releases the native arrays. Idempotent, and safe to call before <see cref="TryBuild"/> ever succeeds.</summary>
        /// <summary>
        /// Puts every node back where it stood when the drop started (spec §7.4's preview half).
        /// </summary>
        /// <remarks>
        /// Safe to call when nothing was built, and safe to call twice — it clears the captured
        /// poses as it applies them, so a second call restores nothing rather than re-applying a
        /// stale pose over whatever the user has done since.
        /// </remarks>
        public void RestoreCapturedPose()
        {
            int restorableCount = nodes.Count < restPoses.Count ? nodes.Count : restPoses.Count;
            for (int index = 0; index < restorableCount; index++)
            {
                Transform node = nodes[index];
                if (node == null)
                {
                    continue;
                }
                PreviewRestPose restPose = restPoses[index];
                node.localPosition = restPose.localPosition;
                node.localRotation = restPose.localRotation;
                node.localScale = restPose.localScale;
            }
            restPoses.Clear();
        }

        public void Dispose()
        {
            if (bodyParams.IsCreated)
            {
                bodyParams.Dispose();
            }
            if (bodyStates.IsCreated)
            {
                bodyStates.Dispose();
            }
            if (contacts.IsCreated)
            {
                contacts.Dispose();
            }
            nodes.Clear();
            restPoses.Clear();
            built = false;
            sleeping = false;
            substepAccumulator = 0f;
            sleepTimer = 0f;
        }

        // -----------------------------------------------------------------------------------
        // Resolution: address -> node, node -> path key, path key -> parentage. See the type
        // remarks for why parentage is derived from path keys rather than a live Transform walk.
        // -----------------------------------------------------------------------------------

        private struct ResolvedPreviewBody
        {
            public Transform node;
            public RagdollBodyDefinition definition;
            public string pathKey;
            public int depth;
            public int parentBodyIndex;
            public quaternion restRelativeRotation;
            public float3 parentAnchorOffset;
        }

        private static List<ResolvedPreviewBody> ResolveBodies(
            RigAsset rig, ClipPreviewController controller, List<string> unresolvedNames)
        {
            List<ResolvedPreviewBody> resolvedBodies = new List<ResolvedPreviewBody>();
            Transform skeletonRoot = controller.HierarchyRoot;

            for (int index = 0; index < rig.ragdollBodies.Count; index++)
            {
                RagdollBodyDefinition definition = rig.ragdollBodies[index];
                if (definition == null)
                {
                    continue;
                }

                Transform node = controller.ResolveRagdollNode(definition.address);
                if (node == null)
                {
                    unresolvedNames.Add(DescribeBody(definition));
                    continue;
                }

                string pathKey = ComputePathKey(definition.address, rig, node, skeletonRoot);
                if (pathKey == null)
                {
                    unresolvedNames.Add(DescribeBody(definition) + " (no hierarchy to place it in)");
                    continue;
                }

                resolvedBodies.Add(new ResolvedPreviewBody
                {
                    node = node,
                    definition = definition,
                    pathKey = pathKey,
                    depth = SegmentCount(pathKey),
                    parentBodyIndex = -1,
                    restRelativeRotation = quaternion.identity,
                    parentAnchorOffset = float3.zero
                });
            }

            SortByDepth(resolvedBodies);
            ResolveParentage(resolvedBodies);
            return resolvedBodies;
        }

        /// <summary>
        /// A body's path key: what its "nearest ragdolled ancestor" is measured against. See the
        /// type remarks for why this is a string rather than a live Transform walk.
        /// </summary>
        private static string ComputePathKey(
            in RigNodeAddress address, RigAsset rig, Transform node, Transform skeletonRoot)
        {
            switch (address.kind)
            {
                case RigNodeAddressKind.RigTarget:
                {
                    RigTargetDefinition target = FindTarget(rig, address.targetId);
                    return target != null ? (target.sourceNodePath ?? string.Empty) : null;
                }

                case RigNodeAddressKind.HierarchyPath:
                    return address.hierarchyPath ?? string.Empty;

                default: // Bone
                    return skeletonRoot == null ? null : BuildPathFromRoot(node, skeletonRoot);
            }
        }

        private static RigTargetDefinition FindTarget(RigAsset rig, uint targetId)
        {
            if (rig.targets == null)
            {
                return null;
            }
            for (int index = 0; index < rig.targets.Count; index++)
            {
                RigTargetDefinition target = rig.targets[index];
                if (target != null && target.Id.Value == targetId)
                {
                    return target;
                }
            }
            return null;
        }

        /// <summary>A '/'-joined name path from <paramref name="root"/> down to <paramref name="node"/>, matching <c>Transform.Find</c>'s own convention.</summary>
        private static string BuildPathFromRoot(Transform node, Transform root)
        {
            if (node == root)
            {
                return string.Empty;
            }
            List<string> segments = new List<string>();
            Transform walker = node;
            while (walker != null && walker != root)
            {
                segments.Add(walker.name);
                walker = walker.parent;
            }
            if (walker != root)
            {
                return null;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static int SegmentCount(string pathKey)
        {
            return string.IsNullOrEmpty(pathKey) ? 0 : pathKey.Split('/').Length;
        }

        /// <summary>Insertion sort by depth, ascending and stable — matching <c>RagdollBodyResolver.SortByDepth</c>'s own reasoning exactly.</summary>
        private static void SortByDepth(List<ResolvedPreviewBody> resolvedBodies)
        {
            for (int index = 1; index < resolvedBodies.Count; index++)
            {
                ResolvedPreviewBody current = resolvedBodies[index];
                int scan = index - 1;
                while (scan >= 0 && resolvedBodies[scan].depth > current.depth)
                {
                    resolvedBodies[scan + 1] = resolvedBodies[scan];
                    scan--;
                }
                resolvedBodies[scan + 1] = current;
            }
        }

        private static void ResolveParentage(List<ResolvedPreviewBody> resolvedBodies)
        {
            for (int index = 0; index < resolvedBodies.Count; index++)
            {
                ResolvedPreviewBody body = resolvedBodies[index];
                int parentIndex = FindNearestAncestorIndex(resolvedBodies, body.pathKey, index);
                body.parentBodyIndex = parentIndex;

                if (parentIndex >= 0)
                {
                    ResolvedPreviewBody parentBody = resolvedBodies[parentIndex];
                    ComputeRestRelation(
                        body.node, parentBody.node, parentBody.definition.boxCenter,
                        out body.restRelativeRotation, out body.parentAnchorOffset);
                }

                resolvedBodies[index] = body;
            }
        }

        private static int FindNearestAncestorIndex(
            List<ResolvedPreviewBody> resolvedBodies, string pathKey, int selfIndex)
        {
            int bestIndex = -1;
            int bestDepth = -1;
            for (int candidateIndex = 0; candidateIndex < resolvedBodies.Count; candidateIndex++)
            {
                if (candidateIndex == selfIndex)
                {
                    continue;
                }
                string candidatePath = resolvedBodies[candidateIndex].pathKey;
                if (!IsAncestorPath(candidatePath, pathKey))
                {
                    continue;
                }
                int candidateDepth = resolvedBodies[candidateIndex].depth;
                if (candidateDepth > bestDepth)
                {
                    bestDepth = candidateDepth;
                    bestIndex = candidateIndex;
                }
            }
            return bestIndex;
        }

        /// <summary>Whether <paramref name="candidateAncestorPath"/> is a strict prefix segment of <paramref name="nodePath"/>.</summary>
        private static bool IsAncestorPath(string candidateAncestorPath, string nodePath)
        {
            if (candidateAncestorPath.Length == 0)
            {
                return nodePath.Length > 0;
            }
            return nodePath.Length > candidateAncestorPath.Length
                && nodePath.StartsWith(candidateAncestorPath, StringComparison.Ordinal)
                && nodePath[candidateAncestorPath.Length] == '/';
        }

        /// <summary>
        /// The child's orientation relative to its parent, and the joint anchor as an offset from
        /// the parent's centre of mass in the parent's own axes — <c>RagdollBodyResolver.ComputeRestRelation</c>'s
        /// formula exactly, using world transforms in place of an actor-relative local-matrix walk.
        /// Provably equivalent here: both <c>PreviewRigMirror</c> and <c>PreviewSkeletonMirror</c>
        /// plant their root at world origin with identity rotation (see their own remarks), so a
        /// resolved node's world pose <em>is</em> its actor-space pose.
        /// </summary>
        private static void ComputeRestRelation(
            Transform childNode, Transform parentNode, float3 parentBoxCenter,
            out quaternion restRelativeRotation, out float3 parentAnchorOffset)
        {
            quaternion parentRotation = parentNode.rotation;
            quaternion childRotation = childNode.rotation;
            quaternion inverseParentRotation = math.inverse(parentRotation);
            restRelativeRotation = math.mul(inverseParentRotation, childRotation);

            float3 parentPosition = parentNode.position;
            float3 childPosition = childNode.position;
            float3 parentCenterOfMass = parentPosition + math.mul(parentRotation, parentBoxCenter);
            float3 jointOffsetFromParentCenter = childPosition - parentCenterOfMass;
            parentAnchorOffset = math.mul(inverseParentRotation, jointOffsetFromParentCenter);
        }

        /// <summary>Converts one resolved body into its baked configuration — <c>ActorBaker.BuildBodyParams</c>'s conversion exactly, through the same shared solver functions.</summary>
        private static RagdollBodyParams BuildBodyParams(ResolvedPreviewBody body, RagdollRigSettings rigSettings)
        {
            RagdollBodyDefinition definition = body.definition;
            float3 boxHalfExtents = math.max(definition.boxSize, float3.zero) * 0.5f;

            RagdollSolver.ComputeBoxInverseInertia(
                definition.mass, in boxHalfExtents, out float invMass, out float3 invInertiaDiagonal);
            RagdollSolver.ResolveDampingSentinel(
                definition.linearDamping, rigSettings.defaultLinearDamping, out float resolvedLinearDamping);
            RagdollSolver.ResolveDampingSentinel(
                definition.angularDamping, rigSettings.defaultAngularDamping, out float resolvedAngularDamping);

            bool isRoot = body.parentBodyIndex < 0;
            RagdollBodyFlags flags = RagdollBodyFlags.None;
            if (definition.collidesWithWorld)
            {
                flags |= RagdollBodyFlags.CollidesWithWorld;
            }
            if (isRoot)
            {
                flags |= RagdollBodyFlags.IsRoot;
            }

            return new RagdollBodyParams
            {
                boxCenter = definition.boxCenter,
                boxHalfExtents = boxHalfExtents,
                boxRotation = quaternion.Euler(math.radians(definition.boxEulerAngles)),
                invMass = invMass,
                invInertiaDiagonal = invInertiaDiagonal,
                linearDamping = resolvedLinearDamping,
                angularDamping = resolvedAngularDamping,
                restitution = definition.restitution,
                friction = definition.friction,
                limitMin = math.radians(definition.limitMinDegrees),
                limitMax = math.radians(definition.limitMaxDegrees),
                swingLimit = math.radians(definition.swingLimitDegrees),
                twistLimit = math.radians(definition.twistLimitDegrees),
                restRelativeRotation = body.restRelativeRotation,
                parentAnchorOffset = body.parentAnchorOffset,
                parentBodyIndex = body.parentBodyIndex,
                selfGroup = definition.selfGroup,
                selfCollidesWith = definition.selfCollidesWith,
                flags = flags
            };
        }

        private static string DescribeBody(RagdollBodyDefinition definition)
        {
            return string.IsNullOrEmpty(definition.displayName) ? "(unnamed)" : definition.displayName;
        }
    }
}
