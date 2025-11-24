using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class RagDollManager : MonoBehaviour
{
    
    [SerializeField] UnitTypeListSO unitTypeListSO;
    
    void Start()
    {
        DOTSEventsManager.Instance.OnHealthDead += DOTSEventsManager_OnHealthDead;
    }

    private void DOTSEventsManager_OnHealthDead(object sender, System.EventArgs e)
    {
        Entity entity = (Entity)sender;
        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        if (entityManager.HasComponent<UnitTypeHolder>(entity))
        {
            UnitTypeHolder unitTypeHolder = entityManager.GetComponentData<UnitTypeHolder>(entity);
            LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(entity);
            UnitTypeSO unitTypeSo = unitTypeListSO.GetUnitTypeSO(unitTypeHolder.unitType);
            Instantiate(unitTypeSo.ragdollPreFab, localTransform.Position, Quaternion.identity);
        }
    }

}
