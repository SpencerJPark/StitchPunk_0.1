using Unity.Entities;

public struct UnitLibraryBlob
{
    public BlobArray<UnitDataBlob> units;
}

public struct UnitDataBlob
{
    public UnitType unitType;
    public BlobArray<ActionAnimationMappingBlob> actionAnimations;
    public AnimationType idleAnimation;
    public AnimationType movingAnimation;
    public AttackType attackType;
}

public struct ActionAnimationMappingBlob
{
    public ActionType action;
    public AnimationType animation;
}