using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// An abstract unit's position is a pure function of world time, so a skipped day costs the same as a frame.
public static class AbstractTravel
{
    private static readonly float3 DefaultForward = new float3(0f, 0f, 1f);

    public static float BuildCumulativeLengths(ref DynamicBuffer<AbstractRouteWaypoint> waypoints)
    {
        float runningLength = 0f;
        for (int index = 0; index < waypoints.Length; index++)
        {
            if (index > 0)
            {
                runningLength += math.distance(waypoints[index - 1].position, waypoints[index].position);
            }

            AbstractRouteWaypoint waypoint = waypoints[index];
            waypoint.cumulativeLengthMetres = runningLength;
            waypoints[index] = waypoint;
        }

        return runningLength;
    }

    public static void PointAlongRoute(
        in DynamicBuffer<AbstractRouteWaypoint> waypoints,
        in AbstractRoute route,
        double nowWorldSeconds,
        out float3 position,
        out float3 forward,
        out bool isFinished)
    {
        forward = DefaultForward;
        float totalLength = route.totalLengthMetres;
        bool stopsAtEnd = route.endBehaviour == AbstractRouteEnd.Stop;

        if (waypoints.Length < 2 || totalLength <= 0f)
        {
            position = waypoints.Length > 0 ? waypoints[0].position : float3.zero;
            isFinished = stopsAtEnd;
            return;
        }

        double travelledMetres = math.max(0.0, nowWorldSeconds - route.departureWorldSeconds) * route.speedMetresPerSecond;
        double distanceAlongRoute;
        bool onReturnLeg = false;
        isFinished = false;

        if (route.endBehaviour == AbstractRouteEnd.Loop)
        {
            distanceAlongRoute = travelledMetres % totalLength;
        }
        else if (route.endBehaviour == AbstractRouteEnd.PingPong)
        {
            double foldedMetres = travelledMetres % (2.0 * totalLength);
            onReturnLeg = foldedMetres > totalLength;
            distanceAlongRoute = onReturnLeg ? 2.0 * totalLength - foldedMetres : foldedMetres;
        }
        else
        {
            isFinished = travelledMetres >= totalLength;
            distanceAlongRoute = math.min(travelledMetres, (double)totalLength);
        }

        float distance = (float)distanceAlongRoute;
        int segmentStart = FindSegmentStart(waypoints, distance);
        float3 startPosition = waypoints[segmentStart].position;
        float3 endPosition = waypoints[segmentStart + 1].position;
        float segmentLength = waypoints[segmentStart + 1].cumulativeLengthMetres - waypoints[segmentStart].cumulativeLengthMetres;
        float blend = segmentLength > 0f ? math.saturate((distance - waypoints[segmentStart].cumulativeLengthMetres) / segmentLength) : 0f;
        position = math.lerp(startPosition, endPosition, blend);

        float3 segmentDirection = endPosition - startPosition;
        if (math.lengthsq(segmentDirection) > 1e-12f)
        {
            forward = math.normalize(segmentDirection);
            if (onReturnLeg)
            {
                forward = -forward;
            }
        }
    }

    // Assumes the unit already has AbstractRoute, the AbstractRouteWaypoint buffer and a disabled AbstractSimulation baked on it.
    public static void BeginRoute(
        EntityCommandBuffer commandBuffer,
        Entity entity,
        in NativeArray<float3> waypointPositions,
        float speedMetresPerSecond,
        double nowWorldSeconds,
        AbstractRouteEnd endBehaviour)
    {
        float totalLength = 0f;
        for (int index = 1; index < waypointPositions.Length; index++)
        {
            totalLength += math.distance(waypointPositions[index - 1], waypointPositions[index]);
        }

        commandBuffer.SetComponent(entity, new AbstractRoute
        {
            departureWorldSeconds = nowWorldSeconds,
            speedMetresPerSecond = speedMetresPerSecond,
            totalLengthMetres = totalLength,
            endBehaviour = endBehaviour
        });

        commandBuffer.SetBuffer<AbstractRouteWaypoint>(entity);
        float runningLength = 0f;
        for (int index = 0; index < waypointPositions.Length; index++)
        {
            if (index > 0)
            {
                runningLength += math.distance(waypointPositions[index - 1], waypointPositions[index]);
            }

            commandBuffer.AppendToBuffer(entity, new AbstractRouteWaypoint
            {
                position = waypointPositions[index],
                cumulativeLengthMetres = runningLength
            });
        }

        commandBuffer.SetComponentEnabled<AbstractSimulation>(entity, true);
    }

    // Largest segment start index in [0, Length - 2] whose cumulative length is <= distance.
    private static int FindSegmentStart(in DynamicBuffer<AbstractRouteWaypoint> waypoints, float distance)
    {
        int low = 0;
        int high = waypoints.Length - 2;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (waypoints[middle].cumulativeLengthMetres <= distance)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
