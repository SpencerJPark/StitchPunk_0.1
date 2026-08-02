// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using NUnit.Framework;
using StitchPunk.AnimationToolkit;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Pins the system-group skeleton of architecture section 5.1 — the toolkit's insertion contract
    /// with a host frame (build step C4.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These assertions read like bookkeeping and are not. The ungated-logic / gated-presentation
    /// split is the design's load-bearing decision, and its two halves are distinguishable only by
    /// which group a system is placed in. Ordering edges are equally consequential: a host that
    /// spawns actors via ECB relies on the binding group running first, and nothing about a
    /// mis-ordered group announces itself — the symptom is a stale entity reference on spawn frames
    /// only.
    /// </para>
    /// <para>
    /// Each test below names the mutation it catches, because this package has now shipped three
    /// tests that would have passed against the defect they were written for.
    /// </para>
    /// </remarks>
    public sealed class SystemGroupStructureTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ToolkitSystemGroupStructureTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;
        }

        /// <summary>
        /// Catches: changing <c>AnimationToolkitSystemGroup</c>'s parent, or dropping the attribute.
        /// A toolkit that does not sit in <c>SimulationSystemGroup</c> never ticks in a default world.
        /// </summary>
        [Test]
        public void ToolkitGroup_UpdatesInSimulationSystemGroup()
        {
            UpdateInGroupAttribute updateInGroup = GetSingleUpdateInGroup(typeof(AnimationToolkitSystemGroup));

            Assert.AreEqual(
                typeof(SimulationSystemGroup),
                updateInGroup.GroupType,
                "The toolkit's outer group must live in SimulationSystemGroup.");
        }

        /// <summary>
        /// Catches: adding an `UpdateBefore`/`UpdateAfter` to the outer group. Section 5.1 forbids it
        /// — an ordering opinion about assemblies this package has never seen is how two packages
        /// deadlock a host's sort, and the host is supposed to order itself against us instead.
        /// </summary>
        [Test]
        public void ToolkitGroup_DeclaresNoOrderingEdges_SoHostsOrderThemselvesAgainstIt()
        {
            object[] beforeAttributes =
                typeof(AnimationToolkitSystemGroup).GetCustomAttributes(typeof(UpdateBeforeAttribute), false);
            object[] afterAttributes =
                typeof(AnimationToolkitSystemGroup).GetCustomAttributes(typeof(UpdateAfterAttribute), false);

            Assert.IsEmpty(
                beforeAttributes,
                "AnimationToolkitSystemGroup must declare no UpdateBefore edges (architecture section 5.1).");
            Assert.IsEmpty(
                afterAttributes,
                "AnimationToolkitSystemGroup must declare no UpdateAfter edges (architecture section 5.1).");
        }

        /// <summary>
        /// Catches: dropping <c>OrderFirst</c> from the binding group. Without it the group sorts by
        /// its edges alone and a spawn-frame read of <c>RigPartRef</c> can precede the rebuild.
        /// </summary>
        [Test]
        public void BindingGroup_IsOrderFirstInsideTheToolkitGroup()
        {
            UpdateInGroupAttribute updateInGroup =
                GetSingleUpdateInGroup(typeof(AnimationToolkitBindingSystemGroup));

            Assert.AreEqual(typeof(AnimationToolkitSystemGroup), updateInGroup.GroupType);
            Assert.IsTrue(updateInGroup.OrderFirst, "The binding group must be OrderFirst.");
        }

        /// <summary>
        /// Catches: dropping <c>OrderLast</c> from the presentation group, which would let sampling
        /// run before the frame's playback state had been advanced.
        /// </summary>
        [Test]
        public void PresentationGroup_IsOrderLastInsideTheToolkitGroup()
        {
            UpdateInGroupAttribute updateInGroup =
                GetSingleUpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup));

            Assert.AreEqual(typeof(AnimationToolkitSystemGroup), updateInGroup.GroupType);
            Assert.IsTrue(updateInGroup.OrderLast, "The presentation group must be OrderLast.");
        }

        /// <summary>
        /// Catches: removing either edge on the logic group. Logic must follow binding and precede
        /// presentation; with the edges gone the sort is free to interleave them, and the resulting
        /// defect is frame-ordering-dependent and miserable to reproduce.
        /// </summary>
        [Test]
        public void LogicGroup_RunsAfterBinding_AndBeforePresentation()
        {
            UpdateInGroupAttribute updateInGroup =
                GetSingleUpdateInGroup(typeof(AnimationToolkitLogicSystemGroup));
            Assert.AreEqual(typeof(AnimationToolkitSystemGroup), updateInGroup.GroupType);

            object[] afterAttributes =
                typeof(AnimationToolkitLogicSystemGroup).GetCustomAttributes(typeof(UpdateAfterAttribute), false);
            object[] beforeAttributes =
                typeof(AnimationToolkitLogicSystemGroup).GetCustomAttributes(typeof(UpdateBeforeAttribute), false);

            Assert.AreEqual(1, afterAttributes.Length, "Expected exactly one UpdateAfter on the logic group.");
            Assert.AreEqual(
                typeof(AnimationToolkitBindingSystemGroup),
                ((UpdateAfterAttribute)afterAttributes[0]).SystemType);

            Assert.AreEqual(1, beforeAttributes.Length, "Expected exactly one UpdateBefore on the logic group.");
            Assert.AreEqual(
                typeof(AnimationToolkitPresentationSystemGroup),
                ((UpdateBeforeAttribute)beforeAttributes[0]).SystemType);
        }

        /// <summary>
        /// Catches: moving <c>CommandApplySystem</c> out of the logic group, or dropping its
        /// <c>OrderFirst</c>. Both halves matter. In the presentation group its work would happen
        /// after the frame already sampled, so every command would land a frame late; without
        /// <c>OrderFirst</c> the stale-event clear amendment A28 put here can be preceded by a
        /// system that emits, and that system's events are wiped in the frame they were raised.
        /// </summary>
        [Test]
        public void CommandApply_IsOrderFirstInTheLogicGroup()
        {
            UpdateInGroupAttribute updateInGroup = GetSingleUpdateInGroup(typeof(CommandApplySystem));

            Assert.AreEqual(typeof(AnimationToolkitLogicSystemGroup), updateInGroup.GroupType);
            Assert.IsTrue(
                updateInGroup.OrderFirst,
                "CommandApplySystem opens the logic group: it clears last frame's events before " +
                "anything can emit into them.");
        }

        /// <summary>
        /// Catches: dropping the <c>UpdateAfter</c> edge between the two playback systems. Reversed,
        /// the frame advances time and <em>then</em> applies the commands, so a Play would show its
        /// first frame one frame late and — worse — a clip that finished would be advanced past its
        /// end before the Play that was meant to replace it ever ran.
        /// </summary>
        [Test]
        public void PlaybackTime_RunsAfterCommandApply_InTheLogicGroup()
        {
            UpdateInGroupAttribute updateInGroup = GetSingleUpdateInGroup(typeof(PlaybackTimeSystem));
            Assert.AreEqual(typeof(AnimationToolkitLogicSystemGroup), updateInGroup.GroupType);
            Assert.IsFalse(updateInGroup.OrderFirst, "Only CommandApplySystem opens the logic group.");

            object[] afterAttributes =
                typeof(PlaybackTimeSystem).GetCustomAttributes(typeof(UpdateAfterAttribute), false);

            Assert.AreEqual(1, afterAttributes.Length, "Expected exactly one UpdateAfter on PlaybackTimeSystem.");
            Assert.AreEqual(
                typeof(CommandApplySystem),
                ((UpdateAfterAttribute)afterAttributes[0]).SystemType);
        }

        /// <summary>
        /// Catches: deleting the auto-creation branch in <c>ConfigBootstrapSystem</c>. Without it the
        /// package needs manual setup, which the zero-setup promise of section 5.2 forbids.
        /// </summary>
        [Test]
        public void ConfigBootstrap_CreatesTheConfigSingleton_WhenTheWorldHasNone()
        {
            Assert.IsFalse(
                HasConfigSingleton(testWorld),
                "Guard: a fresh world must start without the config singleton, or this test proves nothing.");

            testWorld.GetOrCreateSystem<ConfigBootstrapSystem>();

            Assert.IsTrue(HasConfigSingleton(testWorld), "ConfigBootstrapSystem must create the config singleton.");
        }

        /// <summary>
        /// Catches: turning the "create if absent" branch into an unconditional write. A host that
        /// authored or tuned its own config must not have it replaced by defaults because a system
        /// initialised later.
        /// </summary>
        [Test]
        public void ConfigBootstrap_DoesNotOverwriteAnExistingConfig()
        {
            AnimationToolkitConfig authoredConfig = new AnimationToolkitConfig
            {
                defaultSampleRateHz = 24f,
                distanceLodEnabled = true,
                lodDistancesSq = new Unity.Mathematics.float4(1f, 2f, 3f, 4f)
            };
            testWorld.EntityManager.CreateSingleton(authoredConfig, "HostAuthoredConfig");

            testWorld.GetOrCreateSystem<ConfigBootstrapSystem>();

            EntityQuery configQuery = testWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimationToolkitConfig>());
            Assert.AreEqual(
                1,
                configQuery.CalculateEntityCount(),
                "Bootstrapping must not add a second config entity beside the host's.");

            AnimationToolkitConfig survivingConfig = configQuery.GetSingleton<AnimationToolkitConfig>();
            Assert.AreEqual(24f, survivingConfig.defaultSampleRateHz, "The host's authored rate was overwritten.");
            Assert.IsTrue(survivingConfig.distanceLodEnabled, "The host's authored LOD flag was overwritten.");
        }

        /// <summary>
        /// Catches: defaults that are not the do-nothing ones. A consumer who installs the package
        /// and presses Play must get full-quality animation, not a quietly degraded one.
        /// </summary>
        [Test]
        public void ConfigBootstrap_DefaultsToEveryFrameSampling_AndLodDisabled()
        {
            testWorld.GetOrCreateSystem<ConfigBootstrapSystem>();

            AnimationToolkitConfig createdConfig = testWorld.EntityManager
                .CreateEntityQuery(ComponentType.ReadOnly<AnimationToolkitConfig>())
                .GetSingleton<AnimationToolkitConfig>();

            Assert.AreEqual(
                0f,
                createdConfig.defaultSampleRateHz,
                "0 Hz means 'sample every frame'; any other default silently quantizes a new user's animation.");
            Assert.IsFalse(createdConfig.distanceLodEnabled, "Distance LOD is opt-in (architecture section 5.10).");
        }

        /// <summary>
        /// Catches: zeroed default LOD thresholds. If a host flips <c>distanceLodEnabled</c> without
        /// setting distances, an all-zero set puts every actor past every threshold — the whole crowd
        /// snaps to LOD 3 and the feature looks broken rather than unconfigured.
        /// </summary>
        [Test]
        public void ConfigBootstrap_DefaultLodThresholds_AreAscendingAndNonZero()
        {
            testWorld.GetOrCreateSystem<ConfigBootstrapSystem>();

            AnimationToolkitConfig createdConfig = testWorld.EntityManager
                .CreateEntityQuery(ComponentType.ReadOnly<AnimationToolkitConfig>())
                .GetSingleton<AnimationToolkitConfig>();

            Assert.Greater(createdConfig.lodDistancesSq.x, 0f, "LOD 1 threshold must be positive.");
            Assert.Greater(createdConfig.lodDistancesSq.y, createdConfig.lodDistancesSq.x, "Thresholds must ascend.");
            Assert.Greater(createdConfig.lodDistancesSq.z, createdConfig.lodDistancesSq.y, "Thresholds must ascend.");
        }

        /// <summary>
        /// Catches: a <c>SetEnabled</c> that reports success without touching the group, or one that
        /// cannot find it. This is the only supported way for a host to gate the feature.
        /// </summary>
        [Test]
        public void WorldControl_TogglesTheToolkitGroup()
        {
            testWorld.GetOrCreateSystemManaged<AnimationToolkitSystemGroup>();

            Assert.IsTrue(ToolkitWorldControl.IsEnabled(testWorld), "Groups start enabled.");

            Assert.IsTrue(ToolkitWorldControl.SetEnabled(testWorld, false), "SetEnabled should find the group.");
            Assert.IsFalse(ToolkitWorldControl.IsEnabled(testWorld), "The group should now be disabled.");

            Assert.IsTrue(ToolkitWorldControl.SetEnabled(testWorld, true));
            Assert.IsTrue(ToolkitWorldControl.IsEnabled(testWorld), "The group should be enabled again.");
        }

        /// <summary>
        /// Catches: replacing the null/unborn-world guards with a throw. Hosts call this from
        /// scene-load paths that can run against a torn-down world during shutdown; a throw there
        /// turns a benign ordering quirk into a crash.
        /// </summary>
        [Test]
        public void WorldControl_OnAnUnusableWorld_ReturnsFalseRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => ToolkitWorldControl.SetEnabled(null, true));
            Assert.IsFalse(ToolkitWorldControl.SetEnabled(null, true));
            Assert.IsFalse(ToolkitWorldControl.IsEnabled(null));

            World disposedWorld = new World("DisposedForToolkitWorldControlTest");
            disposedWorld.Dispose();
            Assert.DoesNotThrow(() => ToolkitWorldControl.SetEnabled(disposedWorld, true));
            Assert.IsFalse(ToolkitWorldControl.SetEnabled(disposedWorld, true));
        }

        /// <summary>
        /// Catches: calling <c>SetEnabled</c> on a world that has no toolkit group and getting a
        /// misleading "true". A world that has never held an actor is a normal case, not an error.
        /// </summary>
        [Test]
        public void WorldControl_OnAWorldWithoutTheGroup_ReturnsFalse()
        {
            Assert.IsFalse(
                ToolkitWorldControl.SetEnabled(testWorld, false),
                "No group was created in this world, so there was nothing to set.");
        }

        private static bool HasConfigSingleton(World world)
        {
            EntityQuery configQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimationToolkitConfig>());
            return configQuery.CalculateEntityCount() > 0;
        }

        private static UpdateInGroupAttribute GetSingleUpdateInGroup(Type systemType)
        {
            object[] attributes = systemType.GetCustomAttributes(typeof(UpdateInGroupAttribute), false);
            Assert.AreEqual(
                1,
                attributes.Length,
                "Expected exactly one UpdateInGroup on " + systemType.Name + ".");
            return (UpdateInGroupAttribute)attributes[0];
        }
    }
}
