using UnityEngine;

/// <summary>
/// Attach to a character’s Renderer. Registers with RiveRadio and shows one tile of the shared texture.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public sealed class RiveRadioClient : MonoBehaviour
{
    [Tooltip("If true, the Radio will try to use this index; if taken, it will assign a free one.")]
    public bool RequestSpecific = false;

    [Tooltip("Channel / tile index (0-based). If RequestSpecific=false, this is auto-assigned.")]
    public int ChannelIndex = -1;

    [Header("Optional per-instance properties")]
    public bool useTint = false;
    public Color tint = Color.white;
    [Tooltip("Tint color property name (auto-detects _BaseColor/_Color if empty).")]
    public string tintProperty = "";

    // Runtime
    public Renderer Renderer { get; private set; }
    [HideInInspector] public MaterialPropertyBlock SharedMPB;
    [HideInInspector] public int TintPropertyId;

    void Awake()
    {
        Renderer = GetComponent<Renderer>();
    }

    void OnEnable()
    {
        var radio = RiveRadio.Instance;
        if (radio == null) { Debug.LogWarning($"{name}: No RiveRadio in scene."); return; }

        // Resolve tint property if needed
        ResolveTintProperty();

        radio.Register(this);
    }

    void OnDisable()
    {
        if (RiveRadio.Instance != null)
            RiveRadio.Instance.Unregister(this);
    }

    public void ForceReapply()
    {
        if (RiveRadio.Instance != null)
            RiveRadio.Instance.UpdateClientST(this);
    }

    private void ResolveTintProperty()
    {
        if (!useTint) return;
        if (string.IsNullOrEmpty(tintProperty))
        {
            // Try to autodetect: URP Lit uses _BaseColor, Built-in often _Color
            var mat = Renderer ? Renderer.sharedMaterial : null;
            if (mat && mat.HasProperty("_BaseColor")) tintProperty = "_BaseColor";
            else tintProperty = "_Color";
        }
        TintPropertyId = Shader.PropertyToID(tintProperty);
    }
}
