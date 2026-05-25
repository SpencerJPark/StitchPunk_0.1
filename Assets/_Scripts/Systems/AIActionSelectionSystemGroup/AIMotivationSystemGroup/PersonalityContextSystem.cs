using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// Writes Motivation.contextMultiplier each frame based on per-instance Personality traits.
// Runs after MotivationDecaySystem (which resets contextMultiplier to 1.0) so personality
// overrides are always fresh when AIScoringSystemGroup reads them.
[BurstCompile]
[UpdateInGroup(typeof(AIMotivationSystemGroup))]
[UpdateAfter(typeof(MotivationDecaySystem))]
public partial struct PersonalityContextSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PersonalityContextJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain))]
[WithDisabled(typeof(Dead))]
public partial struct PersonalityContextJob : IJobEntity
{
    public void Execute(
        in Personality personality,
        ref DynamicBuffer<Motivation> motivations)
    {
        for (int i = 0; i < motivations.Length; i++)
        {
            Motivation motivation = motivations[i];
            switch (motivation.motivationType)
            {
                case MotivationType.Social:
                    // socialAffinity [0,1]: 0.5 = neutral (×1.0), 0 = antisocial (×0), 1 = extrovert (×2)
                    motivation.contextMultiplier = personality.socialAffinity * 2f;
                    break;

                case MotivationType.Movement:
                    motivation.contextMultiplier = personality.wanderlust * 2f;
                    break;

                case MotivationType.Hunger:
                    motivation.contextMultiplier = personality.gluttony * 2f;
                    break;

                // All other motivations: contextMultiplier stays 1.0 (set by MotivationDecaySystem)
            }
            motivations[i] = motivation;
        }
    }
}
