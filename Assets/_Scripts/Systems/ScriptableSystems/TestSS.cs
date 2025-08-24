using UnityEngine;

namespace ScriptableSystems
{
    [CreateAssetMenu(fileName = "TestSS", menuName = "Scriptable Systems/Test SS")]
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
}