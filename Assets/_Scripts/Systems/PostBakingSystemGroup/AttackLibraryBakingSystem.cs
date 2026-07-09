using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct AttackLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AttackLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        AttackLibrarySO librarySO = null;
        foreach (RefRO<AttackLibraryReference> reference in SystemAPI.Query<RefRO<AttackLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        int attackCount = BlobLibraryUtils.EnumCount<DamageSource>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref AttackLibraryBlob root = ref builder.ConstructRoot<AttackLibraryBlob>();
        BlobBuilderArray<AttackBlob> attacksBuilder = builder.Allocate(ref root.attacks, attackCount);

        BlobLibraryUtils.FillWithPreFill(
            attacksBuilder, attackCount,
            DefaultAttack,
            librarySO.attacks,
            so => (int)so.damageSource,
            MapAttack
        );

        BlobAssetReference<AttackLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<AttackLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<AttackLibrary> holder in SystemAPI.Query<RefRW<AttackLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<AttackLibrary> holder in SystemAPI.Query<RefRW<AttackLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }

    static AttackBlob DefaultAttack(int i) => new AttackBlob
    {
        damageSource    = (DamageSource)i,
        damageBehaviour = DamageBehaviour.SinlgeTarget,
        damageAmount    = 0,
        range           = 0f,
        ragdollForce    = 1f,
        launchForceY    = 0f,
        launchForceX    = 0f,
        flailIntensity  = 1f,
        spin            = 0f,
        restitution     = 0f,
        hitTime         = 0.3f,
        cooldown        = 1f,
    };

    // A RagdollProfileSO flattens over the inline AttackSO fields; no profile = inline values with
    // baseline flail (1), no spin, and restitution 0 (= RagdollSimConfig default at runtime).
    static AttackBlob MapAttack(AttackSO so)
    {
        bool hasProfile = so.ragdollProfile != null;
        return new AttackBlob
        {
            damageSource    = so.damageSource,
            damageBehaviour = so.damageBehaviour,
            damageAmount    = so.damageAmount,
            range           = so.range,
            ragdollForce    = hasProfile ? so.ragdollProfile.ragdollForce   : so.ragdollForce,
            launchForceY    = hasProfile ? so.ragdollProfile.launchForceY   : so.launchForceY,
            launchForceX    = hasProfile ? so.ragdollProfile.launchForceX   : so.launchForceX,
            flailIntensity  = hasProfile ? so.ragdollProfile.flailIntensity : 1f,
            spin            = hasProfile ? so.ragdollProfile.spin           : 0f,
            restitution     = hasProfile ? so.ragdollProfile.restitution    : 0f,
            hitTime         = so.hitTime,
            cooldown        = so.cooldown,
        };
    }
}
