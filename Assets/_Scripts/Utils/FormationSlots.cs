using Unity.Collections;
using Unity.Mathematics;

// Rounded "sunflower" formation (Vogel spiral): slot k sits at radius ~sqrt(k) on the golden angle,
// so slots fill a disc evenly and slot k's position never depends on how many slots exist. That is
// what lets a rally point hand out slots one at a time while a move order lays out all of them.
public static class FormationSlots
{
    public const float DefaultSpacing = 1.2f;

    // How many slots a rally point fills front-to-back before it starts growing outward.
    public const int RallyFormationCapacity = 24;

    private const float GoldenAngleRadians = 2.39996323f;

    // Each spiral point owns about pi * scale^2 of area, so scale = spacing / sqrt(pi) keeps
    // neighbouring units roughly `spacing` apart.
    private const float InverseSquareRootOfPi = 0.5641896f;

    private const float MinimumSpeedForArrivalEstimate = 0.1f;

    public static float3 RoundedSlotOffset(int slotIndex, float spacing)
    {
        if (slotIndex <= 0) return float3.zero;

        float radius = spacing * InverseSquareRootOfPi * math.sqrt(slotIndex + 0.5f);
        float angle = slotIndex * GoldenAngleRadians;
        return new float3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);
    }

    // The direction a group travels to reach `center`, flattened; +Z when the group is already there.
    public static float3 TravelDirection(float3 fromPosition, float3 center)
    {
        float3 travel = center - fromPosition;
        travel.y = 0f;
        return math.lengthsq(travel) > 0.0001f ? math.normalize(travel) : new float3(0f, 0f, 1f);
    }

    // How far forward (along the travel direction) a slot sits; the front of the formation is largest.
    public static float SlotDepth(int slotIndex, float spacing, float3 travelDirection)
    {
        return math.dot(RoundedSlotOffset(slotIndex, spacing), travelDirection);
    }

    // First arrivals take the front of the formation and late arrivals the back, so nobody has to
    // walk through units already standing in their slots. Arrival order is estimated from distance
    // and speed. Filled in rows across the travel direction; within a row units pair left-to-right
    // with slots so they do not cross each other either.
    public static void AssignFrontToBackSlots(
        in NativeArray<float3> unitPositions,
        in NativeArray<float> unitSpeeds,
        float3 center,
        float spacing,
        ref NativeArray<float3> destinations)
    {
        int unitCount = unitPositions.Length;
        if (unitCount == 0) return;

        float3 groupCentre = float3.zero;
        for (int unitIndex = 0; unitIndex < unitCount; unitIndex++)
            groupCentre += unitPositions[unitIndex];
        groupCentre /= unitCount;

        float3 travelDirection = TravelDirection(groupCentre, center);
        float3 lateralDirection = new float3(travelDirection.z, 0f, -travelDirection.x);

        NativeArray<int> unitsByArrival = new NativeArray<int>(unitCount, Allocator.Temp);
        NativeArray<float> arrivalEstimates = new NativeArray<float>(unitCount, Allocator.Temp);
        NativeArray<int> slotsFrontToBack = new NativeArray<int>(unitCount, Allocator.Temp);
        NativeArray<float> slotDepths = new NativeArray<float>(unitCount, Allocator.Temp);

        for (int unitIndex = 0; unitIndex < unitCount; unitIndex++)
        {
            float3 toCenter = center - unitPositions[unitIndex];
            float distance = math.length(toCenter.xz);
            float speed = math.max(unitSpeeds[unitIndex], MinimumSpeedForArrivalEstimate);
            unitsByArrival[unitIndex] = unitIndex;
            arrivalEstimates[unitIndex] = distance / speed;
        }
        SortIndicesAscending(ref unitsByArrival, in arrivalEstimates, 0, unitCount);

        for (int slotIndex = 0; slotIndex < unitCount; slotIndex++)
        {
            slotsFrontToBack[slotIndex] = slotIndex;
            slotDepths[slotIndex] = -SlotDepth(slotIndex, spacing, travelDirection);
        }
        SortIndicesAscending(ref slotsFrontToBack, in slotDepths, 0, unitCount);

        int rowSize = math.max(1, (int)math.round(math.sqrt(unitCount)));
        NativeArray<float> unitLateral = new NativeArray<float>(unitCount, Allocator.Temp);
        NativeArray<float> slotLateral = new NativeArray<float>(unitCount, Allocator.Temp);
        for (int unitIndex = 0; unitIndex < unitCount; unitIndex++)
            unitLateral[unitIndex] = math.dot(unitPositions[unitIndex] - center, lateralDirection);
        for (int slotIndex = 0; slotIndex < unitCount; slotIndex++)
            slotLateral[slotIndex] = math.dot(RoundedSlotOffset(slotIndex, spacing), lateralDirection);

        for (int rowStart = 0; rowStart < unitCount; rowStart += rowSize)
        {
            int rowLength = math.min(rowSize, unitCount - rowStart);
            SortIndicesAscending(ref unitsByArrival, in unitLateral, rowStart, rowLength);
            SortIndicesAscending(ref slotsFrontToBack, in slotLateral, rowStart, rowLength);
            for (int rowOffset = 0; rowOffset < rowLength; rowOffset++)
            {
                int unitIndex = unitsByArrival[rowStart + rowOffset];
                int slotIndex = slotsFrontToBack[rowStart + rowOffset];
                destinations[unitIndex] = center + RoundedSlotOffset(slotIndex, spacing);
            }
        }

        unitsByArrival.Dispose();
        arrivalEstimates.Dispose();
        slotsFrontToBack.Dispose();
        slotDepths.Dispose();
        unitLateral.Dispose();
        slotLateral.Dispose();
    }

    // Insertion sort of indices[start .. start+length) by keys[index]; selections are squad-sized.
    private static void SortIndicesAscending(ref NativeArray<int> indices, in NativeArray<float> keys, int start, int length)
    {
        for (int sortedEnd = start + 1; sortedEnd < start + length; sortedEnd++)
        {
            int movingIndex = indices[sortedEnd];
            float movingKey = keys[movingIndex];
            int insertAt = sortedEnd - 1;
            while (insertAt >= start && keys[indices[insertAt]] > movingKey)
            {
                indices[insertAt + 1] = indices[insertAt];
                insertAt--;
            }
            indices[insertAt + 1] = movingIndex;
        }
    }
}
