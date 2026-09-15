// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Decides which of a VAT texture set's part textures and runtime meshes no other set references, so deleting the set can trash them too.</summary>
    public static class VatTextureOwnershipResolver
    {
        public static List<UnityEngine.Object> FindTexturesSafeToTrash(VatTextureSetAsset target, IReadOnlyList<VatTextureSetAsset> allSets)
        {
            List<UnityEngine.Object> ownedByTarget = new List<UnityEngine.Object>();
            if (target == null || target.parts == null)
            {
                return ownedByTarget;
            }

            for (int partIndex = 0; partIndex < target.parts.Count; partIndex++)
            {
                VatPartTextures part = target.parts[partIndex];
                if (part == null)
                {
                    continue;
                }

                AddIfNewCandidate(ownedByTarget, part.boneTexture);
                AddIfNewCandidate(ownedByTarget, part.positionTexture);
                AddIfNewCandidate(ownedByTarget, part.normalTexture);
                AddIfNewCandidate(ownedByTarget, part.runtimeMesh);
            }

            if (allSets == null)
            {
                return ownedByTarget;
            }

            List<UnityEngine.Object> safeToTrash = new List<UnityEngine.Object>();
            for (int candidateIndex = 0; candidateIndex < ownedByTarget.Count; candidateIndex++)
            {
                UnityEngine.Object candidate = ownedByTarget[candidateIndex];
                if (!IsReferencedByAnyOtherSet(candidate, target, allSets))
                {
                    safeToTrash.Add(candidate);
                }
            }

            return safeToTrash;
        }

        private static void AddIfNewCandidate(List<UnityEngine.Object> candidates, UnityEngine.Object candidate)
        {
            if (candidate == null)
            {
                return;
            }

            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (ReferenceEquals(candidates[candidateIndex], candidate))
                {
                    return;
                }
            }

            candidates.Add(candidate);
        }

        private static bool IsReferencedByAnyOtherSet(UnityEngine.Object candidate, VatTextureSetAsset target, IReadOnlyList<VatTextureSetAsset> allSets)
        {
            for (int setIndex = 0; setIndex < allSets.Count; setIndex++)
            {
                VatTextureSetAsset otherSet = allSets[setIndex];
                if (otherSet == null || ReferenceEquals(otherSet, target) || otherSet.parts == null)
                {
                    continue;
                }

                for (int partIndex = 0; partIndex < otherSet.parts.Count; partIndex++)
                {
                    VatPartTextures otherPart = otherSet.parts[partIndex];
                    if (otherPart == null)
                    {
                        continue;
                    }

                    if (ReferenceEquals(otherPart.boneTexture, candidate)
                        || ReferenceEquals(otherPart.positionTexture, candidate)
                        || ReferenceEquals(otherPart.normalTexture, candidate)
                        || ReferenceEquals(otherPart.runtimeMesh, candidate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
