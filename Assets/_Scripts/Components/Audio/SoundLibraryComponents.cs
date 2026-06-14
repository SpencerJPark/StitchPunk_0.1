using Unity.Entities;

public struct SoundLibrary : IComponentData
{
    public BlobAssetReference<SoundLibraryBlob> library;
}

public struct SoundLibraryReference : IComponentData
{
    public UnityObjectRef<SoundLibrarySO> library;
}
