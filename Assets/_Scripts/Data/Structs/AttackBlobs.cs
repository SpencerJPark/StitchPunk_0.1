using Unity.Entities;

public struct AttackBlob
{
    public DamageSource damageSource;
    public DamageBehaviour damageBehaviour;
    public int damageAmount;
    public float range;
    public float ragdollForce;
    public float launchForceY;
    public float launchForceX;
    public float hitTime;
    public float cooldown;
}

public struct AttackLibraryBlob
{
    public BlobArray<AttackBlob> attacks;
}
