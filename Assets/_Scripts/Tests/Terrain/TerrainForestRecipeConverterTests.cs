#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainForestRecipeConverterTests
    {
        [Test]
        public void ConvertedRecipeReproducesEachForestRuleDensity()
        {
            TerrainScatterSet scatterSet = ScriptableObject.CreateInstance<TerrainScatterSet>();
            TerrainScatterRuleData treeRule = TerrainScatterSet.CreateRule("Tree");
            treeRule.densityPer100SquareMetres = 1f;
            treeRule.slopeRangeDegrees = new Vector2(0f, 35f);
            treeRule.minSpacingMetres = 3.5f;
            TerrainScatterPrefab treePrefab = new TerrainScatterPrefab();
            treePrefab.prefab = null;
            treePrefab.weight = 1f;
            treeRule.prefabs.Add(treePrefab);
            TerrainScatterRuleData dressingRule = TerrainScatterSet.CreateRule("Dressing");
            dressingRule.densityPer100SquareMetres = 3f;
            dressingRule.slopeRangeDegrees = new Vector2(0f, 30f);
            dressingRule.minSpacingMetres = 3f;
            dressingRule.tier = TerrainScatterTier.Dressing;
            dressingRule.cardAtlasEntry = 4;
            scatterSet.rules.Add(treeRule);
            scatterSet.rules.Add(dressingRule);

            TerrainForestRecipe forest = ScriptableObject.CreateInstance<TerrainForestRecipe>();
            forest.scatterSet = scatterSet;
            TerrainFeatureRecipe target = ScriptableObject.CreateInstance<TerrainFeatureRecipe>();
            TerrainScatterSet transientSet = null;
            try
            {
                TerrainForestRecipeConverter.CopyForestInto(forest, target);
                transientSet = TerrainAreaFiller.ScatterSetFromFillRecipe(target, (TerrainFillPlacer)target.placer);

                Assert.AreEqual(2, transientSet.rules.Count);
                Assert.AreEqual(1f, transientSet.rules[0].densityPer100SquareMetres, 1e-4f);
                Assert.AreEqual(3f, transientSet.rules[1].densityPer100SquareMetres, 1e-4f);
                Assert.AreEqual(35f, transientSet.rules[0].slopeRangeDegrees.y, 1e-4f);
                Assert.AreEqual(3f, transientSet.rules[0].minSpacingMetres, 1e-4f);
                Assert.AreEqual(TerrainScatterTier.Dressing, transientSet.rules[1].tier);
                Assert.AreEqual(4, transientSet.rules[1].cardAtlasEntry);
            }
            finally
            {
                Object.DestroyImmediate(transientSet);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(forest);
                Object.DestroyImmediate(scatterSet);
            }
        }
    }
}
#endif
