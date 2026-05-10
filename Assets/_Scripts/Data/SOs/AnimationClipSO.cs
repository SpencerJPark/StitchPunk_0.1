using UnityEngine;
using System.Collections.Generic;
using System;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "AnimationClip", menuName = "Animation / Clip")]
public class AnimationClipSO : ScriptableObject
{
    [SearchableEnum] public AnimationType animationType;
    public float duration = 1f;
    public bool looping = true;
    public bool allowBlendIn = true;
    public bool allowBlendOut = true;
    
    [SerializeField]
    public List<PartTrack> partTracks = new List<PartTrack>();
    
    [Serializable]
    public class PartTrack
    {
        [FormerlySerializedAs("bodyPart")] public AnimationTarget animationTarget;
        public BlendMode blendMode = BlendMode.Additive;
        public AnimatedProperties animatedProperties = AnimatedProperties.All;
        public InterpolationMode interpolation = InterpolationMode.Linear;
        
        [SerializeField]
        public List<Keyframe> keyframes = new List<Keyframe>();
    }
    
    [Serializable]
    public class Keyframe
    {
        [Range(0f, 1f)]
        public float normalizedTime;
        
        public Vector3 position;       // x, y = local offset, z = layer order offset
        public float rotation;         // Z rotation in degrees
        public Vector2 scale = Vector2.one;  // Multiplier (1,1 = no change, -1,1 = flip X)
        public int imageIndex = -1;    // -1 = don't change
        
        // Optional per-keyframe interpolation override
        public bool overrideInterpolation;
        public InterpolationMode interpolationOverride;
    }
}