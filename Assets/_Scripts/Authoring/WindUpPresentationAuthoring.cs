using Unity.Entities;
using UnityEngine;

// Owner checkpoint H5-D3: these values are judged from a capture, not from code.
public class WindUpPresentationAuthoring : MonoBehaviour
{
    [Tooltip("Colour the body parts flash toward while a unit winds up a ranged shot.")]
    public Color flashColor = Color.white;
    [Tooltip("Flash strength at the end of the wind-up (0 = none, 1 = fully flash colour).")]
    [Range(0f, 1f)] public float flashMaximum = 0.55f;
    [Tooltip("Visual scale multiplier at the end of the wind-up.")]
    [Range(1f, 1.5f)] public float scaleMaximum = 1.10f;

    public class Baker : Baker<WindUpPresentationAuthoring>
    {
        public override void Bake(WindUpPresentationAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new WindUpPresentationConfig
            {
                flashColor = new Unity.Mathematics.float4(authoring.flashColor.r, authoring.flashColor.g, authoring.flashColor.b, authoring.flashColor.a),
                flashMaximum = authoring.flashMaximum,
                scaleMaximum = authoring.scaleMaximum
            });
        }
    }
}
