using Unity.Entities;
using UnityEngine;


public class UnitAnimationsAuthoring : MonoBehaviour {

	public AnimationType idle;
    public AnimationType move;
    public AnimationType attack;
    public AnimationType interact;
    public AnimationType overide = AnimationType.None;

    public class Baker : Baker<UnitAnimationsAuthoring> {


        public override void Bake(UnitAnimationsAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitAnimations{
				idle = authoring.idle,
				move = authoring.move,
				attack = authoring.attack,
				interact = authoring.interact,
				overide = authoring.overide
			});
        }
    }
}