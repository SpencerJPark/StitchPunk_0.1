using System.Diagnostics;
using Unity.Entities;

// Runs every SaveSystemGroup frame: the commit must land on the main thread between sim updates.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(PersistentLoadSystem))]
public partial class MomentSnapshotCommitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (!PendingMomentCommit.HasPending)
        {
            return;
        }

        World pendingWorld = PendingMomentCommit.StagingWorld;
        double savedElapsedTime = PendingMomentCommit.SavedElapsedTime;
        PendingMomentCommit.StagingWorld = null;
        PendingMomentCommit.SavedElapsedTime = 0.0;

        Stopwatch stopwatch = Stopwatch.StartNew();
        MomentSnapshot.Commit(EntityManager, pendingWorld, savedElapsedTime);
        stopwatch.Stop();
        UnityEngine.Debug.Log($"[MomentSnapshot] Committed moment in {stopwatch.ElapsedMilliseconds} ms");
    }

    protected override void OnDestroy()
    {
        PendingMomentCommit.Discard();
    }
}
