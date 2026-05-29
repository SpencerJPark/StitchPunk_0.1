using Unity.Entities;

public struct EffectLibrary : IComponentData
{
    public BlobAssetReference<EffectLibraryBlob> library;
}

public struct EffectLibraryReference : IComponentData
{
    public UnityObjectRef<EffectLibrarySO> library;
}
