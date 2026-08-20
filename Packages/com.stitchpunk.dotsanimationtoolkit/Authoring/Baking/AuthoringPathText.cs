// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using System.Text;
using Unity.Collections;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Renders a hierarchy path as <c>Root/Child/Leaf</c> text sized to fit a
    /// <see cref="FixedString128Bytes"/>, so a Bursted baking system can name the object it is
    /// complaining about without holding a managed reference to it (architecture section 4.1,
    /// amendment A21).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split out from <see cref="AuthoringPathHash"/>, which walks the hierarchy and needs a
    /// <c>Baker</c> to do it, for two reasons. The type name: hashing a path to a number and
    /// rendering it as text are different jobs, and a class called <c>AuthoringPathHash</c> that
    /// also returns text no longer describes itself. And testability: everything that has ever gone
    /// wrong here is in the encoding, and this way it is reachable from an EditMode test with plain
    /// strings rather than only through a live bake — whose fixtures were all short and ASCII, which
    /// is exactly why the overflow below shipped.
    /// </para>
    /// <para>
    /// The budget is counted in <strong>UTF-8 bytes, not characters</strong>. A
    /// <c>FixedString128Bytes</c> holds 125 payload bytes and one character can occupy up to four of
    /// them, so a character count is a permissive bound on a byte capacity, not a conservative one.
    /// Getting that backwards does not produce a truncated message: the
    /// <c>FixedString128Bytes(string)</c> constructor calls <c>CheckCopyError</c>, which throws
    /// under <c>ENABLE_UNITY_COLLECTIONS_CHECKS</c> — always defined in the Editor, which is the
    /// only place baking runs. The exception escapes <c>RigTargetBaker.Bake</c> and the part loses
    /// its rest pose, output pose and technique components. So the copy also goes through
    /// <see cref="FixedStringMethods.CopyFromTruncated"/>, which truncates rather than throwing: a
    /// name this type cannot render must degrade to a shortened diagnostic, never to a broken bake.
    /// </para>
    /// <para>
    /// Truncation drops the outermost ancestors, not the leaf: a message naming
    /// <c>.../Torso/LeftArm</c> locates the part, and one naming <c>SceneRoot/Rig/...</c> does not.
    /// This is text for a human to read, never an identifier — nothing may key off it.
    /// </para>
    /// </remarks>
    internal static class AuthoringPathText
    {
        /// <summary>UTF-8 payload capacity of a <see cref="FixedString128Bytes"/>.</summary>
        internal const int MaximumPathBytes = 125;

        /// <summary>Prefix marking a path whose outermost ancestors were dropped.</summary>
        internal const string TruncationMarker = ".../";

        /// <summary>
        /// Joins node names — supplied <strong>leaf first</strong>, the direction a transform chain
        /// is navigable — into root-first <c>Root/Child/Leaf</c> text.
        /// </summary>
        /// <param name="nodeNamesFromLeaf">Leaf name first, then each ancestor out to the root.</param>
        /// <returns>The path, truncated from the left if it does not fit. Never throws.</returns>
        internal static FixedString128Bytes RenderPath(IReadOnlyList<string> nodeNamesFromLeaf)
        {
            FixedString128Bytes renderedPath = default;
            if (nodeNamesFromLeaf == null || nodeNamesFromLeaf.Count == 0)
            {
                return renderedPath;
            }

            StringBuilder pathBuilder = new StringBuilder();
            for (int nodeIndex = nodeNamesFromLeaf.Count - 1; nodeIndex >= 0; nodeIndex--)
            {
                if (nodeIndex != nodeNamesFromLeaf.Count - 1)
                {
                    pathBuilder.Append('/');
                }
                pathBuilder.Append(nodeNamesFromLeaf[nodeIndex]);
            }

            string fullPath = pathBuilder.ToString();
            if (Encoding.UTF8.GetByteCount(fullPath) > MaximumPathBytes)
            {
                fullPath = TruncationMarker + TakeTrailingBytes(
                    fullPath,
                    MaximumPathBytes - Encoding.UTF8.GetByteCount(TruncationMarker));
            }

            // Truncating copy, never the throwing constructor. If the arithmetic above is ever
            // wrong the message gets shorter; the bake does not break.
            renderedPath.CopyFromTruncated(fullPath);
            return renderedPath;
        }

        /// <summary>
        /// Returns the longest suffix of <paramref name="text"/> that encodes to no more than
        /// <paramref name="byteBudget"/> UTF-8 bytes, never splitting a surrogate pair.
        /// </summary>
        /// <remarks>
        /// Stepping back one UTF-16 code unit at a time would land between the halves of an astral
        /// character, leaving a lone surrogate at the start of the retained text — which both
        /// mis-measures the encoding and makes the first thing the user reads a replacement
        /// character. So a low surrogate is always taken together with the high surrogate before it.
        /// </remarks>
        private static string TakeTrailingBytes(string text, int byteBudget)
        {
            int startIndex = text.Length;
            int usedBytes = 0;
            while (startIndex > 0)
            {
                int candidateIndex = startIndex - 1;
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
