using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_AnimationLibrary", menuName = "Animation / Animation Library")]
public class AnimationLibrarySO : ScriptableObject
{
    public List<AnimationClipSO> clips = new List<AnimationClipSO>();
    
    public AnimationClipSO GetClip(AnimationType type)
    {
        foreach (var clip in clips)
        {
            if (clip != null && clip.animationType == type)
                return clip;
        }
        return null;
    }
}