using System;
using System.Collections.Generic;
using UnityEngine;

public class ResourceManager : Singleton<ResourceManager>
{
    
    public event EventHandler OnResourceAmountChanged;
    
    [SerializeField] private ResourceTypeListSO resourceTypeListSO;
    
    private Dictionary<ResourceTypeSO.ResourceType, int> resourceTypeAmountDictionary;

    private void Start()
    {
        resourceTypeAmountDictionary = new Dictionary<ResourceTypeSO.ResourceType, int>();

        foreach (ResourceTypeSO resourceTypeSo in resourceTypeListSO.resourceTypeSOList)
        {
            resourceTypeAmountDictionary[resourceTypeSo.resourceType] = 0;
        }
        
        AddResourceAmount(ResourceTypeSO.ResourceType.Iron, 50);
        AddResourceAmount(ResourceTypeSO.ResourceType.Gold, 50);
        AddResourceAmount(ResourceTypeSO.ResourceType.Oil, 50);
    }

    public void AddResourceAmount(ResourceTypeSO.ResourceType resourceType, int amount)
    {
        resourceTypeAmountDictionary[resourceType] += amount;
        OnResourceAmountChanged?.Invoke(this, EventArgs.Empty);
    }

    public int GetResourceAmount(ResourceTypeSO.ResourceType resourceType)
    {
        return resourceTypeAmountDictionary[resourceType];
    }

    public bool CanSpendResourceAmount(ResourceAmount resourceAmount)
    {
        return resourceTypeAmountDictionary[resourceAmount.resourceType] >= resourceAmount.amount;
    }
    
    public bool CanSpendResourceAmount(ResourceAmount[] resourceAmountArray)
    {
        foreach (ResourceAmount resourceAmount in resourceAmountArray)
        {
            if (!CanSpendResourceAmount(resourceAmount))
            {
                return false;
            }
        }
        return true;
    }

    public void SpendResourceAmount(ResourceAmount resourceAmount)
    {
        resourceTypeAmountDictionary[resourceAmount.resourceType] -= resourceAmount.amount;
        OnResourceAmountChanged?.Invoke(this, EventArgs.Empty);
    }
    
    public void SpendResourceAmount(ResourceAmount[] resourceAmountArray)
    {
        foreach (ResourceAmount resourceAmount in resourceAmountArray)
        {
            resourceTypeAmountDictionary[resourceAmount.resourceType] -= resourceAmount.amount;
        }
        OnResourceAmountChanged?.Invoke(this, EventArgs.Empty);
    }
}
