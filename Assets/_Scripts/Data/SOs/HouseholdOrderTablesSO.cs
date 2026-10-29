using UnityEngine;

// Each roll invents a one-time client from these tables (TR-D6); it never earns a relationship row.
[CreateAssetMenu(menuName = "Stitch Punk/Trade/Household Order Tables")]
public class HouseholdOrderTablesSO : ScriptableObject
{
    public string[] householdNames;
    public OrderTemplateSO[] templates;
}
