using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Each side of a pair runs this and computes the same beat, mood and speaker from the shared start time and seed,
// so no unit ever writes its partner's EmoteState (LH-D11): parallel-safe, and nothing to sync.
[BurstCompile]
[UpdateInGroup(typeof(CrowdLifeSystemGroup))]
public partial struct ConversationSystem : ISystem
{
    private ComponentLookup<StateMachine> _stateMachineLookup;
    private BufferLookup<PersonalityAttributes> _personalityAttributesLookup;
    private ComponentLookup<Intoxication> _intoxicationLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _stateMachineLookup = state.GetComponentLookup<StateMachine>(true);
        _personalityAttributesLookup = state.GetBufferLookup<PersonalityAttributes>(true);
        _intoxicationLookup = state.GetComponentLookup<Intoxication>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _stateMachineLookup.Update(ref state);
        _personalityAttributesLookup.Update(ref state);
        _intoxicationLookup.Update(ref state);

        float beatIntervalScale = 1f;
        if (SystemAPI.TryGetSingleton<CrowdLifeLod>(out CrowdLifeLod crowdLifeLod) && crowdLifeLod.beatIntervalScale > 0f)
            beatIntervalScale = crowdLifeLod.beatIntervalScale;

        state.Dependency = new ConversationJob
        {
            stateMachineLookup = _stateMachineLookup,
            personalityAttributesLookup = _personalityAttributesLookup,
            intoxicationLookup = _intoxicationLookup,
            elapsedTime = SystemAPI.Time.ElapsedTime,
            beatIntervalScale = beatIntervalScale
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(Conversation), typeof(ActionInterruptRequest))]
public partial struct ConversationJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<StateMachine> stateMachineLookup;
    [ReadOnly] public BufferLookup<PersonalityAttributes> personalityAttributesLookup;
    [ReadOnly] public ComponentLookup<Intoxication> intoxicationLookup;
    public double elapsedTime;
    public float beatIntervalScale;

    public void Execute(
        Entity self,
        in StateMachine stateMachine,
        in DynamicBuffer<PersonalityAttributes> traits,
        ref Conversation conversation,
        EnabledRefRW<Conversation> conversationEnabled,
        ref EmoteState emoteState,
        EnabledRefRW<ActionInterruptRequest> actionInterruptRequestEnabled)
    {
        Entity partner = stateMachine.targetEntity;
        bool mutual = stateMachine.action == ActionType.Talk
            && partner != Entity.Null
            && stateMachineLookup.TryGetComponent(partner, out StateMachine partnerStateMachine)
            && partnerStateMachine.action == ActionType.Talk
            && partnerStateMachine.targetEntity == self
            && personalityAttributesLookup.HasBuffer(partner);

        if (!mutual)
        {
            if (conversationEnabled.ValueRO)
            {
                conversationEnabled.ValueRW = false;
                conversation = default;
            }
            return;
        }

        bool selfIsLower = self.Index < partner.Index;

        if (!conversationEnabled.ValueRO || conversation.partner != partner)
        {
            Entity lowerEntity = selfIsLower ? self : partner;
            Entity higherEntity = selfIsLower ? partner : self;
            conversation = new Conversation
            {
                partner = partner,
                startTime = elapsedTime,
                pairSeed = math.hash(new uint3((uint)lowerEntity.Index, (uint)higherEntity.Index, (uint)(elapsedTime * 10.0))),
                lastBeatHandled = -1
            };
            conversationEnabled.ValueRW = true;
        }

        int beatIndex = ConversationEmoteRules.BeatIndexAt(elapsedTime - conversation.startTime, conversation.pairSeed, beatIntervalScale);
        if (beatIndex <= conversation.lastBeatHandled)
            return;
        conversation.lastBeatHandled = beatIndex;

        DynamicBuffer<PersonalityAttributes> partnerTraitBuffer = personalityAttributesLookup[partner];
        ConversationTraits traitsSelf = PersonalityTraitLookup.ConversationTraitsOf(in traits);
        ConversationTraits traitsPartner = PersonalityTraitLookup.ConversationTraitsOf(in partnerTraitBuffer);
        ConversationTraits lowerTraits = selfIsLower ? traitsSelf : traitsPartner;
        ConversationTraits higherTraits = selfIsLower ? traitsPartner : traitsSelf;

        float pairMood = ConversationEmoteRules.PairMood(in lowerTraits, in higherTraits);
        float mood = ConversationEmoteRules.BeatMood(pairMood, conversation.pairSeed, beatIndex);
        uint beatHash = ConversationEmoteRules.Hash(conversation.pairSeed, beatIndex);

        if (beatIndex >= 2 && ConversationEmoteRules.IsArgument(mood, in lowerTraits, in higherTraits))
        {
            EmoteRules.TryShow(ref emoteState, EmoteIcon.Angry, 2.5f, EmoteRules.ArgumentPriority);
            actionInterruptRequestEnabled.ValueRW = true;
            conversationEnabled.ValueRW = false;
            return;
        }

        if (ConversationEmoteRules.IsSpeaker(selfIsLower, beatIndex))
        {
            float ownIntoxication = 0f;
            if (intoxicationLookup.TryGetComponent(self, out Intoxication ownIntoxicationComponent)
                && intoxicationLookup.IsComponentEnabled(self))
                ownIntoxication = ownIntoxicationComponent.level;

            EmoteIcon emote = ConversationEmoteRules.PickEmote(mood, in traitsSelf, ownIntoxication, beatHash);
            EmoteRules.TryShow(ref emoteState, emote, 2.0f, EmoteRules.ConversationPriority);
        }
        else
        {
            EmoteIcon reaction = ConversationEmoteRules.PickReaction(mood, in traitsSelf, beatHash ^ 0x9E3779B9u);
            if (reaction != EmoteIcon.None)
                EmoteRules.TryShow(ref emoteState, reaction, 1.4f, EmoteRules.ConversationPriority);
        }
    }
}
