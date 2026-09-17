// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// A108: every icon-text button must end up with exactly one variant class, and every
    /// registered tab glyph must actually rasterize ink, not silently resolve to null.
    /// </summary>
    public sealed class ToolkitButtonStyleTests
    {
        private const string PrimaryVariantClassName = "toolkit-primary-action";
        private const string SecondaryVariantClassName = "toolkit-button--secondary";
        private const string GhostVariantClassName = "toolkit-button--ghost";
        private const string DestructiveVariantClassName = "toolkit-button--destructive";

        private const int GlyphSize = 32;
        private const int MinimumInkedPixelCount = 40;

        private static void AssertExactVariantClasses(Button button, params string[] expectedClassNames)
        {
            string[] allVariantClassNames =
            {
                PrimaryVariantClassName, SecondaryVariantClassName, GhostVariantClassName, DestructiveVariantClassName
            };

            foreach (string variantClassName in allVariantClassNames)
            {
                bool expected = Array.IndexOf(expectedClassNames, variantClassName) >= 0;
                Assert.AreEqual(
                    expected,
                    button.ClassListContains(variantClassName),
                    variantClassName + " presence did not match expectation.");
            }
        }

        [Test]
        public void MakeIconTextButton_AppliesSecondaryWhenTheCallerNamesNoVariant()
        {
            Button button = ToolkitIcons.MakeIconTextButton(() => { }, "d_SaveAs", "tip", "Save");

            AssertExactVariantClasses(button, SecondaryVariantClassName);
        }

        [Test]
        public void MakePrimaryAction_CarriesOnlyThePrimaryVariant()
        {
            Button button = ToolkitChrome.MakePrimaryAction(() => { }, "d_SaveAs", "tip", "Bake");

            AssertExactVariantClasses(button, PrimaryVariantClassName);
        }

        [Test]
        public void StyleButton_ReplacesTheVariantRatherThanAccumulating()
        {
            Button button = new Button();

            ToolkitChrome.StyleButton(button, ToolkitButtonVariant.Secondary);
            AssertExactVariantClasses(button, SecondaryVariantClassName);

            ToolkitChrome.StyleButton(button, ToolkitButtonVariant.Ghost);
            AssertExactVariantClasses(button, GhostVariantClassName);

            ToolkitChrome.StyleButton(button, ToolkitButtonVariant.Destructive);
            AssertExactVariantClasses(button, GhostVariantClassName, DestructiveVariantClassName);

            ToolkitChrome.StyleButton(button, ToolkitButtonVariant.Primary);
            AssertExactVariantClasses(button, PrimaryVariantClassName);
        }

        [Test]
        public void Resolve_DrawsEveryGlyphId()
        {
            foreach (ToolkitGlyphId glyphId in Enum.GetValues(typeof(ToolkitGlyphId)))
            {
                Texture2D glyphTexture = ToolkitGlyphs.Resolve(glyphId);

                Assert.IsNotNull(glyphTexture, glyphId + " resolved to a null texture -- its shape hook may not be registered.");
                Assert.AreEqual(GlyphSize, glyphTexture.width, glyphId + " glyph has the wrong width.");
                Assert.AreEqual(GlyphSize, glyphTexture.height, glyphId + " glyph has the wrong height.");

                Color[] glyphPixels = glyphTexture.GetPixels();
                int inkedPixelCount = 0;
                foreach (Color pixel in glyphPixels)
                {
                    if (pixel.a > 0.5f)
                    {
                        inkedPixelCount++;
                    }
                }

                Assert.GreaterOrEqual(
                    inkedPixelCount,
                    MinimumInkedPixelCount,
                    glyphId + " rasterized with fewer than " + MinimumInkedPixelCount +
                    " opaque pixels -- its shape hook likely isn't registered and it fell back to a blank texture.");
            }
        }

        [Test]
        public void Resolve_ReturnsTheCachedTexture()
        {
            Texture2D firstResolve = ToolkitGlyphs.Resolve(ToolkitGlyphId.ClipEditor);
            Texture2D secondResolve = ToolkitGlyphs.Resolve(ToolkitGlyphId.ClipEditor);

            Assert.AreSame(firstResolve, secondResolve);
        }
    }
}
