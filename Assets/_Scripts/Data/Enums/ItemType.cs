public enum ItemType
{
    None,
    Reviver,
    MedKit,
    Weapon,
    Ammo,
    Hat,
    FaceWare,
    Elixir,
    Wood,
    MetalScrap,
    Coins,
    Rock,      // SmartHumanAI H3 — appended; the old Rock.asset sat on slot 2 (MedKit)
    Pistol,    // SmartHumanAI H5 — appended
    Rifle,
}

// How the AI fires a held item (SmartHumanAI H5-D7). Throw = it leaves the hand (rock); Shoot = it spawns a projectile.
public enum RangedKind : byte { None, Throw, Shoot }

public enum WeaponType
{
    None,
    Sword,
    Stone,
    Pistol,
    Rifle,
}

public enum GalvanicOrgans
{
    NerveString,
    Windpipe,
    Spinello,
    Bonehorn,
    Ribcordion,
    Organ
} 
