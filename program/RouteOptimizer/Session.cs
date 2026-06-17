namespace FastDelivery;

public static class Session
{
    public static AppUser? CurrentUser { get; set; }
    public static string? CurrentRoleName { get; set; }
    public static bool IsClient => string.Equals(CurrentRoleName, "Client", StringComparison.OrdinalIgnoreCase);
    public static bool IsCourier => string.Equals(CurrentRoleName, "Courier", StringComparison.OrdinalIgnoreCase);
    public static bool IsDispatcher => string.Equals(CurrentRoleName, "Dispatcher", StringComparison.OrdinalIgnoreCase);
    public static bool IsAdmin => string.Equals(CurrentRoleName, "Admin", StringComparison.OrdinalIgnoreCase);
}
