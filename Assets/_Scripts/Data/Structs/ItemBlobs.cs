using Unity.Entities;

public struct ItemBlob
{
    public ItemType       itemType;
    public ItemCategory   category;
    public int            healAmount;
    public MotivationType satisfiedMotivation;
    public float          restorationAmount;
    public float          pickupRange;
    public float          consumeDuration;
    public float          baseUtility;
}

public struct ItemLibraryBlob
{
    public BlobArray<ItemBlob> items;
}
