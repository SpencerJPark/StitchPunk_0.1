using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainCompositorTests
    {
        private const int Resolution = 8;

        private static readonly Color32 GrassColour = new Color32(255, 0, 0, 255);
        private static readonly Color32 ForestColour = new Color32(0, 255, 0, 255);
        private static readonly Color32 StoneColour = new Color32(0, 0, 255, 255);
        private static readonly Color32 UnpaintedColour = new Color32(0, 0, 0, 0);

        private static Color32[] FilledArray(Color32 colour)
        {
            Color32[] pixels = new Color32[Resolution * Resolution];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++) pixels[pixelIndex] = colour;
            return pixels;
        }

        private static RectInt FullRect()
        {
            return new RectInt(0, 0, Resolution, Resolution);
        }

        [Test]
        public void PaintOverlay_BeatsBaseWhereAlphaIsSet_AndLeavesBaseElsewhere()
        {
            Color32[] baseMask = FilledArray(GrassColour);
            Color32[] overlay = FilledArray(UnpaintedColour);
            overlay[10] = ForestColour;
            Color32[] composite = new Color32[Resolution * Resolution];

            TerrainCompositor.CompositeTexels(baseMask, overlay, null, Resolution, FullRect(), composite);

            Assert.AreEqual(ForestColour, composite[10]);
            Assert.AreEqual(GrassColour, composite[11]);
        }

        [Test]
        public void Road_BeatsPaintOverlay_WhereBothAreSet()
        {
            Color32[] baseMask = FilledArray(GrassColour);
            Color32[] overlay = FilledArray(UnpaintedColour);
            Color32[] road = FilledArray(UnpaintedColour);
            overlay[5] = ForestColour;
            road[5] = StoneColour;
            Color32[] composite = new Color32[Resolution * Resolution];

            TerrainCompositor.CompositeTexels(baseMask, overlay, road, Resolution, FullRect(), composite);

            Assert.AreEqual(StoneColour, composite[5]);
        }

        [Test]
        public void TexelRect_OnlyTouchesItsOwnTexels()
        {
            Color32 sentinel = new Color32(1, 2, 3, 4);
            Color32[] composite = FilledArray(sentinel);

            TerrainCompositor.CompositeTexels(FilledArray(GrassColour), null, null, Resolution, new RectInt(2, 2, 2, 2), composite);

            Assert.AreEqual(GrassColour, composite[2 * Resolution + 2]);
            Assert.AreEqual(GrassColour, composite[3 * Resolution + 3]);
            Assert.AreEqual(sentinel, composite[0]);
            Assert.AreEqual(sentinel, composite[2 * Resolution + 4]);
            Assert.AreEqual(sentinel, composite[4 * Resolution + 2]);
        }

        [Test]
        public void NullOverlayAndRoad_ReturnsBaseUnchanged()
        {
            Color32[] baseMask = FilledArray(StoneColour);
            Color32[] composite = new Color32[Resolution * Resolution];

            TerrainCompositor.CompositeTexels(baseMask, null, null, Resolution, FullRect(), composite);

            CollectionAssert.AreEqual(baseMask, composite);
        }
    }
}
