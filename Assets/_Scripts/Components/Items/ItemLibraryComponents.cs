using Unity.Entities;

public struct ItemLibrary : IComponentData
{
    public BlobAssetReference<ItemLibraryBlob> library;
}

public struct ItemLibraryReference : IComponentData
{
    public UnityObjectRef<ItemLibrarySO> library;
}

// One slot per ItemType on the ItemLibrary entity; Entity.Null when the item fires no projectile (H5-D7).
[InternalBufferCapacity(0)]
public struct ItemProjectilePrefab : IBufferElementData
{
    public Entity prefab;
}
