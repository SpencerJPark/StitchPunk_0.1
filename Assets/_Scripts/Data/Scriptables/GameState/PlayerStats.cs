using UnityEngine;

[CreateAssetMenu(fileName = "PlayerStats", menuName = "GameState/Player Stats", order = 1)]
public class PlayerStats : ScriptableObject
{
    public float CurrentPlayerHealth;
    public bool Dead;
}