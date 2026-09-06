using DotsAnimationToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    // G2-P2: the Dialogue cue round trip — an event on the request entity opens the game's own
    // dialogue, and closing that dialogue hands the cutscene clock back.
    public sealed class CutsceneDialogueCueTests
    {
        private const int TestSequenceId = 7;
        private const string DialogueHoldId = "Dialogue";

        private World testWorld;
        private BlobAssetReference<CutsceneBlob> cutsceneBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneDialogueCueTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            if (cutsceneBlob.IsCreated) cutsceneBlob.Dispose();
        }

        [Test]
        public void DialogueCue_StartsActiveDialogue_AndReleasesTheHoldWhenItEnds()
        {
            EntityManager entityManager = testWorld.EntityManager;

            Entity dialogueManagerEntity = entityManager.CreateEntity();
            entityManager.AddComponent<DialogueManagerTag>(dialogueManagerEntity);
            entityManager.AddComponent<ActiveDialogue>(dialogueManagerEntity);
            entityManager.SetComponentEnabled<ActiveDialogue>(dialogueManagerEntity, false);

            Entity speakerEntity = entityManager.CreateEntity();

            cutsceneBlob = BuildTwoSlotCutsceneHeldOnDialogue();
            Entity playRequestEntity = CreatePausedPlayRequest(entityManager, cutsceneBlob, speakerEntity);

            Entity narrativeEntity = entityManager.CreateEntity();
            entityManager.AddComponent<NarrativeEventTag>(narrativeEntity);
            entityManager.AddComponentData(narrativeEntity, new ActiveCutscene { playRequest = playRequestEntity });
            entityManager.SetComponentEnabled<ActiveCutscene>(narrativeEntity, true);

            RunSystem<CutsceneDialogueCueSystem>();

            Assert.IsTrue(entityManager.IsComponentEnabled<ActiveDialogue>(dialogueManagerEntity),
                "A Dialogue cue must open the game's own dialogue, the same write a DialogueTriggerAction makes.");
            ActiveDialogue activeDialogue = entityManager.GetComponentData<ActiveDialogue>(dialogueManagerEntity);
            Assert.AreEqual(TestSequenceId, activeDialogue.sequenceId, "intParam carries the sequence id.");
            Assert.AreEqual(speakerEntity, activeDialogue.speakerEntity,
                "floatParam carries a slot INDEX, resolved through the blob's slot order to a binding.");
            Assert.IsFalse(entityManager.IsComponentEnabled<CutsceneHoldRelease>(playRequestEntity),
                "The clock stays held while the dialogue is on screen.");

            // What the frame after the cue really looks like: the toolkit has dropped the event, and
            // the UI manager has closed the dialogue.
            entityManager.GetBuffer<AnimEventOutput>(playRequestEntity).Clear();
            entityManager.SetComponentEnabled<AnimEventsPending>(playRequestEntity, false);
            entityManager.SetComponentEnabled<ActiveDialogue>(dialogueManagerEntity, false);

            RunSystem<CutsceneDialogueCueSystem>();

            Assert.IsTrue(entityManager.IsComponentEnabled<CutsceneHoldRelease>(playRequestEntity),
                "Closing the dialogue must release the hold the cue started.");
            Assert.AreEqual(new FixedString64Bytes(DialogueHoldId),
                entityManager.GetComponentData<CutsceneHoldRelease>(playRequestEntity).holdId,
                "A holding event's id is the event's own registry name.");
        }

        // Slot 1 is the speaker, so a fixture that resolved by binding-buffer position rather than by
        // slot id would still have to get the index right.
        private static Entity CreatePausedPlayRequest(
            EntityManager entityManager, BlobAssetReference<CutsceneBlob> blob, Entity speakerEntity)
        {
            Entity playRequestEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(playRequestEntity, new CutscenePlay { blob = blob, layerIndex = 0 });
            entityManager.AddComponentData(playRequestEntity, new CutscenePlaybackState
            {
                segmentIndex   = 0,
                isPausedOnHold = true,
            });

            DynamicBuffer<CutsceneActorBinding> bindings =
                entityManager.AddBuffer<CutsceneActorBinding>(playRequestEntity);
            bindings.Add(new CutsceneActorBinding { slotId = 100u, actorEntity = entityManager.CreateEntity() });
            bindings.Add(new CutsceneActorBinding { slotId = 200u, actorEntity = speakerEntity });

            DynamicBuffer<AnimEventOutput> cutsceneEvents =
                entityManager.AddBuffer<AnimEventOutput>(playRequestEntity);
            cutsceneEvents.Add(new AnimEventOutput
            {
                eventKey   = AnimEvents.Dialogue,
                intParam   = TestSequenceId,
                floatParam = 1f,
            });
            entityManager.AddComponent<AnimEventsPending>(playRequestEntity);
            entityManager.SetComponentEnabled<AnimEventsPending>(playRequestEntity, true);

            entityManager.AddComponent<CutsceneHoldRelease>(playRequestEntity);
            entityManager.SetComponentEnabled<CutsceneHoldRelease>(playRequestEntity, false);

            return playRequestEntity;
        }

        private static BlobAssetReference<CutsceneBlob> BuildTwoSlotCutsceneHeldOnDialogue()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 5;
                root.cutsceneKey   = 4242UL;

                BlobBuilderArray<CutsceneSlotMetaBlob> slots = builder.Allocate(ref root.slots, 2);
                slots[0] = new CutsceneSlotMetaBlob { slotId = 100u, kind = CutsceneSlotKind.Actor };
                slots[1] = new CutsceneSlotMetaBlob { slotId = 200u, kind = CutsceneSlotKind.Actor };

                BlobBuilderArray<CutsceneSegmentBlob> segments = builder.Allocate(ref root.segments, 1);
                ref CutsceneSegmentBlob segment = ref segments[0];
                segment.duration = 1f;
                segment.holdId   = new FixedString64Bytes(DialogueHoldId);
                builder.Allocate(ref segment.slotTracks, 0);
                builder.Allocate(ref segment.cameraKeys, 0);
                builder.Allocate(ref segment.cameraCutTimes, 0);
                builder.Allocate(ref segment.events, 0);

                return builder.CreateBlobAssetReference<CutsceneBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<TSystem>();
            systemHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
