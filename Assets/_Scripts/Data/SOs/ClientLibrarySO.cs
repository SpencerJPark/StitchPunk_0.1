using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Trade/Client Library")]
public class ClientLibrarySO : ScriptableObject
{
    public ClientSO[] clients;
    public HouseholdOrderTablesSO household;
    public TradeTuningSO tuning;
}
