using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Watches for an enabled SaveRequest. When found, snapshots player component data
/// into a SaveFile DTO and writes it to disk as JSON.
///
/// [BurstCompile] intentionally omitted — JsonUtility and System.IO are managed operations.
/// </summary>
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(AutoSaveTimerSystem))]
public partial struct SaveSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<GameDataTag>();
        state.RequireForUpdate<Player>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity saveEntity = SystemAPI.GetSingletonEntity<GameDataTag>();

        if (!state.EntityManager.IsComponentEnabled<SaveRequest>(saveEntity))
            return;

        // Consume immediately — subsequent systems won't re-trigger this frame
        state.EntityManager.SetComponentEnabled<SaveRequest>(saveEntity, false);

        SaveRequest saveRequest         = state.EntityManager.GetComponentData<SaveRequest>(saveEntity);
        PlayTimeTracker playTimeTracker = state.EntityManager.GetComponentData<PlayTimeTracker>(saveEntity);
        GameSettings gameSettings       = state.EntityManager.GetComponentData<GameSettings>(saveEntity);

        Entity playerEntity           = SystemAPI.GetSingletonEntity<Player>();
        LocalTransform playerTransform = state.EntityManager.GetComponentData<LocalTransform>(playerEntity);
        Health playerHealth           = state.EntityManager.GetComponentData<Health>(playerEntity);
        PlayerItemSlots playerItemSlots = state.EntityManager.GetComponentData<PlayerItemSlots>(playerEntity);
        UnitEquipt unitEquipt         = state.EntityManager.GetComponentData<UnitEquipt>(playerEntity);

        int equippedItemType = (int)ItemType.None;
        if (unitEquipt.equiptItemEntity != Entity.Null &&
            state.EntityManager.HasComponent<Item>(unitEquipt.equiptItemEntity))
        {
            equippedItemType = (int)state.EntityManager.GetComponentData<Item>(unitEquipt.equiptItemEntity).itemType;
        }

        SaveFile saveFile = new SaveFile
        {
            version  = 1,
            settings = new SettingsSaveData
            {
                animationFrameRate = gameSettings.animationFrameRate
            },
            player  = new PlayerSaveData
            {
                posX              = playerTransform.Position.x,
                posY              = playerTransform.Position.y,
                posZ              = playerTransform.Position.z,
                rotX              = playerTransform.Rotation.value.x,
                rotY              = playerTransform.Rotation.value.y,
                rotZ              = playerTransform.Rotation.value.z,
                rotW              = playerTransform.Rotation.value.w,
                currentHp         = playerHealth.healthAmount,
                maxHp             = playerHealth.healthAmountMax,
                totalPlaySeconds  = playTimeTracker.totalSeconds,
                equippedItemType  = equippedItemType,
                itemSlot1         = (int)playerItemSlots.itemSlot1,
                itemSlot2         = (int)playerItemSlots.itemSlot2,
                itemSlot3         = (int)playerItemSlots.itemSlot3,
                itemSlot4         = (int)playerItemSlots.itemSlot4
            }
        };

        string json = JsonUtility.ToJson(saveFile, true);
        string path = SavePaths.GetSlotPath(saveRequest.slot);
        System.IO.File.WriteAllText(path, json);

        Debug.Log($"[SaveSystem] Saved to slot {saveRequest.slot}: {path}");
    }

    public void OnDestroy(ref SystemState state) { }
}
