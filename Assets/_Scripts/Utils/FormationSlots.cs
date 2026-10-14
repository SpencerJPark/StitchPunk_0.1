using Unity.Collections;
using Unity.Mathematics;

// Rounded "sunflower" formation (Vogel spiral): slot k sits at radius ~sqrt(k) on the golden angle,
// so slots fill a disc evenly and slot k's position never depends on how many slots exist. That is
// what lets a rally point hand out slots one at a time while a move order lays out all of them.
public static class FormationSlots
{
    public const float DefaultSpacing = 1.2f;

    private const float GoldenAngleRadians = 2.39996323f;

    // Each spiral point owns about pi * scale^2 of area, so scale = spacing / sqrt(pi) keeps
    // neighbouring units roughly `spacing` apart.
    private const float InverseSquareRootOfPi = 0.5641896f;

    public static float3 RoundedSlotOffset(int slotIndex, float spacing)
    {
        if (slotIndex <= 0) return float3.zero;

        float radius = spacing * InverseSquareRootOfPi * math.sqrt(slotIndex + 0.5f);
        float angle = slotIndex * GoldenAngleRadians;
        return new float3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);
    }

    // Gives every unit its own slot around `center`, nearest-first so paths cross as little as
    // possible. Greedy O(n^3), run once per order: fine up to ~100 selected units.
    public static void AssignRoundedSlots(
        in NativeArray<float3> unitPositions,
        float3 center,
        float spacing,
        ref NativeArray<float3> destinations)
    {
        int unitCount = unitPositions.Length;
        NativeArray<float3> slotPositions = new NativeArray<float3>(unitCount, Allocator.Temp);
        NativeArray<bool> slotTaken = new NativeArray<bool>(unitCount, Allocator.Temp);
        NativeArray<bool> unitAssigned = new NativeArray<bool>(unitCount, Allocator.Temp);

        for (int slotIndex = 0; slotIndex < unitCount; slotIndex++)
            slotPositions[slotIndex] = center + RoundedSlotOffset(slotIndex, spacing);

        for (int assignmentIndex = 0; assignmentIndex < unitCount; assignmentIndex++)
        {
            float bestDistanceSquared = float.MaxValue;
            int bestUnit = -1;
            int bestSlot = -1;
            for (int unitIndex = 0; unitIndex < unitCount; unitIndex++)
            {
                if (unitAssigned[unitIndex]) continue;
                for (int slotIndex = 0; slotIndex < unitCount; slotIndex++)
                {
                    if (slotTaken[slotIndex]) continue;
                    float3 toSlot = slotPositions[slotIndex] - unitPositions[unitIndex];
                    float distanceSquared = toSlot.x * toSlot.x + toSlot.z * toSlot.z;
                    if (distanceSquared >= bestDistanceSquared) continue;
                    bestDistanceSquared = distanceSquared;
                    bestUnit = unitIndex;
                    bestSlot = slotIndex;
                }
            }

            unitAssigned[bestUnit] = true;
            slotTaken[bestSlot] = true;
            destinations[bestUnit] = slotPositions[bestSlot];
        }

        slotPositions.Dispose();
        slotTaken.Dispose();
        unitAssigned.Dispose();
    }
}
