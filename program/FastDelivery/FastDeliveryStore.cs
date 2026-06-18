using System.Collections.ObjectModel;

namespace FastDelivery;

public sealed class FastDeliveryStore
{
    public static FastDeliveryStore Instance { get; } = new();

    private readonly List<AppUser> _users = new();
    private readonly List<ClientProfile> _clients = new();
    private readonly List<CourierProfile> _couriers = new();
    private readonly List<ZoneEntity> _zones = new();
    private readonly List<CargoTypeEntity> _cargoTypes = new();
    private readonly List<TariffEntity> _tariffs = new();
    private readonly List<OrderEntity> _orders = new();
    private readonly List<OrderStatusHistoryEntry> _history = new();
    private readonly List<NotificationEntry> _notifications = new();

    private int _nextUserId = 1;
    private int _nextClientId = 1;
    private int _nextCourierId = 1;
    private int _nextTariffId = 1;
    private int _nextOrderId = 1;
    private int _nextHistoryId = 1;
    private int _nextNotificationId = 1;

    private FastDeliveryStore()
    {
        Seed();
    }

    public IReadOnlyList<AppUser> Users => _users;
    public IReadOnlyList<ClientProfile> Clients => _clients;
    public IReadOnlyList<CourierProfile> Couriers => _couriers;
    public IReadOnlyList<ZoneEntity> Zones => _zones;
    public IReadOnlyList<CargoTypeEntity> CargoTypes => _cargoTypes;
    public IReadOnlyList<TariffEntity> Tariffs => _tariffs;
    public IReadOnlyList<OrderEntity> Orders => _orders;
    public IReadOnlyList<OrderStatusHistoryEntry> History => _history;
    public IReadOnlyList<NotificationEntry> Notifications => _notifications;

