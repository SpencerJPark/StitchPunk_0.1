// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// One fixture per rule of the <see cref="ActorProfileValidation"/> table (P1-P7), following
    /// <see cref="RagdollAuthoringTests"/>'s discipline: each starts from a profile that validates
    /// clean and breaks exactly one thing.
    /// </summary>
    public sealed class ActorProfileValidationTests
    {
        private const ulong RigKey = 0x6000UL;
        private const ulong SetKey = 0x6100UL;
        private const ulong IdleClipId = 0x6200UL;
        private const ulong OtherClipId = 0x6300UL;
        private const uint IdleAnimationKey = 1u;
        private const uint OtherAnimationKey = 2u;

        private AuthoringTestAssets assets;

        [SetUp]
        public void SetUp()
        {
            assets = new AuthoringTestAssets();
        }

        [TearDown]
        public void TearDown()
        {
            assets.DestroyAll();
        }

        // -----------------------------------------------------------------------------------
        // Baseline: a profile with one registered, registry-named, non-directional animation.
        // -----------------------------------------------------------------------------------

        private RigAsset CreateValidRig()
        {
            return assets.CreateRig("Rig", RigKey, new uint[0]);
        }

        private ClipAsset CreateIdleClip()
        {
            return assets.CreateClip("Idle", IdleClipId, 1f);
        }

        private ActorAnimationDefinition CreateValidAnimation(uint animationKey, ClipAsset clip)
        {
            return new ActorAnimationDefinition
            {
                animationKey = animationKey,
                hasDirections = false,
                clip = clip
            };
        }

        private ActorProfileAsset CreateProfile(RigAsset rig, ClipSetAsset clipSet)
        {
            ActorProfileAsset profile = assets.Create<ActorProfileAsset>("Profile");
            profile.rig = rig;
            profile.clipSets = new List<ClipSetAsset> { clipSet };
            return profile;
        }

        [Test]
        public void AWellFormedProfileHasNoFindings()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);
            profile.layers[0].animations.Add(CreateValidAnimation(IdleAnimationKey, idleClip));

            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();
            animationNames.Add(IdleAnimationKey, "Idle");

            AssertNoFindings(
                ActorProfileValidation.Validate(profile, animationNames),
                "A profile with valid bookends, a registered/registry-named animation naming a " +
                "clip that is in its own clip set, must validate clean.");
        }

        // -----------------------------------------------------------------------------------
        // P1: 2 <= layers.Count <= 8, and [0]/[^1] must be the Base/Override bookends.
        // -----------------------------------------------------------------------------------

        [Test]
        public void P1_FiresWhenTheFirstAndLastLayersAreNotTheBookends()
        {
            ActorProfileAsset profile = assets.Create<ActorProfileAsset>("Profile");
            profile.layers = new List<ActorLayerDefinition>
            {
                new ActorLayerDefinition { displayName = "Idle" },
                new ActorLayerDefinition { displayName = "Combat" }
            };

            AssertOnlyCode(
                ActorProfileValidation.Validate(profile, null), ValidationCode.P1, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // P2: animationKey must be non-zero and known to the animation name registry.
        // -----------------------------------------------------------------------------------

        [Test]
        public void P2_FiresWhenAnimationKeyIsNotInTheRegistry()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);
            profile.layers[0].animations.Add(CreateValidAnimation(IdleAnimationKey, idleClip));

            // Empty: IdleAnimationKey is not defined in it.
            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();

            AssertOnlyCode(
                ActorProfileValidation.Validate(profile, animationNames), ValidationCode.P2, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // P3: no animationKey may appear twice in one profile, across layers.
        // -----------------------------------------------------------------------------------

        [Test]
        public void P3_FiresWhenTwoEntriesOnDifferentLayersShareAnAnimationKey()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);
            profile.layers[0].animations.Add(CreateValidAnimation(IdleAnimationKey, idleClip));
            profile.layers[profile.layers.Count - 1].animations.Add(CreateValidAnimation(IdleAnimationKey, idleClip));

            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();
            animationNames.Add(IdleAnimationKey, "Idle");

            AssertOnlyCode(
                ActorProfileValidation.Validate(profile, animationNames), ValidationCode.P3, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // P4: a non-directional entry needs a clip; a directional entry needs a valid fill pattern.
        // -----------------------------------------------------------------------------------

        [Test]
        public void P4_FiresWhenADirectionalEntrysFilledSlotsFormNoValidCoveragePattern()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);

            ActorAnimationDefinition directionalAnimation = new ActorAnimationDefinition
            {
                animationKey = IdleAnimationKey,
                hasDirections = true,
                directionSlots = new DirectionSlots { north = idleClip }
            };
            profile.layers[0].animations.Add(directionalAnimation);

            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();
            animationNames.Add(IdleAnimationKey, "Idle");

            AssertOnlyCode(
                ActorProfileValidation.Validate(profile, animationNames), ValidationCode.P4, ValidationSeverity.Error);
        }

        // -----------------------------------------------------------------------------------
        // P5: a named clip must be in one of the profile's own clip sets.
        // -----------------------------------------------------------------------------------


        [Test]
        public void P4_AllowsATriggerOnlyEntryThatNamesNoClip()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);
            profile.layers[0].animations.Add(CreateValidAnimation(IdleAnimationKey, idleClip));
            ActorAnimationDefinition deathEntry = new ActorAnimationDefinition
            {
                animationKey = OtherAnimationKey,
                hasDirections = false,
                clip = null,
                ragdollTrigger = RagdollTrigger.Start
            };
            profile.layers[0].animations.Add(deathEntry);

            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();
            animationNames.Add(IdleAnimationKey, "Idle");
            animationNames.Add(OtherAnimationKey, "Death");

            List<ValidationMessage> messages = ActorProfileValidation.Validate(profile, animationNames);
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                Assert.AreNotEqual(ValidationCode.P4, messages[messageIndex].code,
                    "A ragdoll Start/Stop entry with no clip is a request, not a missing clip.");
            }
        }

        [Test]
        public void P5_FiresWhenTheNamedClipIsNotInAnyOfTheProfilesClipSets()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipAsset unregisteredClip = assets.CreateClip("Unregistered", OtherClipId, 1f);
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);
            profile.layers[0].animations.Add(CreateValidAnimation(IdleAnimationKey, unregisteredClip));

            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();
            animationNames.Add(IdleAnimationKey, "Idle");

            AssertOnlyCode(
                ActorProfileValidation.Validate(profile, animationNames), ValidationCode.P5, ValidationSeverity.Warning);
        }

        // -----------------------------------------------------------------------------------
        // P6: a ragdoll trigger on a rig with no ragdoll bodies.
        // -----------------------------------------------------------------------------------

        [Test]
        public void P6_FiresWhenARagdollTriggerIsSetOnARigWithNoRagdollBodies()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);
            ActorAnimationDefinition animation = CreateValidAnimation(IdleAnimationKey, idleClip);
            animation.ragdollTrigger = RagdollTrigger.Start;
            profile.layers[0].animations.Add(animation);

            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();
            animationNames.Add(IdleAnimationKey, "Idle");

            AssertOnlyCode(
                ActorProfileValidation.Validate(profile, animationNames), ValidationCode.P6, ValidationSeverity.Warning);
        }

        // -----------------------------------------------------------------------------------
        // P7: startingAnimationKey must name an entry on its own layer.
        // -----------------------------------------------------------------------------------

        [Test]
        public void P7_FiresWhenAStartingAnimationKeyNamesAnEntryOnAnotherLayer()
        {
            RigAsset rig = CreateValidRig();
            ClipAsset idleClip = CreateIdleClip();
            ClipSetAsset clipSet = assets.CreateSet("Set", rig, SetKey, idleClip);
            ActorProfileAsset profile = CreateProfile(rig, clipSet);
            profile.layers[0].animations.Add(CreateValidAnimation(IdleAnimationKey, idleClip));

            ActorLayerDefinition middleLayer = new ActorLayerDefinition
            {
                displayName = "Combat",
                startingAnimationKey = IdleAnimationKey
            };
            profile.layers.Insert(1, middleLayer);

            FakeAnimationNameRegistry animationNames = new FakeAnimationNameRegistry();
            animationNames.Add(IdleAnimationKey, "Idle");

            AssertOnlyCode(
                ActorProfileValidation.Validate(profile, animationNames), ValidationCode.P7, ValidationSeverity.Warning);
        }

        // -----------------------------------------------------------------------------------
        // A minimal in-memory IVocabularyRegistry, so this fixture never depends on the real
        // AnimationNameRegistry asset or ProjectSettings.
        // -----------------------------------------------------------------------------------

        private sealed class FakeAnimationNameRegistry : IVocabularyRegistry
        {
            private readonly List<uint> ids = new List<uint>();
            private readonly List<string> names = new List<string>();

            internal void Add(uint id, string name)
            {
                ids.Add(id);
                names.Add(name);
            }

            public int VocabularyEntryCount
            {
                get { return ids.Count; }
            }

            public string VocabularyEntryName(int entryIndex)
            {
                return names[entryIndex];
            }

            public uint VocabularyEntryId(int entryIndex)
            {
                return ids[entryIndex];
            }

            public string FindName(uint id)
            {
                int index = ids.IndexOf(id);
                return index >= 0 ? names[index] : null;
            }

            public bool ContainsId(uint id)
            {
                return ids.Contains(id);
            }

            public uint CreateVocabularyEntry(string name)
            {
                uint newId = (uint)(ids.Count + 1);
                Add(newId, name);
                return newId;
            }

            public string GeneratedConstantsPath { get; set; }
        }

        // -----------------------------------------------------------------------------------
        // Assertion helpers. Copies of RagdollAuthoringTests', so this fixture stands alone.
        // -----------------------------------------------------------------------------------

        private static void AssertNoFindings(IReadOnlyList<ValidationMessage> messages, string because)
        {
            Assert.AreEqual(0, messages.Count, because + " Got: " + Describe(messages));
        }

        private static void AssertOnlyCode(
            IReadOnlyList<ValidationMessage> messages,
            ValidationCode expectedCode,
            ValidationSeverity expectedSeverity)
        {
            AssertContainsCode(messages, expectedCode, expectedSeverity);
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                Assert.AreEqual(
                    expectedCode,
                    messages[messageIndex].code,
                    "The fixture breaks exactly one rule, so no other code may fire: " + Describe(messages));
            }
        }

        private static void AssertContainsCode(
            IReadOnlyList<ValidationMessage> messages,
            ValidationCode expectedCode,
            ValidationSeverity expectedSeverity)
        {
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                if (messages[messageIndex].code == expectedCode && messages[messageIndex].severity == expectedSeverity)
                {
                    Assert.IsNotNull(messages[messageIndex].text, "Every finding must carry an explanation.");
                    return;
                }
            }
            Assert.Fail("Expected " + expectedCode + " at severity " + expectedSeverity + " but got: " + Describe(messages));
        }

        private static string Describe(IReadOnlyList<ValidationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return "(no findings)";
            }
            StringBuilder description = new StringBuilder();
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                description.Append(messages[messageIndex].ToString());
                description.Append("; ");
            }
            return description.ToString();
        }
    }
}
