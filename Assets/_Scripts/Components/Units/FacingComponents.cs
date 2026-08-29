using DotsAnimationToolkit;
using Unity.Entities;

// Derived state, same as design apply — no request component. Written only by UnitFacingSystem
// (movement/aim quantized through FacingResolver); read by UnitAnimationAssignmentSystem's
// directional clip pick and by UnitFacingSystem's own PartFacing push. See DirectionFacing_System.md.
public struct UnitFacing : IComponentData
{
    public Direction current;
}
