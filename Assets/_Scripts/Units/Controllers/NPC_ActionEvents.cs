using System;
using UnityEngine;

public static class NPC_ActionEvents
{
    public static Action<GameObject, string> OnActionChange;

    public static void Trigger(GameObject npc, string action)
    {
        OnActionChange?.Invoke(npc, action);
    }
}