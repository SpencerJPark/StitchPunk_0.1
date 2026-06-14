// Stable key for the SoundLibrary blob (enum-indexed) and the AudioManager clip registry.
// Append-only — never reorder existing values (saved/baked data is indexed by these).
public enum SoundType
{
    None,
    Footstep,
    AttackSwing,
    AttackImpact,
    Hurt,
    Death,
    Pickup,
    Equip,
    Consume,
    MachineHum,
    FireCrackle,
    Wind,
    AmbientCrowd,
}
