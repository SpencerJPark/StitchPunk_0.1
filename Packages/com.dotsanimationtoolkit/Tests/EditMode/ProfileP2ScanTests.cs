// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Confirms <see cref="ProfileP2Scan"/> filters a profile's validation messages down to P2
    /// only, and that an unsupplied registry falls back to the project one.
    /// </summary>
    public sealed class ProfileP2ScanTests
    {
        private const uint UnknownAnimationKey = 7u;
        private const uint KeyAbsentFromProjectRegistry = 0xDEADBEEFu;

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

        [Test]
        public void ScanProfile_ReportsOnlyP2()
        {
            ActorProfileAsset profile = assets.Create<ActorProfileAsset>("Profile");
            profile.layers = new List<ActorLayerDefinition>
            {
                new ActorLayerDefinition
                {
                    displayName = ActorProfileAsset.BaseLayerName,
                    animations = new List<ActorAnimationDefinition>
                    {
                        new ActorAnimationDefinition
                        {
                            animationKey = UnknownAnimationKey,
                            hasDirections = false,
                            clip = null
                        }
                    }
                }
            };

            FakeAnimationNameRegistry fakeRegistry = new FakeAnimationNameRegistry();

            List<ValidationMessage> allMessages = ActorProfileValidation.Validate(profile, fakeRegistry);
            Assert.Greater(allMessages.Count, 1, "the profile must also break a non-P2 rule, or the filter is untested");

            List<ValidationMessage> scanned = ProfileP2Scan.ScanProfile(profile, fakeRegistry);
            Assert.AreEqual(1, scanned.Count);
            Assert.AreEqual(ValidationCode.P2, scanned[0].code);
        }

        [Test]
        public void ScanProfile_WithNoRegistrySuppliedChecksTheProjectRegistry()
        {
            Assume.That(VocabularyRegistryProvider.AnimationNames.ContainsId(KeyAbsentFromProjectRegistry), Is.False);

            ActorProfileAsset profile = assets.CreateProfile(null, null, 2);
            profile.layers[0].animations.Add(new ActorAnimationDefinition
            {
                animationKey = KeyAbsentFromProjectRegistry,
                hasDirections = false,
                clip = null
            });

            List<ValidationMessage> scanned = ProfileP2Scan.ScanProfile(profile);
            Assert.AreEqual(1, scanned.Count, "a non-zero key can only produce P2 through the registry check, so a null registry must resolve to the project one");
            Assert.AreEqual(ValidationCode.P2, scanned[0].code);
        }

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
    }
}
