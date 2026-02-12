using Unity.Burst;
using Unity.Entities;

// if there is no waypoints to satisfy a motivation, the motivation stops decreasing since it can't be filled anyway

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
partial struct BathroomScoringSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BathroomInteraction>();
        // sets up component lookup
    }
    

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // updates look ups outside job
        
        // queeries those with needs action
        
        // adjusts motivation value
        //query motivation, action, 
        
        // queries for options
        
        // adds universal dynamic buffer for ActionOption with score
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}

// score = motivations added together with multiplyier