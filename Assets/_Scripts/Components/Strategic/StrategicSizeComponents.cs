using Unity.Entities;

public enum StrategicSizeCategory : byte
{
    Wanderer,
    Horde,
    Vehicle,
    Landmark,
    Player,
}

// Marks an entity that grows in the zoomed-out view. Absence of the tag means "keeps its true size".
public struct StrategicSizeTag : IComponentData
{
    public StrategicSizeCategory category;
    // The caravan's drive system keeps pushing this forward while someone sits on the seat; a seated body scales with the vehicle, not as a lone player.
    public double seatedUntilSeconds;
}

// Already blended by zoom mix (1 at ground level), written each frame by StrategicRigDriver from the StrategicZoomProfile.
public struct StrategicSizeScales : IComponentData
{
    public float wandererScale;
    public float hordeScale;
    public float vehicleScale;
    public float landmarkScale;
    public float playerScale;

    public static StrategicSizeScales Identity()
    {
        return new StrategicSizeScales
        {
            wandererScale = 1f,
            hordeScale = 1f,
            vehicleScale = 1f,
            landmarkScale = 1f,
            playerScale = 1f,
        };
    }

    public float ScaleFor(StrategicSizeCategory category)
    {
        switch (category)
        {
            case StrategicSizeCategory.Horde: return hordeScale;
            case StrategicSizeCategory.Vehicle: return vehicleScale;
            case StrategicSizeCategory.Landmark: return landmarkScale;
            case StrategicSizeCategory.Player: return playerScale;
            default: return wandererScale;
        }
    }
}