    public AppUser? Authenticate(string login, string password)
    {
        return _users.FirstOrDefault(x => x.IsActive && string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase) && x.PasswordHash == password);
    }

    public List<ClientProfile> GetClientProfiles()
    {
        return (from client in _clients
                join user in _users on client.UserId equals user.UserId
                orderby user.FullName
                select new ClientProfile
                {
                    ClientId = client.ClientId,
                    UserId = client.UserId,
                    LegalAddress = client.LegalAddress,
                    ClientType = client.ClientType,
                    Inn = client.Inn,
                    DisplayName = $"{user.FullName} ({client.ClientType})"
                }).ToList();
    }

    public List<CourierProfile> GetCourierProfiles()
    {
        UpdateCourierLoads();
        return (from courier in _couriers
                join user in _users on courier.UserId equals user.UserId
                orderby user.FullName
                select new CourierProfile
                {
                    CourierId = courier.CourierId,
                    UserId = courier.UserId,
                    TransportType = courier.TransportType,
                    VehicleNumber = courier.VehicleNumber,
                    ZoneId = courier.ZoneId,
                    WorkSchedule = courier.WorkSchedule,
                    Rating = courier.Rating,
                    IsActive = courier.IsActive,
                    ActiveOrders = courier.ActiveOrders,
                    DisplayName = $"{user.FullName} · {GetZoneName(courier.ZoneId)}"
                }).ToList();
    }

    public List<OrderEntity> GetVisibleOrders(AppUser user)
    {
        UpdateOrderLabels();
        IEnumerable<OrderEntity> query = _orders;

        if (user.Role == UserRole.Client)
        {
            var clientIds = _clients.Where(x => x.UserId == user.UserId).Select(x => x.ClientId).ToHashSet();
            query = query.Where(x => clientIds.Contains(x.ClientId));
        }
        else if (user.Role == UserRole.Courier)
        {
            var courierIds = _couriers.Where(x => x.UserId == user.UserId).Select(x => x.CourierId).ToHashSet();
            query = query.Where(x => x.AssignedCourierId.HasValue && courierIds.Contains(x.AssignedCourierId.Value));
        }

        return query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.OrderId).Select(CloneOrder).ToList();
    }

    public List<OrderStatusHistoryEntry> GetHistoryForOrder(int orderId)
    {
        return _history.Where(x => x.OrderId == orderId).OrderByDescending(x => x.ChangedAt).Select(x => new OrderStatusHistoryEntry
        {
            HistoryId = x.HistoryId,
            OrderId = x.OrderId,
            Status = x.Status,
            ChangedAt = x.ChangedAt,
            Latitude = x.Latitude,
            Longitude = x.Longitude,
            Comment = x.Comment
        }).ToList();
    }

    public List<NotificationEntry> GetNotificationsForUser(int userId)
    {
        return _notifications.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).Select(x => new NotificationEntry
        {
            NotificationId = x.NotificationId,
            UserId = x.UserId,
            Text = x.Text,
            CreatedAt = x.CreatedAt,
            IsRead = x.IsRead
        }).ToList();
    }

    public void MarkNotificationRead(int notificationId)
    {
        var notification = _notifications.FirstOrDefault(x => x.NotificationId == notificationId);
        if (notification is not null)
        {
            notification.IsRead = true;
        }
    }

    public void MarkAllNotificationsRead(int userId)
    {
        foreach (var item in _notifications.Where(x => x.UserId == userId))
        {
            item.IsRead = true;
        }
    }

    public OrderEntity? GetOrderById(int orderId)
    {
        UpdateOrderLabels();
        return _orders.FirstOrDefault(x => x.OrderId == orderId) is { } order ? CloneOrder(order) : null;
    }

    public OrderEntity CreateOrUpdateOrder(OrderUpsertRequest request, AppUser currentUser)
    {
        if (request.Weight <= 0)
        {
            throw new InvalidOperationException("Вес должен быть больше нуля.");
        }

        if (request.DesiredDeliveryDate.HasValue && request.DesiredDeliveryDate.Value.Date < DateTime.Today)
        {
            throw new InvalidOperationException("Желаемая дата доставки не может быть в прошлом.");
        }

        var clientId = ResolveClientId(request.ClientId, currentUser);
        if (_clients.All(x => x.ClientId != clientId))
        {
            throw new InvalidOperationException("Клиент не найден.");
        }

        var zone = _zones.FirstOrDefault(x => x.ZoneId == request.ZoneId) ?? throw new InvalidOperationException("Зона не найдена.");
        var cargoType = _cargoTypes.FirstOrDefault(x => x.CargoTypeId == request.CargoTypeId) ?? throw new InvalidOperationException("Тип груза не найден.");
        if (request.Weight > cargoType.MaxWeight)
        {
            throw new InvalidOperationException($"Для типа груза '{cargoType.Name}' максимально допустимый вес {cargoType.MaxWeight} кг.");
        }

        var totalCost = CalculateCost(request.CargoTypeId, request.Weight, request.ZoneId, request.IsUrgent);

        OrderEntity order;
        if (request.OrderId == 0)
        {
            order = new OrderEntity
            {
                OrderId = _nextOrderId++,
                TrackingNumber = GenerateTrackingNumber(),
                ClientId = clientId,
                ZoneId = request.ZoneId,
                CargoTypeId = request.CargoTypeId,
                AssignedCourierId = request.AssignedCourierId,
                DispatcherId = request.DispatcherId,
                RecipientName = request.RecipientName.Trim(),
                RecipientPhone = request.RecipientPhone.Trim(),
                RecipientAddress = request.RecipientAddress.Trim(),
                PickupAddress = request.PickupAddress.Trim(),
                DeliveryAddress = request.DeliveryAddress.Trim(),
                Weight = request.Weight,
                Volume = request.Volume,
                IsUrgent = request.IsUrgent,
                CreatedAt = DateTime.Now,
                DesiredDeliveryDate = request.DesiredDeliveryDate,
                Status = OrderStatus.New,
                TotalCost = totalCost,
                Notes = request.Notes.Trim()
            };

            _orders.Add(order);
            AddHistory(order.OrderId, OrderStatus.New, "Заказ создан");
            AddNotificationForRole(UserRole.Dispatcher, $"Создан новый заказ {order.TrackingNumber}.");
            AddNotificationForRole(UserRole.Admin, $"Создан новый заказ {order.TrackingNumber}.");
        }
        else
        {
            order = _orders.FirstOrDefault(x => x.OrderId == request.OrderId) ?? throw new InvalidOperationException("Заказ не найден.");
            if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            {
                throw new InvalidOperationException("Нельзя редактировать завершенный заказ.");
            }

            order.ClientId = clientId;
            order.ZoneId = request.ZoneId;
            order.CargoTypeId = request.CargoTypeId;
            order.AssignedCourierId = request.AssignedCourierId;
            order.DispatcherId = request.DispatcherId;
            order.RecipientName = request.RecipientName.Trim();
            order.RecipientPhone = request.RecipientPhone.Trim();
            order.RecipientAddress = request.RecipientAddress.Trim();
            order.PickupAddress = request.PickupAddress.Trim();
            order.DeliveryAddress = request.DeliveryAddress.Trim();
            order.Weight = request.Weight;
            order.Volume = request.Volume;
            order.IsUrgent = request.IsUrgent;
            order.DesiredDeliveryDate = request.DesiredDeliveryDate;
            order.TotalCost = totalCost;
            order.Notes = request.Notes.Trim();
            AddHistory(order.OrderId, order.Status, "Заказ обновлён");
        }

        UpdateOrderLabels();
        return CloneOrder(order);
    }

    public void DeleteOrder(int orderId)
    {
        var order = _orders.FirstOrDefault(x => x.OrderId == orderId) ?? throw new InvalidOperationException("Заказ не найден.");
        if (order.Status is OrderStatus.Delivered or OrderStatus.InTransit or OrderStatus.AtRecipient)
        {
            throw new InvalidOperationException("Нельзя удалить заказ, который уже в процессе доставки.");
        }

        _orders.Remove(order);
        _history.RemoveAll(x => x.OrderId == orderId);
        _notifications.Add(new NotificationEntry
        {
            NotificationId = _nextNotificationId++,
            UserId = GetClientUserId(order.ClientId),
            Text = $"Заказ {order.TrackingNumber} удалён.",
            CreatedAt = DateTime.Now,
            IsRead = false
        });
    }

    public OrderEntity AssignCourier(int orderId, int? courierId, int dispatcherUserId)
    {
        var order = _orders.FirstOrDefault(x => x.OrderId == orderId) ?? throw new InvalidOperationException("Заказ не найден.");
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Нельзя назначить курьера для завершенного заказа.");
        }

        var selectedCourierId = courierId ?? PickCourier(order.ZoneId);
        if (selectedCourierId is null)
        {
            throw new InvalidOperationException("Нет доступного курьера в зоне заказа.");
        }

        var courier = _couriers.FirstOrDefault(x => x.CourierId == selectedCourierId.Value && x.IsActive) ?? throw new InvalidOperationException("Курьер не найден.");
        var activeOrders = _orders.Count(x => x.AssignedCourierId == courier.CourierId && x.Status is not OrderStatus.Delivered and not OrderStatus.Cancelled);
        if (activeOrders >= 5)
        {
            throw new InvalidOperationException("У курьера уже 5 активных заказов.");
        }

        order.AssignedCourierId = courier.CourierId;
        order.DispatcherId = dispatcherUserId;
        order.Status = OrderStatus.Assigned;
        AddHistory(order.OrderId, OrderStatus.Assigned, $"Назначен курьер {GetCourierName(courier.CourierId)}");
        AddNotification(courier.UserId, $"Вам назначен заказ {order.TrackingNumber}.");
        UpdateOrderLabels();
        return CloneOrder(order);
    }

    public OrderEntity SetOrderStatus(int orderId, OrderStatus newStatus, string comment, decimal? latitude = null, decimal? longitude = null)
    {
        var order = _orders.FirstOrDefault(x => x.OrderId == orderId) ?? throw new InvalidOperationException("Заказ не найден.");
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Заказ уже завершен.");
        }

        if (newStatus == OrderStatus.Delivered && order.AssignedCourierId is null)
        {
            throw new InvalidOperationException("Перед доставкой нужно назначить курьера.");
        }

        if (newStatus == OrderStatus.Cancelled && order.Status == OrderStatus.Delivered)
        {
            throw new InvalidOperationException("Нельзя отменить доставленный заказ.");
        }

        order.Status = newStatus;
        AddHistory(order.OrderId, newStatus, comment, latitude, longitude);

        if (newStatus == OrderStatus.Delivered)
        {
            AddNotification(GetClientUserId(order.ClientId), $"Ваш заказ {order.TrackingNumber} доставлен.");
            if (order.AssignedCourierId.HasValue)
            {
                var courier = _couriers.FirstOrDefault(x => x.CourierId == order.AssignedCourierId.Value);
                if (courier is not null)
                {
                    AddNotification(courier.UserId, $"Заказ {order.TrackingNumber} закрыт как доставленный.");
                }
            }
        }

        UpdateOrderLabels();
        return CloneOrder(order);
    }

    public List<ZoneEntity> GetZones()
    {
        return _zones.Select(x => new ZoneEntity
        {
            ZoneId = x.ZoneId,
            ZoneName = x.ZoneName,
            City = x.City,
            Coefficient = x.Coefficient
        }).ToList();
    }

    public List<CargoTypeEntity> GetCargoTypes()
    {
        return _cargoTypes.Select(x => new CargoTypeEntity
        {
            CargoTypeId = x.CargoTypeId,
            Name = x.Name,
            MaxWeight = x.MaxWeight,
            IsFragile = x.IsFragile,
            Description = x.Description
        }).ToList();
    }

    public List<TariffEntity> GetTariffs()
    {
        return _tariffs.Select(x => new TariffEntity
        {
            TariffId = x.TariffId,
            CargoTypeId = x.CargoTypeId,
            ZoneId = x.ZoneId,
            PricePerKg = x.PricePerKg,
            PricePerKm = x.PricePerKm,
            UrgentMarkup = x.UrgentMarkup
        }).ToList();
    }

    public List<CourierEfficiencyRow> GetCourierEfficiencyRows()
    {
        UpdateCourierLoads();
        return (from courier in _couriers
                join user in _users on courier.UserId equals user.UserId
                let courierOrders = _orders.Where(x => x.AssignedCourierId == courier.CourierId)
                let delivered = courierOrders.Count(x => x.Status == OrderStatus.Delivered)
                let late = courierOrders.Count(x => x.Status == OrderStatus.Delivered && x.DesiredDeliveryDate.HasValue && x.CreatedAt.Date < x.DesiredDeliveryDate.Value.Date)
                let average = courierOrders.Where(x => x.Status == OrderStatus.Delivered).Select(x => (x.CreatedAt - x.CreatedAt.Date).TotalMinutes + 120).DefaultIfEmpty(0).Average()
                orderby user.FullName
                select new CourierEfficiencyRow
                {
                    CourierName = user.FullName,
                    TotalDeliveries = delivered,
                    AverageMinutes = Math.Round((decimal)average, 1),
                    LateDeliveries = late,
                    Rating = courier.Rating
                }).ToList();
    }

    public List<StatusSummaryRow> GetStatusSummaryRows()
    {
        return Enum.GetValues<OrderStatus>()
            .Select(status => new StatusSummaryRow
            {
                Status = GetStatusName(status),
                Count = _orders.Count(x => x.Status == status)
            })
            .ToList();
    }

    public decimal CalculateCost(int cargoTypeId, decimal weight, int zoneId, bool urgent)
    {
        var tariff = _tariffs.FirstOrDefault(x => x.CargoTypeId == cargoTypeId && x.ZoneId == zoneId)
            ?? throw new InvalidOperationException("Тариф для выбранных параметров не найден.");

        var distanceKm = 5m + (zoneId * 7m);
        var zoneCoefficient = _zones.First(x => x.ZoneId == zoneId).Coefficient;
        var total = (tariff.PricePerKg * weight) + (tariff.PricePerKm * distanceKm * zoneCoefficient);
        if (urgent)
        {
            total += tariff.UrgentMarkup;
        }

        return Math.Round(total, 2);
    }

    public void SaveCourier(CourierUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Заполните ФИО, логин и пароль.");
        }

        if (request.Rating < 0 || request.Rating > 5)
        {
            throw new InvalidOperationException("Рейтинг должен быть в диапазоне от 0 до 5.");
        }

        if (!_zones.Any(x => x.ZoneId == request.ZoneId))
        {
            throw new InvalidOperationException("Выберите зону доставки.");
        }

        if (request.CourierId == 0)
        {
            if (_users.Any(x => string.Equals(x.Login, request.Login, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Логин уже используется.");
            }

            var user = new AppUser
            {
                UserId = _nextUserId++,
                Login = request.Login.Trim(),
                PasswordHash = request.Password,
                FullName = request.FullName.Trim(),
                Email = request.Email.Trim(),
                Phone = request.Phone.Trim(),
                Role = UserRole.Courier,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            _users.Add(user);

            _couriers.Add(new CourierProfile
            {
                CourierId = _nextCourierId++,
                UserId = user.UserId,
                TransportType = request.TransportType,
                VehicleNumber = request.VehicleNumber?.Trim(),
                ZoneId = request.ZoneId,
                WorkSchedule = request.WorkSchedule.Trim(),
                Rating = request.Rating,
                IsActive = true
            });

            AddNotificationForRole(UserRole.Dispatcher, $"Добавлен курьер {user.FullName}.");
            AddNotificationForRole(UserRole.Admin, $"Добавлен курьер {user.FullName}.");
        }
        else
        {
            var courier = _couriers.FirstOrDefault(x => x.CourierId == request.CourierId) ?? throw new InvalidOperationException("Курьер не найден.");
            var user = _users.FirstOrDefault(x => x.UserId == courier.UserId) ?? throw new InvalidOperationException("Пользователь курьера не найден.");
            courier.TransportType = request.TransportType;
            courier.VehicleNumber = request.VehicleNumber?.Trim();
            courier.ZoneId = request.ZoneId;
            courier.WorkSchedule = request.WorkSchedule.Trim();
            courier.Rating = request.Rating;
            user.FullName = request.FullName.Trim();
            user.Login = request.Login.Trim();
            user.PasswordHash = request.Password;
            user.Email = request.Email.Trim();
            user.Phone = request.Phone.Trim();
            courier.IsActive = true;
        }

        UpdateCourierLoads();
    }

    public void DeleteCourier(int courierId)
    {
        var courier = _couriers.FirstOrDefault(x => x.CourierId == courierId) ?? throw new InvalidOperationException("Курьер не найден.");
        var activeOrders = _orders.Any(x => x.AssignedCourierId == courierId && x.Status is not OrderStatus.Delivered and not OrderStatus.Cancelled);
        if (activeOrders)
        {
            throw new InvalidOperationException("Нельзя удалить курьера с активными заказами.");
        }

        var userId = courier.UserId;
        _couriers.Remove(courier);
        _users.RemoveAll(x => x.UserId == userId);
    }

    public void SaveCatalogs(IEnumerable<ZoneEntity> zones, IEnumerable<TariffEntity> tariffs)
    {
        _zones.Clear();
        _zones.AddRange(zones.Select(x => new ZoneEntity
        {
            ZoneId = x.ZoneId,
            ZoneName = x.ZoneName.Trim(),
            City = x.City.Trim(),
            Coefficient = x.Coefficient
        }));

        _tariffs.Clear();
        _tariffs.AddRange(tariffs.Select(x => new TariffEntity
        {
            TariffId = x.TariffId,
            CargoTypeId = x.CargoTypeId,
            ZoneId = x.ZoneId,
            PricePerKg = x.PricePerKg,
            PricePerKm = x.PricePerKm,
            UrgentMarkup = x.UrgentMarkup
        }));
    }

    public string GetStatusName(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.New => "Новый",
            OrderStatus.Assigned => "Назначен",
            OrderStatus.InTransit => "В пути",
            OrderStatus.AtRecipient => "У получателя",
            OrderStatus.Delivered => "Доставлен",
            OrderStatus.Cancelled => "Отменён",
            _ => status.ToString()
        };
    }

    public string GetRoleName(UserRole role)
    {
        return role switch
        {
            UserRole.Client => "Клиент",
            UserRole.Courier => "Курьер",
            UserRole.Dispatcher => "Диспетчер",
            UserRole.Admin => "Администратор",
            _ => role.ToString()
        };
    }

    public string GetCourierName(int courierId)
    {
        return (from courier in _couriers
                join user in _users on courier.UserId equals user.UserId
                where courier.CourierId == courierId
                select user.FullName).FirstOrDefault() ?? "Не назначен";
    }

    public string GetClientName(int clientId)
    {
        return (from client in _clients
                join user in _users on client.UserId equals user.UserId
                where client.ClientId == clientId
                select user.FullName).FirstOrDefault() ?? "Неизвестный клиент";
    }

    public string GetZoneName(int zoneId) => _zones.FirstOrDefault(x => x.ZoneId == zoneId)?.ZoneName ?? "-";
    public string GetCargoTypeName(int cargoTypeId) => _cargoTypes.FirstOrDefault(x => x.CargoTypeId == cargoTypeId)?.Name ?? "-";

    public int GetClientUserId(int clientId) => _clients.First(x => x.ClientId == clientId).UserId;

    public int? PickCourier(int zoneId)
    {
        var candidates = _couriers
            .Where(x => x.IsActive && x.ZoneId == zoneId)
            .Select(x => new
            {
                Courier = x,
                ActiveOrders = _orders.Count(o => o.AssignedCourierId == x.CourierId && o.Status is not OrderStatus.Delivered and not OrderStatus.Cancelled)
            })
            .Where(x => x.ActiveOrders < 5)
            .OrderBy(x => x.ActiveOrders)
            .ThenByDescending(x => x.Courier.Rating)
            .ThenBy(x => x.Courier.CourierId)
            .ToList();

        return candidates.FirstOrDefault()?.Courier.CourierId;
    }

    private void Seed()
    {
        AddUser("admin", "admin", "Администратор", "admin@fastdelivery.local", "+79990000001", UserRole.Admin);
        AddUser("dispatcher", "dispatcher", "Диспетчер Петров", "dispatcher@fastdelivery.local", "+79990000002", UserRole.Dispatcher);
        AddUser("courier1", "courier1", "Курьер Иванов", "courier1@fastdelivery.local", "+79990000003", UserRole.Courier);
        AddUser("courier2", "courier2", "Курьер Сидорова", "courier2@fastdelivery.local", "+79990000004", UserRole.Courier);
        AddUser("client1", "client1", "ООО Ромашка", "client1@fastdelivery.local", "+79990000005", UserRole.Client);
        AddUser("client2", "client2", "Иванов С.П.", "client2@fastdelivery.local", "+79990000006", UserRole.Client);

        _clients.Add(new ClientProfile { ClientId = _nextClientId++, UserId = 5, LegalAddress = "г. Санкт-Петербург, ул. Ленина, д. 1", ClientType = "LegalEntity", Inn = "7812345678" });
        _clients.Add(new ClientProfile { ClientId = _nextClientId++, UserId = 6, LegalAddress = "г. Санкт-Петербург, пр. Невский, д. 10", ClientType = "Individual", Inn = null });

        _zones.AddRange(new[]
        {
            new ZoneEntity { ZoneId = 1, ZoneName = "Центр", City = "Санкт-Петербург", Coefficient = 1.0m },
            new ZoneEntity { ZoneId = 2, ZoneName = "Пригород", City = "Санкт-Петербург", Coefficient = 1.5m },
            new ZoneEntity { ZoneId = 3, ZoneName = "Область", City = "Ленинградская обл.", Coefficient = 2.0m }
        });

        _cargoTypes.AddRange(new[]
        {
            new CargoTypeEntity { CargoTypeId = 1, Name = "Документы", MaxWeight = 0.5m, IsFragile = false, Description = "Папки, договоры, ценные бумаги" },
            new CargoTypeEntity { CargoTypeId = 2, Name = "Одежда и текстиль", MaxWeight = 10m, IsFragile = false, Description = "Мягкие грузы и упаковки" },
            new CargoTypeEntity { CargoTypeId = 3, Name = "Техника и электроника", MaxWeight = 20m, IsFragile = true, Description = "Хрупкий и ценный груз" },
            new CargoTypeEntity { CargoTypeId = 4, Name = "Продукты", MaxWeight = 15m, IsFragile = true, Description = "Срочные продукты питания" }
        });

        _couriers.AddRange(new[]
        {
            new CourierProfile { CourierId = 1, UserId = 3, TransportType = CourierTransport.Auto, VehicleNumber = "А123ВС78", ZoneId = 1, WorkSchedule = "Пн-Пт 09:00-18:00", Rating = 4.8m, IsActive = true },
            new CourierProfile { CourierId = 2, UserId = 4, TransportType = CourierTransport.OnFoot, VehicleNumber = null, ZoneId = 1, WorkSchedule = "Пн-Сб 10:00-20:00", Rating = 4.6m, IsActive = true }
        });

        _tariffs.AddRange(new[]
        {
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 1, ZoneId = 1, PricePerKg = 50, PricePerKm = 10, UrgentMarkup = 200 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 1, ZoneId = 2, PricePerKg = 60, PricePerKm = 15, UrgentMarkup = 250 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 1, ZoneId = 3, PricePerKg = 80, PricePerKm = 20, UrgentMarkup = 300 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 2, ZoneId = 1, PricePerKg = 40, PricePerKm = 10, UrgentMarkup = 150 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 2, ZoneId = 2, PricePerKg = 50, PricePerKm = 15, UrgentMarkup = 200 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 2, ZoneId = 3, PricePerKg = 70, PricePerKm = 20, UrgentMarkup = 250 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 3, ZoneId = 1, PricePerKg = 100, PricePerKm = 15, UrgentMarkup = 400 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 3, ZoneId = 2, PricePerKg = 120, PricePerKm = 20, UrgentMarkup = 500 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 3, ZoneId = 3, PricePerKg = 150, PricePerKm = 25, UrgentMarkup = 600 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 4, ZoneId = 1, PricePerKg = 60, PricePerKm = 12, UrgentMarkup = 300 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 4, ZoneId = 2, PricePerKg = 75, PricePerKm = 18, UrgentMarkup = 350 },
            new TariffEntity { TariffId = _nextTariffId++, CargoTypeId = 4, ZoneId = 3, PricePerKg = 90, PricePerKm = 22, UrgentMarkup = 450 }
        });

        var dispatcherId = 2;
        AddOrderInternal(new OrderUpsertRequest
        {
            ClientId = 1,
            ZoneId = 1,
            CargoTypeId = 1,
            RecipientName = "Сидоров Д.Н.",
            RecipientPhone = "+79110000001",
            RecipientAddress = "г. СПб, ул. Садовая, д. 5",
            PickupAddress = "г. СПб, ул. Ленина, д. 1",
            DeliveryAddress = "г. СПб, ул. Садовая, д. 5",
            Weight = 0.5m,
            IsUrgent = false,
            DesiredDeliveryDate = DateTime.Today.AddDays(1),
            Notes = "Документы в конверте"
        }, dispatcherId, OrderStatus.New, daysOffset: 0);

        AddOrderInternal(new OrderUpsertRequest
        {
            ClientId = 2,
            ZoneId = 1,
            CargoTypeId = 3,
            AssignedCourierId = 1,
            RecipientName = "ООО Бета",
            RecipientPhone = "+79220000002",
            RecipientAddress = "г. СПб, пр. Энтузиастов, д. 20",
            PickupAddress = "г. СПб, ул. Невский, д. 10",
            DeliveryAddress = "г. СПб, пр. Энтузиастов, д. 20",
            Weight = 5m,
            IsUrgent = true,
            DesiredDeliveryDate = DateTime.Today.AddDays(2),
            Notes = "Хрупкий товар"
        }, dispatcherId, OrderStatus.Assigned, daysOffset: 1);

        AddOrderInternal(new OrderUpsertRequest
        {
            ClientId = 1,
            ZoneId = 2,
            CargoTypeId = 2,
            AssignedCourierId = 2,
            RecipientName = "Иванова Е.П.",
            RecipientPhone = "+79330000003",
            RecipientAddress = "г. СПб, ул. Пушкина, д. 3",
            PickupAddress = "г. СПб, ул. Невский, д. 10",
            DeliveryAddress = "г. СПб, ул. Пушкина, д. 3",
            Weight = 8m,
            IsUrgent = false,
            DesiredDeliveryDate = DateTime.Today.AddDays(1),
            Notes = "Крупная посылка"
        }, dispatcherId, OrderStatus.InTransit, daysOffset: -1);

        AddOrderInternal(new OrderUpsertRequest
        {
            ClientId = 2,
            ZoneId = 1,
            CargoTypeId = 4,
            AssignedCourierId = 1,
            RecipientName = "Петров С.М.",
            RecipientPhone = "+79440000004",
            RecipientAddress = "г. СПб, ул. Лесная, д. 8",
            PickupAddress = "г. СПб, ул. Ленина, д. 1",
            DeliveryAddress = "г. СПб, ул. Лесная, д. 8",
            Weight = 12m,
            IsUrgent = false,
            DesiredDeliveryDate = DateTime.Today.AddDays(-1),
            Notes = "Ожидает закрытия"
        }, dispatcherId, OrderStatus.Delivered, daysOffset: -2);

        AddOrderInternal(new OrderUpsertRequest
        {
            ClientId = 1,
            ZoneId = 3,
            CargoTypeId = 2,
            RecipientName = "Клиент Область",
            RecipientPhone = "+79550000005",
            RecipientAddress = "Ленинградская обл., Выборгский р-н",
            PickupAddress = "г. СПб, ул. Невский, д. 10",
            DeliveryAddress = "Ленинградская обл., Выборгский р-н",
            Weight = 6m,
            IsUrgent = true,
            DesiredDeliveryDate = DateTime.Today.AddDays(3),
            Notes = "Срочный выезд"
        }, dispatcherId, OrderStatus.New, daysOffset: -1);

        _notifications.Add(new NotificationEntry { NotificationId = _nextNotificationId++, UserId = 2, Text = "Открыта смена диспетчера FastDelivery.", CreatedAt = DateTime.Now.AddMinutes(-40), IsRead = false });
        _notifications.Add(new NotificationEntry { NotificationId = _nextNotificationId++, UserId = 3, Text = "Вам назначен заказ TRK-00002.", CreatedAt = DateTime.Now.AddMinutes(-25), IsRead = false });
        _notifications.Add(new NotificationEntry { NotificationId = _nextNotificationId++, UserId = 5, Text = "Заказ TRK-00004 доставлен.", CreatedAt = DateTime.Now.AddMinutes(-15), IsRead = true });

        UpdateCourierLoads();
        UpdateOrderLabels();
    }

    private void AddUser(string login, string password, string fullName, string email, string phone, UserRole role)
    {
        _users.Add(new AppUser
        {
            UserId = _nextUserId++,
            Login = login,
            PasswordHash = password,
            FullName = fullName,
            Email = email,
            Phone = phone,
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.Now.AddDays(-30 + _nextUserId)
        });
    }

    private void AddOrderInternal(OrderUpsertRequest request, int dispatcherId, OrderStatus status, int daysOffset)
    {
        var order = new OrderEntity
        {
            OrderId = _nextOrderId++,
            TrackingNumber = $"TRK-{_nextOrderId:00000}",
            ClientId = request.ClientId,
            ZoneId = request.ZoneId,
            CargoTypeId = request.CargoTypeId,
            AssignedCourierId = request.AssignedCourierId,
            DispatcherId = dispatcherId,
            RecipientName = request.RecipientName,
            RecipientPhone = request.RecipientPhone,
            RecipientAddress = request.RecipientAddress,
            PickupAddress = request.PickupAddress,
            DeliveryAddress = request.DeliveryAddress,
            Weight = request.Weight,
            Volume = request.Volume,
            IsUrgent = request.IsUrgent,
            CreatedAt = DateTime.Now.AddDays(daysOffset),
            DesiredDeliveryDate = request.DesiredDeliveryDate,
            Status = status,
            TotalCost = CalculateCost(request.CargoTypeId, request.Weight, request.ZoneId, request.IsUrgent),
            Notes = request.Notes
        };

        _orders.Add(order);
        AddHistory(order.OrderId, OrderStatus.New, "Заказ создан");
        if (status != OrderStatus.New)
        {
            AddHistory(order.OrderId, status, $"Статус изменён на {GetStatusName(status)}");
        }
    }

    private void AddHistory(int orderId, OrderStatus status, string comment, decimal? latitude = null, decimal? longitude = null)
    {
        _history.Add(new OrderStatusHistoryEntry
        {
            HistoryId = _nextHistoryId++,
            OrderId = orderId,
            Status = status,
            ChangedAt = DateTime.Now,
            Latitude = latitude,
            Longitude = longitude,
            Comment = comment
        });
    }

    private void AddNotification(int userId, string text)
    {
        _notifications.Add(new NotificationEntry
        {
            NotificationId = _nextNotificationId++,
            UserId = userId,
            Text = text,
            CreatedAt = DateTime.Now,
            IsRead = false
        });
    }

    private void AddNotificationForRole(UserRole role, string text)
    {
        foreach (var user in _users.Where(x => x.Role == role))
        {
            AddNotification(user.UserId, text);
        }
    }

    private int ResolveClientId(int requestedClientId, AppUser currentUser)
    {
        if (currentUser.Role == UserRole.Client)
        {
            var client = _clients.FirstOrDefault(x => x.UserId == currentUser.UserId) ?? throw new InvalidOperationException("Для клиента не найден профиль.");
            return client.ClientId;
        }

        if (requestedClientId > 0)
        {
            return requestedClientId;
        }

        return _clients.First().ClientId;
    }

    private void UpdateOrderLabels()
    {
        foreach (var order in _orders)
        {
            order.ClientName = GetClientName(order.ClientId);
            order.CourierName = order.AssignedCourierId.HasValue ? GetCourierName(order.AssignedCourierId.Value) : "Не назначен";
            order.ZoneName = GetZoneName(order.ZoneId);
            order.CargoTypeName = GetCargoTypeName(order.CargoTypeId);
        }
    }

    private void UpdateCourierLoads()
    {
        foreach (var courier in _couriers)
        {
            courier.ActiveOrders = _orders.Count(x => x.AssignedCourierId == courier.CourierId && x.Status is not OrderStatus.Delivered and not OrderStatus.Cancelled);
        }
    }

    private OrderEntity CloneOrder(OrderEntity source)
    {
        return new OrderEntity
        {
            OrderId = source.OrderId,
            TrackingNumber = source.TrackingNumber,
            ClientId = source.ClientId,
            ZoneId = source.ZoneId,
            CargoTypeId = source.CargoTypeId,
            AssignedCourierId = source.AssignedCourierId,
            DispatcherId = source.DispatcherId,
            RecipientName = source.RecipientName,
            RecipientPhone = source.RecipientPhone,
            RecipientAddress = source.RecipientAddress,
            PickupAddress = source.PickupAddress,
            DeliveryAddress = source.DeliveryAddress,
            Weight = source.Weight,
            Volume = source.Volume,
            IsUrgent = source.IsUrgent,
            CreatedAt = source.CreatedAt,
            DesiredDeliveryDate = source.DesiredDeliveryDate,
            Status = source.Status,
            TotalCost = source.TotalCost,
            Notes = source.Notes,
            ClientName = source.ClientName,
            CourierName = source.CourierName,
            ZoneName = source.ZoneName,
            CargoTypeName = source.CargoTypeName
        };
    }

    private string GenerateTrackingNumber()
    {
        return $"TRK-{_nextOrderId:00000}";
    }
}