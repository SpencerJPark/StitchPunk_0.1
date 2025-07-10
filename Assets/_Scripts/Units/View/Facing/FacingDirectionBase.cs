using UnityEngine;
using Rive;
using Rive.Components;

public abstract class FacingDirectionBase : MonoBehaviour
{
    [SerializeField] protected RiveAnimator animator;
    [SerializeField] protected Camera mainCamera; // Switch to injection
    public abstract Direction CurrentDirection { get; }
    protected string defaultDirection = "SouthEast";

    protected ViewModelInstance viewModel;
    protected ViewModelInstanceEnumProperty facingDirectionProperty;

    protected virtual string FacingPropertyName => "Direction";

    protected virtual void OnEnable()
    {
        if (animator == null)
            animator = GetComponent<RiveAnimator>();

        if (mainCamera == null)
            mainCamera = Camera.main;

        var widget = GetComponent<RiveWidget>();
        if (widget != null)
            widget.OnWidgetStatusChanged += OnRiveReady;
    }

    protected virtual void OnDisable()
    {
        var widget = GetComponent<RiveWidget>();
        if (widget != null)
            widget.OnWidgetStatusChanged -= OnRiveReady;
    }

    private void OnRiveReady()
    {
        var widget = GetComponent<RiveWidget>();
        if (widget == null || widget.Status != WidgetStatus.Loaded)
            return;

        viewModel = widget.StateMachine.ViewModelInstance;
        facingDirectionProperty = viewModel.GetEnumProperty(FacingPropertyName);

        SetDefaultFacing();
    }

    private void SetDefaultFacing()
    {
        if (facingDirectionProperty != null)
            facingDirectionProperty.Value = defaultDirection;
    }

    public abstract void UpdateFacing(Vector3 moveDirection);
}
