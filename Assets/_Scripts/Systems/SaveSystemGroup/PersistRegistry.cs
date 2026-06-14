using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Builds the set of component types the save system serializes. The set is the union of:
///   • every value-type component implementing <see cref="IPersist"/> (designer opt-in), and
///   • an explicit list of Unity package types we cannot annotate (e.g. LocalTransform).
///
/// Types containing an <see cref="Entity"/> or <see cref="BlobAssetReference{T}"/> field are
/// filtered out (not yet round-trippable). Built once, lazily, on first access.
///
/// Lives in the Systems assembly (which references Unity.Transforms); the <see cref="IPersist"/>
/// marker it scans for lives in the Components assembly.
/// </summary>
public static class PersistRegistry
{
    /// <summary>A component type the save system will snapshot, with its resolved metadata.</summary>
    public struct SaveableType
    {
        public Type managedType;
        public ComponentType componentType;
        public bool isEnableable;
    }

    // Unity package components we want persisted but cannot mark with IPersist.
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

    /// <summary>
    /// Resolves a saved component's type name back to a <see cref="Type"/>. Tries the stored
    /// assembly-qualified name first, then falls back to a full-name lookup across the saveable
    /// set so a save still loads after an assembly rename. Returns false for unknown types
    /// (forward/backward compatibility — the caller skips the record).
    /// </summary>
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

        // Fallback: match on full name (strip assembly qualifier if present).
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

        // Scan loaded assemblies for value-type components implementing IPersist.
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

    /// <summary>
    /// True if the type (recursively) contains a field the raw-byte encoder can't safely
    /// round-trip across sessions: an <see cref="Entity"/>, a <see cref="BlobAssetReference{T}"/>,
    /// or any managed reference (non-blittable).
    /// </summary>
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
