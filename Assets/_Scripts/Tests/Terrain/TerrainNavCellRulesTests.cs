using NUnit.Framework;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainNavCellRulesTests
    {
        private static TerrainNavCellInput FlatGround(byte groundCost)
        {
            TerrainNavCellInput input = new TerrainNavCellInput();
            input.groundCost = groundCost;
            input.roadCost = TerrainNavCellRules.LeaveGroundCost;
            return input;
        }

        [Test]
        public void DeepWaterIsAWall()
        {
            TerrainNavCellInput input = FlatGround(1);
            input.deepWater = true;
            TerrainNavCell cell = TerrainNavCellRules.Classify(input);
            Assert.AreEqual(TerrainNavCellRules.WallCost, cell.cost);
            Assert.AreEqual(TerrainSurface.Deep, cell.surface);
        }

        [Test]
        public void SlopeOfExactlyFortyFiveDegreesIsAWallButFortyFourPointNineIsNot()
        {
            TerrainNavCellInput input = FlatGround(1);
            input.slopeDegrees = 45f;
            Assert.AreEqual(TerrainNavCellRules.WallCost, TerrainNavCellRules.Classify(input).cost);
            input.slopeDegrees = 44.9f;
            Assert.AreNotEqual(TerrainNavCellRules.WallCost, TerrainNavCellRules.Classify(input).cost);
        }

        [Test]
        public void HeavySlopeKeepsTheLargerOfGroundAndHeavyCost()
        {
            TerrainNavCellInput input = FlatGround(10);
            input.slopeDegrees = 30f;
            Assert.AreEqual(TerrainNavCellRules.HeavyCost, TerrainNavCellRules.Classify(input).cost);
            input.groundCost = 80;
            Assert.AreEqual(80, TerrainNavCellRules.Classify(input).cost);
        }

        [Test]
        public void ShallowWaterBeatsRoad()
        {
            TerrainNavCellInput input = FlatGround(1);
            input.shallowWater = true;
            input.onRoad = true;
            input.roadCost = 3;
            TerrainNavCell cell = TerrainNavCellRules.Classify(input);
            Assert.AreEqual(TerrainSurface.Shallow, cell.surface);
            Assert.AreEqual(TerrainNavCellRules.HeavyCost, cell.cost);
        }

        [Test]
        public void RoadWithLeaveGroundCostKeepsGroundCostAndOtherwiseUsesRoadCost()
        {
            TerrainNavCellInput input = FlatGround(7);
            input.onRoad = true;
            Assert.AreEqual(7, TerrainNavCellRules.Classify(input).cost);
            input.roadCost = 3;
            TerrainNavCell cell = TerrainNavCellRules.Classify(input);
            Assert.AreEqual(3, cell.cost);
            Assert.AreEqual(TerrainSurface.Road, cell.surface);
        }

        [Test]
        public void SteepRoadIsStillHeavy()
        {
            TerrainNavCellInput input = FlatGround(1);
            input.onRoad = true;
            input.roadCost = 2;
            input.slopeDegrees = 30f;
            Assert.AreEqual(TerrainNavCellRules.HeavyCost, TerrainNavCellRules.Classify(input).cost);
        }

        [Test]
        public void BridgeClearsDeepWallToRoadAtGroundCost()
        {
            TerrainNavCellInput input = FlatGround(4);
            input.deepWater = true;
            TerrainNavCell crossed = TerrainNavCellRules.ApplyCrossing(TerrainNavCellRules.Classify(input), true, 4);
            Assert.AreEqual(TerrainSurface.Road, crossed.surface);
            Assert.AreEqual(4, crossed.cost);
        }

        [Test]
        public void FordTurnsDeepIntoShallowAndLeavesNormalAlone()
        {
            TerrainNavCell deepCell = new TerrainNavCell { cost = TerrainNavCellRules.WallCost, surface = TerrainSurface.Deep };
            TerrainNavCell forded = TerrainNavCellRules.ApplyCrossing(deepCell, false, 1);
            Assert.AreEqual(TerrainSurface.Shallow, forded.surface);
            Assert.AreEqual(TerrainNavCellRules.HeavyCost, forded.cost);

            TerrainNavCell normalCell = new TerrainNavCell { cost = 9, surface = TerrainSurface.Normal };
            TerrainNavCell unchanged = TerrainNavCellRules.ApplyCrossing(normalCell, false, 1);
            Assert.AreEqual(9, unchanged.cost);
            Assert.AreEqual(TerrainSurface.Normal, unchanged.surface);
        }
    }
}
