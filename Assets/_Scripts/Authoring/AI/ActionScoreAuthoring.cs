using Unity.Entities;
using UnityEngine;

public class ActionScoreAuthoring : MonoBehaviour
{
    public class Baker : Baker<ActionScoreAuthoring>
    {
        public override void Bake(ActionScoreAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddBuffer<ActionScore>(entity);
        }
    }
}

public struct ActionScore : IBufferElementData
{
    public ActionType actionType;
    public float score;
    public bool isValid;
}