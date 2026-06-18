using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FastDelivery;

public partial class MainWindow : Window
{
    private readonly FastDeliveryStore _store = FastDeliveryStore.Instance;

    private readonly ObservableCollection<OrderEntity> _orders = new();
    private readonly ObservableCollection<CourierProfile> _couriers = new();
    private readonly ObservableCollection<ClientProfile> _clients = new();
    private readonly ObservableCollection<ZoneEntity> _zones = new();
    private readonly ObservableCollection<CargoTypeEntity> _cargoTypes = new();
    private readonly ObservableCollection<TariffEntity> _tariffs = new();
    private readonly ObservableCollection<OrderStatusHistoryEntry> _history = new();
    private readonly ObservableCollection<NotificationEntry> _notifications = new();
    private readonly ObservableCollection<CourierEfficiencyRow> _efficiencyRows = new();
    private readonly ObservableCollection<StatusSummaryRow> _statusSummaryRows = new();

    private OrderEntity? _selectedOrder;
    private CourierProfile? _selectedCourier;
    private NotificationEntry? _selectedNotification;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Session.CurrentUser is null)
        {
            Close();
            return;
        }

        OrdersGrid.ItemsSource = _orders;
        CouriersGrid.ItemsSource = _couriers;
        ClientBox.ItemsSource = _clients;
        CourierBox.ItemsSource = _couriers;
        ZoneBox.ItemsSource = _zones;
        CargoBox.ItemsSource = _cargoTypes;
        CourierZoneBox.ItemsSource = _zones;
        CourierTransportBox.ItemsSource = Enum.GetValues<CourierTransport>();
        ZonesGrid.ItemsSource = _zones;
        TariffsGrid.ItemsSource = _tariffs;
        HistoryGrid.ItemsSource = _history;
        NotificationsGrid.ItemsSource = _notifications;
        EfficiencyGrid.ItemsSource = _efficiencyRows;
        StatusSummaryGrid.ItemsSource = _statusSummaryRows;
        OrderStatusFilterBox.ItemsSource = new[] { "Все" }.Concat(Enum.GetValues<OrderStatus>().Select(x => _store.GetStatusName(x))).ToList();
        OrderStatusFilterBox.SelectedIndex = 0;

        RoleText.Text = $"Роль: {_store.GetRoleName(Session.CurrentUser.Role)}";
        UserText.Text = $"Пользователь: {Session.CurrentUser.FullName} · {Session.CurrentUser.Email}";

        ApplyRoleVisibility();
        ReloadAll();
    }

    private void ApplyRoleVisibility()
    {
        if (Session.CurrentUser is null)
        {
            return;
        }

        var role = Session.CurrentUser.Role;
        CatalogsTab.Visibility = role is UserRole.Admin or UserRole.Dispatcher ? Visibility.Visible : Visibility.Collapsed;
        CouriersTab.Visibility = role is UserRole.Admin or UserRole.Dispatcher ? Visibility.Visible : Visibility.Collapsed;
        ReportsTab.Visibility = Visibility.Visible;
        NotificationsTab.Visibility = Visibility.Visible;
        OrdersTab.Visibility = Visibility.Visible;
    }

    private void ReloadAll()
    {
        LoadReferenceData();
        ReloadOrders();
        ReloadCouriers();
        ReloadReports();
        ReloadNotifications();
        ReloadCatalogs();
        SelectFirstOrderIfNeeded();
    }

    private void LoadReferenceData()
    {
        _clients.Clear();
        foreach (var item in _store.GetClientProfiles())
        {
            _clients.Add(item);
        }

        _couriers.Clear();
        foreach (var item in _store.GetCourierProfiles())
        {
            _couriers.Add(item);
        }

        _zones.Clear();
        foreach (var item in _store.GetZones())
        {
            _zones.Add(item);
        }

        _cargoTypes.Clear();
        foreach (var item in _store.GetCargoTypes())
        {
            _cargoTypes.Add(item);
        }

        _tariffs.Clear();
        foreach (var item in _store.GetTariffs())
        {
            _tariffs.Add(item);
        }
    }

    private void ReloadOrders()
    {
        var source = _store.GetVisibleOrders(Session.CurrentUser!);
        source = ApplyOrderFilter(source).ToList();

        _orders.Clear();
        foreach (var order in source)
        {
            _orders.Add(order);
        }

        OrdersInfoText.Text = $"Показано {_orders.Count} заказов";
    }

    private void ReloadCouriers()
    {
        var currentId = _selectedCourier?.CourierId;

        _couriers.Clear();
        foreach (var item in _store.GetCourierProfiles())
        {
            _couriers.Add(item);
        }

        if (currentId.HasValue)
        {
            var courier = _couriers.FirstOrDefault(x => x.CourierId == currentId.Value);
            if (courier is not null)
            {
                CouriersGrid.SelectedItem = courier;
                CourierBox.SelectedItem = courier;
            }
        }
    }

    private void ReloadCatalogs()
    {
        ZonesGrid.ItemsSource = _zones;
        TariffsGrid.ItemsSource = _tariffs;
    }

    private void ReloadReports()
    {
        _efficiencyRows.Clear();
        foreach (var row in _store.GetCourierEfficiencyRows())
        {
            _efficiencyRows.Add(row);
        }

        _statusSummaryRows.Clear();
        foreach (var row in _store.GetStatusSummaryRows())
        {
            _statusSummaryRows.Add(row);
        }

        TotalOrdersText.Text = _store.Orders.Count.ToString(CultureInfo.InvariantCulture);
        ActiveOrdersText.Text = _store.Orders.Count(x => x.Status is OrderStatus.New or OrderStatus.Assigned or OrderStatus.InTransit or OrderStatus.AtRecipient).ToString(CultureInfo.InvariantCulture);
        DeliveredOrdersText.Text = _store.Orders.Count(x => x.Status == OrderStatus.Delivered).ToString(CultureInfo.InvariantCulture);
        RevenueText.Text = $"{_store.Orders.Sum(x => x.TotalCost):N0} ₽";
    }

    private void ReloadNotifications()
    {
        _notifications.Clear();
        foreach (var item in _store.GetNotificationsForUser(Session.CurrentUser!.UserId))
        {
            _notifications.Add(item);
        }
    }

    private IEnumerable<OrderEntity> ApplyOrderFilter(IEnumerable<OrderEntity> source)
    {
        var statusFilter = OrderStatusFilterBox.SelectedItem as string ?? "Все";
        var search = OrderSearchBox.Text.Trim();

        if (statusFilter != "Все")
        {
            source = source.Where(x => _store.GetStatusName(x.Status) == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            source = source.Where(x =>
                x.TrackingNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.ClientName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.RecipientName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.DeliveryAddress.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return source;
    }

    private void SelectFirstOrderIfNeeded()
    {
        if (_orders.Count > 0 && OrdersGrid.SelectedItem is null)
        {
            OrdersGrid.SelectedIndex = 0;
        }
    }

    private void LoadOrderToForm(OrderEntity? order)
    {
        _selectedOrder = order;

        if (order is null)
        {
            OrderIdBox.Text = string.Empty;
            TrackingBox.Text = string.Empty;
            ClientBox.SelectedIndex = -1;
            CourierBox.SelectedIndex = -1;
            ZoneBox.SelectedIndex = -1;
            CargoBox.SelectedIndex = -1;
            WeightBox.Text = string.Empty;
            VolumeBox.Text = string.Empty;
            RecipientNameBox.Text = string.Empty;
            RecipientPhoneBox.Text = string.Empty;
            RecipientAddressBox.Text = string.Empty;
            PickupAddressBox.Text = string.Empty;
            DeliveryAddressBox.Text = string.Empty;
            DesiredDateBox.SelectedDate = DateTime.Today.AddDays(1);
            CostBox.Text = string.Empty;
            UrgentBox.IsChecked = false;
            NotesBox.Text = string.Empty;
            SelectedOrderSummary.Text = string.Empty;
            _history.Clear();
            return;
        }

        OrderIdBox.Text = order.OrderId.ToString(CultureInfo.InvariantCulture);
        TrackingBox.Text = order.TrackingNumber;
        ClientBox.SelectedValue = order.ClientId;
        CourierBox.SelectedValue = order.AssignedCourierId;
        ZoneBox.SelectedValue = order.ZoneId;
        CargoBox.SelectedValue = order.CargoTypeId;
        WeightBox.Text = order.Weight.ToString("0.##", CultureInfo.InvariantCulture);
        VolumeBox.Text = order.Volume?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        RecipientNameBox.Text = order.RecipientName;
        RecipientPhoneBox.Text = order.RecipientPhone;
        RecipientAddressBox.Text = order.RecipientAddress;
        PickupAddressBox.Text = order.PickupAddress;
        DeliveryAddressBox.Text = order.DeliveryAddress;
        DesiredDateBox.SelectedDate = order.DesiredDeliveryDate?.Date;
        CostBox.Text = $"{order.TotalCost:N2} ₽";
        UrgentBox.IsChecked = order.IsUrgent;
        NotesBox.Text = order.Notes;

        _history.Clear();
        foreach (var item in _store.GetHistoryForOrder(order.OrderId))
        {
            _history.Add(item);
        }

        SelectedOrderSummary.Text = $"Статус: {_store.GetStatusName(order.Status)}\nКурьер: {order.CourierName}\nЗона: {order.ZoneName}\nПолучатель: {order.RecipientName}";
    }

    private OrderUpsertRequest ReadOrderForm()
    {
        if (ZoneBox.SelectedValue is not int zoneId)
        {
            throw new InvalidOperationException("Выберите зону доставки.");
        }

        if (CargoBox.SelectedValue is not int cargoTypeId)
        {
            throw new InvalidOperationException("Выберите тип груза.");
        }

        if (ClientBox.SelectedValue is not int clientId)
        {
            throw new InvalidOperationException("Выберите клиента.");
        }

        if (!decimal.TryParse(WeightBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var weight))
        {
            throw new InvalidOperationException("Укажите корректный вес.");
        }

        decimal? volume = null;
        if (!string.IsNullOrWhiteSpace(VolumeBox.Text) &&
            decimal.TryParse(VolumeBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVolume))
        {
            volume = parsedVolume;
        }

        int? courierId = CourierBox.SelectedValue is int selectedCourierId ? selectedCourierId : null;

        return new OrderUpsertRequest
        {
            OrderId = _selectedOrder?.OrderId ?? 0,
            ClientId = clientId,
            ZoneId = zoneId,
            CargoTypeId = cargoTypeId,
            AssignedCourierId = courierId,
            DispatcherId = Session.CurrentUser!.Role is UserRole.Dispatcher or UserRole.Admin ? Session.CurrentUser.UserId : _selectedOrder?.DispatcherId,
            RecipientName = RecipientNameBox.Text.Trim(),
            RecipientPhone = RecipientPhoneBox.Text.Trim(),
            RecipientAddress = RecipientAddressBox.Text.Trim(),
            PickupAddress = PickupAddressBox.Text.Trim(),
            DeliveryAddress = DeliveryAddressBox.Text.Trim(),
            Weight = weight,
            Volume = volume,
            IsUrgent = UrgentBox.IsChecked == true,
            DesiredDeliveryDate = DesiredDateBox.SelectedDate,
            Notes = NotesBox.Text.Trim()
        };
    }

    private void RefreshAfterOrderChange(int orderId)
    {
        ReloadAll();
        var current = _store.GetOrderById(orderId);
        if (current is not null)
        {
            LoadOrderToForm(current);
        }
        ReloadReports();
        ReloadNotifications();
    }

    private void ReloadAll_Click(object sender, RoutedEventArgs e)
    {
        ReloadAll();
    }

    private void MarkAllNotificationsRead_Click(object sender, RoutedEventArgs e)
    {
        _store.MarkAllNotificationsRead(Session.CurrentUser!.UserId);
        ReloadNotifications();
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        Session.CurrentUser = null;
        Session.CurrentRoleName = null;

        var login = new LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();
        Close();
    }

    private void NewOrder_Click(object sender, RoutedEventArgs e) => LoadOrderToForm(null);

    private void SaveOrder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = ReadOrderForm();
            var order = _store.CreateOrUpdateOrder(request, Session.CurrentUser!);
            RefreshAfterOrderChange(order.OrderId);
            MessageBox.Show($"Заказ {order.TrackingNumber} сохранен.", "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrder is null)
        {
            return;
        }

        try
        {
            _store.DeleteOrder(_selectedOrder.OrderId);
            ReloadAll();
            LoadOrderToForm(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AssignCourier_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrder is null)
        {
            return;
        }

        try
        {
            var courierId = CourierBox.SelectedValue is int selectedCourierId ? selectedCourierId : (int?)null;
            var order = _store.AssignCourier(_selectedOrder.OrderId, courierId, Session.CurrentUser!.UserId);
            RefreshAfterOrderChange(order.OrderId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartTransit_Click(object sender, RoutedEventArgs e) => ChangeOrderStatus(OrderStatus.InTransit, "Заказ передан курьеру");

    private void ArriveRecipient_Click(object sender, RoutedEventArgs e) => ChangeOrderStatus(OrderStatus.AtRecipient, "Курьер прибыл на адрес");

    private void DeliverOrder_Click(object sender, RoutedEventArgs e) => ChangeOrderStatus(OrderStatus.Delivered, "Заказ доставлен вручную в MVP");

    private void CancelOrder_Click(object sender, RoutedEventArgs e) => ChangeOrderStatus(OrderStatus.Cancelled, "Заказ отменён");

    private void ChangeOrderStatus(OrderStatus status, string comment)
    {
        if (_selectedOrder is null)
        {
            return;
        }

        try
        {
            var order = _store.SetOrderStatus(_selectedOrder.OrderId, status, comment);
            RefreshAfterOrderChange(order.OrderId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OrdersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadOrderToForm(OrdersGrid.SelectedItem as OrderEntity);
    }

    private void OrderFilter_Changed(object sender, RoutedEventArgs e)
    {
        ReloadOrders();
    }

    private void OrderFilter_Changed(object sender, TextChangedEventArgs e)
    {
        ReloadOrders();
    }

    private void OrderCost_Recalculate(object sender, RoutedEventArgs e) => RecalculateOrderCost();

    private void OrderCost_Recalculate(object sender, SelectionChangedEventArgs e) => RecalculateOrderCost();

    private void OrderCost_Recalculate(object sender, TextChangedEventArgs e) => RecalculateOrderCost();

    private void RecalculateOrderCost()
    {
        try
        {
            if (ZoneBox.SelectedValue is not int zoneId || CargoBox.SelectedValue is not int cargoTypeId)
            {
                CostBox.Text = string.Empty;
                return;
            }

            if (!decimal.TryParse(WeightBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var weight) || weight <= 0)
            {
                CostBox.Text = string.Empty;
                return;
            }

            var cost = _store.CalculateCost(cargoTypeId, weight, zoneId, UrgentBox.IsChecked == true);
            CostBox.Text = $"{cost:N2} ₽";
        }
        catch
        {
            CostBox.Text = string.Empty;
        }
    }

    private void NewCourier_Click(object sender, RoutedEventArgs e)
    {
        _selectedCourier = null;
        CourierIdBox.Text = string.Empty;
        CourierNameBox.Text = string.Empty;
        CourierLoginBox.Text = string.Empty;
        CourierPasswordBox.Text = string.Empty;
        CourierEmailBox.Text = string.Empty;
        CourierPhoneBox.Text = string.Empty;
        CourierZoneBox.SelectedIndex = 0;
        CourierTransportBox.SelectedIndex = 0;
        VehicleNumberBox.Text = string.Empty;
        WorkScheduleBox.Text = "Пн-Пт 09:00-18:00";
        RatingBox.Text = "4.5";
        CourierHintText.Text = "Заполните карточку курьера и сохраните её.";
    }

    private void SaveCourier_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CourierZoneBox.SelectedValue is not int zoneId)
            {
                throw new InvalidOperationException("Выберите зону доставки.");
            }

            if (CourierTransportBox.SelectedItem is not CourierTransport transport)
            {
                throw new InvalidOperationException("Выберите тип транспорта.");
            }

            if (!decimal.TryParse(RatingBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var rating))
            {
                throw new InvalidOperationException("Укажите корректный рейтинг.");
            }

            var request = new CourierUpsertRequest
            {
                CourierId = _selectedCourier?.CourierId ?? 0,
                FullName = CourierNameBox.Text.Trim(),
                Login = CourierLoginBox.Text.Trim(),
                Password = CourierPasswordBox.Text,
                Email = CourierEmailBox.Text.Trim(),
                Phone = CourierPhoneBox.Text.Trim(),
                TransportType = transport,
                VehicleNumber = VehicleNumberBox.Text.Trim(),
                ZoneId = zoneId,
                WorkSchedule = WorkScheduleBox.Text.Trim(),
                Rating = rating
            };

            _store.SaveCourier(request);
            ReloadAll();
            MessageBox.Show("Курьер сохранен.", "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteCourier_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCourier is null)
        {
            return;
        }

        try
        {
            _store.DeleteCourier(_selectedCourier.CourierId);
            ReloadAll();
            NewCourier_Click(sender, e);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CouriersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedCourier = CouriersGrid.SelectedItem as CourierProfile;
        if (_selectedCourier is null)
        {
            return;
        }

        var user = _store.Users.First(x => x.UserId == _selectedCourier.UserId);
        CourierIdBox.Text = _selectedCourier.CourierId.ToString(CultureInfo.InvariantCulture);
        CourierNameBox.Text = user.FullName;
        CourierLoginBox.Text = user.Login;
        CourierPasswordBox.Text = user.PasswordHash;
        CourierEmailBox.Text = user.Email;
        CourierPhoneBox.Text = user.Phone;
        CourierZoneBox.SelectedValue = _selectedCourier.ZoneId;
        CourierTransportBox.SelectedItem = _selectedCourier.TransportType;
        VehicleNumberBox.Text = _selectedCourier.VehicleNumber ?? string.Empty;
        WorkScheduleBox.Text = _selectedCourier.WorkSchedule;
        RatingBox.Text = _selectedCourier.Rating.ToString("0.0", CultureInfo.InvariantCulture);
        CourierHintText.Text = $"Активных заказов: {_selectedCourier.ActiveOrders}";
    }

    private void SaveCatalogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.SaveCatalogs(_zones, _tariffs);
            ReloadAll();
            MessageBox.Show("Справочники сохранены.", "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReloadReports_Click(object sender, RoutedEventArgs e)
    {
        ReloadReports();
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var lines = new List<string>
            {
                "CourierName;TotalDeliveries;AverageMinutes;LateDeliveries;Rating"
            };

            lines.AddRange(_efficiencyRows.Select(x => $"{x.CourierName};{x.TotalDeliveries};{x.AverageMinutes};{x.LateDeliveries};{x.Rating}"));
            lines.Add(string.Empty);
            lines.Add("Status;Count");
            lines.AddRange(_statusSummaryRows.Select(x => $"{x.Status};{x.Count}"));

            var path = Path.Combine(Environment.CurrentDirectory, "FastDelivery_Report.csv");
            File.WriteAllLines(path, lines, Encoding.UTF8);
            MessageBox.Show($"Отчет сохранен: {path}", "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NotificationsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedNotification = NotificationsGrid.SelectedItem as NotificationEntry;
    }

    private void MarkSelectedNotificationRead_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNotification is null)
        {
            return;
        }

        _store.MarkNotificationRead(_selectedNotification.NotificationId);
        ReloadNotifications();
    }
}