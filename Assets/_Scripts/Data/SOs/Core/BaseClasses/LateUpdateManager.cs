using UnityEngine;
using System.Collections.Generic;

// This file is used on a game object in scenes to efficiently handle all object updates

public interface ILateUpdateObserver
{
    void ObservedLateUpdate();
}

public class LateUpdateManager : PersistentSingleton<LateUpdateManager>
{
    private static List<ILateUpdateObserver> _observers = new List<ILateUpdateObserver>();
    private static List<ILateUpdateObserver> _pendingObservers = new List<ILateUpdateObserver>();
    private static int _currentIndex;
    
    private void LateUpdate()
    {

        for (_currentIndex = _observers.Count - 1; _currentIndex >= 0; _currentIndex--)
        {
            _observers[_currentIndex].ObservedLateUpdate();
        }

        // Add pending observers after the loop
        if (_pendingObservers.Count > 0)
        {
            _observers.AddRange(_pendingObservers);
            _pendingObservers.Clear();
        }
    }

    public static void RegisterObserver(ILateUpdateObserver observer)
    {
        _pendingObservers.Add(observer);
    }

    public static void UnregisterObserver(ILateUpdateObserver observer)
    {
        _observers.Remove(observer);
        _currentIndex--;
    }
}



