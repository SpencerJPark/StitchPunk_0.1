// Player ledger rows (CampLoop_System.md §3 / PlayerResource_System.md §4). Append only: the HUD,
// the save snapshot and the baked starting ledger all index by these values.
public enum ResourceType { None, Wood, ScrapMetal, Corpse, Electricity, Currency, RevivalRound }

public enum ResourceChangeType { Add, Set }
