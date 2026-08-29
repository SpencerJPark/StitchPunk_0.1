using Unity.Entities;

// Game-side companion to a package DotsMovementToolkit.Horde entity: the scene marker
// visual shown at a player-issued group order destination. Split out of Horde itself
// (movement toolkit extraction) because a marker GameObject reference is game presentation,
// not a generic pathfinding concept. Shown by MinionCommandSystem when an order is issued;
// hidden by OrderMarkerSystem on completion.
public struct HordeOrderMarker : IComponentData
{
    public Entity markerEntity;
}
