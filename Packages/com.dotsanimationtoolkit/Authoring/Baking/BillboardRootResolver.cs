// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// One billboard root, matched to the transform it turns (amendment A44).
    /// </summary>
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
    /// and answers which root any given node inherits (amendment A44).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Separate from the bakers because the interesting part is not baking.</strong>
    /// Nearest-ancestor inheritance, override precedence and depth ordering are ordinary tree
    /// questions, and pulling them out of <c>ActorBaker</c> makes them answerable in an EditMode
    /// fixture with a handful of <c>GameObject</c>s rather than only through a full bake.
    /// </para>
    /// <para>
    /// <strong>This is where the other half of validation rule V21 lives.</strong>
    /// <c>ClipValidation</c> resolves a root's target address against the rig, because a target is a
    /// row of the rig asset. It cannot resolve a <em>path</em> address, because a
    /// <see cref="RigAsset"/> neither references a prefab nor carries a hierarchy of its own. Here
    /// the prefab is in hand, so here is where an unresolvable path is reported.
    /// </para>
    /// </remarks>
    public static class BillboardRootResolver
    {
        /// <summary>
        /// Matches every billboard root the rig declares to a transform under
        /// <paramref name="actorRoot"/>, sorted shallowest first.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The sort is the contract.</strong> <c>BillboardResolveSystem</c> walks the baked
        /// buffer in order and reads each node's orientation through live transform values, so an
        /// outer root must already have written its result before a nested one reads through it.
        /// Depth order is what guarantees that, and it is why this returns a sorted list rather than
        /// leaving the caller to preserve authoring order.
        /// </para>
        /// <para>
        /// The sort is stable within a depth: two roots at the same depth cannot be ancestors of one
        /// another, so their relative order cannot matter, and keeping authoring order makes the
        /// baked output reproducible (architecture section 4.5).
        /// </para>
        /// </remarks>
        /// <param name="rig">The rig whose roots are being resolved. Null yields nothing.</param>
        /// <param name="actorRoot">The actor's transform; addresses are relative to it.</param>
        /// <param name="unresolvedRoots">
        /// Receives the display names of roots whose address matched no transform. The caller
        /// reports them, because only it can attach a click-to-select context object.
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

            SortByDepth(resolvedRoots);
            return resolvedRoots;
        }

        /// <summary>
        /// The root <paramref name="node"/> billboards with: the nearest declared root at or above
        /// it, or −1 when no ancestor declares one.
        /// </summary>
        /// <remarks>
        /// <strong>Inclusive of the node itself, and that inclusion is the override rule.</strong> A
        /// node that declares its own root answers with that root rather than with its ancestor's,
        /// which is exactly what lets a held item billboard independently of the character holding
        /// it.
        /// </remarks>
        /// <param name="resolvedRoots">The roots from <see cref="Resolve"/>.</param>
        /// <param name="node">The node to place.</param>
        /// <param name="actorRoot">The actor's transform; the walk stops here.</param>
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

        /// <summary>
        /// The transform an address names, or null when it names nothing.
        /// </summary>
        /// <remarks>
        /// A rig-target address is matched by searching for the <see cref="RigTargetAuthoring"/>
        /// carrying that stable id, never by name — the id is the identity and a part may be renamed
        /// or reparented freely (architecture section 3.4). A path address is resolved literally, and
        /// an empty path names the actor root, which is a real node and the whole-actor billboard
        /// case A41 shipped.
        /// </remarks>
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

        /// <summary>
        /// Insertion sort by depth, ascending and stable.
        /// </summary>
        /// <remarks>
        /// Written out rather than using <c>List.Sort</c>, which is introsort and <em>not</em>
        /// stable. Stability is what keeps two roots at the same depth in authoring order, and the
        /// bake has to be reproducible byte for byte (architecture section 4.5). A rig has a handful
        /// of roots, so the quadratic worst case is irrelevant.
        /// </remarks>
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
