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
        hitTime         = 0.3f,
        cooldown        = 1f,
    };

    static AttackBlob MapAttack(AttackSO so) => new AttackBlob
    {
        damageSource    = so.damageSource,
        damageBehaviour = so.damageBehaviour,
        damageAmount    = so.damageAmount,
        range           = so.range,
        ragdollForce    = so.ragdollForce,
        launchForceY    = so.launchForceY,
        launchForceX    = so.launchForceX,
        hitTime         = so.hitTime,
        cooldown        = so.cooldown,
    };
}
