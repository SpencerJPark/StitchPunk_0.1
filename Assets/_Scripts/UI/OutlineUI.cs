using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;

[RequireComponent(typeof(RawImage))]
public class OutlineUI : MonoBehaviour, IUpdateObserver
{
    [Header("Render Texture")]
    [SerializeField] private RenderTexture outlineRenderTexture;
    
    [Header("Shader")]
    [SerializeField] private Shader outlineShader;
    
    [Header("Outline Settings")]
    [SerializeField] private Color outlineColor = Color.white;
    [SerializeField, Range(1f, 10f)] private float outlineWidth = 2f;
    
    private RawImage rawImage;
    private Material outlineMaterial;
    private EntityManager entityManager;
    private Entity playerEntity;
    private bool dotsWorldInitialized;
    
    private void Awake()
    {
        rawImage = GetComponent<RawImage>();

        if (outlineShader != null)
        {
            outlineMaterial = new Material(outlineShader);
        }
        else
        {
            Debug.LogError("[OutlineUI] Outline shader not assigned!");
        }
        
        if (rawImage != null)
        {
            rawImage.texture = outlineRenderTexture;
        }
        
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld != null && defaultWorld.IsCreated)
        {
            entityManager = defaultWorld.EntityManager;
            dotsWorldInitialized = true;
        }
        else
        {
            Debug.LogWarning("[OutlineUI] DOTS world not initialized yet");
        }

        AdjustToScreenSize();
    }

    private void OnEnable() => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);
    
    public void ObservedUpdate()
    {
        if (EarlyOut()) return;
        
        AdjustToScreenSize();
        
        if (rawImage != null && !rawImage.enabled)
        {
            rawImage.enabled = true;
        }
        
        UpdateMaterialProperties();
    }

    private bool EarlyOut()
    {
        if (outlineRenderTexture == null || outlineMaterial == null)
        {
            return true;
        }
        
        if (!dotsWorldInitialized)
        {
            TryInitializeDOTS();
            if (!dotsWorldInitialized)
            {
                return true;
            }
        }
        
        if (!ShouldRenderOutline())
        {
            if (rawImage != null)
            {
                rawImage.enabled = false;
            }

            return true;
        }

        return false;
    }

    private void TryInitializeDOTS()
    {
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld != null && defaultWorld.IsCreated)
        {
            entityManager = defaultWorld.EntityManager;
            dotsWorldInitialized = true;
        }
    }
    
    private bool ShouldRenderOutline()
    {
        if (playerEntity == Entity.Null || !entityManager.Exists(playerEntity))
        {
            EntityQuery playerQuery = entityManager.CreateEntityQuery(typeof(Player));
            
            if (playerQuery.IsEmpty)
            {
                return false;
            }
            
            playerEntity = playerQuery.GetSingletonEntity();
        }
        
        if (entityManager.HasComponent<Player>(playerEntity))
        {
            Player playerData = entityManager.GetComponentData<Player>(playerEntity);

            if (playerData.interactableEntity == Entity.Null)
            {
                return false;
            }

            if (!entityManager.Exists(playerData.interactableEntity))
            {
                return false;
            }
            
            return true;
        }
        
        return false;
    }
    
    private void AdjustToScreenSize()
    {
        if (rawImage == null) return;
        
        RectTransform rectTransform = rawImage.rectTransform;
        
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }
    
    private void UpdateMaterialProperties()
    {
        if (outlineMaterial == null) return;
        
        outlineMaterial.SetColor("_OutlineColor", outlineColor);
        outlineMaterial.SetFloat("_OutlineWidth", outlineWidth);

        if (rawImage != null)
        {
            rawImage.material = outlineMaterial;
        }
    }
    
    private void OnDestroy()
    {
        if (outlineMaterial != null)
        {
            Destroy(outlineMaterial);
        }
    }
    
    private void OnValidate()
    {
        if (Application.isPlaying && outlineMaterial != null)
        {
            UpdateMaterialProperties();
        }
    }
}