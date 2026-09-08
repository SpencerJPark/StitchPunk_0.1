using DotsAnimationToolkit;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    // Pins the P3 cutover (ActorProfileCutover_System.md §5): the assignment job now only ever
    // issues PlayAnimation(idle/walk) on change, driven purely by PlaybackApi.IsAnimationPlaying —
    // the old Action branch, IsLayerActive/IsIdleAction/GetBaseAnimation/GetAnimationForAction and
    // the UnitFacing parameter are gone.
    public sealed class UnitAnimationAssignmentSystemTests
    {
        private const uint IdleKey = 42;
        private const uint WalkKey = 43;

        private World testWorld;
        private BlobAssetReference<UnitLibraryBlob> unitLibraryBlob;
        private Entity unit;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("UnitAnimationAssignmentSystemTests");
            EntityManager entityManager = testWorld.EntityManager;

            unitLibraryBlob = BuildOneUnitLibrary();

            Entity configEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(configEntity, new UnitDataLibrary { library = unitLibraryBlob });

            unit = entityManager.CreateEntity();
            entityManager.AddComponentData(unit, new UnitData { unitType = UnitType.None });
            entityManager.AddComponentData(unit, new Movement { isMoving = false });
            entityManager.AddComponentData(unit, new LocomotionStance { stance = StanceType.Normal });
            entityManager.AddBuffer<AnimationCommand>(unit);
            entityManager.AddComponentData(unit, new AnimationCommandPending());
            entityManager.SetComponentEnabled<AnimationCommandPending>(unit, false);
            entityManager.AddBuffer<PlaybackLayer>(unit);
            entityManager.AddComponentData(unit, new CutsceneActor());
            entityManager.SetComponentEnabled<CutsceneActor>(unit, false);
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
        public void StandingUnit_WithInactiveBaseLayer_GetsExactlyOnePlayAnimationCommand_WithIdleKey()
        {
            EntityManager entityManager = testWorld.EntityManager;
            DynamicBuffer<PlaybackLayer> layers = entityManager.GetBuffer<PlaybackLayer>(unit);
            layers.Add(new PlaybackLayer { flags = PlaybackFlags.None, animationKey = 0 });

            RunAssignmentSystem();

            DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(unit);
            Assert.AreEqual(1, commands.Length,
                "An inactive Base layer must get exactly one command — not zero, not re-issued extras.");
            Assert.AreEqual(CommandKind.PlayAnimation, commands[0].kind);
            Assert.AreEqual(IdleKey, commands[0].animationKey);
        }

        [Test]
        public void StandingUnit_AlreadyPlayingIdleKey_GetsNoCommand()
        {
            EntityManager entityManager = testWorld.EntityManager;
            DynamicBuffer<PlaybackLayer> layers = entityManager.GetBuffer<PlaybackLayer>(unit);
            layers.Add(new PlaybackLayer { flags = PlaybackFlags.Active, animationKey = IdleKey });

            RunAssignmentSystem();

            DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(unit);
            Assert.AreEqual(0, commands.Length,
                "A layer already playing the idle key must not be re-issued a Play every frame.");
        }

        [Test]
        public void CutsceneActor_Enabled_IssuesNoCommand()
        {
            EntityManager entityManager = testWorld.EntityManager;
            DynamicBuffer<PlaybackLayer> layers = entityManager.GetBuffer<PlaybackLayer>(unit);
            layers.Add(new PlaybackLayer { flags = PlaybackFlags.None, animationKey = 0 });
            entityManager.SetComponentEnabled<CutsceneActor>(unit, true);

            RunAssignmentSystem();

            DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(unit);
            Assert.AreEqual(0, commands.Length,
                "A unit puppeted by a cutscene must get zero commands from the game's own assignment job — the cutscene's own locomotion is Base's writer while CutsceneActor is enabled.");
        }

        private void RunAssignmentSystem()
        {
            SystemHandle assignmentSystem = testWorld.GetOrCreateSystem<UnitAnimationAssignmentSystem>();
            assignmentSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        // One unit (index 0, UnitType.None) with idle/walk keys and empty action/stance arrays —
        // GetLocomotionKeys falls through to the bare idle/walk pair with no stance entries to match.
        private static BlobAssetReference<UnitLibraryBlob> BuildOneUnitLibrary()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref UnitLibraryBlob root = ref builder.ConstructRoot<UnitLibraryBlob>();
            BlobBuilderArray<UnitDataBlob> unitsBuilder = builder.Allocate(ref root.units, 1);

            unitsBuilder[0].unitType         = UnitType.None;
            unitsBuilder[0].idleAnimationKey = IdleKey;
            unitsBuilder[0].walkAnimationKey = WalkKey;
            builder.Allocate(ref unitsBuilder[0].motivation, 0);
            builder.Allocate(ref unitsBuilder[0].randomMotivations, 0);
            builder.Allocate(ref unitsBuilder[0].attackFactions, 0);
            builder.Allocate(ref unitsBuilder[0].socialFactions, 0);
            builder.Allocate(ref unitsBuilder[0].attacks, 0);
            builder.Allocate(ref unitsBuilder[0].actionAnimationKeys, 0);
            builder.Allocate(ref unitsBuilder[0].stanceAnimationKeys, 0);

            return builder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
        }
    }
}
