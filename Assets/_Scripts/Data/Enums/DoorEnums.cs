using System;

// Door (Caravan_System.md CV-D20). DoorControl turns phase + phaseSeconds into how far it is open.
public enum DoorPhase : byte { Closed, Opening, Open, Closing, BurstOpen }

// What a script (a summon exit) wants from a door, over whatever its zones want.
public enum DoorOverride : byte { None, HoldOpen, HoldShut }

// Who standing in a DoorZone opens its doors.
[Flags]
public enum DoorOpeners : byte
{
    None = 0,
    Player = 1,
    Minions = 2,
    OtherUnits = 4,
}
