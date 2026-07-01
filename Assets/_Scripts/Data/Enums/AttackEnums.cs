public enum DamageSource   // was AttackType — attack + environmental damage origins
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
    // v2 environmental — sourceless / non-attack damage origins
    Fall,
    Hazard,
    Burn,
    Drown,
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