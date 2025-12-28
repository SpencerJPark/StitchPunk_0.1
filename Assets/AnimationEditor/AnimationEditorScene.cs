// =====================================
// ANIMATION EDITOR TAG & COMPONENTS
// =====================================

using UnityEngine;

/// <summary>
/// Place this component on a GameObject in your animation editing scene.
/// The animation systems will only run in Play mode when this tag is present.
/// </summary>
public class AnimationEditorScene : MonoBehaviour
{
    [Header("Editor Settings")]
    public bool autoPlayOnStart = true;
    public AnimationType defaultAnimation = AnimationType.Idle;
    
    [Header("Preview Character")]
    [Tooltip("The character prefab/object to preview animations on")]
    public GameObject previewCharacter;
    
    [Header("Camera Setup")]
    public Camera editorCamera;
    public float orthoSize = 5f;
    
    private void Awake()
    {
        if (editorCamera != null)
        {
            editorCamera.orthographic = true;
            editorCamera.orthographicSize = orthoSize;
            editorCamera.transform.rotation = Quaternion.identity;
        }
    }
    
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw a frame to show this is the animation editor scene
        Gizmos.color = Color.cyan;
        float size = orthoSize * 2f;
        float aspect = editorCamera != null ? editorCamera.aspect : 16f/9f;
        Vector3 center = Vector3.zero;
        
        Gizmos.DrawWireCube(center, new Vector3(size * aspect, size, 0.1f));
        
        UnityEditor.Handles.Label(center + Vector3.up * (size * 0.5f + 0.5f), 
            "Animation Editor Scene", 
            new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.cyan } });
    }
#endif
}