using UnityEngine;

/// <summary>
/// Resolves save file paths by slot index.
/// Slot 0 is reserved for auto-save. Slots 1–3 are manual save slots.
/// </summary>
public static class SavePaths
{
    private const string FilePrefix    = "save_slot_";
    private const string FileExtension = ".json";

    public static string GetSlotPath(int slot)
    {
        return System.IO.Path.Combine(
            Application.persistentDataPath,
            $"{FilePrefix}{slot}{FileExtension}"
        );
    }
}
