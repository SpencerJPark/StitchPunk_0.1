// What a behaviour module needs from the unit that runs it (SmartHumanAI H0). Used by bake validation
// and, later, the brain editor's filters. Flags: a module can need several.
[System.Flags]
public enum ModuleTags
{
    None    = 0,
    Hands   = 1 << 0,   // picks up / holds / throws items
    Ranged  = 1 << 1,   // attacks from range
    Melee   = 1 << 2,
    Social  = 1 << 3,   // talks, gathers with others
    Needs   = 1 << 4,   // driven by motivations (eat, sit, music)
    Combat  = 1 << 5,
}
