using Unity.Entities;
using UnityEngine;

public class VehicleLibraryAuthoring : MonoBehaviour
{
    public VehicleLibrarySO library;

    public class Baker : Baker<VehicleLibraryAuthoring>
    {
        public override void Bake(VehicleLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);
            if (authoring.library.vehicles != null)
            {
                foreach (VehicleProfileSO profile in authoring.library.vehicles)
                {
                    if (profile != null) DependsOn(profile);
                }
            }

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new VehicleLibraryReference { library = authoring.library });
            AddComponent(entity, new VehicleLibrary());
        }
    }
}
