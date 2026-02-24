using Unity.Entities;
using UnityEngine;

public struct ScoringLibrary : IComponentData
{
    public BlobAssetReference<AIScoringLibraryBlob> library;
}
public struct ScoringLibraryReference : IComponentData
{
    public UnityObjectRef<AIScoringLibrarySO> library;
}


public struct AnimationLibrary : IComponentData {
    public BlobAssetReference<AnimationLibraryBlob> library;
}
public struct AnimationLibraryReference : IComponentData
{
    public UnityObjectRef<AnimationLibrarySO> library;
}

