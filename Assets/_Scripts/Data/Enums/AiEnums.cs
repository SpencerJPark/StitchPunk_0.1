public enum ActionType
{
    None,
    Idle,
    Walk,
    Run,
    Jump,
    Interact,
    Repair,
    Build,
    Eat,
    Sleep,
    Talk,
    Smoke,
    Drink,
    Attack,
    Patrol,
    Flee,
    SeekEntertainment,
    UseBathroom,
    Sit,
    Death,
    Resurrection
}

public enum ActionCategory
{
    None,
    Interaction,
    Action,
    Attack,
    UseItem,
    Wander,
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
}

public enum BehaviourType
{
    PunchAttack,
    SlashAttack,
    Dance,
    Smoke
}

public enum AttackType
{
    None,
    Instant,
    Punch,
    Claw,
    Throw,
    Kick,
    Slash,
    Stab,
    Swing,
    ShootOneHand,
    ShootTwoHand,
    Explode,
}

public enum AttackDelivery // attack delivery behaviour
{
    None,
    Melee,
    Throw,
    Shoot,
    Projectile,
    Hitscan,
    Beam
}

public enum DamageBehaviour // attack effect behaviour
{
    None,
    SinlgeTarget,
    AreaOfEffect,
    Cone,
    Line,
    Chain,
}

public enum BrainType
{
    None        = 0,
    Citizen     = 1,   // Full motivation-driven AI — seeks interactions to satisfy needs
    Guard       = 2,   // Safety-focused, chases and attacks Undead
    FeralZombie = 3,   // Pure combat — chases and attacks Player + Human
    PlayerZombie = 4,  // Player-controlled minion (PlayerControlled component enabled)
    Panic       = 5,   // Flee-dominant — runs from all threats
    Merchant    = 6,   // Social and work motivated, no combat
    StoryCharacter   = 7,   // Story character — scripted via narrative system
    Bandit = 8,
}