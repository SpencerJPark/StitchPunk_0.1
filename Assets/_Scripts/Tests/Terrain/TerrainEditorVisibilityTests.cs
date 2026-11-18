using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainEditorVisibilityTests
    {
        private bool toolsLiveBeforeTest;
        private int changedCount;
        private TerrainFeatureRecipe recipe;

        private void CountChanged()
        {
            changedCount++;
        }

        [SetUp]
        public void SetUp()
        {
            toolsLiveBeforeTest = TerrainEditorVisibility.ToolsLive;
            TerrainEditorVisibility.SetToolsLive(false);
            changedCount = 0;
            TerrainEditorVisibility.Changed += CountChanged;
        }

        [TearDown]
        public void TearDown()
        {
            TerrainEditorVisibility.Changed -= CountChanged;
            TerrainFeatureTool.CancelDrawing();
            if (recipe != null)
            {
                Object.DestroyImmediate(recipe);
                recipe = null;
            }

            TerrainEditorVisibility.SetToolsLive(toolsLiveBeforeTest);
        }

        [Test]
        public void ChangedFiresOnlyOnceWhenTheSameValueIsSetTwice()
        {
            TerrainEditorVisibility.SetToolsLive(true);
            TerrainEditorVisibility.SetToolsLive(true);

            Assert.AreEqual(1, changedCount);
            Assert.IsTrue(TerrainEditorVisibility.ToolsLive);
        }

        [Test]
        public void GoingHiddenWhileDrawingCancelsTheDrawing()
        {
            recipe = ScriptableObject.CreateInstance<TerrainFeatureRecipe>();
            recipe.placer = new TerrainFillPlacer();
            recipe.supportedShapes = TerrainFeatureShapeSupport.ClosedArea;
            TerrainEditorVisibility.SetToolsLive(true);
            TerrainFeatureTool.BeginDraw(recipe, TerrainFeatureShapeKind.Curve);
            Assert.IsTrue(TerrainFeatureTool.IsDrawing);

            TerrainEditorVisibility.SetToolsLive(false);

            Assert.IsFalse(TerrainFeatureTool.IsDrawing);
        }
    }
}
