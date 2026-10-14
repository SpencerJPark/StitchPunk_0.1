using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(WorkstationInputSystemGroup))]
[UpdateAfter(typeof(WorkstationPlacementSystem))]
public partial struct WorkstationDeploySystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        Entity playerToDeployFor = Entity.Null;
        float3 deployPosition = float3.zero;
        quaternion deployRotation = quaternion.identity;

        foreach ((
            RefRO<PlayerWorkstationPlacement> placement,
            EnabledRefRW<PlayerWorkstationPlacement> playerWorkstationPlacementEnabled,
            EnabledRefRW<OnInteractPlayerInput> onInteractPlayerInputEnabled,
            Entity playerEntity)
            in SystemAPI.Query<
                RefRO<PlayerWorkstationPlacement>,
                EnabledRefRW<PlayerWorkstationPlacement>,
                EnabledRefRW<OnInteractPlayerInput>>()
                .WithAll<Player>()
                .WithEntityAccess())
        {
            if (!onInteractPlayerInputEnabled.ValueRO)
            {
                continue;
            }

            onInteractPlayerInputEnabled.ValueRW = false;

            if (!placement.ValueRO.isValid)
            {
                continue;
            }

            playerToDeployFor = playerEntity;
            deployPosition = placement.ValueRO.position;
            deployRotation = placement.ValueRO.rotation;
            playerWorkstationPlacementEnabled.ValueRW = false;
        }

        if (playerToDeployFor == Entity.Null)
        {
            return;
        }

        Entity workstationPrefab = SystemAPI.GetComponent<WorkstationPrefabReference>(playerToDeployFor).prefab;
        if (workstationPrefab == Entity.Null)
        {
            return;
        }

        Workstation prefabWorkstation = SystemAPI.GetComponent<Workstation>(workstationPrefab);
        prefabWorkstation.ownerPlayer = playerToDeployFor;

        Entity deployedWorkstation = state.EntityManager.Instantiate(workstationPrefab);
        state.EntityManager.SetComponentData(deployedWorkstation, LocalTransform.FromPositionRotationScale(deployPosition, deployRotation, 1f));
        state.EntityManager.SetComponentData(deployedWorkstation, prefabWorkstation);
        state.EntityManager.SetComponentData(playerToDeployFor, new PlayerWorkstationLink { workstation = deployedWorkstation });
    }
}
