using Unity.Entities;
using UnityEngine;


public class LoggingConfigAuthoring : MonoBehaviour
{
    public bool ai           = true;
    public bool stateMachine = true;
    public bool combat       = true;
    public bool movement     = true;
    public bool social       = true;
    public bool items        = true;
    public bool factory      = true;
    public bool health       = true;
    public bool general      = true;

    public class Baker : Baker<LoggingConfigAuthoring>
    {
        public override void Bake(LoggingConfigAuthoring authoring)
        {
            int mask = 0;
            if (authoring.ai)           mask |= (int)LogCategory.AI;
            if (authoring.stateMachine) mask |= (int)LogCategory.StateMachine;
            if (authoring.combat)       mask |= (int)LogCategory.Combat;
            if (authoring.movement)     mask |= (int)LogCategory.Movement;
            if (authoring.social)       mask |= (int)LogCategory.Social;
            if (authoring.items)        mask |= (int)LogCategory.Items;
            if (authoring.factory)      mask |= (int)LogCategory.Factory;
            if (authoring.health)       mask |= (int)LogCategory.Health;
            if (authoring.general)      mask |= (int)LogCategory.General;

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new LoggingConfig { EnabledCategories = mask });
        }
    }
}
