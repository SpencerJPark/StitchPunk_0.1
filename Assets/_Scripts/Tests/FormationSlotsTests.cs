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
        public void AssignRoundedSlots_GivesEveryUnitADistinctSpacedSlot()
        {
            float3 center = new float3(10f, 0f, 10f);
            NativeArray<float3> unitPositions = new NativeArray<float3>(UnitCount, Allocator.Temp);
            NativeArray<float3> destinations = new NativeArray<float3>(UnitCount, Allocator.Temp);
            for (int unitIndex = 0; unitIndex < UnitCount; unitIndex++)
            {
                unitPositions[unitIndex] = float3.zero;
            }

            FormationSlots.AssignRoundedSlots(unitPositions, center, TestSpacing, ref destinations);

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
