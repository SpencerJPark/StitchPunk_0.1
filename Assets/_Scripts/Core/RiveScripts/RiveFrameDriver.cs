using UnityEngine;

public class RiveFrameDriver : MonoBehaviour
{
    [SerializeField] private Rive.Components.RivePanel panel;
    [SerializeField] private MeshRenderer mr;
    private bool registered;

    void Awake()
    {
        if (!mr) mr = GetComponent<MeshRenderer>();            // quad's renderer
        if (!panel) panel = GetComponent<Rive.Components.RivePanel>();
    }

    void OnEnable()
    {
        // initialize in case Unity hasn't sent OnBecameVisible yet
        if (mr == null || mr.isVisible) Register();
    }

    void OnDisable() => Unregister();

    void OnBecameVisible()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return; // ignore Scene view when not playing
#endif
        Register();
    }

    void OnBecameInvisible()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        Unregister();
    }

    private void Register()
    {
        if (registered) return;
        //TickManager.Register(this);
        registered = true;

        // optional: force a refresh so the first visible frame isn't stale
        panel?.Tick(0f);
    }

    private void Unregister()
    {
        if (!registered) return;
        //TickManager.Unregister(this);
        registered = false;
    }

    public void Tick()
    {
        if (!panel) return;
        //panel.Tick(TickManager.StepSeconds);
    }
}
