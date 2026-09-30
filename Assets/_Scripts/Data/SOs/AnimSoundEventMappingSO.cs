using UnityEngine;
using System.Collections.Generic;

// Maps a clip's AnimEvent keys to a SoundType: either a fixed Sound per key, or soundFromIntParam to play the SoundType carried in the event's int param.
[CreateAssetMenu(fileName = "_AnimSoundEventMapping", menuName = "Sound/Anim Sound Event Mapping")]
public class AnimSoundEventMappingSO : ScriptableObject
{
    public List<AnimSoundEventEntry> entries = new List<AnimSoundEventEntry>();
}

[System.Serializable]
public struct AnimSoundEventEntry
{
    [Tooltip("The clip's authored event key (from the toolkit's Generate Event Name Constants).")]
    public uint eventKey;

    [Tooltip("Tick to play the SoundType named by the event's int param, so one Sound key covers every sound. The Sound field is then ignored.")]
    public bool soundFromIntParam;

    [SearchableEnum] public SoundType sound;
}
