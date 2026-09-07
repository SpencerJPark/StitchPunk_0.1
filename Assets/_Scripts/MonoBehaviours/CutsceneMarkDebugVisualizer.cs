using System.Collections.Generic;
using DotsAnimationToolkit;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Debug-only: draws a flat translucent disc in the Game view over every currently outstanding
/// cutscene mark (Player and NPC alike), sized to its tolerance radius, so "where do I need to
/// stand" is visible without opening the Entities window. The toolkit's own Scene-view mark
/// overlay (CutsceneMarkSceneOverlay) is editor-authoring-only and never runs in Play mode, which is
/// the gap this fills. One disc GameObject per marked entity, created/destroyed as marks come and go.
/// </summary>
public class CutsceneMarkDebugVisualizer : MonoBehaviour
{
    private static readonly Color DiscColor = new Color(0.2f, 1f, 1f, 0.35f);

    private readonly Dictionary<Entity, GameObject> discsByEntity = new Dictionary<Entity, GameObject>();
    private Material discMaterial;
    private readonly List<Entity> staleEntities = new List<Entity>();

    private void Update()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        EntityManager entityManager = world.EntityManager;
        EntityQuery markQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CutsceneMoveToMark>());
        Unity.Collections.NativeArray<Entity> markedEntities =
            markQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

        HashSet<Entity> stillOutstanding = new HashSet<Entity>();

        for (int i = 0; i < markedEntities.Length; i++)
        {
            Entity entity = markedEntities[i];
            if (!entityManager.IsComponentEnabled<CutsceneMoveToMark>(entity))
                continue;

            stillOutstanding.Add(entity);
            CutsceneMoveToMark mark = entityManager.GetComponentData<CutsceneMoveToMark>(entity);

            if (!discsByEntity.TryGetValue(entity, out GameObject disc) || disc == null)
            {
                disc = CreateDisc();
                discsByEntity[entity] = disc;
            }

            float diameter = mark.toleranceMeters * 2f;
            disc.transform.position = new Vector3(mark.position.x, mark.position.y + 0.02f, mark.position.z);
            disc.transform.localScale = new Vector3(diameter, 0.01f, diameter);
        }

        markedEntities.Dispose();

        staleEntities.Clear();
        foreach (KeyValuePair<Entity, GameObject> pair in discsByEntity)
        {
            if (!stillOutstanding.Contains(pair.Key))
                staleEntities.Add(pair.Key);
        }

        for (int i = 0; i < staleEntities.Count; i++)
        {
            GameObject disc = discsByEntity[staleEntities[i]];
            if (disc != null) Destroy(disc);
            discsByEntity.Remove(staleEntities[i]);
        }
    }

    private GameObject CreateDisc()
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "CutsceneMarkDebugDisc";
        Collider discCollider = disc.GetComponent<Collider>();
        if (discCollider != null) Destroy(discCollider);

        if (discMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            discMaterial = new Material(shader) { color = DiscColor };
            if (discMaterial.HasProperty("_Surface")) discMaterial.SetFloat("_Surface", 1f); // URP Unlit: transparent
            discMaterial.SetFloat("_Blend", 0f);
            discMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            discMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        disc.GetComponent<Renderer>().sharedMaterial = discMaterial;

        return disc;
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<Entity, GameObject> pair in discsByEntity)
        {
            if (pair.Value != null) Destroy(pair.Value);
        }
        discsByEntity.Clear();
    }
}
