using Unity.Entities;

public struct InteractionBlob
{
    public ActionType             actionType;
    public int                    priority;
    public int                    maxOccupants;
    public float                  range;
    public BlobArray<FactionType> allowedFactions;
    public NeedType               satisfiedNeed;
    public float                  restorationAmount;
    public float                  duration;
}

public struct InteractionLibraryBlob
{
    public BlobArray<InteractionBlob> interactions;
}
