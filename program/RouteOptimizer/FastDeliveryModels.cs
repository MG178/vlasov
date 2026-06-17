namespace FastDelivery;

public enum UserRole
{
    Client,
    Courier,
    Dispatcher,
    Admin
}

public enum CourierTransport
{
    Auto,
    OnFoot,
    Bicycle
}

public enum OrderStatus
{
    New,
    Assigned,
    InTransit,
    AtRecipient,
    Delivered,
    Cancelled
}

public sealed class AppUser
{
    public int UserId { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public sealed class ClientProfile
{
    public int ClientId { get; set; }
    public int UserId { get; set; }
    public string LegalAddress { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty;
    public string? Inn { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class CourierProfile
{
    public int CourierId { get; set; }
    public int UserId { get; set; }
    public CourierTransport TransportType { get; set; }
    public string? VehicleNumber { get; set; }
    public int ZoneId { get; set; }
    public string WorkSchedule { get; set; } = string.Empty;
    public decimal Rating { get; set; }
    public bool IsActive { get; set; } = true;
    public string DisplayName { get; set; } = string.Empty;
    public int ActiveOrders { get; set; }
}

public sealed class ZoneEntity
{
    public int ZoneId { get; set; }
    public string ZoneName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
}

public sealed class CargoTypeEntity
{
    public int CargoTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MaxWeight { get; set; }
    public bool IsFragile { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class TariffEntity
{
    public int TariffId { get; set; }
    public int CargoTypeId { get; set; }
    public int ZoneId { get; set; }
    public decimal PricePerKg { get; set; }
    public decimal PricePerKm { get; set; }
    public decimal UrgentMarkup { get; set; }
}

public sealed class OrderEntity
{
    public int OrderId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public int ZoneId { get; set; }
    public int CargoTypeId { get; set; }
    public int? AssignedCourierId { get; set; }
    public int? DispatcherId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string RecipientAddress { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal? Volume { get; set; }
    public bool IsUrgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DesiredDeliveryDate { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalCost { get; set; }
    public string Notes { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;
    public string CourierName { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public string CargoTypeName { get; set; } = string.Empty;
}

public sealed class OrderStatusHistoryEntry
{
    public int HistoryId { get; set; }
    public int OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime ChangedAt { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public sealed class NotificationEntry
{
    public int NotificationId { get; set; }
    public int UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class CourierEfficiencyRow
{
    public string CourierName { get; set; } = string.Empty;
    public int TotalDeliveries { get; set; }
    public decimal AverageMinutes { get; set; }
    public int LateDeliveries { get; set; }
    public decimal Rating { get; set; }
}

public sealed class StatusSummaryRow
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class OrderUpsertRequest
{
    public int OrderId { get; set; }
    public int ClientId { get; set; }
    public int ZoneId { get; set; }
    public int CargoTypeId { get; set; }
    public int? AssignedCourierId { get; set; }
    public int? DispatcherId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string RecipientAddress { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal? Volume { get; set; }
    public bool IsUrgent { get; set; }
    public DateTime? DesiredDeliveryDate { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class CourierUpsertRequest
{
    public int CourierId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public CourierTransport TransportType { get; set; }
    public string? VehicleNumber { get; set; }
    public int ZoneId { get; set; }
    public string WorkSchedule { get; set; } = string.Empty;
    public decimal Rating { get; set; }
}