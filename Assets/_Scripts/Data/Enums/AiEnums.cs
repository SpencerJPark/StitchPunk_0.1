
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
    SpawnEntity,       // e.g., Spawn a projectile / bullet
    ModifyStat,        // e.g., Hurt health, add money, satisfy hunger
    StartDialogue,
    ApplyForce,        // e.g., Dodge, knockback, dash
    WaitTime,          // Pause execution for a set duration. Duration = seconds.
    Approach,          // Navigate to stateMachine.targetEntity. FloatParam = stopping distance. IntParam = (int)StanceType.

    // Request emitters — enable a request component and advance immediately.
    // BehaviorExecutionSystem emits these; downstream systems handle the rest.
    RequestAttack,     // Enables AttackRequest on the unit. IntParam unused.
    RequestPickup,     // Enables PickupRequest on stateMachine.targetEntity (item). Sets EquipBy + AttachedTo first.
    ModifyMotivation,  // Appends MotivationChangeRequest. IntParam = (int)NeedType. FloatParam = Add delta.

    // Flee — blocking navigate-away command.
    // stateMachine.targetEntity must be the aggressor entity.
    // Scans nearby waypoints, paths at Running speed to the one FARTHEST from the aggressor.
    // Replaces stateMachine.targetEntity with the chosen waypoint on arrival so PushRecent works.
    FleeFromTarget,

    // Writes the interaction target to RecentInteraction so the unit won't immediately re-select it.
    // IntParam = unused. FloatParam = cooldown seconds (default 30 if 0).
    ReleaseInteraction,

    // Jump back to command index IntParam until ANY ticked Qualifier flag is true (OR semantics).
    // FloatParam = range for TargetOutOfRange. Duration = loop timeout seconds (0 = global default).
    // Guards: timeout + iteration cap — a loop can never hang a unit.
    LoopUntil,

    // Deactivates the Action animation layer (plays AnimationType.None). Non-blocking;
    // legal in interruptionCleanup. IntParam/FloatParam/Duration unused.
    StopAnimation,

    // Plays the unit's own animation for stateMachine.action, resolved per-unit from
    // UnitDataBlob.actionAnimations (zombie claws, citizen punches — same behavior asset).
    // FloatParam = speed (0 = 1x). Looping = loop the clip. IntParam/Duration unused.
    PlayActionAnimation,

    // Enables SocialInvite on stateMachine.targetEntity (via ECB — multiple initiators may target
    // the same invitee in one frame). SocialResponseSystem accepts or declines next frame.
    // Non-blocking. IntParam/FloatParam/Duration unused.
    RequestSocialResponse,
}

// Exit conditions for LoopUntil (and later: early-exit on blocking commands).
// OR semantics — exit when ANY ticked flag evaluates true.
// Missing-data convention: a qualifier whose referenced data is gone evaluates TRUE (exit),
// so loops can never spin on a despawned target. Append-only.
[System.Flags]
public enum LoopQualifier : ushort
{
    None                     = 0,
    TargetDead               = 1 << 0, // stateMachine.targetEntity has Dead enabled, or entity gone
    TargetLost               = 1 << 1, // target has no LocalTransform (despawned)
    TargetOutOfRange         = 1 << 2, // distance(unit, target) > command.FloatParam
    TimerExpired             = 1 << 3, // stateMachine.LoopTimer >= command.Duration
    MotivationSatisfied      = 1 << 4, // Motivation[QualifierIntParam].value >= QualifierFloatParam
    TargetNotEngagedWithSelf = 1 << 5, // target's StateMachine.targetEntity != self (Talk partner lost)
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

public enum PersonalityTypes
{
    Bravery,
    Romance,
    Comedy,
    Friendly,
    Anger,
    Partier,
    Rest,
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

// Stable key for the BehaviorLibrary blob (enum-indexed). Append-only — never reorder.
public enum BehaviorType : byte
{
    None,
    Wander,
    MeleeSwing,
    Flee,
    Sit,
    Pickup,
    Talk,
    MeleeContinuous,
    MeleeSingle,
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

