using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

public class SaveSlotStoreTests
{
    private string temporaryRootDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryRootDirectory = Path.Combine(Path.GetTempPath(), "StitchPunkSaveTests_" + Guid.NewGuid().ToString("N"));
        SaveSlotStore.RootDirectory = temporaryRootDirectory;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryRootDirectory))
        {
            Directory.Delete(temporaryRootDirectory, true);
        }
        SaveSlotStore.RootDirectory = null;
    }

    private static SaveHeaderV2 BuildHeader(string saveId, SaveKind kind, long timestampUnix)
    {
        SaveHeaderV2 header = new SaveHeaderV2();
        header.formatVersion = 2;
        header.saveId = saveId;
        header.kind = kind;
        header.timestampUnix = timestampUnix;
        return header;
    }

    [Test]
    public void ASixthAutosaveDeletesTheOldest()
    {
        string firstSaveId = null;
        for (int autosaveIndex = 0; autosaveIndex < SaveSlotStore.AutosaveRingSize + 1; autosaveIndex++)
        {
            string saveId = SaveSlotStore.CreateSaveFolder(SaveKind.Autosave);
            if (autosaveIndex == 0)
            {
                firstSaveId = saveId;
            }
            SaveSlotStore.WriteHeader(BuildHeader(saveId, SaveKind.Autosave, (autosaveIndex + 1) * 1000L));
        }

        IReadOnlyList<SaveHeaderV2> headers = SaveSlotStore.ListHeadersNewestFirst();
        int autosaveCount = 0;
        for (int headerIndex = 0; headerIndex < headers.Count; headerIndex++)
        {
            if (headers[headerIndex].kind == SaveKind.Autosave)
            {
                autosaveCount++;
            }
        }

        Assert.AreEqual(SaveSlotStore.AutosaveRingSize, autosaveCount);
        Assert.IsFalse(Directory.Exists(SaveSlotStore.GetSaveFolderPath(firstSaveId)));
    }

    [Test]
    public void ACorruptHeaderFallsBackToTheBackup()
    {
        string saveId = SaveSlotStore.CreateSaveFolder(SaveKind.Manual);
        SaveSlotStore.WriteHeader(BuildHeader(saveId, SaveKind.Manual, 1000L));
        SaveSlotStore.WriteHeader(BuildHeader(saveId, SaveKind.Manual, 2000L));

        string headerPath = Path.Combine(SaveSlotStore.GetSaveFolderPath(saveId), SaveSlotStore.HeaderFileName);
        File.WriteAllText(headerPath, "{not json");

        IReadOnlyList<SaveHeaderV2> headers = SaveSlotStore.ListHeadersNewestFirst();
        bool foundSave = false;
        for (int headerIndex = 0; headerIndex < headers.Count; headerIndex++)
        {
            if (headers[headerIndex].saveId == saveId)
            {
                foundSave = true;
            }
        }

        Assert.IsTrue(foundSave);
    }

    [Test]
    public void AnInterruptedWriteLeavesTheOldFileReadable()
    {
        string saveId = SaveSlotStore.CreateSaveFolder(SaveKind.Manual);
        byte[] originalBytes = new byte[] { 1, 2, 3 };
        byte[] interruptedBytes = new byte[] { 9, 9, 9, 9 };
        SaveSlotStore.WriteFileAtomically(saveId, SaveSlotStore.ProgressFileName, originalBytes);

        string temporaryPath = Path.Combine(SaveSlotStore.GetSaveFolderPath(saveId), SaveSlotStore.ProgressFileName + ".tmp");
        File.WriteAllBytes(temporaryPath, interruptedBytes);

        byte[] readBytes;
        bool didRead = SaveSlotStore.TryReadFile(saveId, SaveSlotStore.ProgressFileName, out readBytes);

        Assert.IsTrue(didRead);
        CollectionAssert.AreEqual(originalBytes, readBytes);
    }
}
