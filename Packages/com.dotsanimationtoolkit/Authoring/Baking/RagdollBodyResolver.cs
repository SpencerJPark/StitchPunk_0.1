// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// One ragdoll body matched to the transform it is welded to, plus the rest-hierarchy facts
    /// <see cref="ActorBaker"/> needs to finish a <see cref="RagdollBodyParams"/> (Phase D,
    /// amendment A50): which other resolved body is its implied parent, and the joint geometry
    /// measured between the two at rest.
    /// </summary>
    internal struct ResolvedRagdollBody
    {
        /// <summary>The transform this body is welded to.</summary>
        public Transform node;

        /// <summary>The rig row this came from.</summary>
        public RagdollBodyDefinition definition;

        /// <summary>How far below the actor root the node sits; the actor root itself is 0.</summary>
        public int depth;

        /// <summary>Index, into the same resolved list, of this body's nearest ragdolled ancestor; −1 for a root.</summary>
        public int parentBodyIndex;

        /// <summary>The child's orientation relative to its parent at rest. Meaningless (identity) on a root.</summary>
        public quaternion restRelativeRotation;

        /// <summary>The joint anchor, as an offset from the parent's centre of mass in the parent's rest-local axes. Meaningless (zero) on a root.</summary>
        public float3 parentAnchorOffset;
    }

    /// <summary>
    /// Matches a rig's authored ragdoll bodies to the transforms of an actor's prefab hierarchy, and
    /// answers each body's implied parent and rest-pose joint geometry (Phase D, amendment A50).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Built the same way as <see cref="BillboardRootResolver"/>, on purpose.</strong>
    /// Nearest-ancestor parenting and depth ordering are the same ordinary tree questions billboard
    /// inheritance already answers, so this follows that file's shape closely enough that a reader
    /// of one recognises the other: a resolve pass an EditMode fixture can exercise with a handful
    /// of <c>GameObject</c>s, kept out of <see cref="ActorBaker"/> so the interesting part is not
    /// tangled up with baking.
    /// </para>
    /// <para>
    /// <strong>A <see cref="RigNodeAddressKind.Bone"/> address is never resolved here.</strong> This
    /// is not a defect this resolver works around — it is spec §5's D1 finding stated in code: a VAT
    /// actor's runtime prefab carries no bone <c>GameObject</c>s at all (the skeleton the VAT bake
    /// sampled lived on a separate source rig, and <c>rigged-characters.md</c> is explicit that the
    /// baked actor has "no GameObject bone hierarchy"). A skinned-bone ragdoll body is legal
    /// authoring data — it is what makes editor preview complete for a rigged character — but it can
    /// never name a transform under <paramref name="actorRoot"/>, so <see cref="Resolve"/> routes it
    /// to <paramref name="boneOnlyBodies"/> rather than treating it as the same kind of failure a
    /// broken <see cref="RigNodeAddressKind.RigTarget"/> or <see cref="RigNodeAddressKind.HierarchyPath"/>
    /// address is. Reported, never silently dropped, but reported as the documented limitation it
    /// is rather than as an authoring mistake — see <see cref="ActorBaker"/>'s caller for the exact
    /// wording.
    /// </para>
    /// </remarks>
    internal static class RagdollBodyResolver
    {
        /// <summary>
        /// Matches every ragdoll body the rig declares to a transform under
        /// <paramref name="actorRoot"/>, sorted shallowest first, with each body's implied parent and
        /// rest-pose joint geometry filled in.
        /// </summary>
        /// <param name="rig">The rig whose bodies are being resolved. Null yields nothing.</param>
        /// <param name="actorRoot">The actor's transform; addresses are relative to it.</param>
        /// <param name="unresolvedBodies">
        /// Receives a description of every <see cref="RigNodeAddressKind.RigTarget"/> or
        /// <see cref="RigNodeAddressKind.HierarchyPath"/> body whose address matched no transform —
        /// a genuine authoring mistake (rule V-R1/V26's runtime half).
        /// </param>
        /// <param name="boneOnlyBodies">
        /// Receives a description of every <see cref="RigNodeAddressKind.Bone"/> body — legal
        /// authoring data that never resolves against a VAT actor's runtime prefab (see the type
        /// remarks).
        /// </param>
        /// <returns>The matched, runtime-simulatable bodies, shallowest first.</returns>
        public static List<ResolvedRagdollBody> Resolve(
            RigAsset rig,
            Transform actorRoot,
            List<string> unresolvedBodies,
            List<string> boneOnlyBodies)
        {
            List<ResolvedRagdollBody> resolvedBodies = new List<ResolvedRagdollBody>();
            if (rig == null || rig.ragdollBodies == null || actorRoot == null)
            {
                return resolvedBodies;
            }

            for (int bodyIndex = 0; bodyIndex < rig.ragdollBodies.Count; bodyIndex++)
            {
                RagdollBodyDefinition definition = rig.ragdollBodies[bodyIndex];
                if (definition == null)
                {
                    continue;
                }

                if (definition.address.kind == RigNodeAddressKind.Bone)
                {
                    if (boneOnlyBodies != null)
                    {
                        boneOnlyBodies.Add(DescribeBody(definition));
                    }
                    continue;
                }

                Transform node = BillboardRootResolver.FindNode(definition.address, actorRoot);
                if (node == null)
                {
                    if (unresolvedBodies != null)
                    {
                        unresolvedBodies.Add(DescribeBody(definition));
                    }
                    continue;
                }

                resolvedBodies.Add(new ResolvedRagdollBody
                {
                    node = node,
                    definition = definition,
                    depth = DepthBelow(node, actorRoot),
                    parentBodyIndex = -1,
                    restRelativeRotation = quaternion.identity,
                    parentAnchorOffset = float3.zero
                });
            }

            SortByDepth(resolvedBodies);
            ResolveParentage(resolvedBodies, actorRoot);
            return resolvedBodies;
        }

        /// <summary>Renders an address for a diagnostic message.</summary>
        public static string DescribeBody(RagdollBodyDefinition definition)
        {
            string label = string.IsNullOrEmpty(definition.displayName)
                ? "(unnamed)"
                : definition.displayName;
            switch (definition.address.kind)
            {
                case RigNodeAddressKind.RigTarget:
                    return label + " -> target id " + definition.address.targetId.ToString();
                case RigNodeAddressKind.Bone:
                    return label + " -> bone '" + definition.address.boneName + "'";
                default:
                    return label + " -> path '" + definition.address.hierarchyPath + "'";
            }
        }

        // -----------------------------------------------------------------------------------
        // Parentage: each body's nearest ragdolled ancestor, and the rest-pose joint geometry
        // measured against it.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Fills every resolved body's <c>parentBodyIndex</c>, <c>restRelativeRotation</c> and
        /// <c>parentAnchorOffset</c> in place.
        /// </summary>
        /// <remarks>
        /// Safe to search only among bodies already placed at a lower list index than the one being
        /// resolved: <paramref name="resolvedBodies"/> is depth-sorted first, two bodies at the same
        /// depth cannot be ancestors of one another (the same invariant
        /// <see cref="BillboardRootResolver"/> relies on), and a body's parent — being strictly
        /// shallower — has therefore always already been placed.
        /// </remarks>
        private static void ResolveParentage(List<ResolvedRagdollBody> resolvedBodies, Transform actorRoot)
        {
            for (int bodyIndex = 0; bodyIndex < resolvedBodies.Count; bodyIndex++)
            {
                ResolvedRagdollBody body = resolvedBodies[bodyIndex];
                int parentIndex = FindNearestAncestorBodyIndex(resolvedBodies, body.node, actorRoot);
                body.parentBodyIndex = parentIndex;

                if (parentIndex >= 0)
                {
                    ResolvedRagdollBody parentBody = resolvedBodies[parentIndex];
                    ComputeRestRelation(
                        body.node,
                        parentBody.node,
                        parentBody.definition.boxCenter,
                        actorRoot,
                        out body.restRelativeRotation,
                        out body.parentAnchorOffset);
                }

                resolvedBodies[bodyIndex] = body;
            }
        }

        /// <summary>
        /// The index, into <paramref name="resolvedBodies"/>, of the nearest ragdolled ancestor of
        /// <paramref name="node"/> — strictly above it, unlike
        /// <see cref="BillboardRootResolver.FindNearestRootIndex"/>'s inclusive walk, because a body
        /// cannot be its own parent.
        /// </summary>
        private static int FindNearestAncestorBodyIndex(
            List<ResolvedRagdollBody> resolvedBodies, Transform node, Transform actorRoot)
        {
            Transform walker = node == actorRoot ? null : node.parent;
            while (walker != null)
            {
                for (int candidateIndex = 0; candidateIndex < resolvedBodies.Count; candidateIndex++)
                {
                    if (resolvedBodies[candidateIndex].node == walker)
                    {
                        return candidateIndex;
                    }
                }
                if (walker == actorRoot)
                {
                    break;
                }
                walker = walker.parent;
            }
            return -1;
        }

        /// <summary>
        /// The child's rest orientation relative to its parent, and the joint anchor as an offset
        /// from the parent's rest centre of mass in the parent's own rest-local axes (D2's finding —
        /// see <see cref="RagdollBodyParams.parentAnchorOffset"/>'s remarks for why the field exists
        /// and why the child needs no matching offset of its own: a joint authored by hierarchy
        /// places the child node <em>at</em> the joint).
        /// </summary>
        /// <remarks>
        /// Computed in the actor's own local space — position and rotation composed from each
        /// node's local transform up to <paramref name="actorRoot"/> — never in scene-world space,
        /// for the same reason <see cref="ActorBaker"/>'s own rest-bounds walk does the same: the
        /// result must not change when the same prefab is placed somewhere else in the scene.
        /// </remarks>
        private static void ComputeRestRelation(
            Transform childNode,
            Transform parentNode,
            float3 parentBoxCenter,
            Transform actorRoot,
            out quaternion restRelativeRotation,
            out float3 parentAnchorOffset)
        {
            ComputeActorSpaceRestPose(childNode, actorRoot, out float3 childPosition, out quaternion childRotation);
            ComputeActorSpaceRestPose(parentNode, actorRoot, out float3 parentPosition, out quaternion parentRotation);

            quaternion inverseParentRotation = math.inverse(parentRotation);
            restRelativeRotation = math.mul(inverseParentRotation, childRotation);

            // The parent's own centre of mass sits at its box centre, not at its node origin (the
            // same "the body is the box" convention RagdollBodyParams' remarks establish) — the
            // joint itself sits at the child node's origin, since the child was authored to be
            // exactly where the joint is.
            float3 parentCenterOfMass = parentPosition + math.mul(parentRotation, parentBoxCenter);
            float3 jointOffsetFromParentCenter = childPosition - parentCenterOfMass;
            parentAnchorOffset = math.mul(inverseParentRotation, jointOffsetFromParentCenter);
        }

        /// <summary>Composes a node's local transforms up to <paramref name="actorRoot"/> into an actor-space position and rotation.</summary>
        private static void ComputeActorSpaceRestPose(
            Transform node, Transform actorRoot, out float3 position, out quaternion rotation)
        {
            float4x4 nodeToActor = float4x4.identity;
            Transform currentTransform = node;
            while (currentTransform != null && currentTransform != actorRoot)
            {
                nodeToActor = math.mul(ReadLocalMatrix(currentTransform), nodeToActor);
                currentTransform = currentTransform.parent;
            }
            position = nodeToActor.c3.xyz;
            rotation = ExtractRotation(nodeToActor);
        }

        private static float4x4 ReadLocalMatrix(Transform transform)
        {
            Vector3 localPosition = transform.localPosition;
            Quaternion localRotation = transform.localRotation;
            Vector3 localScale = transform.localScale;
            return float4x4.TRS(
                new float3(localPosition.x, localPosition.y, localPosition.z),
                new quaternion(localRotation.x, localRotation.y, localRotation.z, localRotation.w),
                new float3(localScale.x, localScale.y, localScale.z));
        }

        /// <summary>
        /// Extracts a pure rotation from a composed matrix that may carry non-uniform scale, by
        /// orthonormalising its basis columns rather than assuming the matrix already is one.
        /// </summary>
        private static quaternion ExtractRotation(float4x4 matrix)
        {
            float3 columnX = math.normalizesafe(matrix.c0.xyz, new float3(1f, 0f, 0f));
            float3 columnY = math.normalizesafe(matrix.c1.xyz, new float3(0f, 1f, 0f));
            float3 columnZ = math.normalizesafe(matrix.c2.xyz, new float3(0f, 0f, 1f));
            return new quaternion(new float3x3(columnX, columnY, columnZ));
        }

        // -----------------------------------------------------------------------------------
        // Depth ordering — identical in shape to BillboardRootResolver's.
        // -----------------------------------------------------------------------------------

        private static int DepthBelow(Transform node, Transform actorRoot)
        {
            int depth = 0;
            Transform walker = node;
            while (walker != null && walker != actorRoot)
            {
                depth++;
                walker = walker.parent;
            }
            return depth;
        }

        /// <summary>
        /// Insertion sort by depth, ascending and stable — <see cref="List{T}.Sort"/> is introsort
        /// and not stable, and the bake must be reproducible byte for byte (architecture section
        /// 4.5). A rig has a handful of bodies, so the quadratic worst case is irrelevant.
        /// </summary>
        private static void SortByDepth(List<ResolvedRagdollBody> resolvedBodies)
        {
            for (int index = 1; index < resolvedBodies.Count; index++)
            {
                ResolvedRagdollBody current = resolvedBodies[index];
                int scan = index - 1;
                while (scan >= 0 && resolvedBodies[scan].depth > current.depth)
                {
                    resolvedBodies[scan + 1] = resolvedBodies[scan];
                    scan--;
                }
                resolvedBodies[scan + 1] = current;
            }
        }
    }
}
