using Unity.Mathematics;

// Pure squad-control rules, kept out of the MonoBehaviours so they can be tested.
public static class SquadRules
{
    // Owner call 2026-10-13: a number assigns the selection to that squad; with nothing selected, or
    // with Ctrl held, it switches to that squad instead. Locked squads do nothing.
    public static SquadKeyAction ResolveNumberKey(int squadNumber, int unlockedSquadCount, bool hasSelection, bool controlHeld)
    {
        if (squadNumber < 1 || squadNumber > unlockedSquadCount) return SquadKeyAction.None;
        if (controlHeld || !hasSelection) return SquadKeyAction.SwitchToSquad;
        return SquadKeyAction.AssignSelectionToSquad;
    }

    // Cycles 1..unlocked and wraps; never lands on 0 (no squad).
    public static int NextSquadIndex(int currentSquadIndex, int unlockedSquadCount)
    {
        if (unlockedSquadCount < 1) return 0;
        if (currentSquadIndex < 1 || currentSquadIndex >= unlockedSquadCount) return 1;
        return currentSquadIndex + 1;
    }

    // Mounted at the workstation the whole squad hears a command; on foot only members in range.
    public static bool HearsCommand(bool commanderIsMounted, float3 commanderPosition, float3 memberPosition, float onFootCommandRadius)
    {
        if (commanderIsMounted) return true;
        float2 offset = memberPosition.xz - commanderPosition.xz;
        return math.lengthsq(offset) <= onFootCommandRadius * onFootCommandRadius;
    }
}
