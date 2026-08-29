using UnityEngine;
using System.Collections.Generic;

// Maps a clip's authored AnimEvent keys to a SoundType. Populated once real clips carry event
// markers (Generate Event Name Constants in the toolkit mints the uint keys this list points at).
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

    [SearchableEnum] public SoundType sound;
}
