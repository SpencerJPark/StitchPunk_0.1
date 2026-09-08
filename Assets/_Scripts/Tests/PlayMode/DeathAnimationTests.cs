using DotsAnimationToolkit;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // G5 follow-up: pins that DeathSystem/DeathJob issues the profile's Death body animation and
    // (when authored) its DeathFace clip on the first-death frame, through the same
    // BufferLookup<AnimationCommand>/ComponentLookup<AnimationCommandPending> pattern the
    // BehaviorCommands job context uses.
    public sealed class DeathAnimationTests
    {
        private const uint DeathAnimationKey = 77;
        private const uint DeathFaceAnimationKey = 78;

        private World testWorld;
        private BlobAssetReference<UnitLibraryBlob> unitLibraryBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("DeathAnimationTests");
            // DeathSystem logs through an EndSimulation ECB when no LoggingConfig singleton says otherwise.
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            if (unitLibraryBlob.IsCreated) unitLibraryBlob.Dispose();
        }

        [Test]
        public void DeathSystem_FirstDeathFrame_IssuesDeathAndDeathFacePlayAnimationCommands()
        {
            EntityManager entityManager = testWorld.EntityManager;
            unitLibraryBlob = BuildOneUnitLibrary();

            Entity configEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(configEntity, new GameSceneTag());
            entityManager.AddComponentData(configEntity, new UnitDataLibrary { library = unitLibraryBlob });
            // Logging off: the logging branch builds an EndSimulation ECB, which a bare test World has no
            // business exercising here.
            entityManager.AddComponentData(configEntity, new LoggingConfig { EnabledCategories = 0 });

            Entity unit = entityManager.CreateEntity();
            entityManager.AddComponentData(unit, new Dead());
            entityManager.SetComponentEnabled<Dead>(unit, true);
            entityManager.AddComponentData(unit, new Health { healthAmount = 0, healthAmountMax = 10 });
            entityManager.AddComponentData(unit, LocalTransform.Identity);
            entityManager.AddComponentData(unit, new UnitData { unitType = UnitType.None });
            entityManager.AddComponentData(unit, new UnitAction { current = ActionType.Idle });
            entityManager.AddComponentData(unit, new Movement());
            entityManager.AddComponentData(unit, new Gravity());
            entityManager.AddComponentData(unit, new HordeMembership());
            entityManager.AddComponentData(unit, new PathRequest());
            entityManager.AddComponentData(unit, new DStarLiteFollower());
            entityManager.AddComponentData(unit, new FlowFieldFollower());
            entityManager.AddBuffer<AnimationCommand>(unit);
            entityManager.AddComponentData(unit, new AnimationCommandPending());
            entityManager.SetComponentEnabled<AnimationCommandPending>(unit, false);

            SystemHandle deathSystem = testWorld.GetOrCreateSystem<DeathSystem>();
            deathSystem.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(unit);
            Assert.AreEqual(2, commands.Length,
                "Death must issue exactly the Death body clip and the DeathFace clip — no more, no less.");
            Assert.AreEqual(CommandKind.PlayAnimation, commands[0].kind);
            Assert.AreEqual(DeathAnimationKey, commands[0].animationKey);
            Assert.AreEqual(CommandKind.PlayAnimation, commands[1].kind);
            Assert.AreEqual(DeathFaceAnimationKey, commands[1].animationKey);
        }

        // One unit (UnitType.None) with a Death body key and a Death face key; every other blob
        // array empty — DeathJob only ever needs the Death row of each.
        private static BlobAssetReference<UnitLibraryBlob> BuildOneUnitLibrary()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref UnitLibraryBlob root = ref builder.ConstructRoot<UnitLibraryBlob>();
            BlobBuilderArray<UnitDataBlob> unitsBuilder = builder.Allocate(ref root.units, 1);

            unitsBuilder[0].unitType = UnitType.None;
            builder.Allocate(ref unitsBuilder[0].motivation, 0);
            builder.Allocate(ref unitsBuilder[0].randomMotivations, 0);
            builder.Allocate(ref unitsBuilder[0].attackFactions, 0);
            builder.Allocate(ref unitsBuilder[0].socialFactions, 0);
            builder.Allocate(ref unitsBuilder[0].attacks, 0);

            BlobBuilderArray<ActionAnimationKeyBlob> actionKeysArray =
                builder.Allocate(ref unitsBuilder[0].actionAnimationKeys, 1);
            actionKeysArray[0] = new ActionAnimationKeyBlob { action = ActionType.Death, animationKey = DeathAnimationKey };

            BlobBuilderArray<ActionAnimationKeyBlob> faceKeysArray =
                builder.Allocate(ref unitsBuilder[0].faceAnimationKeys, 1);
            faceKeysArray[0] = new ActionAnimationKeyBlob { action = ActionType.Death, animationKey = DeathFaceAnimationKey };

            builder.Allocate(ref unitsBuilder[0].stanceAnimationKeys, 0);

            return builder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
        }
    }
}
