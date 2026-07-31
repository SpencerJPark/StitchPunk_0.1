// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Collections;
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

        /// <summary>
        /// Renders the transform's hierarchy path as <c>Root/Child/Leaf</c> text, sized to fit a
        /// <see cref="FixedString128Bytes"/> so a Bursted system can name the object it is
        /// complaining about.
        /// </summary>
        /// <remarks>
        /// Truncation drops the outermost ancestors, not the leaf: a message naming
        /// <c>.../Torso/LeftArm</c> locates the part, and one naming <c>SceneRoot/Rig/...</c> does
        /// not. This is text for a human to read, never an identifier — nothing may key off it.
        /// </remarks>
        /// <param name="authoringTransform">The transform to describe. Null yields an empty path.</param>
        internal static FixedString128Bytes PathOf(Transform authoringTransform)
        {
            if (authoringTransform == null)
            {
                return default;
            }

            string fullPath = authoringTransform.name;
            Transform currentNode = authoringTransform.parent;
            while (currentNode != null)
            {
                fullPath = currentNode.name + "/" + fullPath;
                currentNode = currentNode.parent;
            }

            // FixedString128Bytes holds 125 UTF-8 bytes. Trimming by character count is conservative
            // for any non-ASCII name — the safe direction to be wrong in, since overflowing throws.
            const int MaximumPathCharacters = 110;
            if (fullPath.Length > MaximumPathCharacters)
            {
                fullPath = ".../" + fullPath.Substring(fullPath.Length - MaximumPathCharacters);
            }
            return new FixedString128Bytes(fullPath);
        }
    }
}
