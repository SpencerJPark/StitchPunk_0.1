using System.Collections.Generic;

/// <summary>
/// Managed hand-off between <c>PersistentLoadSystem</c> (instantiates restored minions in
/// SaveSystemGroup) and <c>MinionRestoreApplySystem</c> (patches them a frame later in
/// SpawnInitSystemGroup). The save layer is managed / main-thread, so a static side-table keyed
/// by an incrementing id is the simplest way to carry the <see cref="EntityRecord"/> without
/// stuffing managed data into an ECS component.
/// </summary>
public static class MinionRestoreQueue
{
    private static readonly Dictionary<int, EntityRecord> pending = new Dictionary<int, EntityRecord>();
    private static int nextId = 1;

    /// <summary>Stores a record and returns the id to stamp on the instantiated entity.</summary>
    public static int Enqueue(EntityRecord record)
    {
        int id = nextId++;
        pending[id] = record;
        return id;
    }

    /// <summary>Removes and returns the record for an id, if still present.</summary>
    public static bool TryConsume(int recordId, out EntityRecord record)
    {
        if (pending.TryGetValue(recordId, out record))
        {
            pending.Remove(recordId);
            return true;
        }
        return false;
    }
}
