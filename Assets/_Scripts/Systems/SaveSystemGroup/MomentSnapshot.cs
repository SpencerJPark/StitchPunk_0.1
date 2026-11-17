using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Core;
using Unity.Entities.Serialization;
using UnityEngine;

public static class MomentSnapshot
{
    public const int SnapshotFormatVersion = 1;

    [Serializable]
    private class MomentTypeEntry
    {
        public string typeName;
        public string stableTypeHash;
    }

    [Serializable]
    private class MomentObjectEntry
    {
        public string assemblyQualifiedTypeName;
        public string objectName;
    }

    [Serializable]
    private class MomentManifest
    {
        public int formatVersion;
        public MomentTypeEntry[] types;
        public MomentObjectEntry[] objects;
    }

    public static unsafe byte[] Capture(EntityManager liveEntityManager, out string layoutHash, out int referencedObjectCount)
    {
        EntityQuery momentQuery = liveEntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { ComponentType.ReadOnly<MomentPersist>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        NativeArray<Entity> entities = default;
        World stagingWorld = null;
        MemoryBinaryWriter writer = null;
        try
        {
            entities = CollectMomentEntitiesWithLinkedGroups(liveEntityManager, momentQuery);

            stagingWorld = new World("MomentCaptureStaging", WorldFlags.Staging);
            EntityManager stagingEntityManager = stagingWorld.EntityManager;
            stagingEntityManager.CopyEntitiesFrom(liveEntityManager, entities);

            // Scene membership and blob owners are re-created on load; SerializeWorld throws on BlobAssetOwner's pointer field.
            EntityQuery everythingQuery = stagingEntityManager.UniversalQuery;
            stagingEntityManager.RemoveComponent(everythingQuery, ComponentType.ReadWrite<SceneTag>());
            stagingEntityManager.RemoveComponent(everythingQuery, ComponentType.ReadWrite<SceneSection>());
            Type blobAssetOwnerType = Type.GetType("Unity.Entities.BlobAssetOwner, Unity.Entities");
            if (blobAssetOwnerType != null)
            {
                stagingEntityManager.RemoveComponent(everythingQuery, ComponentType.ReadWrite(TypeManager.GetTypeIndex(blobAssetOwnerType)));
            }

            List<MomentTypeEntry> typeEntries = BuildSortedTypeEntries(stagingEntityManager);
            layoutHash = ComputeLayoutHash(typeEntries);

            writer = new MemoryBinaryWriter();
            SerializeUtility.SerializeWorld(stagingEntityManager, writer, out object[] referencedObjects);
            byte[] worldBytes = new byte[writer.Length];
            if (writer.Length > 0)
            {
                fixed (byte* destination = worldBytes)
                {
                    Buffer.MemoryCopy(writer.Data, destination, writer.Length, writer.Length);
                }
            }

            MomentObjectEntry[] objectEntries = new MomentObjectEntry[referencedObjects.Length];
            for (int objectIndex = 0; objectIndex < referencedObjects.Length; objectIndex++)
            {
                UnityEngine.Object referencedObject = referencedObjects[objectIndex] as UnityEngine.Object;
                if (referencedObject != null)
                {
                    objectEntries[objectIndex] = new MomentObjectEntry
                    {
                        assemblyQualifiedTypeName = referencedObject.GetType().AssemblyQualifiedName,
                        objectName = referencedObject.name
                    };
                }
                else
                {
                    string describedType = referencedObjects[objectIndex] == null ? "null" : referencedObjects[objectIndex].GetType().FullName;
                    Debug.LogError("[MomentSnapshot] Non-UnityObject reference in snapshot: " + describedType);
                    objectEntries[objectIndex] = new MomentObjectEntry { assemblyQualifiedTypeName = "", objectName = "" };
                }
            }
            referencedObjectCount = referencedObjects.Length;

            MomentManifest manifest = new MomentManifest
            {
                formatVersion = SnapshotFormatVersion,
                types = typeEntries.ToArray(),
                objects = objectEntries
            };
            byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest));

            byte[] combinedBytes = new byte[4 + manifestBytes.Length + worldBytes.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(manifestBytes.Length), 0, combinedBytes, 0, 4);
            Buffer.BlockCopy(manifestBytes, 0, combinedBytes, 4, manifestBytes.Length);
            Buffer.BlockCopy(worldBytes, 0, combinedBytes, 4 + manifestBytes.Length, worldBytes.Length);
            return combinedBytes;
        }
        finally
        {
            if (writer != null)
            {
                writer.Dispose();
            }
            if (entities.IsCreated)
            {
                entities.Dispose();
            }
            if (stagingWorld != null && stagingWorld.IsCreated)
            {
                stagingWorld.Dispose();
            }
            momentQuery.Dispose();
        }
    }

    public static bool LayoutMatches(byte[] snapshotBytes, out string mismatchReason)
    {
        mismatchReason = "";
        try
        {
            MomentManifest manifest = ReadManifest(snapshotBytes, out int worldByteOffset);
            if (manifest.formatVersion != SnapshotFormatVersion)
            {
                mismatchReason = "Snapshot format version " + manifest.formatVersion + " is not " + SnapshotFormatVersion;
                return false;
            }
            for (int typeEntryIndex = 0; typeEntryIndex < manifest.types.Length; typeEntryIndex++)
            {
                MomentTypeEntry typeEntry = manifest.types[typeEntryIndex];
                TypeIndex resolved = TypeManager.GetTypeIndexFromStableTypeHash(ulong.Parse(typeEntry.stableTypeHash));
                if (resolved == TypeIndex.Null)
                {
                    mismatchReason = "Component layout changed: " + typeEntry.typeName;
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            mismatchReason = "Snapshot manifest unreadable: " + exception.Message;
            return false;
        }
    }

    public static unsafe World DeserializeIntoStagingWorld(byte[] snapshotBytes)
    {
        if (!LayoutMatches(snapshotBytes, out string mismatchReason))
        {
            Debug.LogWarning("[MomentSnapshot] " + mismatchReason + " - falling back to the progress layer.");
            return null;
        }

        MomentManifest manifest = ReadManifest(snapshotBytes, out int worldByteOffset);
        UnityEngine.Object[] resolvedObjects = new UnityEngine.Object[manifest.objects.Length];
        Dictionary<string, UnityEngine.Object[]> loadedObjectsByTypeName = new Dictionary<string, UnityEngine.Object[]>();
        int unresolvedCount = 0;
        string firstUnresolved = "";
        for (int objectIndex = 0; objectIndex < manifest.objects.Length; objectIndex++)
        {
            MomentObjectEntry objectEntry = manifest.objects[objectIndex];
            UnityEngine.Object match = null;
            Type objectType = string.IsNullOrEmpty(objectEntry.assemblyQualifiedTypeName) ? null : Type.GetType(objectEntry.assemblyQualifiedTypeName);
            if (objectType != null)
            {
                if (!loadedObjectsByTypeName.TryGetValue(objectEntry.assemblyQualifiedTypeName, out UnityEngine.Object[] candidates))
                {
                    candidates = Resources.FindObjectsOfTypeAll(objectType);
                    loadedObjectsByTypeName[objectEntry.assemblyQualifiedTypeName] = candidates;
                }
                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    if (candidates[candidateIndex].name == objectEntry.objectName)
                    {
                        match = candidates[candidateIndex];
                        break;
                    }
                }
            }
            if (match == null)
            {
                if (unresolvedCount == 0)
                {
                    firstUnresolved = objectEntry.assemblyQualifiedTypeName + " '" + objectEntry.objectName + "'";
                }
                unresolvedCount++;
            }
            resolvedObjects[objectIndex] = match;
        }
        if (unresolvedCount > 0)
        {
            Debug.LogWarning("[MomentSnapshot] Cannot resolve " + unresolvedCount + " referenced objects (first: " + firstUnresolved + ") - falling back to the progress layer.");
            return null;
        }

        World stagingWorld = new World("MomentLoadStaging", WorldFlags.Staging);
        try
        {
            ExclusiveEntityTransaction transaction = stagingWorld.EntityManager.BeginExclusiveEntityTransaction();
            try
            {
                fixed (byte* bytePointer = snapshotBytes)
                {
                    MemoryBinaryReader reader = new MemoryBinaryReader(bytePointer + worldByteOffset, snapshotBytes.Length - worldByteOffset);
                    try
                    {
                        SerializeUtility.DeserializeWorld(transaction, reader, resolvedObjects);
                    }
                    finally
                    {
                        reader.Dispose();
                    }
                }
            }
            finally
            {
                stagingWorld.EntityManager.EndExclusiveEntityTransaction();
            }
            return stagingWorld;
        }
        catch (Exception exception)
        {
            Debug.LogError("[MomentSnapshot] DeserializeWorld failed: " + exception);
            if (stagingWorld.IsCreated)
            {
                stagingWorld.Dispose();
            }
            return null;
        }
    }

    public static void Commit(EntityManager liveEntityManager, World stagingWorld, double savedElapsedTime)
    {
        liveEntityManager.CompleteAllTrackedJobs();
        try
        {
            CommitIntoLiveWorld(liveEntityManager, stagingWorld, savedElapsedTime);
        }
        finally
        {
            if (stagingWorld.IsCreated)
            {
                stagingWorld.Dispose();
            }
        }
    }

    private static void CommitIntoLiveWorld(EntityManager liveEntityManager, World stagingWorld, double savedElapsedTime)
    {
        EntityQuery momentQuery = liveEntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new ComponentType[] { ComponentType.ReadOnly<MomentPersist>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        NativeArray<Entity> liveMomentEntities = momentQuery.ToEntityArray(Allocator.Temp);
        liveEntityManager.DestroyEntity(liveMomentEntities);
        liveMomentEntities.Dispose();
        momentQuery.Dispose();

        liveEntityManager.MoveEntitiesFrom(stagingWorld.EntityManager);

        World liveWorld = liveEntityManager.World;
        liveWorld.SetTime(new TimeData(savedElapsedTime, liveWorld.Time.DeltaTime));

        // The fixed-step catch-up clock does not follow SetTime; left alone, physics stalls or fast-forwards.
        FixedStepSimulationSystemGroup fixedGroup = liveWorld.GetExistingSystemManaged<FixedStepSimulationSystemGroup>();
        if (fixedGroup != null && fixedGroup.RateManager != null)
        {
            FieldInfo lastFixedUpdateTimeField = fixedGroup.RateManager.GetType().GetField("m_LastFixedUpdateTime", BindingFlags.Instance | BindingFlags.NonPublic);
            if (lastFixedUpdateTimeField != null)
            {
                lastFixedUpdateTimeField.SetValue(fixedGroup.RateManager, savedElapsedTime);
            }
            else
            {
                Debug.LogWarning("[MomentSnapshot] FixedRateCatchUpManager.m_LastFixedUpdateTime not found; fixed step clock not realigned.");
            }
        }
    }

    private static NativeArray<Entity> CollectMomentEntitiesWithLinkedGroups(EntityManager liveEntityManager, EntityQuery momentQuery)
    {
        NativeArray<Entity> roots = momentQuery.ToEntityArray(Allocator.Temp);
        NativeHashSet<Entity> collected = new NativeHashSet<Entity>(roots.Length * 4 + 4, Allocator.Temp);
        NativeList<Entity> ordered = new NativeList<Entity>(roots.Length * 4 + 4, Allocator.Temp);
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Entity root = roots[rootIndex];
            if (collected.Add(root))
            {
                ordered.Add(root);
            }
            if (!liveEntityManager.HasBuffer<LinkedEntityGroup>(root))
            {
                continue;
            }
            DynamicBuffer<LinkedEntityGroup> group = liveEntityManager.GetBuffer<LinkedEntityGroup>(root, true);
            for (int memberIndex = 0; memberIndex < group.Length; memberIndex++)
            {
                Entity member = group[memberIndex].Value;
                if (collected.Add(member))
                {
                    ordered.Add(member);
                }
            }
        }
        NativeArray<Entity> result = new NativeArray<Entity>(ordered.AsArray(), Allocator.Persistent);
        roots.Dispose();
        collected.Dispose();
        ordered.Dispose();
        return result;
    }

    private static List<MomentTypeEntry> BuildSortedTypeEntries(EntityManager stagingEntityManager)
    {
        HashSet<int> seenTypeIndices = new HashSet<int>();
        List<MomentTypeEntry> typeEntries = new List<MomentTypeEntry>();
        NativeArray<ArchetypeChunk> chunks = stagingEntityManager.GetAllChunks(Allocator.Temp);
        for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            NativeArray<ComponentType> componentTypes = chunks[chunkIndex].Archetype.GetComponentTypes(Allocator.Temp);
            for (int componentTypeIndex = 0; componentTypeIndex < componentTypes.Length; componentTypeIndex++)
            {
                TypeIndex typeIndex = componentTypes[componentTypeIndex].TypeIndex;
                if (!seenTypeIndices.Add(typeIndex.Value))
                {
                    continue;
                }
                ref readonly TypeManager.TypeInfo typeInfo = ref TypeManager.GetTypeInfo(typeIndex);
                Type managedType = TypeManager.GetType(typeIndex);
                typeEntries.Add(new MomentTypeEntry
                {
                    typeName = managedType != null ? managedType.FullName : typeInfo.DebugTypeName.ToString(),
                    stableTypeHash = typeInfo.StableTypeHash.ToString()
                });
            }
            componentTypes.Dispose();
        }
        chunks.Dispose();
        typeEntries.Sort((leftEntry, rightEntry) => string.CompareOrdinal(leftEntry.typeName, rightEntry.typeName));
        return typeEntries;
    }

    private static string ComputeLayoutHash(List<MomentTypeEntry> sortedTypeEntries)
    {
        ulong hash = 14695981039346656037UL;
        for (int entryIndex = 0; entryIndex < sortedTypeEntries.Count; entryIndex++)
        {
            string line = sortedTypeEntries[entryIndex].typeName + ":" + sortedTypeEntries[entryIndex].stableTypeHash + ";";
            byte[] lineBytes = Encoding.UTF8.GetBytes(line);
            for (int byteIndex = 0; byteIndex < lineBytes.Length; byteIndex++)
            {
                hash ^= lineBytes[byteIndex];
                hash *= 1099511628211UL;
            }
        }
        return hash.ToString("x16");
    }

    private static MomentManifest ReadManifest(byte[] snapshotBytes, out int worldByteOffset)
    {
        int manifestByteLength = BitConverter.ToInt32(snapshotBytes, 0);
        string manifestJson = Encoding.UTF8.GetString(snapshotBytes, 4, manifestByteLength);
        worldByteOffset = 4 + manifestByteLength;
        MomentManifest manifest = JsonUtility.FromJson<MomentManifest>(manifestJson);
        if (manifest == null || manifest.types == null || manifest.objects == null)
        {
            throw new InvalidOperationException("manifest missing fields");
        }
        return manifest;
    }
}

public static class PendingMomentCommit
{
    public static World StagingWorld;
    public static double SavedElapsedTime;
    public static bool HasPending => StagingWorld != null && StagingWorld.IsCreated;

    public static void Set(World stagingWorld, double savedElapsedTime)
    {
        Discard();
        StagingWorld = stagingWorld;
        SavedElapsedTime = savedElapsedTime;
    }

    public static void Discard()
    {
        if (StagingWorld != null && StagingWorld.IsCreated)
        {
            StagingWorld.Dispose();
        }
        StagingWorld = null;
        SavedElapsedTime = 0.0;
    }
}
