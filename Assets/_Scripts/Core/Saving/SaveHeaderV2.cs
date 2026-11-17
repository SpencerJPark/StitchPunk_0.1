using System;

public enum SaveKind
{
    Autosave,
    Quicksave,
    Manual
}

[Serializable]
public class SaveHeaderV2
{
    public int formatVersion = 2;
    public string saveId;
    public SaveKind kind;
    public long timestampUnix;
    public double totalPlaySeconds;
    public string locationLabel = "";
    public string buildVersion;
    public bool hasMomentLayer;
    public string momentLayoutHash;
    public double savedElapsedTime;    // World.Time.ElapsedTime at capture; the moment commit restores it
}
