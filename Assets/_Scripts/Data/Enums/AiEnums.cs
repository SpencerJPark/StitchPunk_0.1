
public enum BehaviorPhase : byte
{
    Approach,
    Execute,
    InterruptionCleanup,
    Recovery,
    Complete
}

public enum BehaviorCommandType : byte
{
    PlayAnimation,
    SpawnEntity,     // e.g., Spawn a projectile / bullet
    ModifyStat,      // e.g., Hurt health, add money, satisfy hunger
    StartDialogue,
    ApplyForce,      // e.g., Dodge, knockback, dash
    WaitTime         // Pause execution for a set duration
}

public enum ConsiderationType : byte
{
    Health,
    LineOfSight,
    DistanceToTarget,
    AmmoCount,
    Motivation,
    Trait,
}

public enum CurveType : byte
{
    Linear,
    Quadratic,
    Inverse
}


public enum NeedType
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
    Work,
}

public enum ActionType // used for animation linking
{
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

// Utility AI v2 — how an action picks its target.
public enum TargetingMode : byte
{
    Self,          // action runs on the owner (Wander, Idle, Sleep)
    SingleTarget,  // action needs one target entity (MeleeAttack, Talk, Pickup)
}

// Utility AI v2 — stable key for the BehaviorLibrary blob (enum-indexed like ItemType/AttackType).
// Append-only: add new values at the end, never reorder existing ones.
public enum BehaviorType : byte
{
    None,
    Wander,
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

