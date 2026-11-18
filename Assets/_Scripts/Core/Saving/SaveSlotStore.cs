using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SaveSlotStore
{
    public const string HeaderFileName = "header.json";
    public const string ProgressFileName = "progress.json";
    public const string MomentFileName = "moment.bin.gz";
    public const string ThumbnailFileName = "thumb.png";
    public const int AutosaveRingSize = 5;

    const string BackupSuffix = ".bak";
    const string TemporarySuffix = ".tmp";
    const string AutosavePrefix = "autosave_";
    const string QuicksaveId = "quicksave";

    static string rootDirectoryOverride;
    static readonly System.Random randomSource = new System.Random();

    public static string RootDirectory
    {
        get
        {
            if (!string.IsNullOrEmpty(rootDirectoryOverride))
            {
                return rootDirectoryOverride;
            }
            if (string.IsNullOrEmpty(cachedDefaultRootDirectory))
            {
                cachedDefaultRootDirectory = Path.Combine(Application.persistentDataPath, "saves");
            }
            return cachedDefaultRootDirectory;
        }
        set { rootDirectoryOverride = value; }
    }

    // The save write runs on a Task, and persistentDataPath throws off the main thread: resolve it before any save.
    static string cachedDefaultRootDirectory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CacheDefaultRootDirectoryOnMainThread()
    {
        cachedDefaultRootDirectory = Path.Combine(Application.persistentDataPath, "saves");
    }

    public static string GetSaveFolderPath(string saveId)
    {
        return Path.Combine(RootDirectory, saveId);
    }

    public static IReadOnlyList<SaveHeaderV2> ListHeadersNewestFirst()
    {
        List<SaveHeaderV2> headers = new List<SaveHeaderV2>();
        if (!Directory.Exists(RootDirectory))
        {
            return headers;
        }

        string[] folderPaths = Directory.GetDirectories(RootDirectory);
        for (int folderIndex = 0; folderIndex < folderPaths.Length; folderIndex++)
        {
            string saveId = Path.GetFileName(folderPaths[folderIndex]);
            if (TryReadHeader(saveId, out SaveHeaderV2 header))
            {
                headers.Add(header);
            }
        }

        headers.Sort((SaveHeaderV2 left, SaveHeaderV2 right) => right.timestampUnix.CompareTo(left.timestampUnix));
        return headers;
    }

    public static bool TryGetNewest(out SaveHeaderV2 header)
    {
        IReadOnlyList<SaveHeaderV2> headers = ListHeadersNewestFirst();
        if (headers.Count == 0)
        {
            header = null;
            return false;
        }
        header = headers[0];
        return true;
    }

    public static string CreateSaveFolder(SaveKind kind)
    {
        if (kind == SaveKind.Quicksave)
        {
            Directory.CreateDirectory(GetSaveFolderPath(QuicksaveId));
            return QuicksaveId;
        }

        string timestampText = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string randomHex = randomSource.Next(0, 0x10000).ToString("x4");
        string saveId = kind.ToString().ToLowerInvariant() + "_" + timestampText + "_" + randomHex;
        Directory.CreateDirectory(GetSaveFolderPath(saveId));

        if (kind == SaveKind.Autosave)
        {
            ApplyAutosaveRing();
        }
        return saveId;
    }

    public static void WriteFileAtomically(string saveId, string fileName, byte[] bytes)
    {
        string folderPath = GetSaveFolderPath(saveId);
        Directory.CreateDirectory(folderPath);
        string targetPath = Path.Combine(folderPath, fileName);
        string temporaryPath = targetPath + TemporarySuffix;

        File.WriteAllBytes(temporaryPath, bytes);
        if (File.Exists(targetPath))
        {
            File.Replace(temporaryPath, targetPath, targetPath + BackupSuffix);
        }
        else
        {
            File.Move(temporaryPath, targetPath);
        }
    }

    public static bool TryReadFile(string saveId, string fileName, out byte[] bytes)
    {
        string targetPath = Path.Combine(GetSaveFolderPath(saveId), fileName);
        if (TryReadBytes(targetPath, out bytes))
        {
            return true;
        }
        return TryReadBytes(targetPath + BackupSuffix, out bytes);
    }

    public static void WriteHeader(SaveHeaderV2 header)
    {
        string json = JsonUtility.ToJson(header);
        WriteFileAtomically(header.saveId, HeaderFileName, System.Text.Encoding.UTF8.GetBytes(json));
    }

    public static bool TryReadHeader(string saveId, out SaveHeaderV2 header)
    {
        string headerPath = Path.Combine(GetSaveFolderPath(saveId), HeaderFileName);
        if (TryParseHeaderFile(headerPath, out header))
        {
            return true;
        }
        return TryParseHeaderFile(headerPath + BackupSuffix, out header);
    }

    public static void DeleteSave(string saveId)
    {
        string folderPath = GetSaveFolderPath(saveId);
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
        }
    }

    static bool TryReadBytes(string path, out byte[] bytes)
    {
        bytes = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            bytes = File.ReadAllBytes(path);
            return true;
        }
        catch (Exception)
        {
            bytes = null;
            return false;
        }
    }

    static bool TryParseHeaderFile(string path, out SaveHeaderV2 header)
    {
        header = null;
        if (!TryReadBytes(path, out byte[] bytes))
        {
            return false;
        }
        try
        {
            SaveHeaderV2 parsed = JsonUtility.FromJson<SaveHeaderV2>(System.Text.Encoding.UTF8.GetString(bytes));
            if (parsed == null || string.IsNullOrEmpty(parsed.saveId))
            {
                return false;
            }
            header = parsed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static void ApplyAutosaveRing()
    {
        string[] folderPaths = Directory.GetDirectories(RootDirectory);
        List<KeyValuePair<string, long>> autosaveEntries = new List<KeyValuePair<string, long>>();
        for (int folderIndex = 0; folderIndex < folderPaths.Length; folderIndex++)
        {
            string saveId = Path.GetFileName(folderPaths[folderIndex]);
            if (!saveId.StartsWith(AutosavePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // A folder without a readable header is brand new, so it sorts as newest via its creation time.
            long sortKey;
            if (TryReadHeader(saveId, out SaveHeaderV2 header))
            {
                sortKey = header.timestampUnix;
            }
            else
            {
                sortKey = new DateTimeOffset(Directory.GetCreationTimeUtc(folderPaths[folderIndex])).ToUnixTimeSeconds();
            }
            autosaveEntries.Add(new KeyValuePair<string, long>(saveId, sortKey));
        }

        autosaveEntries.Sort((KeyValuePair<string, long> left, KeyValuePair<string, long> right) =>
        {
            int keyComparison = left.Value.CompareTo(right.Value);
            return keyComparison != 0 ? keyComparison : string.CompareOrdinal(left.Key, right.Key);
        });

        int excessCount = autosaveEntries.Count - AutosaveRingSize;
        for (int entryIndex = 0; entryIndex < excessCount; entryIndex++)
        {
            try
            {
                DeleteSave(autosaveEntries[entryIndex].Key);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("SaveSlotStore could not delete old autosave: " + exception.Message);
            }
        }
    }
}
