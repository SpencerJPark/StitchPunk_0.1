// The one priority ladder. WinnerSelection only lets a higher tier preempt a live behaviour (AF-D1), so
// these numbers ARE the override rules. Actions author their tier on the UtilityActionSO (brains may
// override per ActionType); awareness systems use these names only for context bumps.
public static class AIPriorityTier
{
    public const int Idle     = 0;
    public const int Ambient  = 1;  // needs, social, wander
    public const int Engage   = 2;  // arming up before a fight
    public const int Defend   = 3;  // fighting a detected or attacking enemy, answering an alarm, ordinary flee
    public const int Critical = 4;  // critical flee break-off, mid-fight weapon grab, throw
    public const int Leash    = 5;  // guard leash return beats every combat option
}
