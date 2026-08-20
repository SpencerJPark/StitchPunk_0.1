// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Derives bake-stable values from an authoring object's hierarchy path: a 32-bit hash
    /// (<see cref="Of"/>) wherever a per-object <em>number</em> is needed that must survive from one
    /// bake to the next, and the path as readable text (<see cref="PathOf"/>) wherever a Bursted
    /// system must name an object it cannot hold a managed reference to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because Unity's per-object numbers do not survive a session.
    /// <c>Object.GetInstanceID</c> is deprecated, its successor <c>EntityId</c> is documented as no
    /// longer representable by an <c>int</c>, and both are assigned fresh every time the project
    /// loads. Baking either of them into entity data makes the same prefab produce different bytes
    /// on every bake, which costs reproducible subscene bakes — the property architecture section
    /// 4.5 spends real effort guaranteeing for the registry blob.
    /// </para>
    /// <para>
    /// A path is stable instead: it is a pure function of where the object sits in its hierarchy, so
    /// the same source produces the same value on every machine and in every session. Sibling
    /// indices are folded into the hash as well as names, so two identically named siblings still
    /// hash differently. Renaming or reparenting changes the value, which is acceptable for the two
    /// things that consume it — spreading sampling phase (<see cref="Of"/>) and naming an object in
    /// a diagnostic (<see cref="PathOf"/>) — and is never acceptable for identity, which is what the
    /// stable ids of section 3.4 are for.
    /// </para>
    /// <para>
    /// <strong>Both walks read ancestors, so both take bake dependencies on them.</strong> Every
    /// ancestor name is read through <c>IBaker.GetName</c> and the chain itself through
    /// <c>IBaker.GetParents</c>, each of which registers the corresponding dependency. Reading the
    /// names off <c>Transform.name</c> directly would compile and produce the same value, but an
    /// ancestor rename would then not retrigger the bake: an incremental bake and a clean bake of
    /// the same scene would disagree about <c>SampleSettings.phase01</c>, and a part's recorded
    /// <c>authoringPath</c> would go on naming a hierarchy that no longer exists — which is the one
    /// thing that field is for. The <em>sibling index</em> is the acknowledged gap: Entities exposes
    /// no per-object sibling-index dependency, so reordering siblings without renaming or
    /// reparenting anything does not retrigger the bake. That case changes only which frame an actor
    /// samples on, never what it looks like.
    /// </para>
    /// <para>
    /// The algorithm is FNV-1a, written out here rather than taken from a library so that the baked
    /// value cannot change underneath the package when a dependency updates its hashing.
    /// </para>
    /// </remarks>
    internal static class AuthoringPathHash
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>
        /// Hashes the transform's full path from the scene root, including each node's sibling
        /// index, taking a bake dependency on every name and on the ancestor chain.
        /// </summary>
        /// <param name="baker">The baker whose dependencies the walk registers against.</param>
        /// <param name="authoringTransform">The transform to hash. Null yields the bare basis.</param>
        /// <returns>A stable 32-bit hash of the hierarchy path.</returns>
        internal static uint Of(IBaker baker, Transform authoringTransform)
        {
            uint pathHash = FnvOffsetBasis;
            if (authoringTransform == null)
            {
                return pathHash;
            }

            List<GameObject> pathNodes = CollectPathFromLeaf(baker, authoringTransform);
            for (int nodeIndex = 0; nodeIndex < pathNodes.Count; nodeIndex++)
            {
                GameObject pathNode = pathNodes[nodeIndex];
                string nodeName = baker.GetName(pathNode);
                for (int characterIndex = 0; characterIndex < nodeName.Length; characterIndex++)
                {
                    pathHash = (pathHash ^ nodeName[characterIndex]) * FnvPrime;
                }
                // Folded in so two identically named siblings do not collide.
                pathHash = (pathHash ^ (uint)pathNode.transform.GetSiblingIndex()) * FnvPrime;
                // Separator, so "A/BC" and "AB/C" cannot hash alike.
                pathHash = (pathHash ^ '/') * FnvPrime;
            }
            return pathHash;
        }

        /// <summary>
        /// Returns the node itself followed by every ancestor, leaf first, registering the
        /// hierarchy dependency that makes a reparent retrigger the bake.
        /// </summary>
        private static List<GameObject> CollectPathFromLeaf(IBaker baker, Transform authoringTransform)
        {
            GameObject leafGameObject = authoringTransform.gameObject;
            List<GameObject> ancestors = new List<GameObject>();
            baker.GetParents(leafGameObject, ancestors);

            List<GameObject> pathNodes = new List<GameObject>(ancestors.Count + 1);
            pathNodes.Add(leafGameObject);
            pathNodes.AddRange(ancestors);
            return pathNodes;
        }

        /// <summary>
        /// Walks the transform's hierarchy path and hands the node names to
        /// <see cref="AuthoringPathText.RenderPath"/>, which turns them into
        /// <c>Root/Child/Leaf</c> text a Bursted system can carry in a
        /// <see cref="FixedString128Bytes"/>.
        /// </summary>
        /// <remarks>
        /// Only the walk lives here. Everything about fitting the result into a fixed string — the
        /// UTF-8 byte budget, truncating from the left so the leaf survives, and never using the
        /// throwing constructor — is <see cref="AuthoringPathText"/>'s, where an EditMode test can
        /// reach it with plain strings.
        /// </remarks>
        /// <param name="baker">The baker whose dependencies the walk registers against.</param>
        /// <param name="authoringTransform">The transform to describe. Null yields an empty path.</param>
        internal static FixedString128Bytes PathOf(IBaker baker, Transform authoringTransform)
        {
            if (authoringTransform == null)
            {
                return default;
            }

            List<GameObject> pathNodes = CollectPathFromLeaf(baker, authoringTransform);
            List<string> nodeNamesFromLeaf = new List<string>(pathNodes.Count);
            for (int nodeIndex = 0; nodeIndex < pathNodes.Count; nodeIndex++)
            {
                nodeNamesFromLeaf.Add(baker.GetName(pathNodes[nodeIndex]));
            }
            return AuthoringPathText.RenderPath(nodeNamesFromLeaf);
        }
    }
}
