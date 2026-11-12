using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.Transforms;

// Saved set = IComponentData structs marked IPersist plus ExternalSaveableTypes, built lazily once.
// Types holding an Entity or BlobAssetReference are filtered out (not round-trippable by raw bytes).
public static class PersistRegistry
{
    public struct SaveableType
    {
        public Type managedType;
        public ComponentType componentType;
        public bool isEnableable;
    }

    // Package components we cannot mark with IPersist.
    private static readonly Type[] ExternalSaveableTypes =
    {
        typeof(LocalTransform),
    };

    private static bool initialized;
    private static readonly List<SaveableType> saveableTypes = new List<SaveableType>();
    private static readonly Dictionary<string, Type> typeByFullName = new Dictionary<string, Type>();

    public static IReadOnlyList<SaveableType> SaveableTypes
    {
        get
        {
            EnsureInitialized();
            return saveableTypes;
        }
    }

    // Falls back to a full-name lookup so a save still loads after an assembly rename.
    public static bool TryResolveType(string typeName, out Type type)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(typeName))
        {
            type = null;
            return false;
        }

        type = Type.GetType(typeName);
        if (type != null)
            return true;

        int comma = typeName.IndexOf(',');
        string fullName = comma >= 0 ? typeName.Substring(0, comma).Trim() : typeName;
        return typeByFullName.TryGetValue(fullName, out type);
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;
        initialized = true;

        HashSet<Type> candidates = new HashSet<Type>();

        foreach (Type external in ExternalSaveableTypes)
            candidates.Add(external);

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException loadException)
            {
                types = loadException.Types;
            }

            foreach (Type type in types)
            {
                if (type == null || !type.IsValueType || type.IsInterface)
                    continue;
                if (!typeof(IPersist).IsAssignableFrom(type))
                    continue;
                if (!typeof(IComponentData).IsAssignableFrom(type))
                    continue; // v1: value components only (buffers deferred)
                candidates.Add(type);
            }
        }

        foreach (Type type in candidates)
        {
            if (ContainsUnsupportedField(type))
            {
                UnityEngine.Debug.LogWarning(
                    $"[PersistRegistry] '{type.FullName}' has an Entity/BlobAssetReference field and is not yet supported — skipping.");
                continue;
            }

            saveableTypes.Add(new SaveableType
            {
                managedType   = type,
                componentType = ComponentType.ReadWrite(type),
                isEnableable  = typeof(IEnableableComponent).IsAssignableFrom(type),
            });
            typeByFullName[type.FullName] = type;
        }
    }

    // Recursive: Entity, BlobAssetReference or any managed reference cannot survive a raw-byte round trip.
    private static bool ContainsUnsupportedField(Type type)
    {
        if (type == typeof(Entity))
            return true;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BlobAssetReference<>))
            return true;
        if (type.IsPrimitive || type.IsEnum)
            return false;
        if (!type.IsValueType)
            return true; // managed reference — not blittable

        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (FieldInfo field in fields)
        {
            if (ContainsUnsupportedField(field.FieldType))
                return true;
        }
        return false;
    }
}
