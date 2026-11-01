using Unity.Entities;
using UnityEngine;

// Put on a placed unit instance (not the shared prefab) to have it spawn holding this weapon item.
public class StartingWeaponAuthoring : MonoBehaviour
{
    [Tooltip("Weapon item prefab (e.g. Pistol) equipped through the normal pickup path when the unit spawns.")]
    public GameObject weaponPrefab;

    public class Baker : Baker<StartingWeaponAuthoring>
    {
        public override void Bake(StartingWeaponAuthoring authoring)
        {
            if (authoring.weaponPrefab == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new StartingWeapon
            {
                weaponPrefab = GetEntity(authoring.weaponPrefab, TransformUsageFlags.Dynamic),
            });
            SetComponentEnabled<StartingWeapon>(entity, true);
        }
    }
}
