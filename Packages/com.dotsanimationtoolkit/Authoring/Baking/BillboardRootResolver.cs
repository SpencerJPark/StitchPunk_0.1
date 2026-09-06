// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>One billboard root, matched to the transform it turns.</summary>
    public struct ResolvedBillboardRoot
    {
        /// <summary>The transform this root turns.</summary>
        public Transform node;

        /// <summary>The rig row this came from.</summary>
        public BillboardRootDefinition definition;

        /// <summary>How far below the actor root the node sits; the actor root itself is 0.</summary>
        public int depth;
    }

    /// <summary>
    /// Matches a rig's authored billboard roots to the transforms of an actor's prefab hierarchy,
    /// and answers which root any given node inherits.
    /// </summary>
    public static class BillboardRootResolver
    {
        /// <param name="rig">The rig whose roots are being resolved. Null yields nothing.</param>
        /// <param name="unresolvedRoots">
        /// Receives the display names of roots whose address matched no transform, for the caller to
        /// report with a click-to-select context object.
        /// </param>
        /// <returns>The matched roots, shallowest first.</returns>
        public static List<ResolvedBillboardRoot> Resolve(
            RigAsset rig, Transform actorRoot, List<string> unresolvedRoots)
        {
            List<ResolvedBillboardRoot> resolvedRoots = new List<ResolvedBillboardRoot>();
            if (rig == null || rig.billboardRoots == null || actorRoot == null)
            {
                return resolvedRoots;
            }

            for (int rootIndex = 0; rootIndex < rig.billboardRoots.Count; rootIndex++)
            {
                BillboardRootDefinition definition = rig.billboardRoots[rootIndex];
                if (definition == null)
                {
                    continue;
                }

                Transform node = FindNode(definition.address, actorRoot);
                if (node == null)
                {
                    if (unresolvedRoots != null)
                    {
                        unresolvedRoots.Add(DescribeAddress(definition));
                    }
                    continue;
                }

                resolvedRoots.Add(new ResolvedBillboardRoot
                {
                    node = node,
                    definition = definition,
                    depth = DepthBelow(node, actorRoot)
                });
            }

            // Shallowest first: BillboardResolveSystem walks this buffer in order and reads each
            // node's orientation through live transform values, so an outer root must already have
            // written its result before a nested one reads through it.
            SortByDepth(resolvedRoots);
            return resolvedRoots;
        }

        /// <summary>
        /// The root <paramref name="node"/> billboards with: the nearest declared root at or above
        /// it, or −1 when no ancestor declares one.
        /// </summary>
        /// <returns>An index into <paramref name="resolvedRoots"/>, or −1.</returns>
        public static int FindNearestRootIndex(
            List<ResolvedBillboardRoot> resolvedRoots, Transform node, Transform actorRoot)
        {
            if (resolvedRoots == null || node == null)
            {
                return -1;
            }

            Transform walker = node;
            while (walker != null)
            {
                for (int rootIndex = 0; rootIndex < resolvedRoots.Count; rootIndex++)
                {
                    // Inclusive of node itself — a node that declares its own root answers with
                    // that root rather than its ancestor's, which is the override rule.
                    if (resolvedRoots[rootIndex].node == walker)
                    {
                        return rootIndex;
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

        /// <summary>The transform an address names, or null when it names nothing.</summary>
        public static Transform FindNode(RigNodeAddress address, Transform actorRoot)
        {
            if (actorRoot == null)
            {
                return null;
            }

            if (address.kind == RigNodeAddressKind.HierarchyPath)
            {
                return string.IsNullOrEmpty(address.hierarchyPath)
                    ? actorRoot
                    : actorRoot.Find(address.hierarchyPath);
            }

            if (address.targetId == 0u)
            {
                return null;
            }

            // Matched by stable id, never by name, so a part may be renamed or reparented freely.
            RigTargetAuthoring[] partAuthorings =
                actorRoot.GetComponentsInChildren<RigTargetAuthoring>(true);
            for (int partIndex = 0; partIndex < partAuthorings.Length; partIndex++)
            {
                if (partAuthorings[partIndex].targetStableId == address.targetId)
                {
                    return partAuthorings[partIndex].transform;
                }
            }
            return null;
        }

        /// <summary>Renders an address for a diagnostic message.</summary>
        public static string DescribeAddress(BillboardRootDefinition definition)
        {
            string label = string.IsNullOrEmpty(definition.displayName)
                ? "(unnamed)"
                : definition.displayName;
            if (definition.address.kind == RigNodeAddressKind.RigTarget)
            {
                return label + " -> target id " + definition.address.targetId.ToString();
            }
            return label + " -> path '" + definition.address.hierarchyPath + "'";
        }

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

        // Insertion sort, not List.Sort (introsort, not stable): stability keeps same-depth roots in
        // authoring order, which the bake must reproduce byte for byte. A rig has few roots, so the
        // quadratic worst case is irrelevant.
        private static void SortByDepth(List<ResolvedBillboardRoot> resolvedRoots)
        {
            for (int index = 1; index < resolvedRoots.Count; index++)
            {
                ResolvedBillboardRoot current = resolvedRoots[index];
                int scan = index - 1;
                while (scan >= 0 && resolvedRoots[scan].depth > current.depth)
                {
                    resolvedRoots[scan + 1] = resolvedRoots[scan];
                    scan--;
                }
                resolvedRoots[scan + 1] = current;
            }
        }
    }
}
