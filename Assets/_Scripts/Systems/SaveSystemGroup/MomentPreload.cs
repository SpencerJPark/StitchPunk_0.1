using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Entities;

// The title preloads the newest save's moment layer into a staging world (SM-D10); the load path takes it instead of re-deserializing.
public static class MomentPreload
{
    private static string preloadedSaveId;
    private static World preloadedStagingWorld;

    public static string PreloadedSaveId => preloadedSaveId;

    public static void Offer(string saveId, World stagingWorld)
    {
        DisposePreloadedWorld();
        preloadedSaveId = saveId;
        preloadedStagingWorld = stagingWorld;
    }

    public static bool TryTake(string saveId, out World stagingWorld)
    {
        stagingWorld = null;
        if (preloadedStagingWorld == null || preloadedSaveId != saveId)
            return false;

        stagingWorld = preloadedStagingWorld;
        preloadedStagingWorld = null;
        preloadedSaveId = null;
        return true;
    }

    public static void Discard()
    {
        DisposePreloadedWorld();
    }

    private static void DisposePreloadedWorld()
    {
        if (preloadedStagingWorld != null && preloadedStagingWorld.IsCreated)
            preloadedStagingWorld.Dispose();
        preloadedStagingWorld = null;
        preloadedSaveId = null;
    }
}

public static class MomentFiles
{
    public static byte[] Decompress(byte[] gzipBytes)
    {
        using (MemoryStream compressedStream = new MemoryStream(gzipBytes))
        using (GZipStream gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        using (MemoryStream decompressedStream = new MemoryStream())
        {
            gzipStream.CopyTo(decompressedStream);
            return decompressedStream.ToArray();
        }
    }

    public static bool TryReadMomentBytes(string saveId, out byte[] rawSnapshotBytes)
    {
        rawSnapshotBytes = null;
        if (!SaveSlotStore.TryReadFile(saveId, SaveSlotStore.MomentFileName, out byte[] gzipBytes))
            return false;

        try
        {
            rawSnapshotBytes = Decompress(gzipBytes);
            return rawSnapshotBytes != null && rawSnapshotBytes.Length > 0;
        }
        catch (System.Exception)
        {
            rawSnapshotBytes = null;
            return false;
        }
    }

    // Saves whose moment layer no longer matches the current component layout get the "Older version: units reset" badge.
    public static HashSet<string> FindSavesWithStaleMomentLayer(IReadOnlyList<SaveHeaderV2> headers)
    {
        HashSet<string> staleSaveIds = new HashSet<string>();
        for (int headerIndex = 0; headerIndex < headers.Count; headerIndex++)
        {
            SaveHeaderV2 header = headers[headerIndex];
            if (!header.hasMomentLayer)
                continue;

            if (!TryReadMomentBytes(header.saveId, out byte[] rawSnapshotBytes)
                || !MomentSnapshot.LayoutMatches(rawSnapshotBytes, out string mismatchReason))
                staleSaveIds.Add(header.saveId);
        }
        return staleSaveIds;
    }
}
