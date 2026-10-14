public enum FactionType : byte
{
    None = 0,
    Player = 1,
    StoryNPC = 2,
    Citizens = 3,
    Animals = 4,
    Rottens = 5,
    Army = 6,
    NorthenForces = 7,
    Church,
    Elites,
    Bandits,        // CampLoop Phase 5. Append only: FactionRegistry and assets key by the byte.
    
    
}


public enum UnitType {
    None,
    Player,
    MaleCitizen,
    FemaleCitizen,
    MaleRotter,
    FemaleZombie,
    PlayerZombie,
    // Camp Loop station-summon variants (CampLoop_System.md §4). Append only: assets serialize the int.
    PlayerZombieBrute,
    PlayerZombieRunner,
    Bandit,         // CampLoop Phase 5 camp guard; revives into PlayerZombie.
}
