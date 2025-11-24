using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class DOTSEventsManager : PersistentSingleton<DOTSEventsManager> {
    
    public event EventHandler OnBarracksUnitQueueChanged;
    public event EventHandler OnHealthDead;
    public event EventHandler OnHQDead;


    public void TriggerOnBarracksUnitQueueChanged(NativeList<Entity> entityNativeList) {
        foreach (Entity entity in entityNativeList) {
            OnBarracksUnitQueueChanged?.Invoke(entity, EventArgs.Empty);
        }
    }

    public void TriggerOnHealthDead(NativeList<Entity> entityNativeList)
    {
        foreach (Entity entity in entityNativeList) {
            OnHealthDead?.Invoke(entity, EventArgs.Empty);
        }
    }

    public void TriggerOnHQDead() {
        OnHQDead?.Invoke(this, EventArgs.Empty);
    }

}