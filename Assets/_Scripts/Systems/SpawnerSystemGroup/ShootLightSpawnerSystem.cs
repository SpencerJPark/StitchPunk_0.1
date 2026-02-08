using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(LateSimulationSystemGroup))]
partial struct ShootLightSpawnerSystem : ISystem {


    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<StructureLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        StructureLibrary structureLibrary = SystemAPI.GetSingleton<StructureLibrary>();

        foreach (RefRO<ShootAttack> shootAttack in SystemAPI.Query<RefRO<ShootAttack>>()) {

            if (shootAttack.ValueRO.onShoot.isTriggered) {
                Entity shootLightEntity = state.EntityManager.Instantiate(structureLibrary.shootLightPrefabEntity);
                SystemAPI.SetComponent(shootLightEntity, LocalTransform.FromPosition(shootAttack.ValueRO.onShoot.shootFromPosition));
            }

        }
    }


}