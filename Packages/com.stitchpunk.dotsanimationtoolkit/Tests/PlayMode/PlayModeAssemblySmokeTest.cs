// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Smoke coverage proving the PlayMode test assembly compiles, loads, and runs
    /// <strong>in PlayMode</strong> under its contracted name (architecture section 1.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mode assertion is the point of this fixture, and it is the assertion the original
    /// version lacked. That version checked only <c>Assembly.GetName().Name</c> — a string that is
    /// equally true whichever mode the assembly runs in. It passed for the whole of build step C3
    /// while the entire 27-test suite was executing in EditMode, because
    /// <c>StitchPunk.AnimationToolkit.Tests.PlayMode.asmdef</c> had been given
    /// <c>"includePlatforms": ["Editor"]</c> and an editor-only assembly is classified as an
    /// EditMode test assembly. A project-wide PlayMode run discovered zero tests and reported
    /// Passed, which is indistinguishable from success at a glance.
    /// </para>
    /// <para>
    /// So this test asserts something only PlayMode can satisfy. <c>Application.isPlaying</c> is
    /// false in an EditMode run and true here, and a <c>[UnityTest]</c> coroutine yielding a frame
    /// additionally requires a running player loop — an EditMode run cannot produce one. If the
    /// asmdef is ever restricted to <c>Editor</c> again, this fixture fails rather than quietly
    /// changing mode with the rest of the suite.
    /// </para>
    /// </remarks>
    public sealed class PlayModeAssemblySmokeTest
    {
        [Test]
        public void PlayModeTestAssembly_HasContractedName()
        {
            string assemblyName = typeof(PlayModeAssemblySmokeTest).Assembly.GetName().Name;
            Assert.AreEqual("StitchPunk.AnimationToolkit.Tests.PlayMode", assemblyName);
        }

        [Test]
        public void PlayModeTestAssembly_ActuallyRunsInPlayMode()
        {
            Assert.IsTrue(
                Application.isPlaying,
                "This assembly is running in EditMode. Its asmdef has almost certainly been given " +
                "\"includePlatforms\": [\"Editor\"] — an editor-only assembly is classified as an " +
                "EditMode test assembly, which silently moves this whole suite out of PlayMode and " +
                "leaves a project-wide PlayMode run discovering nothing.");
        }

        [UnityTest]
        public IEnumerator PlayModeTestAssembly_HasARunningPlayerLoop()
        {
            int frameBefore = Time.frameCount;
            yield return null;
            Assert.Greater(
                Time.frameCount,
                frameBefore,
                "Yielding a frame did not advance Time.frameCount, so no player loop is running. " +
                "PlayMode tests that need a ticking world cannot be trusted in this assembly.");
        }
    }
}
