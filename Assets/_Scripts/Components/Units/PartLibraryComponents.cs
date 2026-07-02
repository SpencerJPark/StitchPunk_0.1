using Unity.Entities;

// Singleton holder for the baked PartLibrary blob. Mirrors ItemLibrary / ItemLibraryReference.
// PartLibraryReference carries the managed SO ref through baking; PartLibraryBakingSystem
// (PostBakingSystemGroup) reads it, builds the blob, and writes it into PartLibrary.

public struct PartLibrary : IComponentData
{
    public BlobAssetReference<PartLibraryBlob> library;
}

public struct PartLibraryReference : IComponentData
{
    public UnityObjectRef<PartLibrarySO> library;
}
