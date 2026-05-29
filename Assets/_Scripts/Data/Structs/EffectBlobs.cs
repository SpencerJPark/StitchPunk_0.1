using Unity.Entities;

public struct EffectBlob
{
    public EffectType                 effectType;
    public float                      value;
    public float                      timer;
    public BlobArray<MotivationType>  behaviours;
}

public struct EffectLibraryBlob
{
    public BlobArray<EffectBlob> effects;
}
