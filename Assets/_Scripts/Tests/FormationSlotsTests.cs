using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class FormationSlotsTests
    {
        private const int UnitCount = 12;
        private const float TestSpacing = 1.2f;

        // Units stacked on one point must still each get their own spot, or they all walk to the same tile.
        [Test]
        public void AssignFrontToBackSlots_GivesEveryUnitADistinctSpacedSlot()
        {
            float3 center = new float3(10f, 0f, 10f);
            NativeArray<float3> unitPositions = new NativeArray<float3>(UnitCount, Allocator.Temp);
            NativeArray<float> unitSpeeds = new NativeArray<float>(UnitCount, Allocator.Temp);
            NativeArray<float3> destinations = new NativeArray<float3>(UnitCount, Allocator.Temp);
            for (int unitIndex = 0; unitIndex < UnitCount; unitIndex++)
            {
                unitPositions[unitIndex] = float3.zero;
                unitSpeeds[unitIndex] = 5f;
            }

            FormationSlots.AssignFrontToBackSlots(unitPositions, unitSpeeds, center, TestSpacing, ref destinations);

            float minimumAllowedGap = TestSpacing * 0.5f;
            float maximumAllowedRadius = TestSpacing * 3f;
            for (int firstIndex = 0; firstIndex < UnitCount; firstIndex++)
            {
                float3 firstDestination = destinations[firstIndex];
                float distanceFromCenter = math.distance(new float3(firstDestination.x, 0f, firstDestination.z), new float3(center.x, 0f, center.z));
                Assert.LessOrEqual(distanceFromCenter, maximumAllowedRadius, "Unit " + firstIndex + " destination is too far from the formation center");

                for (int secondIndex = firstIndex + 1; secondIndex < UnitCount; secondIndex++)
                {
                    float3 secondDestination = destinations[secondIndex];
                    float gapXZ = math.distance(new float3(firstDestination.x, 0f, firstDestination.z), new float3(secondDestination.x, 0f, secondDestination.z));
                    Assert.GreaterOrEqual(gapXZ, minimumAllowedGap, "Units " + firstIndex + " and " + secondIndex + " were given slots too close together");
                }
            }

            unitPositions.Dispose();
            unitSpeeds.Dispose();
            destinations.Dispose();
        }

        // A column marching +Z: the unit that arrives first must end up ahead of (deeper than) the
        // unit that arrives last, or the latecomer walks through the early arrivals to reach the front.
        [Test]
        public void AssignFrontToBackSlots_FirstArrivalTakesTheFrontAndLastTakesTheBack()
        {
            float3 center = new float3(0f, 0f, 20f);
            NativeArray<float3> unitPositions = new NativeArray<float3>(UnitCount, Allocator.Temp);
            NativeArray<float> unitSpeeds = new NativeArray<float>(UnitCount, Allocator.Temp);
            NativeArray<float3> destinations = new NativeArray<float3>(UnitCount, Allocator.Temp);
            for (int unitIndex = 0; unitIndex < UnitCount; unitIndex++)
            {
                // Unit 0 is closest to the destination, unit 11 the farthest back.
                unitPositions[unitIndex] = new float3(0f, 0f, 10f - unitIndex);
                unitSpeeds[unitIndex] = 5f;
            }

            FormationSlots.AssignFrontToBackSlots(unitPositions, unitSpeeds, center, TestSpacing, ref destinations);

            float firstArrivalDepth = destinations[0].z - center.z;
            float lastArrivalDepth = destinations[UnitCount - 1].z - center.z;
            Assert.Greater(firstArrivalDepth, 0f, "The first arrival should stand in the front half");
            Assert.Less(lastArrivalDepth, 0f, "The last arrival should stand in the back half");

            unitPositions.Dispose();
            unitSpeeds.Dispose();
            destinations.Dispose();
        }

        // The centre slot must sit exactly on the rally point, and later slots must actually move off it.
        [Test]
        public void RoundedSlotOffset_CentreSlotIsZeroAndLaterSlotsAreOffset()
        {
            Assert.AreEqual(float3.zero, FormationSlots.RoundedSlotOffset(0, TestSpacing));
            Assert.AreNotEqual(float3.zero, FormationSlots.RoundedSlotOffset(5, TestSpacing));
        }
    }
}
