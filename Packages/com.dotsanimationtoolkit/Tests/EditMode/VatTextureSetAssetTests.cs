// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using System.Collections.Generic;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Covers VatTextureSetAsset.TryGetPart's exact-target-then-untargeted fallback.</summary>
    public sealed class VatTextureSetAssetTests
    {
        private readonly List<UnityEngine.Object> spawnedObjects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object spawnedObject in spawnedObjects)
            {
                if (spawnedObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(spawnedObject);
                }
            }
            spawnedObjects.Clear();
        }

        [Test]
        public void TryGetPart_PrefersTheExactTarget_ThenFallsBackToUntargeted()
        {
            VatTextureSetAsset multiPartSet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            spawnedObjects.Add(multiPartSet);
            VatPartTextures untargetedPart = new VatPartTextures { targetId = 0u };
            VatPartTextures targetSevenPart = new VatPartTextures { targetId = 7u };
            multiPartSet.parts.Add(untargetedPart);
            multiPartSet.parts.Add(targetSevenPart);

            bool foundExactMatch = multiPartSet.TryGetPart(7u, out VatPartTextures exactMatch);
            bool foundFallback = multiPartSet.TryGetPart(9u, out VatPartTextures fallbackMatch);

            VatTextureSetAsset targetedOnlySet = ScriptableObject.CreateInstance<VatTextureSetAsset>();
            spawnedObjects.Add(targetedOnlySet);
            targetedOnlySet.parts.Add(new VatPartTextures { targetId = 7u });

            bool foundMatchWithNothingToFallBackOn =
                targetedOnlySet.TryGetPart(9u, out VatPartTextures unmatchedPart);

            Assert.IsTrue(foundExactMatch);
            Assert.AreSame(targetSevenPart, exactMatch);
            Assert.IsTrue(foundFallback);
            Assert.AreSame(untargetedPart, fallbackMatch);
            Assert.IsFalse(foundMatchWithNothingToFallBackOn);
            Assert.IsNull(unmatchedPart);
        }
    }
}
