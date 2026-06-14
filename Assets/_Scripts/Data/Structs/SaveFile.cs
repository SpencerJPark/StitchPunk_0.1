using System;

/// <summary>
/// Root DTO written to / read from disk as JSON by the generic save system.
///
/// These classes hold ONLY JsonUtility-safe primitives (strings, ints, arrays). Component
/// state is captured generically as Base64 raw bytes inside <see cref="ComponentRecord.data"/> —
/// so ECS types (float3, quaternion, enums) ride along inside the Base64 blob and never appear
/// as raw fields here. This is what lets the system save any IPersist-marked component with no
/// per-field code (contrast the old hand-written PlayerSaveData).
/// </summary>
[Serializable]
public class SaveFile
{
    public int version = 1;
    public SaveHeader header;
    public EntityRecord[] entities;
}

/// <summary>Lightweight, text-only header for populating a future load menu.</summary>
[Serializable]
public class SaveHeader
{
    public long timestampUnix;
    public double totalPlaySeconds;
    public string sceneLabel;
}

/// <summary>
/// Role string constants for <see cref="EntityRecord.role"/>. v1 persists two singletons located
/// by tag; a <c>SaveRole</c> enum + PersistId arrive with the multi-entity (minion) phase.
/// </summary>
public static class SaveRoles
{
    public const string Player   = "Player";
    public const string GameData = "GameData";
    public const string Minion   = "Minion";
}

/// <summary>One persisted entity. <see cref="role"/> tells load which entity to patch.</summary>
[Serializable]
public class EntityRecord
{
    public string role; // "Player" | "GameData" | "Minion"
    public ComponentRecord[] components;

    // Minion-only: empty / 0 for the Player & GameData singletons.
    public string persistId; // PersistId.value (Hash128) as string — stable identity for respawn
    public int unitType;     // (int)UnitType — pool key to respawn the body prefab from
}

/// <summary>One component's snapshot: its type name, enabled bit, and Base64 raw bytes.</summary>
[Serializable]
public class ComponentRecord
{
    public string type;    // assembly-qualified type name (short-name fallback on load)
    public bool enabled;   // for IEnableableComponent; ignored otherwise
    public string data;    // Base64 of the component struct's raw bytes
}
