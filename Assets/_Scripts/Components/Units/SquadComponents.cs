using Unity.Entities;
using Unity.Mathematics;

// Which player squad a minion belongs to: 0 = none, 1..4 = PlayerMinionGroupsData slots (CampLoop
// Phase 3). Deliberately not a movement-toolkit Horde: horde members steer by flow field, which
// would override the per-unit formation slots move orders hand out.
public struct SquadMember : IComponentData
{
    public int squadIndex;
}

// On the player.
public struct PlayerSquadSettings : IComponentData
{
    public float onFootCommandRadius;
}

// Persistent order kept until any other explicit order clears it. Follow re-paths to a formation slot
// around the player and engages hostiles that come near the player; Hold does the same around a fixed
// anchor. MinionStandingOrderSystem drives it; MinionActionSelectionSystem clears it on new orders.
public struct MinionStandingOrder : IComponentData
{
    public StandingOrderType type;
    public float3 anchorPosition;          // HoldPosition only; Follow anchors on the player each frame
    public int formationSlotIndex;         // FormationSlots index around the anchor (>= 1 for Follow, so slot 0 stays the player)
    public float engageRadius;             // hostiles this close to the anchor are attacked
    public float3 lastIssuedDestination;
    public double lastIssuedTime;
}
