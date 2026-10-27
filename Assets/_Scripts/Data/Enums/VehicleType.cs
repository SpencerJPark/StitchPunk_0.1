// One entry per drivable vehicle; its VehicleProfileSO holds the numbers. Append only: entities and the
// library blob index by these values.
public enum VehicleType : byte { None, Caravan }

// Drawn = pulled by a puller (a horse) that stands at a per-direction offset and walks/runs with speed.
// SelfPropelled = a car or steam wagon: no puller.
public enum VehiclePropulsion : byte { Drawn, SelfPropelled }

// What the puller's animation should be doing, from speed against the profile's gait thresholds.
public enum VehicleGait : byte { Idle, Walk, Run }
