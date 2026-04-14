using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class FactoryStationAuthoring : MonoBehaviour
{
    [FormerlySerializedAs("stationType")] public StructureType structureType;
    public int gridX;
    public int gridZ;
    public int workerSlots;   // how many workers can be assigned to this station (0 = automatic)

    public class Baker : Baker<FactoryStationAuthoring>
    {
        public override void Bake(FactoryStationAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new FactoryStation
            {
                structureType = authoring.structureType,
                gridX       = authoring.gridX,
                gridZ       = authoring.gridZ,
            });

            AddBuffer<StationInputSlot>(entity);
            AddBuffer<StationOutputSlot>(entity);

            AddComponent<ProductionProgress>(entity);
            SetComponentEnabled<ProductionProgress>(entity, false);

            DynamicBuffer<StationWorkerSlot> workerSlotBuffer = AddBuffer<StationWorkerSlot>(entity);
            for (int i = 0; i < authoring.workerSlots; i++)
                workerSlotBuffer.Add(new StationWorkerSlot { workerEntity = Entity.Null });
        }
    }
}
