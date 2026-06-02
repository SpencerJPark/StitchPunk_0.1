using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;


public struct StateMachine : IComponentData
{
    public ActionType    action;
    public BehaviorType  activeBehavior;   // written by WinnerSelectionSystem; drives BehaviorExecutionSystem blob lookup
    public Entity        targetEntity;
    public BehaviorPhase currentPhase;
    public int           CurrentCommandIndex;
    public float         CommandTimer;
    public StanceType    currentStance;    // written by Approach command; read by locomotion system
}



// A single execution instruction
