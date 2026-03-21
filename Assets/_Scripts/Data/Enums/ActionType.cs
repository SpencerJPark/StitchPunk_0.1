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
    Sit
}

public enum AttackType // Animation Guide
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