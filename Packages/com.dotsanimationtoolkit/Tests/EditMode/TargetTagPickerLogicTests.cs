// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers the two pure predicates <see cref="VocabularyPicker"/>'s row-building callbacks call
    /// (Phase E target-tags spec §4.2.1): the filter match every row is tested against, and the
    /// near-duplicate guard that makes "Create tag..." safe. Neither test touches a
    /// <see cref="UnityEngine.UIElements.VisualElement"/>, an overlay, or a window - both methods
    /// are plain static logic, extracted specifically so this fixture would not need any of that.
    /// </summary>
    /// <remarks>
    /// <strong>Why the duplicate guard gets the most scrutiny.</strong> Spec section 6.1 makes rule
    /// T2 deliberately lenient - a tag-bound track whose tag is absent from a rig is skipped with a
    /// warning, not an error - and the whole safety argument for that leniency is that a tag can
    /// only ever be picked, never typed (spec section 4.2.1). "Create tag..." is the one place that
    /// argument could quietly break: if it let two differently-cased spellings of the same word both
    /// exist as tags, they would look identical everywhere and match nothing in common, which is
    /// exactly the half-animated-roster failure this whole phase exists to remove. That is why
    /// <see cref="IsNearDuplicateName"/> gets more fixtures here than <see cref="MatchesFilter"/>.
    /// </remarks>
    public sealed class VocabularyPickerLogicTests
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

        private TargetTagRegistry CreateRegistry(params string[] entryNames)
        {
            TargetTagRegistry registry = ScriptableObject.CreateInstance<TargetTagRegistry>();
            createdObjects.Add(registry);
            for (int nameIndex = 0; nameIndex < entryNames.Length; nameIndex++)
            {
                registry.entries.Add(new TargetTagEntry
                {
                    name = entryNames[nameIndex],
                    stableId = (uint)(nameIndex + 1)
                });
            }
            return registry;
        }

        // -----------------------------------------------------------------------------------
        // MatchesFilter.
        // -----------------------------------------------------------------------------------

        [Test]
        public void MatchesFilter_IsTrue_ForAnEmptyFilter_RegardlessOfName()
        {
            Assert.IsTrue(VocabularyPicker.MatchesFilter("Jaw", string.Empty));
            Assert.IsTrue(VocabularyPicker.MatchesFilter("Jaw", null));
            Assert.IsTrue(VocabularyPicker.MatchesFilter("Jaw", "   "), "Whitespace-only filters everything too.");
        }

        [Test]
        public void MatchesFilter_IsTrue_ForAnExactCaseMatch()
        {
            Assert.IsTrue(VocabularyPicker.MatchesFilter("EyeL", "EyeL"));
        }

        [Test]
        public void MatchesFilter_IsTrue_ForASubstringMatch_AnywhereInTheName()
        {
            Assert.IsTrue(VocabularyPicker.MatchesFilter("LeftEyeSocket", "Eye"), "A substring in the middle must match.");
            Assert.IsTrue(VocabularyPicker.MatchesFilter("EyeL", "L"), "A trailing substring must match.");
            Assert.IsTrue(VocabularyPicker.MatchesFilter("EyeL", "Eye"), "A leading substring must match.");
        }

        [Test]
        public void MatchesFilter_IsTrue_ForADifferentlyCasedSubstring()
        {
            Assert.IsTrue(VocabularyPicker.MatchesFilter("EyeL", "eyel"), "Filtering must be case-insensitive.");
            Assert.IsTrue(VocabularyPicker.MatchesFilter("eyel", "EYEL"));
        }

        [Test]
        public void MatchesFilter_IsFalse_WhenTheFilterTextDoesNotOccurInTheName()
        {
            Assert.IsFalse(VocabularyPicker.MatchesFilter("Jaw", "Eye"));
        }

        [Test]
        public void MatchesFilter_TreatsANullName_AsEmpty()
        {
            Assert.IsFalse(VocabularyPicker.MatchesFilter(null, "Eye"), "A null name matches nothing but an empty filter.");
            Assert.IsTrue(VocabularyPicker.MatchesFilter(null, string.Empty));
        }

        // -----------------------------------------------------------------------------------
        // IsNearDuplicateName ("Create tag..."'s guard, spec section 4.2.1).
        // -----------------------------------------------------------------------------------

        [Test]
        public void IsNearDuplicateName_IsFalse_ForAnEmptyRegistry()
        {
            TargetTagRegistry registry = CreateRegistry();

            Assert.IsFalse(VocabularyPicker.IsNearDuplicateName(registry, "Jaw"));
        }

        [Test]
        public void IsNearDuplicateName_IsFalse_ForANullRegistry()
        {
            // Cast, because a bare null matches both overloads. This fixture is about the registry
            // one — a picker opened before any vocabulary asset exists must not claim every name is
            // taken.
            Assert.IsFalse(VocabularyPicker.IsNearDuplicateName((IVocabularyRegistry)null, "Jaw"));
        }

        [Test]
        public void IsNearDuplicateName_IsTrue_ForAnExactCaseMatch()
        {
            TargetTagRegistry registry = CreateRegistry("Jaw");

            Assert.IsTrue(VocabularyPicker.IsNearDuplicateName(registry, "Jaw"));
        }

        [Test]
        public void IsNearDuplicateName_IsTrue_WhenTheExistingTagIsLowercaseAndTheCandidateIsNot()
        {
            TargetTagRegistry registry = CreateRegistry("jaw");

            Assert.IsTrue(
                VocabularyPicker.IsNearDuplicateName(registry, "Jaw"),
                "Jaw must be rejected as a near-duplicate of an existing jaw.");
        }

        [Test]
        public void IsNearDuplicateName_IsTrue_WhenTheExistingTagIsUppercaseAndTheCandidateIsNot()
        {
            TargetTagRegistry registry = CreateRegistry("Jaw");

            Assert.IsTrue(
                VocabularyPicker.IsNearDuplicateName(registry, "jaw"),
                "jaw must be rejected as a near-duplicate of an existing Jaw.");
        }

        [Test]
        public void IsNearDuplicateName_IsTrue_ForMixedCaseVariants_OfAnExistingTag()
        {
            TargetTagRegistry registry = CreateRegistry("EyeL");

            Assert.IsTrue(VocabularyPicker.IsNearDuplicateName(registry, "eyel"));
            Assert.IsTrue(VocabularyPicker.IsNearDuplicateName(registry, "EYEL"));
            Assert.IsTrue(VocabularyPicker.IsNearDuplicateName(registry, "eYeL"));
        }

        [Test]
        public void IsNearDuplicateName_IsTrue_WhenOnlyWhitespacePadsTheCandidate()
        {
            TargetTagRegistry registry = CreateRegistry("Jaw");

            Assert.IsTrue(
                VocabularyPicker.IsNearDuplicateName(registry, "  Jaw  "),
                "The candidate is trimmed before comparison, matching how the filter text itself is trimmed.");
        }

        [Test]
        public void IsNearDuplicateName_IsFalse_ForADistinctName()
        {
            TargetTagRegistry registry = CreateRegistry("Jaw");

            Assert.IsFalse(VocabularyPicker.IsNearDuplicateName(registry, "EyeL"));
        }

        [Test]
        public void IsNearDuplicateName_IsFalse_ForAPartialSubstringOfAnExistingName()
        {
            // Unlike MatchesFilter, this is an exact (trimmed, case-insensitive) equality check, not
            // a substring one - "Eye" must not be rejected just because "EyeL" already exists, or a
            // legitimately different tag could never be created.
            TargetTagRegistry registry = CreateRegistry("EyeL");

            Assert.IsFalse(VocabularyPicker.IsNearDuplicateName(registry, "Eye"));
        }

        [Test]
        public void IsNearDuplicateName_ChecksEveryEntry_NotJustTheFirst()
        {
            TargetTagRegistry registry = CreateRegistry("EyeL", "EyeR", "Jaw");

            Assert.IsTrue(VocabularyPicker.IsNearDuplicateName(registry, "jaw"));
            Assert.IsFalse(VocabularyPicker.IsNearDuplicateName(registry, "Ear"));
        }
    }
}
