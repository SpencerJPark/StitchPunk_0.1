using System.Collections.Generic;
using UnityEngine;

public static class DynamicObstacleRegistry
{
    public class Item
    {
        public Transform transform;
        public Vector3   velocityWorld;
        public float     radius;
        public int       team; // optional for filtering
    }

    static readonly List<Item> _items = new(128);
    public static IReadOnlyList<Item> Items => _items;

    public static Item Register(Transform tf, float radius, int team = 0)
    {
        var it = new Item { transform = tf, radius = radius, team = team };
        _items.Add(it);
        return it;
    }

    public static void Unregister(Item item) { _items.Remove(item); }

    public static void UpdateVelocity(Item item, Vector3 vel) { item.velocityWorld = vel; }
}
