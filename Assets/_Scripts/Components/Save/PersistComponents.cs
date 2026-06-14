using Unity.Entities;

/// <summary>
/// Zero-member marker interface. Adding <c>: IComponentData, IPersist</c> to a struct is the
/// entire opt-in for the generic save system — it will be byte-snapshotted and restored
/// automatically, with no per-field copy code.
///
/// v1 limits (see Save_System.md): only unmanaged blittable components round-trip. A struct
/// that contains an <c>Entity</c> or <c>BlobAssetReference&lt;T&gt;</c> field is excluded from the
/// saveable set by <c>PersistRegistry</c> (those need the identity-remap / library re-resolve
/// passes that arrive in later phases). Buffers are not handled in v1.
///
/// The interface lives in the lightweight Components assembly (no Unity.Transforms dependency);
/// the registry that consumes it lives in the Systems assembly — see
/// <c>Systems/SaveSystemGroup/PersistRegistry.cs</c>.
/// </summary>
public interface IPersist { }

/// <summary>
/// Stable cross-session identity for a saved entity. Carried as a top-level string field on the
/// save record (not via the generic component path). Added to a minion at save time (generated
/// if absent) and re-applied to the respawned minion on load — the key the future Entity-remap
/// pass will use to relink references.
/// </summary>
public struct PersistId : IComponentData
{
    public Hash128 value;
}

/// <summary>
/// Transient carrier placed on a freshly-instantiated minion during load. Read by
/// <c>MinionRestoreApplySystem</c> the frame after spawn-init settles, which patches the saved
/// components (via the save record keyed by <c>recordId</c>) and then removes this component.
/// </summary>
public struct RestorePending : IComponentData
{
    public int recordId;
}
