using Unity.Entities;
using UnityEngine;

public class EnemyAttackHQAuthoring : MonoBehaviour {


    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<EnemyAttackHQAuthoring> {


        public override void Bake(EnemyAttackHQAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new EnemyAttackHQ());
        }
    }

}

public struct EnemyAttackHQ : IComponentData {

}