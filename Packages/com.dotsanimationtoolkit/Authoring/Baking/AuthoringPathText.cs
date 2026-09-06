// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
using Unity.Collections;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Renders a hierarchy path as <c>Root/Child/Leaf</c> text sized to fit a
    /// <see cref="FixedString128Bytes"/>, so a Bursted system can name an object it holds no
    /// managed reference to.
    /// </summary>
    internal static class AuthoringPathText
    {
        /// <summary>UTF-8 payload capacity of a <see cref="FixedString128Bytes"/>.</summary>
        internal const int MaximumPathBytes = 125;

        /// <summary>Prefix marking a path whose outermost ancestors were dropped.</summary>
        internal const string TruncationMarker = ".../";

        /// <summary>Joins names supplied leaf first into root-first <c>Root/Child/Leaf</c> text.</summary>
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
            // Budget is UTF-8 bytes, not characters — a character can take up to 4 bytes, so
            // comparing character count against the byte budget would under-truncate.
            if (Encoding.UTF8.GetByteCount(fullPath) > MaximumPathBytes)
            {
                // Truncate from the left (drop outermost ancestors) so the leaf, which locates
                // the object, always survives.
                fullPath = TruncationMarker + TakeTrailingBytes(
                    fullPath,
                    MaximumPathBytes - Encoding.UTF8.GetByteCount(TruncationMarker));
            }

            // CopyFromTruncated, never the throwing constructor: FixedString128Bytes(string) throws
            // under ENABLE_UNITY_COLLECTIONS_CHECKS on overflow, which would abort the bake this
            // diagnostic is meant to explain. Worst case here is a shorter message, not a broken bake.
            renderedPath.CopyFromTruncated(fullPath);
            return renderedPath;
        }

        // Steps back a full character at a time, not one UTF-16 code unit, so a low surrogate is
        // never split from the high surrogate before it.
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
