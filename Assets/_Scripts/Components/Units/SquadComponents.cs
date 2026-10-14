using Unity.Entities;

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
