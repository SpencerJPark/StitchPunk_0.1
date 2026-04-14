using Unity.Entities;

public struct RecipeBlob
{
    public StructureType structureType;
    public BlobArray<FactoryItemType> inputs;
    public BlobArray<FactoryItemType> outputs;
    public float durationSeconds;
    public int workersRequired;
}

public struct FactoryLibraryBlob
{
    public BlobArray<RecipeBlob> recipes;

    public int FindRecipeIndex(StructureType structureType)
    {
        for (int i = 0; i < recipes.Length; i++)
        {
            if (recipes[i].structureType == structureType)
                return i;
        }
        return -1;
    }
}
