using Unity.Entities;

public struct InteractionBlob
{
    public ActionType             actionType;
    public int                    priority;
    public int                    maxOccupants;
    public float                  range;
    public BlobArray<FactionType> allowedFactions;
    public MotivationType         satisfiedMotivation;
    public float                  restorationAmount;
}

public struct InteractionLibraryBlob
{
    public BlobArray<InteractionBlob> interactions;
}
