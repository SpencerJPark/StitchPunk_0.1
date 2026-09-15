// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers the A93 Events tab's emitted host stub: <see cref="AnimEventConsumerStubBuilder.BuildSource"/>
    /// must never emit <c>var</c> and must switch on exactly the route kinds it was asked for.
    /// </summary>
    public sealed class AnimEventConsumerStubBuilderTests
    {
        [Test]
        public void EmittedSource_HasNoVarAndOneCasePerKind()
        {
            string emittedSource = AnimEventConsumerStubBuilder.BuildSource(
                "Game", new List<AnimEventRouteKind> { AnimEventRouteKind.Sound });

            Assert.IsFalse(emittedSource.Contains(" var "), "Emitted source must never declare a var.");

            int soundCaseCount = CountOccurrences(emittedSource, "case AnimEventRouteKind.Sound:");
            Assert.AreEqual(1, soundCaseCount, "Expected exactly one Sound case.");

            int vfxCaseCount = CountOccurrences(emittedSource, "case AnimEventRouteKind.Vfx:");
            Assert.AreEqual(0, vfxCaseCount, "A kind that was not requested must not get a case.");

            Assert.IsTrue(emittedSource.Contains("GameAnimEventSystem : ISystem"));
        }

        private static int CountOccurrences(string sourceText, string searchText)
        {
            int occurrenceCount = 0;
            int searchIndex = 0;
            while (true)
            {
                int foundIndex = sourceText.IndexOf(searchText, searchIndex, System.StringComparison.Ordinal);
                if (foundIndex < 0)
                {
                    break;
                }
                occurrenceCount++;
                searchIndex = foundIndex + searchText.Length;
            }
            return occurrenceCount;
        }
    }
}
