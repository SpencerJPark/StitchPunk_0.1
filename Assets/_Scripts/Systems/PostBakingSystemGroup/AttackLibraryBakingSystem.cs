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
        foreach (var reference in SystemAPI.Query<RefRO<AttackLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        int attackCount = System.Enum.GetValues(typeof(AttackType)).Length;

        using var builder = new BlobBuilder(Allocator.Temp);
        ref AttackLibraryBlob root = ref builder.ConstructRoot<AttackLibraryBlob>();
        var attacksBuilder = builder.Allocate(ref root.attacks, attackCount);

        // Pre-fill all slots with defaults so unregistered attack types are safe to read
        for (int i = 0; i < attackCount; i++)
        {
            attacksBuilder[i].attackType      = (AttackType)i;
            attacksBuilder[i].damageBehaviour = DamageBehaviour.SinlgeTarget;
            attacksBuilder[i].damageAmount    = 0;
            attacksBuilder[i].range           = 0f;
            attacksBuilder[i].ragdollForce    = 1f;
            attacksBuilder[i].launchForceY    = 0f;
            attacksBuilder[i].launchForceX    = 0f;
            attacksBuilder[i].hitTime         = 0.3f;
        }

        foreach (var attackSO in librarySO.attacks)
        {
            if (attackSO == null) continue;

            int index = (int)attackSO.attackType;
            attacksBuilder[index].attackType      = attackSO.attackType;
            attacksBuilder[index].damageBehaviour = attackSO.damageBehaviour;
            attacksBuilder[index].damageAmount    = attackSO.damageAmount;
            attacksBuilder[index].range           = attackSO.range;
            attacksBuilder[index].ragdollForce    = attackSO.ragdollForce;
            attacksBuilder[index].launchForceY    = attackSO.launchForceY;
            attacksBuilder[index].launchForceX    = attackSO.launchForceX;
            attacksBuilder[index].hitTime         = attackSO.hitTime;
        }

        var blobRef = builder.CreateBlobAssetReference<AttackLibraryBlob>(Allocator.Persistent);

        foreach (var holder in SystemAPI.Query<RefRW<AttackLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (var holder in SystemAPI.Query<RefRW<AttackLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }
}
