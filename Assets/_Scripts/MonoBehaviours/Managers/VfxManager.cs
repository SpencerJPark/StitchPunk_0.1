using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// Plays the one-shot particle effects the ECS side asks for (VfxRequest -> VfxDrainSystem -> ResolvedVfx),
// from a small pool of ParticleSystem instances per VfxType. Mirrors AudioManager's read of ResolvedVoices.
public class VfxManager : MonoBehaviour
{
    [Serializable]
    public class VfxPrefabEntry
    {
        public VfxType type;
        [Tooltip("A prefab with a ParticleSystem on its root. Its +Z is pointed along the request's direction.")]
        public ParticleSystem prefab;
        [Tooltip("Most copies of this effect alive at once; the oldest is restarted when all are busy.")]
        public int maxInstances = 6;
    }

    public List<VfxPrefabEntry> effects = new List<VfxPrefabEntry>();

    private readonly Dictionary<VfxType, VfxPrefabEntry> entriesByType = new Dictionary<VfxType, VfxPrefabEntry>();
    private readonly Dictionary<VfxType, List<ParticleSystem>> poolsByType = new Dictionary<VfxType, List<ParticleSystem>>();
    private readonly Dictionary<VfxType, int> nextRecycleIndexByType = new Dictionary<VfxType, int>();

    private EntityQuery resolvedVfxQuery;
    private bool worldReady;

    private void Awake()
    {
        foreach (VfxPrefabEntry entry in effects)
        {
            if (entry == null || entry.prefab == null || entry.type == VfxType.None) continue;
            entriesByType[entry.type] = entry;
            poolsByType[entry.type] = new List<ParticleSystem>();
            nextRecycleIndexByType[entry.type] = 0;
        }
    }

    private bool EnsureWorld()
    {
        if (worldReady) return true;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        resolvedVfxQuery = world.EntityManager.CreateEntityQuery(typeof(ResolvedVfx));
        worldReady = true;
        return true;
    }

    private void LateUpdate()
    {
        if (!EnsureWorld()) return;
        if (resolvedVfxQuery.CalculateEntityCount() != 1) return;

        ResolvedVfx resolvedVfx = resolvedVfxQuery.GetSingleton<ResolvedVfx>();
        if (!resolvedVfx.requests.IsCreated) return;

        NativeList<VfxRequest> requests = resolvedVfx.requests;
        for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            PlayRequest(requests[requestIndex]);
    }

    private void PlayRequest(VfxRequest request)
    {
        if (!entriesByType.TryGetValue(request.type, out VfxPrefabEntry entry)) return;

        ParticleSystem effectInstance = TakeInstance(entry);
        Transform effectTransform = effectInstance.transform;
        effectTransform.position = request.position;

        Vector3 flatDirection = new Vector3(request.direction.x, 0f, request.direction.z);
        effectTransform.rotation = flatDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(flatDirection.normalized, Vector3.up)
            : Quaternion.identity;
        effectTransform.localScale = Vector3.one * (request.scale > 0f ? request.scale : 1f);

        effectInstance.Clear(true);
        effectInstance.Play(true);
    }

    private ParticleSystem TakeInstance(VfxPrefabEntry entry)
    {
        List<ParticleSystem> pool = poolsByType[entry.type];
        foreach (ParticleSystem pooledInstance in pool)
        {
            if (pooledInstance != null && !pooledInstance.IsAlive(true))
                return pooledInstance;
        }

        if (pool.Count < Mathf.Max(1, entry.maxInstances))
        {
            ParticleSystem newInstance = Instantiate(entry.prefab, transform);
            ParticleSystem.MainModule mainModule = newInstance.main;
            mainModule.playOnAwake = false;
            newInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pool.Add(newInstance);
            return newInstance;
        }

        int recycleIndex = nextRecycleIndexByType[entry.type] % pool.Count;
        nextRecycleIndexByType[entry.type] = recycleIndex + 1;
        return pool[recycleIndex];
    }
}
