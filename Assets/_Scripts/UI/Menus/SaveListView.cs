using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public enum SaveListMode
{
    Load,
    Save
}

// Shared save list for title Load, pause Save and pause Load. The host owns the delete confirm screen.
public sealed class SaveListView
{
    private const float ThumbnailWidth = 96f;
    private const float ThumbnailHeight = 54f;

    private readonly VisualElement container;
    private readonly Dictionary<string, Texture2D> thumbnailTexturesBySaveId = new Dictionary<string, Texture2D>();

    public event Action<string> SaveChosen;
    public event Action<string> DeleteRequested;
    public event Action NewSaveRequested;

    public SaveListView(VisualElement container)
    {
        this.container = container;
    }

    public void Fill(IReadOnlyList<SaveHeaderV2> headers, HashSet<string> savesWithStaleMomentLayer, SaveListMode mode)
    {
        if (container == null)
            return;

        container.Clear();
        DestroyThumbnailTextures();

        if (mode == SaveListMode.Save)
            container.Add(BuildNewSaveRow());

        if (headers == null)
            return;

        for (int index = 0; index < headers.Count; index++)
        {
            SaveHeaderV2 header = headers[index];
            bool unitsReset = savesWithStaleMomentLayer != null && savesWithStaleMomentLayer.Contains(header.saveId);
            container.Add(BuildSaveRow(header, unitsReset, mode));
        }
    }

    public void Release()
    {
        DestroyThumbnailTextures();
    }

    private VisualElement BuildNewSaveRow()
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("save-row");

        Button newSaveButton = new Button(() => NewSaveRequested?.Invoke());
        newSaveButton.AddToClassList("save-row-main");

        Label newSaveLabel = new Label("New save");
        newSaveLabel.AddToClassList("save-row-location");
        newSaveButton.Add(newSaveLabel);

        row.Add(newSaveButton);
        return row;
    }

    private VisualElement BuildSaveRow(SaveHeaderV2 header, bool unitsReset, SaveListMode mode)
    {
        string saveId = header.saveId;

        VisualElement row = new VisualElement();
        row.AddToClassList("save-row");

        Button chooseButton = new Button(() => SaveChosen?.Invoke(saveId));
        chooseButton.AddToClassList("save-row-main");
        // Autosave ring and quicksave slots are owned by their triggers; only manual saves can be overwritten.
        if (mode == SaveListMode.Save && header.kind != SaveKind.Manual)
            chooseButton.SetEnabled(false);

        VisualElement thumbnail = new VisualElement();
        thumbnail.AddToClassList("save-row-thumbnail");
        thumbnail.style.width = ThumbnailWidth;
        thumbnail.style.height = ThumbnailHeight;
        thumbnail.style.flexShrink = 0f;
        ApplyThumbnail(thumbnail, saveId);
        chooseButton.Add(thumbnail);

        Label kindLabel = new Label(FormatKind(header.kind));
        kindLabel.AddToClassList("save-row-kind");
        chooseButton.Add(kindLabel);

        Label locationLabel = new Label(string.IsNullOrEmpty(header.locationLabel) ? "Unknown location" : header.locationLabel);
        locationLabel.AddToClassList("save-row-location");
        chooseButton.Add(locationLabel);

        Label playTimeLabel = new Label(FormatPlayTime(header.totalPlaySeconds));
        playTimeLabel.AddToClassList("save-row-playtime");
        chooseButton.Add(playTimeLabel);

        Label dateLabel = new Label(FormatLocalDateTime(header.timestampUnix));
        dateLabel.AddToClassList("save-row-date");
        chooseButton.Add(dateLabel);

        if (unitsReset)
        {
            Label badgeLabel = new Label("Older version: units reset");
            badgeLabel.AddToClassList("save-row-badge");
            chooseButton.Add(badgeLabel);
        }

        Button deleteButton = new Button(() => DeleteRequested?.Invoke(saveId));
        deleteButton.text = "Delete";
        deleteButton.AddToClassList("save-row-delete");

        row.Add(chooseButton);
        row.Add(deleteButton);
        return row;
    }

    private void ApplyThumbnail(VisualElement thumbnail, string saveId)
    {
        if (!SaveSlotStore.TryReadFile(saveId, "thumb.png", out byte[] bytes) || bytes == null || bytes.Length == 0)
            return;

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(texture);
            return;
        }

        thumbnailTexturesBySaveId[saveId] = texture;
        thumbnail.style.backgroundImage = new StyleBackground(texture);
    }

    private void DestroyThumbnailTextures()
    {
        foreach (KeyValuePair<string, Texture2D> pair in thumbnailTexturesBySaveId)
        {
            if (pair.Value != null)
                UnityEngine.Object.Destroy(pair.Value);
        }
        thumbnailTexturesBySaveId.Clear();
    }

    private static string FormatPlayTime(double totalPlaySeconds)
    {
        int totalMinutes = (int)(Math.Max(0.0, totalPlaySeconds) / 60.0);
        return (totalMinutes / 60) + ":" + (totalMinutes % 60).ToString("00");
    }

    private static string FormatLocalDateTime(long timestampUnix)
    {
        return DateTimeOffset.FromUnixTimeSeconds(timestampUnix).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string FormatKind(SaveKind kind)
    {
        switch (kind)
        {
            case SaveKind.Autosave: return "Auto";
            case SaveKind.Quicksave: return "Quick";
            default: return "Manual";
        }
    }
}
