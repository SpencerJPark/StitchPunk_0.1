using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place on a world emitter GameObject (machine, fire, wind). Bakes a LoopingSound; the AudioManager
/// maps the entity to a pooled AudioSource and stops it automatically when the component is disabled
/// or the entity is destroyed.
/// </summary>
public class AmbientSoundAuthoring : MonoBehaviour
{
    [SearchableEnum] public SoundType type;
    public float volumeMul = 1f;
    public float pitchMul  = 1f;
    [Tooltip("Start the loop playing on spawn.")]
    public bool startEnabled = true;

    public class Baker : Baker<AmbientSoundAuthoring>
    {
        public override void Bake(AmbientSoundAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new LoopingSound
            {
                type      = authoring.type,
                volumeMul = authoring.volumeMul,
                pitchMul  = authoring.pitchMul,
            });
            SetComponentEnabled<LoopingSound>(entity, authoring.startEnabled);
        }
    }
}
