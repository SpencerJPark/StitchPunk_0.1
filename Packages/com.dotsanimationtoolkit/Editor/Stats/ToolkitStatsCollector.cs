// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.Collections.Generic;
using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Samples one world's toolkit entity counts and system group timings into a ToolkitStatsSample.
    /// </summary>
    public sealed class ToolkitStatsCollector : IDisposable
    {
        private const int LodFullBucketLimit = 100000;
        private const int LodSampleSize = 1024;

        private World trackedWorld;
        private bool resourcesCreated;

        private EntityQuery actorQuery;
        private EntityQuery ragdollQuery;
        private EntityQuery cutsceneQuery;
        private EntityQuery lodQuery;
        private EntityQuery pendingQuery;
        private EntityQuery windowQuery;
        private EntityQuery vatQuery;

        private ProfilerRecorder toolkitGroupRecorder;
        private ProfilerRecorder bindingGroupRecorder;
        private ProfilerRecorder logicGroupRecorder;
        private ProfilerRecorder presentationGroupRecorder;
        private ProfilerRecorder ragdollGroupRecorder;

        private readonly HashSet<Texture2D> distinctVatTextures = new HashSet<Texture2D>();

        public void Sample(World world, ref ToolkitStatsSample output)
        {
            if (world == null || !world.IsCreated)
            {
                ReleaseWorldResources();
                output = default(ToolkitStatsSample);
                return;
            }

            if (world != this.trackedWorld)
            {
                ReleaseWorldResources();
                CreateWorldResources(world);
            }

            EntityManager entityManager = world.EntityManager;

            entityManager.CompleteDependencyBeforeRO<PlaybackLayer>();
            BufferTypeHandle<PlaybackLayer> playbackLayerHandle = entityManager.GetBufferTypeHandle<PlaybackLayer>(true);
            NativeArray<ArchetypeChunk> actorChunks = this.actorQuery.ToArchetypeChunkArray(Allocator.Temp);
            int layerCount = 0;
            for (int chunkIndex = 0; chunkIndex < actorChunks.Length; chunkIndex++)
            {
                BufferAccessor<PlaybackLayer> bufferAccessor = actorChunks[chunkIndex].GetBufferAccessor(ref playbackLayerHandle);
                for (int entityIndex = 0; entityIndex < bufferAccessor.Length; entityIndex++)
                {
                    layerCount += bufferAccessor[entityIndex].Length;
                }
            }
            actorChunks.Dispose();

            int pendingEventActorCount = this.pendingQuery.CalculateEntityCount();
            NativeArray<Entity> pendingEntities = this.pendingQuery.ToEntityArray(Allocator.Temp);
            int eventsThisFrame = 0;
            for (int pendingIndex = 0; pendingIndex < pendingEntities.Length; pendingIndex++)
            {
                eventsThisFrame += entityManager.GetBuffer<AnimEventOutput>(pendingEntities[pendingIndex], true).Length;
            }
            pendingEntities.Dispose();

            NativeArray<AnimLod> lodLevels = this.lodQuery.ToComponentDataArray<AnimLod>(Allocator.Temp);
            int lodEntityCount = lodLevels.Length;
            bool lodSampled = lodEntityCount > LodFullBucketLimit;
            int lodBucketCount = lodSampled ? LodSampleSize : lodEntityCount;
            int lodLevel0Count = 0;
            int lodLevel1Count = 0;
            int lodLevel2Count = 0;
            int lodLevel3Count = 0;
            for (int lodIndex = 0; lodIndex < lodBucketCount; lodIndex++)
            {
                int clampedLevel = lodLevels[lodIndex].level > 3 ? 3 : lodLevels[lodIndex].level;
                switch (clampedLevel)
                {
                    case 0:
                        lodLevel0Count++;
                        break;
                    case 1:
                        lodLevel1Count++;
                        break;
                    case 2:
                        lodLevel2Count++;
                        break;
                    default:
                        lodLevel3Count++;
                        break;
                }
            }
            lodLevels.Dispose();

            NativeArray<VatPartTextureBinding> vatBindings = this.vatQuery.ToComponentDataArray<VatPartTextureBinding>(Allocator.Temp);
            int vatBoundPartCount = vatBindings.Length;
            this.distinctVatTextures.Clear();
            long vatTextureBytes = 0L;
            for (int vatIndex = 0; vatIndex < vatBindings.Length; vatIndex++)
            {
                Texture2D boneOrPositionTexture = vatBindings[vatIndex].boneOrPositionTexture.Value;
                Texture2D normalTexture = vatBindings[vatIndex].normalTexture.Value;
                if (boneOrPositionTexture != null && this.distinctVatTextures.Add(boneOrPositionTexture))
                {
                    vatTextureBytes += Profiler.GetRuntimeMemorySizeLong(boneOrPositionTexture);
                }
                if (normalTexture != null && this.distinctVatTextures.Add(normalTexture))
                {
                    vatTextureBytes += Profiler.GetRuntimeMemorySizeLong(normalTexture);
                }
            }
            int vatDistinctTextureCount = this.distinctVatTextures.Count;
            vatBindings.Dispose();

            bool timingsAvailable = this.toolkitGroupRecorder.Valid && this.toolkitGroupRecorder.Count > 0;

            output.worldAvailable = true;
            output.worldName = world.Name;
            output.actorCount = this.actorQuery.CalculateEntityCount();
            output.layerCount = layerCount;
            output.ragdollingActorCount = this.ragdollQuery.CalculateEntityCount();
            output.cutscenePlayerCount = this.cutsceneQuery.CalculateEntityCount();
            output.lodLevel0Count = lodLevel0Count;
            output.lodLevel1Count = lodLevel1Count;
            output.lodLevel2Count = lodLevel2Count;
            output.lodLevel3Count = lodLevel3Count;
            output.lodSampled = lodSampled;
            output.eventsThisFrame = eventsThisFrame;
            output.pendingEventActorCount = pendingEventActorCount;
            output.actorsWithOpenWindowsCount = this.windowQuery.CalculateEntityCount();
            output.vatBoundPartCount = vatBoundPartCount;
            output.vatDistinctTextureCount = vatDistinctTextureCount;
            output.vatTextureBytes = vatTextureBytes;
            output.timingsAvailable = timingsAvailable;
            output.toolkitGroupMilliseconds = ComputeMilliseconds(this.toolkitGroupRecorder);
            output.bindingGroupMilliseconds = ComputeMilliseconds(this.bindingGroupRecorder);
            output.logicGroupMilliseconds = ComputeMilliseconds(this.logicGroupRecorder);
            output.presentationGroupMilliseconds = ComputeMilliseconds(this.presentationGroupRecorder);
            output.ragdollGroupMilliseconds = ComputeMilliseconds(this.ragdollGroupRecorder);
        }

        public void Dispose()
        {
            ReleaseWorldResources();
        }

        private void CreateWorldResources(World world)
        {
            EntityManager entityManager = world.EntityManager;

            this.actorQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlaybackLayer>());
            this.ragdollQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RagdollActor>());
            this.cutsceneQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CutscenePlay>());
            this.lodQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AnimLod>());
            this.pendingQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AnimEventsPending>(), ComponentType.ReadOnly<AnimEventOutput>());
            this.windowQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AnimEventMask>());
            this.vatQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<VatPartTextureBinding>());

            string worldPrefix = world.Name + " ";
            this.toolkitGroupRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, worldPrefix + typeof(AnimationToolkitSystemGroup).FullName, 8);
            this.bindingGroupRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, worldPrefix + typeof(AnimationToolkitBindingSystemGroup).FullName, 8);
            this.logicGroupRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, worldPrefix + typeof(AnimationToolkitLogicSystemGroup).FullName, 8);
            this.presentationGroupRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, worldPrefix + typeof(AnimationToolkitPresentationSystemGroup).FullName, 8);
            this.ragdollGroupRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, worldPrefix + typeof(AnimationToolkitRagdollSystemGroup).FullName, 8);

            this.trackedWorld = world;
            this.resourcesCreated = true;
        }

        private void ReleaseWorldResources()
        {
            if (!this.resourcesCreated)
            {
                this.trackedWorld = null;
                return;
            }

            // A query's world may already be destroyed; disposing it then throws, so only dispose while the world is still alive.
            if (this.trackedWorld != null && this.trackedWorld.IsCreated)
            {
                this.actorQuery.Dispose();
                this.ragdollQuery.Dispose();
                this.cutsceneQuery.Dispose();
                this.lodQuery.Dispose();
                this.pendingQuery.Dispose();
                this.windowQuery.Dispose();
                this.vatQuery.Dispose();
            }

            if (this.toolkitGroupRecorder.Valid)
            {
                this.toolkitGroupRecorder.Dispose();
            }
            if (this.bindingGroupRecorder.Valid)
            {
                this.bindingGroupRecorder.Dispose();
            }
            if (this.logicGroupRecorder.Valid)
            {
                this.logicGroupRecorder.Dispose();
            }
            if (this.presentationGroupRecorder.Valid)
            {
                this.presentationGroupRecorder.Dispose();
            }
            if (this.ragdollGroupRecorder.Valid)
            {
                this.ragdollGroupRecorder.Dispose();
            }

            this.trackedWorld = null;
            this.resourcesCreated = false;
        }

        private static double ComputeMilliseconds(ProfilerRecorder recorder)
        {
            int sampleCount = recorder.Count;
            if (sampleCount == 0)
            {
                return 0.0;
            }

            long sampleSum = 0L;
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                sampleSum += recorder.GetSample(sampleIndex).Value;
            }

            double averageNanoseconds = (double)sampleSum / sampleCount;
            return averageNanoseconds / 1000000.0;
        }
    }
}
