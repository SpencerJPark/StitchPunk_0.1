using System;

/// <summary>
/// Root DTO written to / read from disk as JSON.
/// Add new top-level domains (WorldSaveData, MinionSaveData[], etc.) here as the game grows.
/// Never put ECS types (float3, quaternion, Entity) in these classes — JsonUtility can't serialize them.
/// </summary>
[Serializable]
public class SaveFile
{
    public int version = 1;
    public PlayerSaveData player;
    public SettingsSaveData settings;
}

[Serializable]
public class PlayerSaveData
{
    // Position
    public float posX;
    public float posY;
    public float posZ;

    // Rotation (quaternion components)
    public float rotX;
    public float rotY;
    public float rotZ;
    public float rotW;

    // Health
    public int currentHp;
    public int maxHp;

    // Session
    public double totalPlaySeconds;

    // Inventory — currently equipped item type (int cast of ItemType enum)
    // Restoring the live equipped entity requires spawn/equip integration — see LoadSystem TODO.
    public int equippedItemType;

    // Quick-slot assignments
    public int itemSlot1;
    public int itemSlot2;
    public int itemSlot3;
    public int itemSlot4;
}

[Serializable]
public class SettingsSaveData
{
    public int animationFrameRate;
}
