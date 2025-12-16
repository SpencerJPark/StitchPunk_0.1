using UnityEngine;
using UnityEngine.UI;
using Unity.Entities;

/// <summary>
/// UI component that renders outline effect to screen
/// Queries DOTS Player entity to determine if outline should render
/// </summary>
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
    
    // initialize
    private void Awake()
    {
        // Get RawImage component
        rawImage = GetComponent<RawImage>();
        
        // Create material from shader
        if (outlineShader != null)
        {
            outlineMaterial = new Material(outlineShader);
        }
        else
        {
            Debug.LogError("[OutlineUI] Outline shader not assigned!");
        }
        
        // Set up RawImage
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
        
        // Ensure UI is stretched to fill screen
        AdjustToScreenSize();
    }
    
    
    private void OnEnable() => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);
    
    public void ObservedUpdate()
    {
        if (EarlyOut()) return;
        
        AdjustToScreenSize();
        
        // Enable UI rendering
        if (rawImage != null && !rawImage.enabled)
        {
            rawImage.enabled = true;
        }
        
        UpdateMaterialProperties();
    }

    private bool EarlyOut()
    {
        // Early out if no render texture or material
        if (outlineRenderTexture == null || outlineMaterial == null)
        {
            return true;
        }
        
        // Early out if DOTS world not ready
        if (!dotsWorldInitialized)
        {
            TryInitializeDOTS();
            if (!dotsWorldInitialized)
            {
                return true;
            }
        }
        
        // Early out if no player entity or no interactable
        if (!ShouldRenderOutline())
        {
            // Optionally hide the UI when not needed
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
        // Find player entity if we don't have it
        if (playerEntity == Entity.Null || !entityManager.Exists(playerEntity))
        {
            EntityQuery playerQuery = entityManager.CreateEntityQuery(typeof(Player));
            
            if (playerQuery.IsEmpty)
            {
                return false;
            }
            
            playerEntity = playerQuery.GetSingletonEntity();
        }
        
        // Check if player has an interactable entity assigned
        if (entityManager.HasComponent<Player>(playerEntity))
        {
            Player playerData = entityManager.GetComponentData<Player>(playerEntity);
            
            // Early out if no interactable
            if (playerData.interactableEntity == Entity.Null)
            {
                return false;
            }
            
            // Check if interactable entity still exists
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
        
        // Stretch to fill entire screen
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }
    
    private void UpdateMaterialProperties()
    {
        if (outlineMaterial == null) return;
        
        // Update shader properties
        outlineMaterial.SetColor("_OutlineColor", outlineColor);
        outlineMaterial.SetFloat("_OutlineWidth", outlineWidth);
        
        // Assign material to RawImage
        if (rawImage != null)
        {
            rawImage.material = outlineMaterial;
        }
    }
    
    private void OnDestroy()
    {
        // Clean up material
        if (outlineMaterial != null)
        {
            Destroy(outlineMaterial);
        }
    }
    
    private void OnValidate()
    {
        // Update material in editor when values change
        if (Application.isPlaying && outlineMaterial != null)
        {
            UpdateMaterialProperties();
        }
    }
}