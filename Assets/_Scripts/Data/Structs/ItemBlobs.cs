using Unity.Entities;

public struct ItemBlob
{
    public ItemType     itemType;
    public ItemCategory category;
    public AttackType   weaponAttack;
    public EffectType   onHitEffect;
    public EffectType   consumeEffect;
    public float        pickupRange;
    public float        consumeDuration;
    public float        baseUtility;
    public float        throwSpeed;
    public float        throwArc;
    public int          throwDamage;
    public float        throwRagdollForce;
    public float        throwLaunchForceY;
    public float        throwLaunchForceX;
}

public struct ItemLibraryBlob
{
    public BlobArray<ItemBlob> items;
}
