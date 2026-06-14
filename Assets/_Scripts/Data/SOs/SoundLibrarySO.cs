using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "_SoundLibrary", menuName = "Audio/Sound Library")]
public class SoundLibrarySO : ScriptableObject
{
    public List<SoundSO> sounds = new List<SoundSO>();

    public SoundSO GetSoundSO(SoundType type)
    {
        foreach (SoundSO so in sounds)
        {
            if (so != null && so.type == type)
                return so;
        }
        return null;
    }
}
