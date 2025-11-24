using System;
using System.Collections.Generic;
using UnityEngine;

public class ResourceManagerUI : MonoBehaviour
{
    [SerializeField] private Transform container;
    [SerializeField] private Transform template;
    [SerializeField] private ResourceTypeListSO resourceTypeListSO;

    private Dictionary<ResourceTypeSO.ResourceType, ResourceManagerUI_Single> resourceTypeUIDictionary;

    private void Awake()
    {
        template.gameObject.SetActive(false);
    }
    
    private void Start()
    {
        ResourceManager.Instance.OnResourceAmountChanged += ResourceManager_OnResourceAmountChanged;
        
        SetUp();
        UpdateAmounts();
    }

    private void ResourceManager_OnResourceAmountChanged(object sender, System.EventArgs e)
    {
        UpdateAmounts();
    }

    private void SetUp()
    {
        foreach (Transform child in container)
        {
            if (child == template)
            {
                continue;
            }
            Destroy(child.gameObject);
        }
        
        resourceTypeUIDictionary = new Dictionary<ResourceTypeSO.ResourceType, ResourceManagerUI_Single>();

        foreach (ResourceTypeSO resourceTypeSo in resourceTypeListSO.resourceTypeSOList)
        {
            Transform resourceTransform = Instantiate(template, container);
            resourceTransform.gameObject.SetActive(true);
            ResourceManagerUI_Single resourceManagerUISingle = resourceTransform.GetComponent<ResourceManagerUI_Single>();
            resourceManagerUISingle.Setup(resourceTypeSo.sprite);
            
            resourceTypeUIDictionary[resourceTypeSo.resourceType] = resourceManagerUISingle;
        }
    }

    private void UpdateAmounts()
    {
        foreach (ResourceTypeSO resourceTypeSo in resourceTypeListSO.resourceTypeSOList)
        {
            resourceTypeUIDictionary[resourceTypeSo.resourceType]
                .UpdateAmount(ResourceManager.Instance.GetResourceAmount(resourceTypeSo.resourceType));
        }
    }
}

