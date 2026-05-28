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
    
    MeleeContinuous,
    MeleeSingle,
    ProjectileContinuous,
    ProjectileSingle,
    Swing,
    Throw,
    Shoot,
    Spawn,

    // Item-awareness actions — appended; do not reorder existing values.
    EquipWeapon,
    UseHealingItem,
}


public static class ActionTypeExtensions
{
    public static bool IsCombatAction(this ActionType actionType) =>
        actionType == ActionType.Flee                 ||
        actionType == ActionType.MeleeContinuous      ||
        actionType == ActionType.MeleeSingle          ||
        actionType == ActionType.ProjectileContinuous ||
        actionType == ActionType.ProjectileSingle     ||
        actionType == ActionType.Swing                ||
        actionType == ActionType.Throw                ||
        actionType == ActionType.Shoot;
}

public enum MotivationChangeType
{
    Add,
    Set,
}

public enum UnitStateType
{
    None,
}

public enum StanceType
{
    Normal    = 0,
    Defensive = 1,
    Running   = 2,
}

