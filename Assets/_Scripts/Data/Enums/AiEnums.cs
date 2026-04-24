public enum BrainType
{
    None        = 0,
    Citizen     = 1,
    Guard       = 2,
    Rotted = 3, 
    PlayerZombie = 4, 
    Panic       = 5, 
    Merchant    = 6, 
    StoryCharacter   = 7, 
    Bandit = 8,
    AngryMob,
    
}

public enum MotivationType
{
    None,
    Hunger,
    Energy,
    Fun,
    Social,
    Comfort,
    Bladder,
    Safety,
    Movement,
    SelfPreservation,
    SelfDefence,
    BloodLust,
    Bookworm,
    Work,
    NightOwl,
    EarlyBird,
    Glutton,
    Grumpy,
    Depressed,
    Lazy,
    Nervous,
    Slob, // fart and burp
}

public enum ActionType // used for animation linking
{
    None,
    Idle,
    Wander, // picks a random spot around the waypoint to move to, meant for simulating controlled city walking
    Interact,
    Death,
    Resurrection,
    
    Flee,
    Repair,
    Build,
    Eat,
    Sleep,
    Talk,
    Smoke,
    Drink,
    Patrol,

    SeekEntertainment,
    Bathroom,
    Sit,
    
    Melee,
    Projectile,
    Swing,
    Throw,
    Shoot,
    Spawn,
}


public enum UnitStateType
{
    None,
}

