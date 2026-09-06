// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// One ragdoll body matched to the transform it is welded to, plus the rest-hierarchy facts
    /// <see cref="ActorBaker"/> needs to finish a <see cref="RagdollBodyParams"/>.
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
    /// answers each body's implied parent and rest-pose joint geometry.
    /// </summary>
    internal static class RagdollBodyResolver
    {
        /// <param name="unresolvedBodies">
        /// Receives every RigTarget- or HierarchyPath-addressed body whose address matched no
        /// transform — a genuine authoring mistake.
        /// </param>
        /// <param name="boneOnlyBodies">
        /// Receives every Bone-addressed body. Legal authoring data, but a skinned-bone ragdoll body
        /// never resolves against a VAT actor's runtime prefab — it has no bone GameObject hierarchy
        /// at all — so it is reported here rather than treated as the same fault as an unresolved
        /// RigTarget or HierarchyPath address.
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

        // Safe to search only among bodies already placed at a lower list index: resolvedBodies is
        // depth-sorted first, so a body's parent — being strictly shallower — has always already
        // been placed by the time this reaches it.
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

        // Strictly above the node, unlike BillboardRootResolver's inclusive walk — a body cannot be
        // its own parent.
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
        /// from the parent's rest centre of mass in the parent's own rest-local axes.
        /// </summary>
        // Computed in the actor's own local space, never scene-world space, so the result does not
        // change when the same prefab is placed elsewhere in the scene.
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

            // The parent's centre of mass sits at its box centre, not its node origin; the joint
            // itself sits at the child node's origin, since the child was authored to be there.
            float3 parentCenterOfMass = parentPosition + math.mul(parentRotation, parentBoxCenter);
            float3 jointOffsetFromParentCenter = childPosition - parentCenterOfMass;
            parentAnchorOffset = math.mul(inverseParentRotation, jointOffsetFromParentCenter);
        }

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

        // Orthonormalises the composed matrix's basis columns rather than assuming it already is
        // one, since the matrix may carry non-uniform scale.
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

        // Insertion sort, not List.Sort (introsort, not stable): stability keeps same-depth bodies
        // in authoring order, which the bake must reproduce byte for byte.
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
