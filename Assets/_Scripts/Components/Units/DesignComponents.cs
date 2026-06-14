using Unity.Collections;
using Unity.Entities;

// Unit Design System — per-instance visual identity (texture-array image indices per body part).
// All components live on the ROOT body entity; child quads are resolved at runtime through the
// AnimatorTarget buffer (rebuilt from BaseParent by AnimatorTargetInitSystem). See
// _Vault/Tasks/Claude/Plans/UnitDesign_System.md.

// Config — baked by DesignAuthoring. Mirrors the Ragdoll2DJointRef/Zone flat-buffer shape:
// each part holds a [rangeStart, rangeCount) window into the DesignRange buffer.
[InternalBufferCapacity(16)]
public struct DesignPart : IBufferElementData
{
    public AnimationTarget target;  // which body part this entry customizes
    public int rangeStart;          // window start into the DesignRange buffer
    public int rangeCount;          // number of ranges for this part
}

// Config — flat list of valid [min,max] index ranges, windowed by DesignPart.
[InternalBufferCapacity(32)]
public struct DesignRange : IBufferElementData
{
    public int min;                 // inclusive valid texture-array index
    public int max;                 // inclusive
}

// Blittable result entry. Unmanaged (two ints) so it rides inside a FixedList.
public struct DesignSlot
{
    public int target;              // (int)AnimationTarget
    public int imageIndex;          // chosen slice
}

// Result + persistence. A value-type IPersist IComponentData with no Entity/Blob fields is
// auto-discovered by PersistRegistry and round-tripped by the generic save pipeline with no
// per-field code (~64 slot cap >> ~36 AnimationTarget parts).
public struct PersistedDesign : IComponentData, IPersist
{
    public FixedList512Bytes<DesignSlot> slots;
}

// Runtime re-skin request — a caller fills `changes` with explicit (target, imageIndex) entries
// and enables the component; DesignChangeSystem upserts them into PersistedDesign, applies them to
// the child quads, then disables it. NOT IPersist — the request itself is never saved.
public struct ChangeDesignRequest : IComponentData, IEnableableComponent
{
    public FixedList512Bytes<DesignSlot> changes;
}
