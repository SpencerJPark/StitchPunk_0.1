// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers A70-T2's third vocabulary: <see cref="AnimationNameRegistry"/> mints ids the same
    /// collision-free way <see cref="TargetTagRegistry"/> does (mirrors
    /// <c>TargetTagRegistryTests.MintTagId_NeverCollidesWithAnIdAlreadyInTheRegistry</c>).
    /// </summary>
    public sealed class AnimationNameRegistryTests
    {
        [Test]
        public void MintAnimationKey_NeverCollidesWithAKeyAlreadyInTheRegistry()
        {
            AnimationNameRegistry registry = ScriptableObject.CreateInstance<AnimationNameRegistry>();
            try
            {
                registry.entries.Add(new AnimationNameEntry { name = "Walk", animationKey = 12345u });
                registry.entries.Add(new AnimationNameEntry { name = "Idle", animationKey = 67890u });

                for (int mintIndex = 0; mintIndex < 50; mintIndex++)
                {
                    uint mintedKey = registry.MintAnimationKey();
                    Assert.AreNotEqual(0u, mintedKey, "0 is reserved for \"unresolved\".");
                    Assert.AreNotEqual(12345u, mintedKey, "Must not collide with an existing entry.");
                    Assert.AreNotEqual(67890u, mintedKey, "Must not collide with an existing entry.");
                }
            }
            finally
            {
                Object.DestroyImmediate(registry);
            }
        }
    }
}
