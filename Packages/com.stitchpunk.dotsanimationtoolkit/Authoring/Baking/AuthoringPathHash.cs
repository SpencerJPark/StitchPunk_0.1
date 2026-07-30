// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// Hashes an authoring object's hierarchy path into a stable 32-bit value, used at bake time
    /// wherever a per-object number is needed that must survive from one bake to the next.
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
    /// A path hash is stable instead: it is a pure function of where the object sits in its
    /// hierarchy, so the same source produces the same value on every machine and in every session.
    /// Sibling indices are folded in as well as names, so two identically named siblings still hash
    /// differently. Renaming or reparenting changes the value, which is acceptable for the two
    /// things that use it — spreading sampling phase, and naming an object in a diagnostic — and is
    /// never acceptable for identity, which is what the stable ids of section 3.4 are for.
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
        /// index.
        /// </summary>
        /// <param name="authoringTransform">The transform to hash. Null yields the bare basis.</param>
        /// <returns>A stable 32-bit hash of the hierarchy path.</returns>
        internal static uint Of(Transform authoringTransform)
        {
            uint pathHash = FnvOffsetBasis;
            Transform currentNode = authoringTransform;
            while (currentNode != null)
            {
                string nodeName = currentNode.name;
                for (int characterIndex = 0; characterIndex < nodeName.Length; characterIndex++)
                {
                    pathHash = (pathHash ^ nodeName[characterIndex]) * FnvPrime;
                }
                // Folded in so two identically named siblings do not collide.
                pathHash = (pathHash ^ (uint)currentNode.GetSiblingIndex()) * FnvPrime;
                // Separator, so "A/BC" and "AB/C" cannot hash alike.
                pathHash = (pathHash ^ '/') * FnvPrime;
                currentNode = currentNode.parent;
            }
            return pathHash;
        }
    }
}
