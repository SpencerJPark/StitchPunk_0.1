using Unity.Entities;
using Unity.Mathematics;

public struct KeyframeBlob
{
    public float normalizedTime;
    public float3 position;
    public float rotation;
    public float2 scale;
    public int imageIndex;
    public InterpolationMode interpolation;
}

public struct AnimationTargetTrackBlob
{
    public AnimationTarget animationTarget;
    public BlendMode blendMode;
    public AnimatedProperties animatedProperties;
    public InterpolationMode defaultInterpolation;
    public BlobArray<KeyframeBlob> keyframes;
}

public struct SoundMarkerBlob
{
    public SoundType type;
    public float normalizedTime;
}

public struct AnimationClipBlob
{
    public AnimationType animationType;
    public float duration;
    public bool looping;
    public bool allowBlendIn;
    public bool allowBlendOut;
    public BlobArray<AnimationTargetTrackBlob> animationTargetTracks;
    public BlobArray<SoundMarkerBlob> soundMarkers;
}

public struct AnimationLibraryBlob
{
    public BlobArray<AnimationClipBlob> clips;
}