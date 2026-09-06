// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Derives bake-stable values from an authoring object's hierarchy path, since Unity's own
    /// per-object identifiers are reassigned every session and are unsuitable for baked data.
    /// </summary>
    internal static class AuthoringPathHash
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>Hashes the transform's full path from the scene root. Null yields the bare basis.</summary>
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
                // Sibling index folded in so identically named siblings don't collide. Reordering
                // siblings alone does not retrigger the bake — Entities exposes no dependency for it.
                pathHash = (pathHash ^ (uint)pathNode.transform.GetSiblingIndex()) * FnvPrime;
                // Separator, so "A/BC" and "AB/C" cannot hash alike.
                pathHash = (pathHash ^ '/') * FnvPrime;
            }
            return pathHash;
        }

        // Reads names through IBaker.GetName and the chain through IBaker.GetParents rather than
        // Transform directly, so an ancestor rename or reparent retriggers the bake.
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

        /// <summary>Renders the transform's hierarchy path as text via <see cref="AuthoringPathText"/>.</summary>
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
