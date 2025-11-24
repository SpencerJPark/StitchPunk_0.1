using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ResourceTypeListSO", menuName = "Scriptable Objects/ResourceTypeListSO")]
public class ResourceTypeListSO : ScriptableObject
{
    public List<ResourceTypeSO> resourceTypeSOList;
}
