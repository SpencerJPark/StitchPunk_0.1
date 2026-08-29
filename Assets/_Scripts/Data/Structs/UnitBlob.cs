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
    public BlobArray<NeedType> motivation;
    public int randomMotivationAmount;
    public BlobArray<NeedType> randomMotivations;
    public BlobArray<FactionType> attackFactions;
    public BlobArray<FactionType> socialFactions;
    public BlobArray<AttackActionMappingBlob> attacks;
    public BlobArray<ActionAnimationMappingBlob> actionAnimations;
    public BlobArray<StanceAnimationBlob> stanceAnimations;
    public ClipId idleAnimation;
    public ClipId movingAnimation;
}

public struct ActionAnimationMappingBlob
{
    public ActionType action;
    public ClipId animation;
}

public struct AttackActionMappingBlob
{
    public ActionType   action;
    public DamageSource attack;
}

public struct StanceAnimationBlob
{
    public StanceType stance;
    public ClipId idleAnimation;
    public ClipId movingAnimation;
}
