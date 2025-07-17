using UnityEngine;
using System.Collections.Generic;

// This file is used on a game object in scenes to efficiently handle all object updates

public interface IFixedUpdateObserver
{
    void ObservedFixedUpdate();
}

public class FixedUpdateManager : PersistentSingleton<FixedUpdateManager>
{
    private static List<IFixedUpdateObserver> _observers = new List<IFixedUpdateObserver>();
    private static List<IFixedUpdateObserver> _pendingObservers = new List<IFixedUpdateObserver>();
    private static int _currentIndex;
    
    private void FixedUpdate()
    {

        for (_currentIndex = _observers.Count - 1; _currentIndex >= 0; _currentIndex--)
        {
            _observers[_currentIndex].ObservedFixedUpdate();
        }

        // Add pending observers after the loop
        if (_pendingObservers.Count > 0)
        {
            _observers.AddRange(_pendingObservers);
            _pendingObservers.Clear();
        }
    }

    public static void RegisterObserver(IFixedUpdateObserver observer)
    {
        _pendingObservers.Add(observer);
    }

    public static void UnregisterObserver(IFixedUpdateObserver observer)
    {
        _observers.Remove(observer);
        _currentIndex--;
    }
}



