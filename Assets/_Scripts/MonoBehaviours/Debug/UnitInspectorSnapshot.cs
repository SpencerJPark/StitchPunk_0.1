#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

public enum UnitInspectorNavState { None, Idle, WaitingForPath, Walking, Stuck, Abstract }

public sealed class UnitInspectorOptionRow
{
    public ActionType action;
    public int priority;
    public float totalUtility;
    public bool isPlayerOrdered;
    public bool isWinner;
    public string targetLabel;
}

public sealed class UnitInspectorMotivationRow
{
    public NeedType need;
    public float value;
    public float decayRate;
}

public sealed class UnitInspectorPersonalityRow
{
    public PersonalityTypes personality;
    public float value;
}

public sealed class UnitInspectorHistoryRow
{
    public float secondsAgo;
    public ActionType fromAction;
    public ActionType toAction;
    public BehaviorType toBehavior;
    public int priority;
    public float winningUtility;
    public bool wasPlayerOrdered;
    public DecisionCause cause;
}

public sealed class UnitInspectorSnapshot
{
    public bool hasUnit;
    public string unitLabel;
    public UnitType unitType;
    public FactionType faction;
    public int health;
    public int healthMax;
    public bool isDead;
    public ActionType action;
    public BehaviorType behavior;
    public BehaviorPhase phase;
    public int commandIndex;
    public int activePriority;
    public string targetLabel;
    public bool hasTargetPosition;
    public Vector3 targetPosition;
    public bool hasTargetEntity;
    public Vector3 targetEntityPosition;
    public Vector3 position;
    public float awarenessRange;
    public float stoppingDistance;
    public UnitInspectorNavState navState;
    public string navModeLabel;
    public List<Vector3> pathPoints = new List<Vector3>();
    public int currentWaypointIndex;
    public float distanceToGoal;
    public float optionsAgeSeconds;      // 0 when the options table was scored this frame, else how long ago
    public Vector3 abstractRouteEnd;
    public bool hasAbstractRouteEnd;
    public List<Vector3> trailPoints = new List<Vector3>();
    public List<UnitInspectorOptionRow> options = new List<UnitInspectorOptionRow>();
    public List<UnitInspectorMotivationRow> motivations = new List<UnitInspectorMotivationRow>();
    public List<UnitInspectorPersonalityRow> personality = new List<UnitInspectorPersonalityRow>();
    public List<UnitInspectorHistoryRow> history = new List<UnitInspectorHistoryRow>();

    public void Clear()
    {
        hasUnit = false;
        unitLabel = null;
        unitType = default;
        faction = default;
        health = 0;
        healthMax = 0;
        isDead = false;
        action = default;
        behavior = default;
        phase = default;
        commandIndex = 0;
        activePriority = 0;
        targetLabel = null;
        hasTargetPosition = false;
        targetPosition = default;
        hasTargetEntity = false;
        targetEntityPosition = default;
        position = default;
        awarenessRange = 0f;
        stoppingDistance = 0f;
        navState = UnitInspectorNavState.None;
        navModeLabel = null;
        pathPoints.Clear();
        currentWaypointIndex = 0;
        distanceToGoal = 0f;
        optionsAgeSeconds = 0f;
        abstractRouteEnd = default;
        hasAbstractRouteEnd = false;
        trailPoints.Clear();
        options.Clear();
        motivations.Clear();
        personality.Clear();
        history.Clear();
    }
}
#endif
