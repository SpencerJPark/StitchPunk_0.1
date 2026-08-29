// Single source of truth for what BehaviorCommandType values can actually DO. The bake validator
// (BehaviorLibraryBakingSystem) and the interpreter (BehaviorExecutionSystem) both consult this
// table, so "authorable in a BehaviorSO" and "runnable at runtime" can never drift apart: when an
// interpreter arm is added or removed, flip the command here in the SAME commit.
// BehaviorCommandCatalogTests pins both sets — changing either without updating the test fails
// the Test Runner on purpose.
public static class BehaviorCommandCatalog
{
    // Commands with an interpreter arm in BehaviorExecutionSystem. Deliberately a whitelist with
    // default-false: a brand-new enum value bake-warns until it is cataloged here, instead of
    // silently claiming to work.
    public static bool IsImplemented(BehaviorCommandType commandType)
    {
        switch (commandType)
        {
            case BehaviorCommandType.PlayAnimation:
            case BehaviorCommandType.WaitTime:
            case BehaviorCommandType.Approach:
            case BehaviorCommandType.RequestAttack:
            case BehaviorCommandType.RequestPickup:
            case BehaviorCommandType.ModifyMotivation:
            case BehaviorCommandType.FleeFromTarget:
            case BehaviorCommandType.ReleaseInteraction:
            case BehaviorCommandType.LoopUntil:
            case BehaviorCommandType.StopAnimation:
            case BehaviorCommandType.PlayActionAnimation:
            case BehaviorCommandType.RequestSocialResponse:
            case BehaviorCommandType.PlaySound:
            case BehaviorCommandType.WaitForAnimEvent:
            case BehaviorCommandType.WaitForClipFinished:
                return true;
            default:
                // SpawnEntity / ModifyStat / StartDialogue / ApplyForce — declared, not implemented.
                // SpawnEntity is scheduled to land with the RangedCombat plan.
                return false;
        }
    }

    // Blocking commands own their advancement across frames — illegal in interruptionCleanup
    // (it runs in one frame). Moved here from BehaviorLibraryBakingSystem so the interpreter's
    // blocking semantics and the bake rule share one definition.
    public static bool IsBlocking(BehaviorCommandType commandType)
    {
        return commandType == BehaviorCommandType.Approach
            || commandType == BehaviorCommandType.WaitTime
            || commandType == BehaviorCommandType.FleeFromTarget
            || commandType == BehaviorCommandType.LoopUntil
            || commandType == BehaviorCommandType.WaitForAnimEvent
            || commandType == BehaviorCommandType.WaitForClipFinished;
    }
}
