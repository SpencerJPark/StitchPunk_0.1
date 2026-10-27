// How a summoned unit leaves the caravan (Caravan_System.md CV-D10). Authored per UnitSO.
public enum CaravanExitStyle : byte { WalkOutAndHop, SmokeBurst }

// The back doors. CaravanPresenter turns phase + phaseSeconds into a hinge angle.
public enum CaravanDoorPhase : byte { Closed, Opening, Open, Closing, BurstOpen }
