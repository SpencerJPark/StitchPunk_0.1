using Unity.Entities;

public struct ItemLibrary : IComponentData
{
    public BlobAssetReference<ItemLibraryBlob> library;
}

public struct ItemLibraryReference : IComponentData
{
    public UnityObjectRef<ItemLibrarySO> library;
}
