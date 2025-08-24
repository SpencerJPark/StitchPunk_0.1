using UnityEngine;

namespace UtilityAI {
    [CreateAssetMenu(menuName = "UtilityAI/Actions/IdleAction")]
    public class IdleAIAction : AIAction {
        public override void Execute(Context context) {
            context.brain.SetDestination(context.agent.position);
        }
    }
}