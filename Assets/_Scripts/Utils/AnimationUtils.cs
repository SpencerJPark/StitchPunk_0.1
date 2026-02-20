using UnityEngine;
using Unity.Entities;

    public static class AnimationUtils 
    { 
        public static float QuantizeTime(float normalizedTime, float duration, int frameRate)
        {
            if (frameRate <= 0 || duration <= 0) return normalizedTime;
        
            float totalFrames = duration * frameRate;
            float currentFrame = Mathf.Floor(normalizedTime * totalFrames);
            return currentFrame / totalFrames;
        }
    
        public static void SetLayer(
        ref DynamicBuffer<AnimationLayer> layers,
        AnimationLayerType layerType,
        AnimationType animation,
        float speed = 1f,
        bool looping = true)
        {
        // Find existing layer slot
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].layer == layerType)
                {
                    var layer = layers[i];
                    layer.animation = animation;
                    layer.time = 0f;
                    layer.speed = speed;
                    layer.active = true;
                    layer.looping = looping;
                    layers[i] = layer;
                    return;
                }
            }
        
        // Add new and sort by layer priority
        layers.Add(new AnimationLayer
        {
            layer = layerType,
            animation = animation,
            time = 0f,
            speed = speed,
            active = true,
            looping = looping
        });
        
        SortLayers(ref layers);
    }
        
    public static bool IsCurrentLayer(ref DynamicBuffer<AnimationLayer> layers, AnimationLayerType layerType, AnimationType animationType)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].layer == layerType)
            {
                return layers[i].animation == animationType;
            }
        }
        return false;
    }    
    
    public static void ClearLayer(ref DynamicBuffer<AnimationLayer> layers, AnimationLayerType layerType)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].layer == layerType)
            {
                var layer = layers[i];
                layer.active = false;
                layers[i] = layer;
                return;
            }
        }
    }
    
    // fix this 
    private static void SortLayers(ref DynamicBuffer<AnimationLayer> layers)
    {
        // Simple sort by layer enum value
        for (int i = 0; i < layers.Length - 1; i++)
        {
            for (int j = 0; j < layers.Length - 1 - i; j++)
            {
                if ((int)layers[j].layer > (int)layers[j + 1].layer)
                {
                    var temp = layers[j];
                    layers[j] = layers[j + 1];
                    layers[j + 1] = temp;
                }
            }
        }
    }
    }



/*
 // In a locomotion system
var layers = SystemAPI.GetBuffer<AnimationLayer>(entity);

if (isMoving)
    AnimationUtil.SetLayer(ref layers, AnimationLayerType.Base, AnimationType.Walk);
else
    AnimationUtil.SetLayer(ref layers, AnimationLayerType.Base, AnimationType.Idle);

// In a blink system
AnimationUtil.SetLayer(ref layers, AnimationLayerType.Eyes, AnimationType.Blink, looping: false);
// When blink finishes, it auto-deactivates because looping = false

// In an emote system
AnimationUtil.SetLayer(ref layers, AnimationLayerType.Override, AnimationType.Scream, looping: false);

// Clear a layer manually
AnimationUtil.ClearLayer(ref layers, AnimationLayerType.Face);
*/