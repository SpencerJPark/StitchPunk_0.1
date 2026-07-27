// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;

namespace StitchPunk.AnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Smoke coverage proving the PlayMode test assembly compiles, loads, and runs under its
    /// contracted name (architecture section 1.3). Build step C0 ships the test fixtures empty
    /// of feature coverage; the World/system integration tests specified in architecture
    /// section 11.2 land with their modules per the section 9 build plan.
    /// </summary>
    public sealed class PlayModeAssemblySmokeTest
    {
        [Test]
        public void PlayModeTestAssembly_HasContractedName()
        {
            string assemblyName = typeof(PlayModeAssemblySmokeTest).Assembly.GetName().Name;
            Assert.AreEqual("StitchPunk.AnimationToolkit.Tests.PlayMode", assemblyName);
        }
    }
}
