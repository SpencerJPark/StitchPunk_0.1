#if false

using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(UnitController))]
public class Brain : MonoBehaviour, IInputProvider
{
    [Header("Brain Settings")]
    //[SerializeField] private List<Trait> traits;
    //[SerializeField] private NeedSet needs;
    //[SerializeField] private Schedule schedule;
    [SerializeField] private UnitController controller;

    //private ISignalProvider currentTarget;


    // IInputProvider implementation
    public Vector2 MoveInput { get; }
    public Vector2 SteerInput { get; private set; }
    public bool ExitVehicleFired => false;
    public bool InteractFired => false;
    public bool ActionFired => false;

    void Update()
    {
        if (EvaluateNeedsAndPickTarget(out Vector3 moveDir))
        {
            SteerInput = new Vector2(moveDir.x, moveDir.z).normalized;
        }
        else
        {
            SteerInput = Vector2.zero;
        }
    }

    private bool EvaluateNeedsAndPickTarget(out Vector3 moveDirection)
    {
        // 1. Evaluate current state
        var signals = SignalRegistry.Instance.GetNearbySignals(transform.position);

        // 2. Score best signal to pursue
        ISignalProvider best = null;
        float bestScore = float.MinValue;

        foreach (var s in signals)
        {
            float urgency = needs.GetUrgency(s.Type);
            float score = s.SatisfactionValue * urgency * s.PriorityModifier;

            if (score > bestScore)
            {
                bestScore = score;
                best = s;
            }
        }

        currentTarget = best;

        // 3. Set movement if target exists
        if (currentTarget != null)
        {
            Vector3 dir = (currentTarget.Position - transform.position);
            moveDirection = dir.normalized;
            return true;
        }

        moveDirection = Vector3.zero;
        return false;
    }
}


#endif