using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace StitchPunk.Tests
{
    // Pure-math coverage of the colour axis in DesignApplyUtil: rolled-colour lookup/upsert,
    // ResolveColor's slot semantics (None skip, [min,max] window clamp on the shared roll,
    // minColorIndex fallback when unrolled, alternative-colour selection via the slot flag or the
    // character's alternate mode), and the CharacterPalette capacity guard. These pin the CURRENT
    // behaviour.
    [TestFixture]
    public sealed class ColorPaletteResolveTests
    {
        // Test library: Skin = 4 colours, Hair = 2 colours, every other palette the 1-entry fallback
        // the baking system also produces. Each colour's RED channel encodes
        // (paletteSlot * 10 + colourIndex); the ALTERNATIVE variant carries the same red with
        // GREEN = 1, so assertions can tell primary from alternative.
        private static BlobAssetReference<ColorPaletteLibraryBlob> BuildTestLibrary()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref ColorPaletteLibraryBlob root = ref builder.ConstructRoot<ColorPaletteLibraryBlob>();

            int paletteCount = BlobLibraryUtils.EnumCount<ColorPaletteType>();
            BlobBuilderArray<ColorPaletteDef> palettesBuilder = builder.Allocate(ref root.palettes, paletteCount);
            for (int slotIndex = 0; slotIndex < paletteCount; slotIndex++)
            {
                palettesBuilder[slotIndex].id = (ColorPaletteType)slotIndex;

                ColorPaletteType paletteType = (ColorPaletteType)slotIndex;
                int colorCount = paletteType == ColorPaletteType.Skin ? 4
                    : paletteType == ColorPaletteType.Hair ? 2
                    : 1;

                BlobBuilderArray<ColorBlob> colorsBuilder = builder.Allocate(ref palettesBuilder[slotIndex].colors, colorCount);
                for (int colorIndex = 0; colorIndex < colorCount; colorIndex++)
                {
                    colorsBuilder[colorIndex] = new ColorBlob
                    {
                        color       = new float4(slotIndex * 10 + colorIndex, 0f, 0f, 1f),
                        alternative = new float4(slotIndex * 10 + colorIndex, 1f, 0f, 1f),
                    };
                }
            }

            BlobAssetReference<ColorPaletteLibraryBlob> blob =
                builder.CreateBlobAssetReference<ColorPaletteLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        private static float ExpectedRed(ColorPaletteType palette, int colorIndex)
            => (int)palette * 10f + colorIndex;

        [Test]
        public void GetColorIndex_UnsetPaletteReturnsMinusOne()
        {
            FixedList64Bytes<ColorChoice> colors = new FixedList64Bytes<ColorChoice>();
            Assert.AreEqual(-1, DesignApplyUtil.GetColorIndex(colors, ColorPaletteType.Hair));
        }

        [Test]
        public void SetColorIndex_UpsertsAnExistingPaletteInsteadOfAppending()
        {
            FixedList64Bytes<ColorChoice> colors = new FixedList64Bytes<ColorChoice>();
            DesignApplyUtil.SetColorIndex(ref colors, ColorPaletteType.Skin, 1);
            DesignApplyUtil.SetColorIndex(ref colors, ColorPaletteType.Skin, 2);

            Assert.AreEqual(1, colors.Length);
            Assert.AreEqual(2, DesignApplyUtil.GetColorIndex(colors, ColorPaletteType.Skin));
        }

        [Test]
        public void ResolveColor_NoneSlotReturnsFalse()
        {
            BlobAssetReference<ColorPaletteLibraryBlob> library = BuildTestLibrary();
            try
            {
                CharacterPalette palette = new CharacterPalette();
                PartPaletteSlot slot = new PartPaletteSlot { palette = ColorPaletteType.None };
                Assert.IsFalse(DesignApplyUtil.ResolveColor(ref library.Value, palette, slot, out float4 _));
            }
            finally { library.Dispose(); }
        }

        [Test]
        public void ResolveColor_UnrolledPaletteFallsBackToMinColorIndex()
        {
            BlobAssetReference<ColorPaletteLibraryBlob> library = BuildTestLibrary();
            try
            {
                CharacterPalette palette = new CharacterPalette();
                PartPaletteSlot slot = new PartPaletteSlot
                {
                    palette       = ColorPaletteType.Skin,
                    minColorIndex = 2,
                    maxColorIndex = 3,
                };

                Assert.IsTrue(DesignApplyUtil.ResolveColor(ref library.Value, palette, slot, out float4 color));
                Assert.AreEqual(ExpectedRed(ColorPaletteType.Skin, 2), color.x);
            }
            finally { library.Dispose(); }
        }

        [Test]
        public void ResolveColor_RolledIndexInsideTheWindowIsUsedAsIs()
        {
            BlobAssetReference<ColorPaletteLibraryBlob> library = BuildTestLibrary();
            try
            {
                CharacterPalette palette = new CharacterPalette();
                DesignApplyUtil.SetColorIndex(ref palette.colors, ColorPaletteType.Skin, 2);

                PartPaletteSlot slot = new PartPaletteSlot
                {
                    palette       = ColorPaletteType.Skin,
                    minColorIndex = 0,
                    maxColorIndex = 3,
                };

                Assert.IsTrue(DesignApplyUtil.ResolveColor(ref library.Value, palette, slot, out float4 color));
                Assert.AreEqual(ExpectedRed(ColorPaletteType.Skin, 2), color.x);
            }
            finally { library.Dispose(); }
        }

        [Test]
        public void ResolveColor_RolledIndexOutsideTheWindowClampsIntoIt()
        {
            BlobAssetReference<ColorPaletteLibraryBlob> library = BuildTestLibrary();
            try
            {
                // Shared Skin roll = 3, but this slot only allows [0,1] → clamps to 1. Parts with a
                // wider window keep 3 — "as close as possible" sharing.
                CharacterPalette palette = new CharacterPalette();
                DesignApplyUtil.SetColorIndex(ref palette.colors, ColorPaletteType.Skin, 3);

                PartPaletteSlot slot = new PartPaletteSlot
                {
                    palette       = ColorPaletteType.Skin,
                    minColorIndex = 0,
                    maxColorIndex = 1,
                };

                Assert.IsTrue(DesignApplyUtil.ResolveColor(ref library.Value, palette, slot, out float4 color));
                Assert.AreEqual(ExpectedRed(ColorPaletteType.Skin, 1), color.x);
            }
            finally { library.Dispose(); }
        }

        [Test]
        public void ResolveColor_WindowPastThePaletteLengthClampsToTheLastEntry()
        {
            BlobAssetReference<ColorPaletteLibraryBlob> library = BuildTestLibrary();
            try
            {
                // Hair has 2 entries; an over-long authored window [5,9] clamps to the last entry.
                CharacterPalette palette = new CharacterPalette();
                PartPaletteSlot slot = new PartPaletteSlot
                {
                    palette       = ColorPaletteType.Hair,
                    minColorIndex = 5,
                    maxColorIndex = 9,
                };

                Assert.IsTrue(DesignApplyUtil.ResolveColor(ref library.Value, palette, slot, out float4 color));
                Assert.AreEqual(ExpectedRed(ColorPaletteType.Hair, 1), color.x);
            }
            finally { library.Dispose(); }
        }

        [Test]
        public void ResolveColor_SlotAlternateFlagPicksTheAlternativeVariant()
        {
            BlobAssetReference<ColorPaletteLibraryBlob> library = BuildTestLibrary();
            try
            {
                CharacterPalette palette = new CharacterPalette();
                DesignApplyUtil.SetColorIndex(ref palette.colors, ColorPaletteType.Skin, 1);

                PartPaletteSlot slot = new PartPaletteSlot
                {
                    palette           = ColorPaletteType.Skin,
                    minColorIndex     = 0,
                    maxColorIndex     = 3,
                    useAlternateColor = true,
                };

                Assert.IsTrue(DesignApplyUtil.ResolveColor(ref library.Value, palette, slot, out float4 color));
                Assert.AreEqual(ExpectedRed(ColorPaletteType.Skin, 1), color.x, "Same rolled identity …");
                Assert.AreEqual(1f, color.y, "… but the ALTERNATIVE variant (green marker) is shown.");
            }
            finally { library.Dispose(); }
        }

        [Test]
        public void ResolveColor_CharacterAlternateModeFlipsEverySlotToTheAlternative()
        {
            BlobAssetReference<ColorPaletteLibraryBlob> library = BuildTestLibrary();
            try
            {
                // Zombify: the character keeps its rolled identity (index 1) but shows the
                // corresponding alternative (zombie) colour — no palette swap involved.
                CharacterPalette palette = new CharacterPalette { useAlternateColors = 1 };
                DesignApplyUtil.SetColorIndex(ref palette.colors, ColorPaletteType.Skin, 1);

                PartPaletteSlot slot = new PartPaletteSlot
                {
                    palette       = ColorPaletteType.Skin,
                    minColorIndex = 0,
                    maxColorIndex = 3,
                };

                Assert.IsTrue(DesignApplyUtil.ResolveColor(ref library.Value, palette, slot, out float4 color));
                Assert.AreEqual(ExpectedRed(ColorPaletteType.Skin, 1), color.x);
                Assert.AreEqual(1f, color.y);
            }
            finally { library.Dispose(); }
        }

        [Test]
        public void SetColorIndex_AtCapacityDropsTheNewEntryWithAWarningInsteadOfThrowing()
        {
            FixedList64Bytes<ColorChoice> colors = new FixedList64Bytes<ColorChoice>();
            int capacity = colors.Capacity;

            for (int entryIndex = 0; entryIndex < capacity; entryIndex++)
                DesignApplyUtil.SetColorIndex(ref colors, (ColorPaletteType)(entryIndex + 1), 0);
            Assert.AreEqual(capacity, colors.Length);

            LogAssert.Expect(LogType.Warning, new Regex("colour choice capacity"));
            DesignApplyUtil.SetColorIndex(ref colors, (ColorPaletteType)(capacity + 1), 0);

            Assert.AreEqual(capacity, colors.Length,
                "The over-capacity colour entry must be dropped (with a warning), not appended or thrown.");
        }
    }
}
