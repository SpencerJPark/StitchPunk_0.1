#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

// One right-hand panel of the Terrain Editor window. The window swaps these and reads StatusText for its footer.
public abstract class TerrainToolPanel : VisualElement
{
    public abstract string DisplayName { get; }
    public virtual string StatusText => string.Empty;

    public event Action StatusChanged;

    public virtual void OnShown() { }
    public virtual void OnHidden() { }

    protected void RaiseStatusChanged()
    {
        StatusChanged?.Invoke();
    }
}
#endif
