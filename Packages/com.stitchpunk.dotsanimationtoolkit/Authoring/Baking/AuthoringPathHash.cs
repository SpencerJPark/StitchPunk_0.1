// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Text;
using Unity.Collections;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
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

        /// <summary>UTF-8 payload capacity of a <see cref="FixedString128Bytes"/>.</summary>
        private const int MaximumPathBytes = 125;

        /// <summary>Prefix marking a path whose outermost ancestors were dropped.</summary>
        private const string TruncationMarker = ".../";

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
        /// <para>
        /// Truncation drops the outermost ancestors, not the leaf: a message naming
        /// <c>.../Torso/LeftArm</c> locates the part, and one naming <c>SceneRoot/Rig/...</c> does
        /// not. This is text for a human to read, never an identifier — nothing may key off it.
        /// </para>
        /// <para>
        /// The budget is counted in <strong>UTF-8 bytes, not characters</strong>. A
        /// <c>FixedString128Bytes</c> holds 125 payload bytes, and one character can occupy up to
        /// four of them, so a character count is a permissive bound on a byte capacity — the
        /// opposite of conservative. Getting this wrong is not a truncated message: the
        /// <c>FixedString128Bytes(string)</c> constructor calls <c>CheckCopyError</c>, which throws
        /// under <c>ENABLE_UNITY_COLLECTIONS_CHECKS</c> — always on in the Editor, which is the only
        /// place baking runs. The exception escapes <c>RigTargetBaker.Bake</c>, Unity logs it and
        /// carries on, and the part silently loses its rest pose, output pose and technique
        /// components. So the copy also goes through <see cref="FixedStringMethods.CopyFromTruncated"/>,
        /// which truncates instead of throwing: a name this method cannot render must degrade to a
        /// shortened diagnostic, never to a broken bake.
        /// </para>
        /// </remarks>
        /// <param name="authoringTransform">The transform to describe. Null yields an empty path.</param>
        internal static FixedString128Bytes PathOf(Transform authoringTransform)
        {
            if (authoringTransform == null)
            {
                return default;
            }

            StringBuilder pathBuilder = new StringBuilder(authoringTransform.name);
            Transform currentNode = authoringTransform.parent;
            while (currentNode != null)
            {
                pathBuilder.Insert(0, '/').Insert(0, currentNode.name);
                currentNode = currentNode.parent;
            }

            string fullPath = pathBuilder.ToString();
            FixedString128Bytes renderedPath = default;
            if (Encoding.UTF8.GetByteCount(fullPath) > MaximumPathBytes)
            {
                fullPath = TruncationMarker + TakeTrailingBytes(
                    fullPath,
                    MaximumPathBytes - Encoding.UTF8.GetByteCount(TruncationMarker));
            }

            // Truncating copy, never the throwing constructor. If the arithmetic above is ever wrong
            // the message gets shorter; the bake does not break.
            renderedPath.CopyFromTruncated(fullPath);
            return renderedPath;
        }

        /// <summary>
        /// Returns the longest suffix of <paramref name="text"/> that encodes to no more than
        /// <paramref name="byteBudget"/> UTF-8 bytes, never splitting a surrogate pair.
        /// </summary>
        private static string TakeTrailingBytes(string text, int byteBudget)
        {
            int startIndex = text.Length;
            int usedBytes = 0;
            while (startIndex > 0)
            {
                int candidateIndex = startIndex - 1;
                // A low surrogate is the second half of an astral character; stepping onto it alone
                // would both mis-measure the encoding and emit a lone half.
                if (candidateIndex > 0 && char.IsLowSurrogate(text[candidateIndex]))
                {
                    candidateIndex--;
                }
                int characterBytes = Encoding.UTF8.GetByteCount(
                    text.ToCharArray(candidateIndex, startIndex - candidateIndex));
                if (usedBytes + characterBytes > byteBudget)
                {
                    break;
                }
                usedBytes += characterBytes;
                startIndex = candidateIndex;
            }
            return text.Substring(startIndex);
        }
    }
}
