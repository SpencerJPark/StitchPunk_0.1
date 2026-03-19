using Unity.Entities;

public struct AttackBlob
{
    public AttackType attackType;
    public AttackDelivery attackDelivery;
    public DamageBehaviour damageBehaviour;
    public int damageAmount;
    public float range;
    public float cooldown;
}

public struct AttackLibraryBlob
{
    public BlobArray<AttackBlob> attacks;
}
