using UnityEngine;

[ExecuteInEditMode]
public class TextureArrayPreview : MonoBehaviour
{
    [Header("Preview Settings")]
    [Tooltip("Index to display in the texture array")]
    public int imageIndex = 0;
    
    [Tooltip("Property name in the shader")]
    public string propertyName = "_ImageIndex";
    
    private Renderer _renderer;
    private MaterialPropertyBlock _propBlock;
    private int _cachedIndex = -1;
    private string _cachedPropertyName;

    void OnEnable()
    {
        UpdatePreview();
    }

    void OnValidate()
    {
        UpdatePreview();
    }

    void Update()
    {
        // Keep updating in edit mode in case something changes
        #if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (_cachedIndex != imageIndex || _cachedPropertyName != propertyName)
            {
                UpdatePreview();
            }
        }
        #endif
    }

    void UpdatePreview()
    {
        if (_renderer == null) 
            _renderer = GetComponent<Renderer>();
        
        if (_renderer == null) 
            return;
        
        if (_propBlock == null) 
            _propBlock = new MaterialPropertyBlock();
        
        _renderer.GetPropertyBlock(_propBlock);
        _propBlock.SetFloat(propertyName, imageIndex);
        _renderer.SetPropertyBlock(_propBlock);
        
        _cachedIndex = imageIndex;
        _cachedPropertyName = propertyName;
    }

    // Optional: clear overrides when component is removed
    void OnDisable()
    {
        if (_renderer != null)
        {
            _renderer.SetPropertyBlock(null);
        }
    }
}