// The squad radial's commands (CampLoop Phase 3; Gather added in Phase 4). Append only.
public enum QuickCommandType : byte { None, Follow, Hold, AttackTarget, Return, Gather }

// What a number key does, given the selection and modifiers (SquadRules.ResolveNumberKey).
public enum SquadKeyAction : byte { None, AssignSelectionToSquad, SwitchToSquad }

// A minion's persistent order, re-applied every frame until another order replaces it (CampLoop CL-D21).
public enum StandingOrderType : byte { None, FollowCommander, HoldPosition }

// Where a gathering minion is in its harvest -> carry -> deposit loop (CampLoop Phase 4).
public enum GatherPhase : byte { None, ToNode, Harvesting, ToStation }
