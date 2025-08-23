using UnityEngine;

[CreateAssetMenu(fileName = "TestSS", menuName = "Scriptable Systems/Test SS", order = 1)]
public class TestSS : ScriptableSystem
{
    public override void Initialize()
    {
        Debug.Log("test run");
    }

    public override void Tick()
    {

    }
}