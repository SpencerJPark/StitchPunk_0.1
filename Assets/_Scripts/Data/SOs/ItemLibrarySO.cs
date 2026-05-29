using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_ItemLibrary", menuName = "Items/Item Library")]
public class ItemLibrarySO : ScriptableObject
{
    public List<ItemSO> items = new List<ItemSO>();

    public ItemSO GetItemSO(ItemType type)
    {
        foreach (ItemSO so in items)
        {
            if (so != null && so.itemType == type)
                return so;
        }
        return null;
    }
}
