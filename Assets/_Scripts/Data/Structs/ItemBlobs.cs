using Unity.Entities;

public struct ItemBlob
{
    public ItemType     itemType;
    public ItemCategory category;
    public DamageSource weaponAttack;
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
    public float        aiThrowMinRange;   // AI throws only when the target is in [min, max]; max 0 = AI never throws it
    public float        aiThrowMaxRange;
}

public struct ItemLibraryBlob
{
    public BlobArray<ItemBlob> items;
}
