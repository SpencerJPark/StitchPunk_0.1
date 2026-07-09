using Unity.Entities;

public struct AttackBlob
{
    public DamageSource damageSource;
    public DamageBehaviour damageBehaviour;
    public int damageAmount;
    public float range;
    // Ragdoll response — flattened from the attack's RagdollProfileSO when assigned, else the
    // AttackSO inline fields (flail/spin/restitution then take their defaults: 1 / 0 / 0).
    public float ragdollForce;
    public float launchForceY;
    public float launchForceX;
    public float flailIntensity;
    public float spin;
    public float restitution;
    public float hitTime;
    public float cooldown;
}

public struct AttackLibraryBlob
{
    public BlobArray<AttackBlob> attacks;
}
