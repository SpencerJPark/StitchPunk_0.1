// How a summoned unit leaves the caravan (Caravan_System.md CV-D10). Authored per UnitSO.
public enum CaravanExitStyle : byte { WalkOutAndHop, SmokeBurst }

// Which shake CaravanShake is playing: the rumble before a smoke burst, or the jolt as the doors slam.
public enum CaravanShakeKind : byte { BuildUp, Burst }
