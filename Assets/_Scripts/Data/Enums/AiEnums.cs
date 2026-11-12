
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
    SpawnEntity,
    ModifyStat,
    StartDialogue,
    ApplyForce,
    WaitTime,          // Duration = seconds.
    Approach,          // FloatParam = stopping distance. IntParam = (int)StanceType.

    // Request emitters: enable a request component and advance immediately.
    RequestAttack,
    RequestPickup,     // Sets EquipBy + AttachedTo before enabling PickupRequest.
    ModifyMotivation,  // IntParam = (int)NeedType. FloatParam = Add delta.

    // Blocking. stateMachine.targetEntity must be the aggressor; swaps it for the chosen waypoint on arrival so PushRecent works.
    FleeFromTarget,

    // FloatParam = cooldown seconds (default 30 if 0).
    ReleaseInteraction,

    // Jumps back to command index IntParam until any ticked LoopQualifier is true.
    // FloatParam = range for TargetOutOfRange. Duration = timeout seconds (0 = global default); timeout and iteration cap stop a hung loop.
    LoopUntil,

    // Non-blocking; legal in interruptionCleanup.
    StopAnimation,

    // Clip is resolved per unit from UnitDataBlob.actionAnimations. FloatParam = speed (0 = 1x).
    PlayActionAnimation,

    // Enabled via ECB because several initiators may target one invitee in a frame.
    RequestSocialResponse,

    // IntParam = (int)SoundType. Append-only, never reorder existing values.
    PlaySound,

    // Blocking. IntParam = anim event key on layer LayerIndex. Duration = timeout seconds (0 = none);
    // timeout completes with a warning so a clip missing its event cannot hang a behavior.
    WaitForAnimEvent,

    // Blocking. Also completes on ClipResolveFailed so a missing clip cannot hang a behavior. Duration = timeout seconds (0 = none).
    WaitForClipFinished,

    RequestThrow,

    // FloatParam = delta added to Intoxication.level (clamped 0..1).
    AddIntoxication,

    // IntParam = (int)EmoteIcon, FloatParam = seconds (0 = 2.5).
    ShowEmote,

    // Blocking. Waits ItemBlob.aiWindUpSeconds, or FloatParam when the item has none.
    WindUp,

    RequestShoot,
}

// OR semantics. A qualifier whose data is gone evaluates true so loops never spin on a despawned target. Append-only.
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
    Vice,
}

public enum ActionType // used for animation linking
{
    Idle,
    Wander,
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

    // Values below were appended; do not reorder existing values.
    EquipWeapon,
    UseHealingItem,
    EatFromSupplies,
    ListenToMusic,
    DrinkAlcohol,
    Brawl,
    PassOut,
    KeepRange,
    Retreat,
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
    Resourceful,
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

// Index into the BehaviorLibrary blob. Append-only, never reorder.
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
    EatFromSupplies,
    ListenToMusic,
    Throw,
    DrinkAlcohol,
    Brawl,
    PassOut,
    KeepRange,
    Retreat,
    Shoot,
}

public enum StanceType
{
    Normal    = 0,
    Defensive = 1,
    Running   = 2,
}

