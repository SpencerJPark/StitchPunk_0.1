using UnityEngine;
using Rive.Components;

public class RiveFrameDriver : MonoBehaviour, ITickable
{
    [SerializeField] private RivePanel panel;

    void OnEnable() => TickManager.Register(this);
    void OnDisable() => TickManager.Unregister(this);

    public void Tick()
    {
        if (panel == null) return;
        panel.Tick(TickManager.StepSeconds); // use global fixed step
    }
}
