using Unity.Entities;
using UnityEngine;
using System.Collections.Generic;

public class AnimatorAuthoring : MonoBehaviour
{
    [Header("Starting Animations")]
    public List<StartingLayer> startingLayers = new List<StartingLayer>();

    [System.Serializable]
    public class StartingLayer
    {
        public AnimationLayerType layer = AnimationLayerType.Base;
        public AnimationType animation = AnimationType.Idle;
        public float speed = 1f;
        public bool looping = true;
    }

    private void Reset()
    {
        // Default setup when component is added
        startingLayers = new List<StartingLayer>
        {
            new StartingLayer
            {
                layer = AnimationLayerType.Base,
                animation = AnimationType.Idle,
                speed = 1f,
                looping = true
            }
        };
    }

    public class Baker : Baker<AnimatorAuthoring>
    {
        public override void Bake(AnimatorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            var layers = AddBuffer<AnimationLayer>(entity);

            foreach (var startingLayer in authoring.startingLayers)
            {
                layers.Add(new AnimationLayer
                {
                    layer = startingLayer.layer,
                    animation = startingLayer.animation,
                    time = 0f,
                    speed = startingLayer.speed,
                    active = true,
                    looping = startingLayer.looping
                });
            }

            SortLayers(ref layers);

            AddBuffer<AnimatorTarget>(entity);

            AddBuffer<SetAnimation>(entity);
            AddComponent<AnimationRequest>(entity);
            SetComponentEnabled<AnimationRequest>(entity, false);
        }

        private void SortLayers(ref DynamicBuffer<AnimationLayer> layers)
        {
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
}
