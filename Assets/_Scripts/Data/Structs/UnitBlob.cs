using DotsAnimationToolkit;
using Unity.Entities;

public struct UnitLibraryBlob
{
    public BlobArray<UnitDataBlob> units;

    public int FindByUnitType(UnitType unitType)
    {
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i].unitType == unitType) return i;
        }
        return -1;
    }
}

public struct UnitDataBlob
{
    public UnitType unitType;
    public FactionType factionType;
    public bool canBePlayerControlled;
    // Authored zombie/conversion form for this unit; None = does not convert.
    public UnitType becomesUnitType;
    public float awarenessRange;
    // Spawn and maximum health. Stamped onto every unit by UnitHealthInitSystem at spawn, which is
    // what makes this — not the prefab's HealthAuthoring numbers — the live value.
    public int maxHealth;
    public BlobArray<NeedType> motivation;
    public int randomMotivationAmount;
    public BlobArray<NeedType> randomMotivations;
    public BlobArray<FactionType> attackFactions;
    public BlobArray<FactionType> socialFactions;
    public BlobArray<AttackActionMappingBlob> attacks;
    // Animation-name-convention bindings (G5 D1): the animation key equals the enum name resolved
    // through the toolkit's AnimationNameRegistry at bake time. 0 = unresolved.
    public uint idleAnimationKey;
    public uint walkAnimationKey;
    public BlobArray<ActionAnimationKeyBlob> actionAnimationKeys;
    public BlobArray<StanceAnimationKeysBlob> stanceAnimationKeys;
}

public struct ActionAnimationKeyBlob
{
    public ActionType action;
    public uint animationKey;
}

public struct StanceAnimationKeysBlob
{
    public StanceType stance;
    public uint idleAnimationKey;
    public uint walkAnimationKey;
}

public struct AttackActionMappingBlob
{
    public ActionType   action;
    public DamageSource attack;
}
