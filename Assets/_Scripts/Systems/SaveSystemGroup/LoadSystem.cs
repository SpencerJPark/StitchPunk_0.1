using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Watches for an enabled LoadRequest. When found, reads the JSON save file for that slot
/// and restores player component data.
///
/// Call order note: enable LoadRequest AFTER the game scene has fully spawned — the player
/// entity must already exist. A loadDelayFrames counter on LoadRequest can defer this if
/// loading from a fresh scene start.
///
/// TODO: Restore equipped item. Requires spawning the item entity and running the equip
/// system — needs save/equip integration. Currently only ItemSlots are restored.
///
/// [BurstCompile] intentionally omitted — System.IO and JsonUtility are managed operations.
/// </summary>
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(SaveSystem))]
public partial struct LoadSystem : ISystem
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

        if (!state.EntityManager.IsComponentEnabled<LoadRequest>(saveEntity))
            return;

        state.EntityManager.SetComponentEnabled<LoadRequest>(saveEntity, false);

        LoadRequest loadRequest = state.EntityManager.GetComponentData<LoadRequest>(saveEntity);
        string path = SavePaths.GetSlotPath(loadRequest.slot);

        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[LoadSystem] No save file found at slot {loadRequest.slot}: {path}");
            return;
        }

        string json = System.IO.File.ReadAllText(path);
        SaveFile saveFile = JsonUtility.FromJson<SaveFile>(json);

        if (saveFile?.player == null)
        {
            Debug.LogWarning("[LoadSystem] Save file is missing player data — aborting load.");
            return;
        }

        PlayerSaveData playerData = saveFile.player;

        // Restore play time
        state.EntityManager.SetComponentData(saveEntity, new PlayTimeTracker
        {
            totalSeconds = playerData.totalPlaySeconds
        });

        // Restore settings — fall back to baked default if settings block is missing (old save file)
        if (saveFile.settings != null)
        {
            state.EntityManager.SetComponentData(saveEntity, new GameSettings
            {
                animationFrameRate = saveFile.settings.animationFrameRate
            });
        }

        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        // Restore position and facing
        state.EntityManager.SetComponentData(playerEntity, LocalTransform.FromPositionRotation(
            new float3(playerData.posX, playerData.posY, playerData.posZ),
            new quaternion(playerData.rotX, playerData.rotY, playerData.rotZ, playerData.rotW)
        ));

        // Restore health — kill snapshot fields intentionally zeroed (only valid on death frame)
        state.EntityManager.SetComponentData(playerEntity, new Health
        {
            healthAmount    = playerData.currentHp,
            healthAmountMax = playerData.maxHp
        });

        // Restore item slot assignments
        state.EntityManager.SetComponentData(playerEntity, new PlayerItemSlots
        {
            itemSlot1 = (ItemType)playerData.itemSlot1,
            itemSlot2 = (ItemType)playerData.itemSlot2,
            itemSlot3 = (ItemType)playerData.itemSlot3,
            itemSlot4 = (ItemType)playerData.itemSlot4
        });

        Debug.Log($"[LoadSystem] Loaded slot {loadRequest.slot} — play time: {playerData.totalPlaySeconds:F0}s");
    }

    public void OnDestroy(ref SystemState state) { }
}
