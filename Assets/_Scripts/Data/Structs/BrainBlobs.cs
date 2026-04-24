using Unity.Entities;

public struct BrainEntryBlob
{
    public BrainType brainType;
    public FactionType factionType;
    public bool canBePlayerControlled;
    public float awarenessRange;
    public BlobArray<MotivationType> motivation;
    public int randomMotivationAmount;
    public BlobArray<MotivationType> randomMotivations;
    public BlobArray<FactionType>   attackFactions;
    public BlobArray<AttackType> attacks;
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
