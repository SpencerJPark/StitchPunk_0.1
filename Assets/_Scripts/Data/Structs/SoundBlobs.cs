using Unity.Entities;

// Burst-readable per-sound params (enum-indexed). No AudioClips — those are managed and live on
// AudioManager; voice selection emits (type, variationIndex) and the manager resolves the clip.
public struct SoundBlob
{
    public SoundType type;
    public SoundBus  bus;
    public int       variationCount;  // number of authored clip variations (random pick range)
    public float     volumeMin;
    public float     volumeMax;
    public float     pitchMin;
    public float     pitchMax;
    public bool      loop;
    public byte      priority;
    public float     minDistance;
    public float     maxDistance;
    public int       maxConcurrent;
    public float     dedupWindow;
}

public struct SoundLibraryBlob
{
    public BlobArray<SoundBlob> sounds;
}
