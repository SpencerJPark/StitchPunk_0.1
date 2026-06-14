using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Generic, marker-driven (de)serializer for ECS components. Managed / main-thread only —
/// no [BurstCompile] (reflection, Marshal, Convert.ToBase64String). Save/load is infrequent
/// and off the hot path, so the reflection cost is irrelevant.
///
/// Encoder (v1): each component is an unmanaged blittable struct, so its raw bytes are copied
/// to a Base64 string and copied straight back on load — fully generic, handles any math type,
/// zero per-type code. The encoder is the single seam to swap later (e.g. for named-field JSON
/// with migration resilience) without touching the systems.
///
/// See <see cref="PersistRegistry"/> for which types are saved and the v1 exclusions.
/// </summary>
public static class SaveSerialization
{
    // Cached open generic method handles, resolved once.
    private static readonly MethodInfo GetComponentDataOpen = ResolveEntityManagerMethod("GetComponentData", 1);
    private static readonly MethodInfo SetComponentDataOpen = ResolveEntityManagerMethod("SetComponentData", 2);
    private static readonly MethodInfo SizeOfOpen = typeof(UnsafeUtility).GetMethods()
        .First(method => method.Name == "SizeOf" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0);

    // Per-type closed-method caches.
    private static readonly Dictionary<Type, MethodInfo> getByType = new Dictionary<Type, MethodInfo>();
    private static readonly Dictionary<Type, MethodInfo> setByType = new Dictionary<Type, MethodInfo>();
    private static readonly Dictionary<Type, int> sizeByType = new Dictionary<Type, int>();

    /// <summary>
    /// Snapshots every saveable component present on <paramref name="entity"/> into an
    /// <see cref="EntityRecord"/>.
    /// </summary>
    public static EntityRecord WriteEntity(EntityManager entityManager, Entity entity, string role)
    {
        List<ComponentRecord> records = new List<ComponentRecord>();

        foreach (PersistRegistry.SaveableType saveable in PersistRegistry.SaveableTypes)
        {
            if (!entityManager.HasComponent(entity, saveable.componentType))
                continue;

            object boxed = ClosedGet(saveable.managedType).Invoke(entityManager, new object[] { entity });
            int size = SizeOf(saveable.managedType);
            byte[] bytes = StructToBytes(boxed, size);

            bool enabled = !saveable.isEnableable
                || entityManager.IsComponentEnabled(entity, saveable.componentType);

            records.Add(new ComponentRecord
            {
                type    = saveable.managedType.AssemblyQualifiedName,
                enabled = enabled,
                data    = Convert.ToBase64String(bytes),
            });
        }

        return new EntityRecord
        {
            role       = role,
            components = records.ToArray(),
        };
    }

    /// <summary>
    /// Restores each saved component from <paramref name="record"/> onto <paramref name="entity"/>.
    /// Records whose type can't be resolved, or that the entity doesn't have, are skipped
    /// (forward/backward compatibility).
    /// </summary>
    public static void ApplyEntity(EntityManager entityManager, Entity entity, EntityRecord record)
    {
        if (record?.components == null)
            return;

        foreach (ComponentRecord component in record.components)
        {
            if (!PersistRegistry.TryResolveType(component.type, out Type type))
            {
                Debug.LogWarning($"[SaveSerialization] Unknown component type '{component.type}' — skipping.");
                continue;
            }

            ComponentType componentType = ComponentType.ReadWrite(type);
            if (!entityManager.HasComponent(entity, componentType))
                continue; // entity archetype no longer carries this component

            byte[] bytes = Convert.FromBase64String(component.data);
            int size = SizeOf(type);
            if (bytes.Length != size)
            {
                Debug.LogWarning(
                    $"[SaveSerialization] Size mismatch for '{type.FullName}' (saved {bytes.Length}, now {size}) — skipping. Struct layout changed since save.");
                continue;
            }

            object boxed = BytesToStruct(type, bytes);
            ClosedSet(type).Invoke(entityManager, new object[] { entity, boxed });

            if (typeof(IEnableableComponent).IsAssignableFrom(type))
                entityManager.SetComponentEnabled(entity, componentType, component.enabled);
        }
    }

    /// <summary>
    /// Decodes a single component of type <typeparamref name="T"/> from a record, if present.
    /// Used at minion-instantiate time to place the body (LocalTransform) before the deferred
    /// <see cref="ApplyEntity"/> runs.
    /// </summary>
    public static bool TryReadComponent<T>(EntityRecord record, out T value) where T : unmanaged
    {
        value = default;
        if (record?.components == null)
            return false;

        foreach (ComponentRecord component in record.components)
        {
            if (!PersistRegistry.TryResolveType(component.type, out Type type) || type != typeof(T))
                continue;

            byte[] bytes = Convert.FromBase64String(component.data);
            if (bytes.Length != SizeOf(type))
                return false;

            value = (T)BytesToStruct(type, bytes);
            return true;
        }
        return false;
    }

    // --- raw-byte encoder -------------------------------------------------

    private static byte[] StructToBytes(object boxedStruct, int size)
    {
        byte[] bytes = new byte[size];
        GCHandle handle = GCHandle.Alloc(boxedStruct, GCHandleType.Pinned);
        try
        {
            Marshal.Copy(handle.AddrOfPinnedObject(), bytes, 0, size);
        }
        finally
        {
            handle.Free();
        }
        return bytes;
    }

    private static object BytesToStruct(Type type, byte[] bytes)
    {
        object boxed = Activator.CreateInstance(type);
        GCHandle handle = GCHandle.Alloc(boxed, GCHandleType.Pinned);
        try
        {
            Marshal.Copy(bytes, 0, handle.AddrOfPinnedObject(), bytes.Length);
        }
        finally
        {
            handle.Free();
        }
        return boxed;
    }

    // --- reflection caches ------------------------------------------------

    private static MethodInfo ClosedGet(Type type)
    {
        if (!getByType.TryGetValue(type, out MethodInfo closed))
        {
            closed = GetComponentDataOpen.MakeGenericMethod(type);
            getByType[type] = closed;
        }
        return closed;
    }

    private static MethodInfo ClosedSet(Type type)
    {
        if (!setByType.TryGetValue(type, out MethodInfo closed))
        {
            closed = SetComponentDataOpen.MakeGenericMethod(type);
            setByType[type] = closed;
        }
        return closed;
    }

    private static int SizeOf(Type type)
    {
        if (!sizeByType.TryGetValue(type, out int size))
        {
            size = (int)SizeOfOpen.MakeGenericMethod(type).Invoke(null, null);
            sizeByType[type] = size;
        }
        return size;
    }

    private static MethodInfo ResolveEntityManagerMethod(string name, int parameterCount)
    {
        return typeof(EntityManager).GetMethods()
            .First(method => method.Name == name
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == parameterCount
                && method.GetParameters()[0].ParameterType == typeof(Entity));
    }
}
