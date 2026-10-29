// Display strings stay out of the blob (TradeBlobs.cs): UI looks names and flavour text up from the SOs by id.
public static class TradeTextLookup
{
    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    public static string DayName(int dayOfWeek)
    {
        return DayNames[dayOfWeek < 0 ? 0 : dayOfWeek >= DayNames.Length ? DayNames.Length - 1 : dayOfWeek];
    }

    public static string UnitLabel(UnitType unitType)
    {
        switch (unitType)
        {
            case UnitType.PlayerZombieBrute: return "Brute";
            case UnitType.PlayerZombieRunner: return "Runner";
            case UnitType.PlayerZombie: return "Zombie";
            default: return unitType.ToString();
        }
    }

    public static string ClientName(ClientLibrarySO library, int clientId)
    {
        if (library == null)
            return $"Client {clientId}";

        if (clientId < 0)
        {
            string[] householdNames = library.household != null ? library.household.householdNames : null;
            if (householdNames == null || householdNames.Length == 0)
                return "A household";
            return householdNames[TradeMath.HouseholdNameIndex(clientId, householdNames.Length)];
        }

        ClientSO client = FindClient(library, clientId);
        return client != null ? client.displayName : $"Client {clientId}";
    }

    public static ClientSO FindClient(ClientLibrarySO library, int clientId)
    {
        if (library == null || library.clients == null)
            return null;

        foreach (ClientSO client in library.clients)
        {
            if (client != null && client.clientId == clientId)
                return client;
        }

        return null;
    }

    // templateIndex indexes the client's pool, or the household table for a one-off.
    public static OrderTemplateSO FindTemplate(ClientLibrarySO library, TradeOrder order)
    {
        if (library == null)
            return null;

        OrderTemplateSO[] pool;
        if (order.clientId < 0)
        {
            pool = library.household != null ? library.household.templates : null;
        }
        else
        {
            ClientSO client = FindClient(library, order.clientId);
            pool = client != null ? client.orderPool : null;
        }

        return pool != null && order.templateIndex >= 0 && order.templateIndex < pool.Length ? pool[order.templateIndex] : null;
    }

    public static string Requirement(TradeOrder order)
    {
        string requirement = $"{order.quantity}× {UnitLabel(order.unitType)}";
        if (order.minHealthFraction > 0f)
            requirement += $" · at least {order.minHealthFraction * 100f:0}% health";
        return requirement;
    }
}
