using Unity.Entities;

public struct AttackBlob
{
    public AttackType attackType;
    public ActionType actionType;
    public DamageBehaviour damageBehaviour;
    public int damageAmount;
    public float range;
    public float cooldown;
    public float ragdollForce;
    public float launchForceY;
    public float launchForceX;
}

public struct AttackLibraryBlob
{
    public BlobArray<AttackBlob> attacks;
}
