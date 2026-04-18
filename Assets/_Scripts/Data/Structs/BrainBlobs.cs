using Unity.Entities;

public struct BrainEntryBlob
{
    public BrainType brainType;
    public FactionType factionType;
    public bool canBePlayerControlled;
    public float awarenessRange;
    public BlobArray<BehaviourType> behaviours;
    public int randomBehaviourAmount;
    public BlobArray<BehaviourType> randomBehaviours;
    public BlobArray<FactionType>   attackFactions;
}

public struct BrainLibraryBlob
{
    public BlobArray<BrainEntryBlob> entries;

    public int FindBrainIndex(BrainType brainType)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].brainType == brainType) return i;
        }
        return -1;
    }
}
