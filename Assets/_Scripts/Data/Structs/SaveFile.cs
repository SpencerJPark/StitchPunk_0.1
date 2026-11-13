using System;

// JSON save DTOs: JsonUtility-safe primitives only. Component state rides as Base64 raw bytes, so ECS types never appear as fields.
[Serializable]
public class SaveFile
{
    public int version = 1;
    public SaveHeader header;
    public EntityRecord[] entities;
}

[Serializable]
public class SaveHeader
{
    public long timestampUnix;
    public double totalPlaySeconds;
    public string sceneLabel;
}

public static class SaveRoles
{
    public const string Player   = "Player";
    public const string GameData = "GameData";
    public const string Minion   = "Minion";
}

[Serializable]
public class EntityRecord
{
    public string role; // "Player" | "GameData" | "Minion"
    public ComponentRecord[] components;

    // Minion-only: empty / 0 for the Player & GameData singletons.
    public string persistId; // PersistId.value (Hash128) as string — stable identity for respawn
    public int unitType;     // (int)UnitType — pool key to respawn the body prefab from
}

[Serializable]
public class ComponentRecord
{
    public string type;    // assembly-qualified type name (short-name fallback on load)
    public bool enabled;   // for IEnableableComponent; ignored otherwise
    public string data;    // Base64 of the component struct's raw bytes
}
