using Unity.Entities;

public struct AttackBlob
{
    public AttackType attackType;
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
