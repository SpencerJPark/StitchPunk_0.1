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