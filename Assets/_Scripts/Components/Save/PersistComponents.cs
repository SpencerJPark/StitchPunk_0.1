using Unity.Entities;

// Opt-in marker for the generic save system. Only unmanaged blittable components round-trip;
// structs holding an Entity or BlobAssetReference are excluded by PersistRegistry. Buffers are not saved.
public interface IPersist { }

// Stable cross-session identity, stored as a top-level field on the save record. Generated at save time if absent.
public struct PersistId : IComponentData
{
    public Hash128 value;
}

// Placed on a minion during load; MinionRestoreApplySystem patches the saved components by recordId, then removes it.
public struct RestorePending : IComponentData
{
    public int recordId;
}
