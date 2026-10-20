using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class PersonalityRollTests
    {
        private const int TraitCount = 6;
        private const float BaseValue = 0.5f;
        private const float Variance = 0.4f;
        private const float UnrolledSentinel = -1f;

        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("PersonalityRollTests");
            testWorld.SetTime(new TimeData(1.0, 0.016f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void TwoInstancesOfOnePrefabRollDifferentValues()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity first = CreateUnit(true);
            Entity second = CreateUnit(true);

            RunRollSystem();

            bool anyTraitDiffers = false;
            DynamicBuffer<PersonalityAttributes> firstTraits = entityManager.GetBuffer<PersonalityAttributes>(first);
            DynamicBuffer<PersonalityAttributes> secondTraits = entityManager.GetBuffer<PersonalityAttributes>(second);
            for (int traitIndex = 0; traitIndex < TraitCount; traitIndex++)
            {
                Assert.That(firstTraits[traitIndex].value, Is.InRange(0.1f, 0.9f));
                Assert.That(secondTraits[traitIndex].value, Is.InRange(0.1f, 0.9f));
                if (firstTraits[traitIndex].value != secondTraits[traitIndex].value) anyTraitDiffers = true;
            }
            Assert.IsTrue(anyTraitDiffers, "two instances sharing one buffer must not roll identical values");
            Assert.IsFalse(entityManager.IsComponentEnabled<PersonalityRollPending>(first));
            Assert.IsFalse(entityManager.IsComponentEnabled<PersonalityRollPending>(second));
        }

        [Test]
        public void SettledUnitDoesNotRerollEveryFrame()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = CreateUnit(true);
            RunRollSystem();
            float[] rolledValues = ReadValues(unit);

            RunRollSystem();

            float[] valuesAfterSecondUpdate = ReadValues(unit);
            for (int traitIndex = 0; traitIndex < TraitCount; traitIndex++)
                Assert.AreEqual(rolledValues[traitIndex], valuesAfterSecondUpdate[traitIndex]);
            Assert.IsFalse(entityManager.IsComponentEnabled<PersonalityRollPending>(unit));
        }

        // Failed before [WithPresent(typeof(PersonalityRollPending))] was added to the job: pool reuse arrives with the pending flag off.
        [Test]
        public void PoolReuseRerolls()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = CreateUnit(true);
            RunRollSystem();

            // Overwrite with a sentinel (the roll is deterministic per entity+seed, so a reroll can equal the first roll).
            DynamicBuffer<PersonalityAttributes> traits = entityManager.GetBuffer<PersonalityAttributes>(unit);
            for (int traitIndex = 0; traitIndex < TraitCount; traitIndex++)
            {
                PersonalityAttributes attribute = traits[traitIndex];
                attribute.value = UnrolledSentinel;
                traits[traitIndex] = attribute;
            }
            entityManager.SetComponentData(unit, new Intoxication { level = 0.8f });
            entityManager.SetComponentEnabled<Intoxication>(unit, true);
            entityManager.SetComponentEnabled<Conversation>(unit, true);
            entityManager.SetComponentEnabled<NewlySpawned>(unit, true);

            RunRollSystem();

            float[] valuesAfterReuse = ReadValues(unit);
            for (int traitIndex = 0; traitIndex < TraitCount; traitIndex++)
                Assert.That(valuesAfterReuse[traitIndex], Is.InRange(0.1f, 0.9f));
            Assert.IsFalse(entityManager.IsComponentEnabled<Intoxication>(unit));
            Assert.AreEqual(0f, entityManager.GetComponentData<Intoxication>(unit).level);
            Assert.IsFalse(entityManager.IsComponentEnabled<Conversation>(unit));
        }

        [Test]
        public void ViceDecayScalesWithViceSquared()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = CreateUnit(true);

            RunRollSystem();

            float vice = 0f;
            DynamicBuffer<PersonalityAttributes> traits = entityManager.GetBuffer<PersonalityAttributes>(unit);
            for (int traitIndex = 0; traitIndex < traits.Length; traitIndex++)
                if (traits[traitIndex].personality == PersonalityTypes.Partier) vice = traits[traitIndex].value;
            DynamicBuffer<Motivation> motivations = entityManager.GetBuffer<Motivation>(unit);
            Assert.AreEqual(0.6f * vice * vice, motivations[0].decayRate, 1e-4f);
        }

        private void RunRollSystem()
        {
            SystemHandle rollHandle = testWorld.GetOrCreateSystem<PersonalityRollSystem>();
            rollHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private float[] ReadValues(Entity unit)
        {
            DynamicBuffer<PersonalityAttributes> traits = testWorld.EntityManager.GetBuffer<PersonalityAttributes>(unit);
            float[] values = new float[traits.Length];
            for (int traitIndex = 0; traitIndex < traits.Length; traitIndex++)
                values[traitIndex] = traits[traitIndex].value;
            return values;
        }

        private Entity CreateUnit(bool rollPending)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(
                typeof(PersonalityRollPending), typeof(NewlySpawned), typeof(Intoxication), typeof(Conversation), typeof(EmoteState));
            entityManager.SetComponentEnabled<PersonalityRollPending>(unit, rollPending);
            entityManager.SetComponentEnabled<NewlySpawned>(unit, false);
            entityManager.SetComponentEnabled<Intoxication>(unit, false);
            entityManager.SetComponentEnabled<Conversation>(unit, false);

            DynamicBuffer<PersonalityAttributes> traits = entityManager.AddBuffer<PersonalityAttributes>(unit);
            PersonalityTypes[] traitTypes =
            {
                PersonalityTypes.Bravery, PersonalityTypes.Romance, PersonalityTypes.Comedy,
                PersonalityTypes.Friendly, PersonalityTypes.Anger, PersonalityTypes.Partier,
            };
            for (int traitIndex = 0; traitIndex < TraitCount; traitIndex++)
                traits.Add(new PersonalityAttributes
                {
                    personality = traitTypes[traitIndex],
                    value = BaseValue,
                    baseValue = BaseValue,
                    variance = Variance,
                });

            DynamicBuffer<Motivation> motivations = entityManager.AddBuffer<Motivation>(unit);
            motivations.Add(new Motivation { needType = NeedType.Vice, value = 0f, decayRate = 0f });
            return unit;
        }
    }
}
