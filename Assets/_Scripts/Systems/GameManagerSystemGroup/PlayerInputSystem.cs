using Unity.Burst;
using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(GameManagerSystemGroup), OrderFirst = true)]
public partial struct PlayerInputSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerInputData>();
        state.RequireForUpdate<Player>();
    }

    // BurstCompile removed temporarily for debug logging
    public void OnUpdate(ref SystemState state)
    {
        PlayerInputData inputData = SystemAPI.GetSingleton<PlayerInputData>();

        Debug.Log($"[PlayerInputSystem] Running. actionMap={inputData.activeActionMap} onAttackInput={inputData.onAttackInput}");

        if (inputData.activeActionMap != ActionMaps.Player)
        {
            ClearOneShotFlags(ref inputData);
            SystemAPI.SetSingleton(inputData);
            return;
        }

        float deltaTime = SystemAPI.Time.DeltaTime;
        int playerCount = 0;

        foreach (var (playerData,
                     attackEnabled,
                     interactEnabled,
                     rollEnabled,
                     sneakEnabled) in
            SystemAPI.Query<
                RefRW<PlayerData>,
                EnabledRefRW<PlayerAttackInput>,
                EnabledRefRW<PlayerInteractInput>,
                EnabledRefRW<PlayerRollInput>,
                EnabledRefRW<PlayerSneakToggle>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            playerCount++;
            bool isRolling = playerData.ValueRO.rollTime > 0f;

            if (isRolling)
            {
                playerData.ValueRW.rollTime -= deltaTime;
                if (playerData.ValueRO.rollTime <= 0f)
                {
                    playerData.ValueRW.rollTime = 0f;
                    rollEnabled.ValueRW = false;
                    isRolling = false;
                }
            }

            if (inputData.onRollInput && !isRolling)
            {
                playerData.ValueRW.rollTime = playerData.ValueRO.rollTimeMax;
                rollEnabled.ValueRW = true;
                attackEnabled.ValueRW = false;
                interactEnabled.ValueRW = false;
                isRolling = true;
            }

            if (!isRolling)
            {
                if (inputData.onAttackInput)
                {
                    Debug.Log("[PlayerInputSystem] Enabling PlayerAttackInput on player entity");
                    attackEnabled.ValueRW = true;
                }

                if (inputData.onInteractInput)
                    interactEnabled.ValueRW = true;
            }
            else if (inputData.onAttackInput)
            {
                Debug.Log("[PlayerInputSystem] Attack input ignored — player is rolling");
            }

            if (inputData.sneakToggle)
                sneakEnabled.ValueRW = !sneakEnabled.ValueRO;
        }

        if (playerCount == 0)
            Debug.LogWarning("[PlayerInputSystem] No player entity found matching query (needs Player + PlayerData + all input components)");

        ClearOneShotFlags(ref inputData);
        SystemAPI.SetSingleton(inputData);
    }

    private static void ClearOneShotFlags(ref PlayerInputData inputData)
    {
        inputData.onAttackInput = false;
        inputData.onInteractInput = false;
        inputData.onRollInput = false;
        inputData.sneakToggle = false;
    }
}
