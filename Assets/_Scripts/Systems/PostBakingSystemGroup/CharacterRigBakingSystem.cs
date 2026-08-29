using Unity.Entities;

// Cross-entity baking for the character rig. Assembles the BodyPart buffer (subscene / non-prefab):
// clear every BodyPart buffer, then for each BodyPartInfo child map it to its root via BaseParent
// and append a BodyPart entry. Prefab-instantiated units rebuild their buffer at runtime in
// BodyPartInitSystem (entity refs aren't reliably remapped).
//
// Ragdoll component stamping (formerly section 2 here, Ragdoll2D/Ragdoll2DJoint) is gone — the
// toolkit's own ActorBaker stamps RagdollActor/RagdollLaunch/RagdollBody from the rig's authored
// ragdoll bodies, nothing for this system to do.
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct CharacterRigBakingSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (DynamicBuffer<BodyPart> buffer in SystemAPI.Query<DynamicBuffer<BodyPart>>())
            buffer.Clear();

        foreach (var (info, baseParent, partEntity) in
            SystemAPI.Query<RefRO<BodyPartInfo>, RefRO<BaseParent>>().WithEntityAccess())
        {
            Entity rootEntity = baseParent.ValueRO.baseParentEntity;
            if (!SystemAPI.HasBuffer<BodyPart>(rootEntity)) continue;

            SystemAPI.GetBuffer<BodyPart>(rootEntity).Add(new BodyPart
            {
                entity  = partEntity,
                target  = info.ValueRO.target,
                unitPart = info.ValueRO.unitPart,
                flags   = info.ValueRO.flags,
            });
        }
    }
}
