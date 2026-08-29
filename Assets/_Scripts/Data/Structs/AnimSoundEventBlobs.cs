using Unity.Entities;

public struct AnimSoundEventMappingBlob
{
    public BlobArray<AnimSoundEventEntryBlob> entries;

    public bool TryGetSound(uint eventKey, out SoundType sound)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].eventKey == eventKey)
            {
                sound = entries[i].sound;
                return true;
            }
        }
        sound = default;
        return false;
    }
}

public struct AnimSoundEventEntryBlob
{
    public uint eventKey;
    public SoundType sound;
}
