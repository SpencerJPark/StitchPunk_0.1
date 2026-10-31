using Unity.Entities;
using Unity.Mathematics;

// An abstract unit's position is a pure function of world time, evaluated only when needed; the same Entity is never despawned.

// Enabled = the unit is simulated only as a route/anchor. Baked disabled.
public struct AbstractSimulation : IComponentData, IEnableableComponent { }

// What to re-enable when the unit materialises.
public struct AbstractSavedState : IComponentData
{
    public bool hadMovementEnabled;
    public bool hadGravityEnabled;
}

public enum AbstractRouteEnd : byte
{
    Stop = 0,
    Loop = 1,
    PingPong = 2
}

public struct AbstractRoute : IComponentData
{
    public double departureWorldSeconds;
    public float speedMetresPerSecond;
    public float totalLengthMetres;
    public AbstractRouteEnd endBehaviour;
}

[InternalBufferCapacity(4)]
public struct AbstractRouteWaypoint : IBufferElementData
{
    public float3 position;

    // Cumulative length from the first waypoint.
    public float cumulativeLengthMetres;
}

public struct AbstractRestAnchor : IComponentData
{
    public float3 position;
}

// Companions, squad, the caravan the player drives and cutscene actors never go abstract.
public struct PinnedActive : IComponentData { }
