// The squad radial's commands (CampLoop Phase 3). Append only. Gather arrives with Phase 4.
public enum QuickCommandType : byte { None, Follow, Hold, AttackTarget, Return }

// What a number key does, given the selection and modifiers (SquadRules.ResolveNumberKey).
public enum SquadKeyAction : byte { None, AssignSelectionToSquad, SwitchToSquad }

// A minion's persistent order, re-applied every frame until another order replaces it (CampLoop CL-D21).
public enum StandingOrderType : byte { None, FollowCommander, HoldPosition }
