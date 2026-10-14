using Unity.Entities;
using UnityEngine;

public class WorkstationGhostPresenter : MonoBehaviour
{
    [SerializeField] private GameObject ghostObject;
    [SerializeField] private Color validColor = new Color(0.3f, 1f, 0.4f, 0.45f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.45f);

    private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    private EntityQuery playerQuery;
    private World queryWorld;
    private Renderer[] ghostRenderers;
    private MaterialPropertyBlock propertyBlock;
    private bool lastWasValid;
    private bool tintApplied;

    private void LateUpdate()
    {
        if (ghostObject == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            ghostObject.SetActive(false);
            return;
        }

        if (queryWorld != world)
        {
            queryWorld = world;
            playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        }

        EntityManager entityManager = world.EntityManager;
        if (playerQuery.IsEmptyIgnoreFilter)
        {
            ghostObject.SetActive(false);
            return;
        }

        Entity playerEntity = playerQuery.GetSingletonEntity();
        if (!entityManager.HasComponent<PlayerWorkstationPlacement>(playerEntity)
            || !entityManager.IsComponentEnabled<PlayerWorkstationPlacement>(playerEntity))
        {
            ghostObject.SetActive(false);
            return;
        }

        PlayerWorkstationPlacement placement = entityManager.GetComponentData<PlayerWorkstationPlacement>(playerEntity);
        ghostObject.SetActive(true);
        ghostObject.transform.SetPositionAndRotation(
            new Vector3(placement.position.x, placement.position.y, placement.position.z),
            new Quaternion(placement.rotation.value.x, placement.rotation.value.y, placement.rotation.value.z, placement.rotation.value.w));

        // Renderers and the block are cached once; the tint is only re-applied when validity flips.
        if (ghostRenderers == null)
        {
            ghostRenderers = ghostObject.GetComponentsInChildren<Renderer>(true);
            propertyBlock = new MaterialPropertyBlock();
        }

        if (tintApplied && lastWasValid == placement.isValid)
            return;

        Color tint = placement.isValid ? validColor : invalidColor;
        propertyBlock.SetColor(BaseColorPropertyId, tint);
        propertyBlock.SetColor(ColorPropertyId, tint);
        for (int index = 0; index < ghostRenderers.Length; index++)
        {
            ghostRenderers[index].SetPropertyBlock(propertyBlock);
        }

        lastWasValid = placement.isValid;
        tintApplied = true;
    }
}
