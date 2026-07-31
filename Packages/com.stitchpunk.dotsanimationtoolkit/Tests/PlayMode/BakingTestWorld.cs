// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// A disposable baking world the M2 acceptance tests bake GameObject hierarchies into
    /// (architecture section 11.2 — "baking tests run here too"), plus the lookup from an authoring
    /// GameObject back to its primary entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entities exposes no public entry point for running a bake outside its own test assemblies:
    /// <c>BakingUtility</c> and <c>BakingSettings</c> are <c>internal</c> to
    /// <c>Unity.Entities.Hybrid</c>, and <c>BakingSystem.GetEntity</c> is an internal member of a
    /// public class. The three are reached by reflection here, in test code only — nothing the
    /// package ships depends on any of it. Each handle is resolved once in the static constructor
    /// and fails with a message naming the member, so an Entities upgrade that moves one produces a
    /// readable failure rather than a null-reference somewhere downstream.
    /// </para>
    /// <para>
    /// One <c>BlobAssetStore</c> lives for the lifetime of the world, which is what makes the
    /// section 4.5 deduplication observable: two actors sharing a <c>ClipSetAsset</c> must come out
    /// of a bake holding the very same <c>BlobAssetReference</c>.
    /// </para>
    /// </remarks>
    internal sealed class BakingTestWorld : IDisposable
    {
        private const string BakingUtilityTypeName = "Unity.Entities.BakingUtility";
        private const string BakingSettingsTypeName = "Unity.Entities.BakingSettings";
        private const string BakingFlagsTypeName = "BakingFlags";
        private const string AssignNameFlagName = "AssignName";

        private static readonly Type BakingFlagsType;
        private static readonly MethodInfo BakeGameObjectsMethod;
        private static readonly MethodInfo GetEntityMethod;
        private static readonly ConstructorInfo BakingSettingsConstructor;

        private readonly World bakingWorld;
        private readonly BlobAssetStore blobAssetStore;
        private readonly BakingSystem bakingSystem;
        private readonly object bakingSettings;

        static BakingTestWorld()
        {
            Assembly hybridAssembly = typeof(PostBakingSystemGroup).Assembly;

            Type bakingUtilityType = ResolveType(hybridAssembly, BakingUtilityTypeName);
            Type bakingSettingsType = ResolveType(hybridAssembly, BakingSettingsTypeName);

            BakingFlagsType = bakingUtilityType.GetNestedType(
                BakingFlagsTypeName,
                BindingFlags.Public | BindingFlags.NonPublic);
            AssertResolved(BakingFlagsType, BakingUtilityTypeName + "+" + BakingFlagsTypeName);

            BakeGameObjectsMethod = bakingUtilityType.GetMethod(
                "BakeGameObjects",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(World), typeof(GameObject[]), bakingSettingsType },
                null);
            AssertResolved(BakeGameObjectsMethod, BakingUtilityTypeName + ".BakeGameObjects");

            BakingSettingsConstructor = bakingSettingsType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { BakingFlagsType, typeof(BlobAssetStore) },
                null);
            AssertResolved(BakingSettingsConstructor, BakingSettingsTypeName + "(BakingFlags, BlobAssetStore)");

            GetEntityMethod = typeof(BakingSystem).GetMethod(
                "GetEntity",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(GameObject) },
                null);
            AssertResolved(GetEntityMethod, "Unity.Entities.BakingSystem.GetEntity(GameObject)");
        }

        internal BakingTestWorld(string worldName)
        {
            bakingWorld = new World(worldName);
            blobAssetStore = new BlobAssetStore(128);
            object assignNameFlag = Enum.Parse(BakingFlagsType, AssignNameFlagName);
            bakingSettings = BakingSettingsConstructor.Invoke(new object[] { assignNameFlag, blobAssetStore });
            bakingSystem = bakingWorld.GetOrCreateSystemManaged<BakingSystem>();
        }

        /// <summary>The world the baked entities live in.</summary>
        internal World World
        {
            get { return bakingWorld; }
        }

        /// <summary>The baked entities' entity manager.</summary>
        internal EntityManager EntityManager
        {
            get { return bakingWorld.EntityManager; }
        }

        /// <summary>
        /// Runs a full bake — bakers, transform baking, <c>PostBakingSystemGroup</c> and all — over
        /// the supplied roots and everything under them.
        /// </summary>
        internal void Bake(params GameObject[] rootGameObjects)
        {
            lock (toolkitLogLock)
            {
                toolkitWarnings.Clear();
                toolkitErrors.Clear();
            }

            // Suppressed here rather than in the fixture's [SetUp]. BeforeAfterTestCommandBase wraps
            // each setup method in its own LogScope and disposes it before the test body runs, so a
            // flag set in [SetUp] is gone by the time anything bakes; the test body's scope starts
            // at the default again. Setting it around the bake also scopes the suppression to the
            // only place foreign logs can appear.
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceivedThreaded += RecordToolkitLog;
            try
            {
                BakeGameObjectsMethod.Invoke(
                    null,
                    new object[] { bakingWorld, rootGameObjects, bakingSettings });
            }
            catch (TargetInvocationException invocationException)
            {
                // Surface what the bake actually threw, with its own stack, rather than the
                // reflection wrapper.
                ExceptionDispatchInfo.Capture(invocationException.InnerException).Throw();
            }
            finally
            {
                Application.logMessageReceivedThreaded -= RecordToolkitLog;
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        // -----------------------------------------------------------------------------------
        // Log capture.
        //
        // A baking world runs every baking system in the project, including the host application's.
        // Their diagnostics are not this package's to control, and asserting through LogAssert makes
        // a foreign warning fail an unrelated toolkit test — which happened, and would happen again
        // in any consumer project with its own bakers.
        //
        // So the suite records the bake's messages itself and asserts only over the ones this
        // package emitted. That is both host-independent and a stronger claim than LogAssert could
        // make: "exactly one warning" becomes a count, not the absence of a complaint.
        //
        // The subscription is to logMessageReceivedThreaded, not logMessageReceived. The binding
        // pass is a Bursted job and reports from a worker thread, and the main-thread-only callback
        // never sees those messages — every RigBindingBakingSystem diagnostic would silently record
        // as zero, so a test asserting one error would fail while the error sat in the console.
        // (UTF's own LogScope subscribes the same way, for the same reason.) That makes this a
        // multi-threaded callback, hence the lock.
        // -----------------------------------------------------------------------------------

        private const string ToolkitMessagePrefix = "[DOTS Animation Toolkit]";

        private readonly object toolkitLogLock = new object();
        private readonly List<string> toolkitWarnings = new List<string>();
        private readonly List<string> toolkitErrors = new List<string>();

        /// <summary>Warnings this package emitted during the most recent <see cref="Bake"/>.</summary>
        internal IReadOnlyList<string> ToolkitWarnings
        {
            get
            {
                lock (toolkitLogLock)
                {
                    return toolkitWarnings.ToArray();
                }
            }
        }

        /// <summary>Errors this package emitted during the most recent <see cref="Bake"/>.</summary>
        internal IReadOnlyList<string> ToolkitErrors
        {
            get
            {
                lock (toolkitLogLock)
                {
                    return toolkitErrors.ToArray();
                }
            }
        }

        private void RecordToolkitLog(string message, string stackTrace, LogType logType)
        {
            if (message == null || !message.Contains(ToolkitMessagePrefix))
            {
                return;
            }
            lock (toolkitLogLock)
            {
                if (logType == LogType.Warning)
                {
                    toolkitWarnings.Add(message);
                }
                else if (logType == LogType.Error || logType == LogType.Exception || logType == LogType.Assert)
                {
                    toolkitErrors.Add(message);
                }
            }
        }

        /// <summary>Returns the primary baked entity of an authoring GameObject.</summary>
        internal Entity GetPrimaryEntity(GameObject authoringGameObject)
        {
            return (Entity)GetEntityMethod.Invoke(bakingSystem, new object[] { authoringGameObject });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (bakingWorld.IsCreated)
            {
                bakingWorld.Dispose();
            }
            if (blobAssetStore.IsCreated)
            {
                blobAssetStore.Dispose();
            }
        }

        private static Type ResolveType(Assembly assembly, string typeName)
        {
            Type resolvedType = assembly.GetType(typeName, false);
            AssertResolved(resolvedType, typeName);
            return resolvedType;
        }

        private static void AssertResolved(object resolvedMember, string memberName)
        {
            if (resolvedMember == null)
            {
                throw new InvalidOperationException(
                    "The baking test harness could not resolve '" + memberName +
                    "'. Unity.Entities changed the internal baking entry points; update BakingTestWorld.");
            }
        }
    }
}
