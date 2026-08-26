// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers amendment E6 Task 2: <see cref="ConstantsGenerator"/> turns a vocabulary's authored
    /// names into constants game code can refer to by name (spec §4.2.3).
    /// </summary>
    /// <remarks>
    /// Both cases here are about emitting <em>legal C#</em> from <em>arbitrary user text</em>, which
    /// is where a generator fails silently: nothing goes wrong at generation time, and the damage
    /// lands in the customer's compiler inside a file whose header tells them not to edit it. The
    /// happy path is asserted for the same reason — the whole promise of the feature is that
    /// <c>TargetTags.Jaw</c> exists and carries the id the registry minted, and an emitter that
    /// silently dropped rows would still produce a file that compiles.
    /// </remarks>
    public sealed class ConstantsGeneratorTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                if (createdObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(createdObjects[objectIndex]);
                }
            }
            createdObjects.Clear();
        }

        private TargetTagRegistry CreateRegistry(params TargetTagEntry[] entries)
        {
            TargetTagRegistry registry = ScriptableObject.CreateInstance<TargetTagRegistry>();
            createdObjects.Add(registry);
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                registry.entries.Add(entries[entryIndex]);
            }
            return registry;
        }

        [Test]
        public void BuildVocabularyConstantsSource_EmitsOneConstantPerRow_AndReportsNamesItHadToChange()
        {
            TargetTagRegistry registry = CreateRegistry(
                new TargetTagEntry { name = "Jaw", stableId = 0x1A2B3C4Du },
                new TargetTagEntry { name = "Eye L", stableId = 0x0000000Au });
            List<string> reports = new List<string>();

            string generatedSource = ConstantsGenerator.BuildVocabularyConstantsSource(
                registry, "TargetTags", "Target tag", "Tag", reports);

            Assert.IsTrue(
                generatedSource.Contains("public static class TargetTags"),
                "The class the owner writes TargetTags.Jaw against must be what is emitted.");
            StringAssert.Contains("public const uint Jaw = 0x1A2B3C4Du;", generatedSource);
            StringAssert.Contains("public const uint Eye_L = 0x0000000Au;", generatedSource);

            // A name that could not be written as C# is the one case that breaks §4.2.3's promise
            // that the owner works in names, so it must be said out loud rather than left in the
            // generated file to be discovered.
            Assert.AreEqual(1, reports.Count, "Only 'Eye L' needed changing.");
            StringAssert.Contains("Eye L", reports[0]);
            StringAssert.Contains("Eye_L", reports[0]);
        }

        [Test]
        public void BuildVocabularyConstantsSource_DisambiguatesRowsThatCollideOnceWrittenAsCSharp()
        {
            // Two distinct, legal tag names that sanitize to the same identifier. Emitted verbatim
            // they would be two constants of the same name, which is a compile error in generated
            // code the customer was told not to hand-edit.
            TargetTagRegistry registry = CreateRegistry(
                new TargetTagEntry { name = "Foot step", stableId = 0x00000001u },
                new TargetTagEntry { name = "Foot-step", stableId = 0x00000002u });

            string generatedSource = ConstantsGenerator.BuildVocabularyConstantsSource(
                registry, "TargetTags", "Target tag", "Tag", null);

            StringAssert.Contains("public const uint Foot_step = 0x00000001u;", generatedSource);
            StringAssert.Contains("public const uint Foot_step_1 = 0x00000002u;", generatedSource);
        }
    }
}
