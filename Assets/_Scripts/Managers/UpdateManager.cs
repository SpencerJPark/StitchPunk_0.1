using UnityEngine;
using System.Collections.Generic;

// This file is used on a game object in scenes to efficiently handle all object updates

public interface IUpdateObserver
{
    void ObservedUpdate();
}

public class UpdateManager : PersistentSingleton<UpdateManager>
{
    private static List<IUpdateObserver> _observers = new List<IUpdateObserver>();
    private static List<IUpdateObserver> _pendingObservers = new List<IUpdateObserver>();
    private static int _currentIndex;
    
    private void Update()
    {

        for (_currentIndex = _observers.Count - 1; _currentIndex >= 0; _currentIndex--)
        {
            _observers[_currentIndex].ObservedUpdate();
        }

        // Add pending observers after the loop
        if (_pendingObservers.Count > 0)
        {
            _observers.AddRange(_pendingObservers);
            _pendingObservers.Clear();
        }
    }

    public static void RegisterObserver(IUpdateObserver observer)
    {
        _pendingObservers.Add(observer);
    }

    public static void UnregisterObserver(IUpdateObserver observer)
    {
        _observers.Remove(observer);
        _currentIndex--;
    }
}



