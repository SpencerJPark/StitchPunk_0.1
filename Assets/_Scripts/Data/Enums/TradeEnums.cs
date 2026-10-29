// Trade_System.md section 4. Append only: client assets and saves serialize these as ints/bytes.
public enum ClientKind : byte { Household, Recurring }

public enum DropOffTag : byte { Household, Military, Noble, Church, Industrial }

public enum ReputationTier : byte { Stranger, Known, Trusted, Partner }
