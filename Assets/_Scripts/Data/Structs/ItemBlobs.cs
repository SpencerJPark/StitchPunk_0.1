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
    public float        aiRangedMinRange;  // AI throws or shoots only when the target is in [min, max]; max 0 = AI never does
    public float        aiRangedMaxRange;
    public RangedKind   rangedKind;
    public float        projectileSpeed;
    public float        rangedCooldownSeconds;
    public float        aiWeaponValue;
    public float        aiWindUpSeconds; // the telegraph before a throw or shot (H5-D8)
}

public struct ItemLibraryBlob
{
    public BlobArray<ItemBlob> items;
}
