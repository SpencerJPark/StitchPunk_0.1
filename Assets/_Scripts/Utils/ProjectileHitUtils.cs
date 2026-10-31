using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct ProjectileHitCandidate
{
    public Entity entity;
    public float3 position;
}

[BurstCompile]
public static class ProjectileHitUtils
{
    public const float UNIT_HIT_RADIUS = 0.4f;
    public const float CLEAR_ORIGIN_DISTANCE = 1.2f;

    private const float DEGENERATE_SEGMENT_LENGTH_SQUARED = 1e-10f;

    // Swept XZ test (Y ignored, 2.5D): the candidate touched first along previous -> current, not the nearest to current.
    public static bool FindFirstHitAlongSegment(
        in float3 previousPosition, in float3 currentPosition, float projectileRadius,
        Entity shooter, in float3 clearOrigin,
        in NativeArray<ProjectileHitCandidate> candidates,
        out int hitCandidateIndex, out float hitFractionAlongSegment)
    {
        hitCandidateIndex = -1;
        hitFractionAlongSegment = 0f;

        float2 segmentStart = previousPosition.xz;
        float2 segmentDelta = currentPosition.xz - segmentStart;

        float clearFraction;
        if (!TryFindFractionWhereSegmentLeavesClearOrigin(segmentStart, segmentDelta, clearOrigin.xz, out clearFraction))
            return false;

        float combinedRadius = UNIT_HIT_RADIUS + projectileRadius;
        float bestFraction = float.MaxValue;

        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            ProjectileHitCandidate candidate = candidates[candidateIndex];
            if (candidate.entity == shooter)
                continue;

            float enterFraction;
            float exitFraction;
            if (!TryFindCircleOverlapInterval(segmentStart, segmentDelta, candidate.position.xz, combinedRadius, out enterFraction, out exitFraction))
                continue;

            float effectiveFraction = math.max(enterFraction, clearFraction);
            if (effectiveFraction > exitFraction || effectiveFraction >= bestFraction)
                continue;

            bestFraction = effectiveFraction;
            hitCandidateIndex = candidateIndex;
        }

        if (hitCandidateIndex < 0)
            return false;

        hitFractionAlongSegment = bestFraction;
        return true;
    }

    private static bool TryFindCircleOverlapInterval(
        float2 segmentStart, float2 segmentDelta, float2 circleCenter, float circleRadius,
        out float enterFraction, out float exitFraction)
    {
        enterFraction = 0f;
        exitFraction = 0f;

        float2 startToCenter = segmentStart - circleCenter;
        float startDistanceSquaredMinusRadiusSquared = math.dot(startToCenter, startToCenter) - circleRadius * circleRadius;
        float quadraticA = math.dot(segmentDelta, segmentDelta);

        if (quadraticA < DEGENERATE_SEGMENT_LENGTH_SQUARED)
            return startDistanceSquaredMinusRadiusSquared <= 0f;

        float quadraticHalfB = math.dot(startToCenter, segmentDelta);
        float discriminant = quadraticHalfB * quadraticHalfB - quadraticA * startDistanceSquaredMinusRadiusSquared;
        if (discriminant < 0f)
            return false;

        float rootSpan = math.sqrt(discriminant);
        float rawEnter = (-quadraticHalfB - rootSpan) / quadraticA;
        float rawExit = (-quadraticHalfB + rootSpan) / quadraticA;
        enterFraction = math.max(rawEnter, 0f);
        exitFraction = math.min(rawExit, 1f);
        return enterFraction <= exitFraction;
    }

    private static bool TryFindFractionWhereSegmentLeavesClearOrigin(
        float2 segmentStart, float2 segmentDelta, float2 clearOrigin, out float clearFraction)
    {
        clearFraction = 0f;

        float2 originToStart = segmentStart - clearOrigin;
        float startDistanceSquaredMinusClearSquared =
            math.dot(originToStart, originToStart) - CLEAR_ORIGIN_DISTANCE * CLEAR_ORIGIN_DISTANCE;
        if (startDistanceSquaredMinusClearSquared >= 0f)
            return true;

        float quadraticA = math.dot(segmentDelta, segmentDelta);
        if (quadraticA < DEGENERATE_SEGMENT_LENGTH_SQUARED)
            return false;

        // Start is inside the clear circle, so the single positive root is where the path exits it.
        float quadraticHalfB = math.dot(originToStart, segmentDelta);
        float discriminant = quadraticHalfB * quadraticHalfB - quadraticA * startDistanceSquaredMinusClearSquared;
        float exitFraction = (-quadraticHalfB + math.sqrt(math.max(discriminant, 0f))) / quadraticA;
        if (exitFraction > 1f)
            return false;

        clearFraction = exitFraction;
        return true;
    }
}
