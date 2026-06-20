-- ======================================================================
-- Скрипт создания базы данных для ИС "ExpressLog: Автоматизация курьерской доставки"
-- Разработано для: Власов Всеволод, группа КИ-31
-- СУБД: MS SQL Server 2019+
-- Версия с улучшенными ограничениями, каскадными операциями и индексами
-- ======================================================================

-- 1. Создание базы данных (если не существует)
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'ExpressLog')
BEGIN
    CREATE DATABASE ExpressLog;
END;
GO

USE ExpressLog;
GO

-- ======================================================================
-- 2. Удаление существующих объектов (для идемпотентности, в порядке зависимостей)
-- ======================================================================
IF OBJECT_ID('trg_OrderStatusHistory_CheckDelivery', 'TR') IS NOT NULL DROP TRIGGER trg_OrderStatusHistory_CheckDelivery;
IF OBJECT_ID('usp_AssignCourier', 'P') IS NOT NULL DROP PROCEDURE usp_AssignCourier;
IF OBJECT_ID('fn_CalculateCost', 'FN') IS NOT NULL DROP FUNCTION fn_CalculateCost;
IF OBJECT_ID('vw_CourierEfficiency', 'V') IS NOT NULL DROP VIEW vw_CourierEfficiency;

-- Удаляем таблицы (с учётом внешних ключей)
IF OBJECT_ID('DeliveryConfirmations', 'U') IS NOT NULL DROP TABLE DeliveryConfirmations;
IF OBJECT_ID('RouteListOrders', 'U') IS NOT NULL DROP TABLE RouteListOrders;
IF OBJECT_ID('RouteLists', 'U') IS NOT NULL DROP TABLE RouteLists;
IF OBJECT_ID('OrderStatusHistory', 'U') IS NOT NULL DROP TABLE OrderStatusHistory;
IF OBJECT_ID('DeliverySessions', 'U') IS NOT NULL DROP TABLE DeliverySessions;
IF OBJECT_ID('Notifications', 'U') IS NOT NULL DROP TABLE Notifications;
IF OBJECT_ID('Orders', 'U') IS NOT NULL DROP TABLE Orders;
IF OBJECT_ID('Tariffs', 'U') IS NOT NULL DROP TABLE Tariffs;
IF OBJECT_ID('CargoTypes', 'U') IS NOT NULL DROP TABLE CargoTypes;
IF OBJECT_ID('Zones', 'U') IS NOT NULL DROP TABLE Zones;
IF OBJECT_ID('Couriers', 'U') IS NOT NULL DROP TABLE Couriers;
IF OBJECT_ID('Clients', 'U') IS NOT NULL DROP TABLE Clients;
IF OBJECT_ID('Users', 'U') IS NOT NULL DROP TABLE Users;
GO

-- ======================================================================
-- 3. Создание таблиц с улучшенными ограничениями
-- ======================================================================

-- 3.1. Пользователи (общая таблица)
CREATE TABLE Users (
    UserID INT IDENTITY(1,1) PRIMARY KEY,
    FullName NVARCHAR(200) NOT NULL,
    Email NVARCHAR(100) NOT NULL UNIQUE,
    Phone NVARCHAR(20) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(256) NOT NULL,
    Role NVARCHAR(20) NOT NULL CHECK (Role IN ('Client', 'Courier', 'Dispatcher', 'Admin')),
    IsActive BIT NOT NULL DEFAULT 1,
    RegistrationDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    -- Дополнительные проверки форматов
    CONSTRAINT CHK_Users_Email CHECK (Email LIKE '%_@__%.__%'), -- простейшая проверка email
    CONSTRAINT CHK_Users_Phone CHECK (Phone LIKE '+%' AND LEN(Phone) BETWEEN 10 AND 15) -- телефон начинается с + и длина 10-15
);
GO

-- 3.2. Клиенты (расширение Users)
CREATE TABLE Clients (
    ClientID INT PRIMARY KEY,
    UserID INT NOT NULL UNIQUE,
    LegalAddress NVARCHAR(200),
    ClientType NVARCHAR(20) NOT NULL CHECK (ClientType IN ('Individual', 'LegalEntity')),
    INN NVARCHAR(20) NULL,
    -- Если пользователь удалён, удаляем и клиента (каскад)
    CONSTRAINT FK_Clients_Users FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE CASCADE
);
GO

-- 3.3. Зоны доставки
CREATE TABLE Zones (
    ZoneID INT IDENTITY(1,1) PRIMARY KEY,
    ZoneName NVARCHAR(100) NOT NULL,
    City NVARCHAR(100) NOT NULL,
    Coefficient DECIMAL(5,2) NOT NULL CHECK (Coefficient >= 0)
);
GO

-- 3.4. Типы грузов
CREATE TABLE CargoTypes (
    CargoTypeID INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL,
    MaxWeight DECIMAL(8,2) NOT NULL CHECK (MaxWeight > 0),
    IsFragile BIT NOT NULL DEFAULT 0
);
GO

-- 3.5. Курьеры (расширение Users)
CREATE TABLE Couriers (
    CourierID INT PRIMARY KEY,
    UserID INT NOT NULL UNIQUE,
    TransportType NVARCHAR(20) NOT NULL CHECK (TransportType IN ('Auto', 'OnFoot', 'Bicycle')),
    VehicleNumber NVARCHAR(20) NULL,
    ZoneID INT NOT NULL,
    WorkSchedule NVARCHAR(100) NULL,
    Rating DECIMAL(3,2) NULL DEFAULT 0 CHECK (Rating BETWEEN 0 AND 5),
    -- Если пользователь удалён, удаляем и курьера
    CONSTRAINT FK_Couriers_Users FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE CASCADE,
    -- Если зона удалена, нельзя оставить курьера без зоны => запрет
    CONSTRAINT FK_Couriers_Zones FOREIGN KEY (ZoneID) REFERENCES Zones(ZoneID) ON DELETE NO ACTION
);
GO

-- 3.6. Тарифы
CREATE TABLE Tariffs (
    TariffID INT IDENTITY(1,1) PRIMARY KEY,
    CargoTypeID INT NOT NULL,
    ZoneID INT NOT NULL,
    PricePerKg DECIMAL(10,2) NOT NULL CHECK (PricePerKg >= 0),
    PricePerKm DECIMAL(10,2) NOT NULL CHECK (PricePerKm >= 0),
    SurchargeUrgent DECIMAL(10,2) NOT NULL DEFAULT 0 CHECK (SurchargeUrgent >= 0),
    -- Каскадное удаление при удалении типа груза или зоны
    CONSTRAINT FK_Tariffs_CargoTypes FOREIGN KEY (CargoTypeID) REFERENCES CargoTypes(CargoTypeID) ON DELETE CASCADE,
    CONSTRAINT FK_Tariffs_Zones FOREIGN KEY (ZoneID) REFERENCES Zones(ZoneID) ON DELETE CASCADE,
    CONSTRAINT UQ_Tariffs_Unique UNIQUE (CargoTypeID, ZoneID)
);
GO

-- 3.7. Заказы (добавлено поле ZoneID для точной привязки к зоне доставки)
CREATE TABLE Orders (
    OrderID INT IDENTITY(1,1) PRIMARY KEY,
    ClientID INT NOT NULL,
    ZoneID INT NOT NULL, -- добавлено
    RecipientName NVARCHAR(200) NOT NULL,
    RecipientPhone NVARCHAR(20) NOT NULL,
    RecipientAddress NVARCHAR(250) NOT NULL,
    CargoTypeID INT NOT NULL,
    Weight DECIMAL(8,2) NOT NULL CHECK (Weight > 0),
    Volume DECIMAL(8,2) NULL,
    PickupAddress NVARCHAR(250) NOT NULL,
    DeliveryAddress NVARCHAR(250) NOT NULL,
    IsUrgent BIT NOT NULL DEFAULT 0,
    CreatedDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    DesiredDeliveryDate DATE NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'New' CHECK (Status IN ('New', 'Assigned', 'Taken', 'InTransit', 'AtRecipient', 'Delivered', 'Cancelled')),
    AssignedCourierID INT NULL,
    DispatcherID INT NULL,
    TotalCost DECIMAL(10,2) NOT NULL CHECK (TotalCost >= 0),
    TrackingNumber NVARCHAR(20) NOT NULL UNIQUE,
    -- Внешние ключи с соответствующими действиями
    CONSTRAINT FK_Orders_Clients FOREIGN KEY (ClientID) REFERENCES Clients(ClientID) ON DELETE NO ACTION, -- не удаляем заказы при удалении клиента
    CONSTRAINT FK_Orders_Zones FOREIGN KEY (ZoneID) REFERENCES Zones(ZoneID) ON DELETE NO ACTION,
    CONSTRAINT FK_Orders_Couriers FOREIGN KEY (AssignedCourierID) REFERENCES Couriers(CourierID) ON DELETE SET NULL, -- при удалении курьера оставляем заказ
    CONSTRAINT FK_Orders_Users_Dispatcher FOREIGN KEY (DispatcherID) REFERENCES Users(UserID) ON DELETE SET NULL,
    CONSTRAINT FK_Orders_CargoTypes FOREIGN KEY (CargoTypeID) REFERENCES CargoTypes(CargoTypeID) ON DELETE NO ACTION
);
GO

-- 3.8. Сессии доставки (QR-сессии)
CREATE TABLE DeliverySessions (
    SessionID INT IDENTITY(1,1) PRIMARY KEY,
    OrderID INT NOT NULL,
    GeneratedDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    Token NVARCHAR(100) NOT NULL UNIQUE,
    StartTime DATETIME2 NOT NULL,
    EndTime DATETIME2 NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Active' CHECK (Status IN ('Active', 'Used', 'Expired')),
    -- При удалении заказа сессии удаляются каскадно
    CONSTRAINT FK_DeliverySessions_Orders FOREIGN KEY (OrderID) REFERENCES Orders(OrderID) ON DELETE CASCADE
);
GO

-- 3.9. История статусов заказа (аудит)
CREATE TABLE OrderStatusHistory (
    HistoryID INT IDENTITY(1,1) PRIMARY KEY,
    OrderID INT NOT NULL,
    Status NVARCHAR(20) NOT NULL CHECK (Status IN ('New', 'Assigned', 'Taken', 'InTransit', 'AtRecipient', 'Delivered', 'Cancelled')),
    ChangeDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    Latitude DECIMAL(10,7) NULL,
    Longitude DECIMAL(10,7) NULL,
    Comment NVARCHAR(500) NULL,
    CONSTRAINT FK_OrderStatusHistory_Orders FOREIGN KEY (OrderID) REFERENCES Orders(OrderID) ON DELETE CASCADE
);
GO

-- 3.10. Подтверждение доставки
CREATE TABLE DeliveryConfirmations (
    ConfirmationID INT IDENTITY(1,1) PRIMARY KEY,
    SessionID INT NOT NULL,
    OrderID INT NOT NULL,
    ScanTime DATETIME2 NOT NULL DEFAULT GETDATE(),
    Latitude DECIMAL(10,7) NULL,
    Longitude DECIMAL(10,7) NULL,
    IPAddress NVARCHAR(45) NULL,
    Status NVARCHAR(20) NOT NULL CHECK (Status IN ('QR', 'Manual')),
    PhotoPath NVARCHAR(500) NULL,
    -- Удаление сессии или заказа приводит к удалению подтверждения (каскад)
    CONSTRAINT FK_DeliveryConfirmations_Sessions FOREIGN KEY (SessionID) REFERENCES DeliverySessions(SessionID) ON DELETE CASCADE,
    CONSTRAINT FK_DeliveryConfirmations_Orders FOREIGN KEY (OrderID) REFERENCES Orders(OrderID) ON DELETE CASCADE,
    CONSTRAINT UQ_DeliveryConfirmations_Order UNIQUE (OrderID) -- один заказ – одно подтверждение
);
GO

-- 3.11. Уведомления
CREATE TABLE Notifications (
    NotificationID INT IDENTITY(1,1) PRIMARY KEY,
    UserID INT NOT NULL,
    Text NVARCHAR(500) NOT NULL,
    Date DATETIME2 NOT NULL DEFAULT GETDATE(),
    IsRead BIT NOT NULL DEFAULT 0,
    CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserID) REFERENCES Users(UserID) ON DELETE CASCADE
);
GO

-- 3.12. Маршрутные листы
CREATE TABLE RouteLists (
    RouteListID INT IDENTITY(1,1) PRIMARY KEY,
    CourierID INT NOT NULL,
    Date DATE NOT NULL,
    CONSTRAINT FK_RouteLists_Couriers FOREIGN KEY (CourierID) REFERENCES Couriers(CourierID) ON DELETE CASCADE
);
GO

-- 3.13. Связь заказов с маршрутными листами (порядок)
CREATE TABLE RouteListOrders (
    RouteListID INT NOT NULL,
    OrderID INT NOT NULL,
    SequenceNumber INT NOT NULL,
    PRIMARY KEY (RouteListID, OrderID),
    CONSTRAINT FK_RouteListOrders_RouteLists FOREIGN KEY (RouteListID) REFERENCES RouteLists(RouteListID) ON DELETE CASCADE,
    CONSTRAINT FK_RouteListOrders_Orders FOREIGN KEY (OrderID) REFERENCES Orders(OrderID) ON DELETE CASCADE
);
GO

-- ======================================================================
-- 4. Индексы (включая индексы для внешних ключей)
-- ======================================================================
-- Индексы для часто используемых полей и внешних ключей
CREATE INDEX IX_Orders_Status_CreatedDate ON Orders (Status, CreatedDate);
CREATE INDEX IX_Orders_TrackingNumber ON Orders (TrackingNumber);
CREATE INDEX IX_Orders_AssignedCourierID_CreatedDate ON Orders (AssignedCourierID, CreatedDate);
CREATE INDEX IX_Orders_ZoneID ON Orders (ZoneID); -- для JOIN с Zones
CREATE INDEX IX_Orders_ClientID ON Orders (ClientID);
CREATE INDEX IX_Orders_CargoTypeID ON Orders (CargoTypeID);

CREATE INDEX IX_DeliverySessions_Token ON DeliverySessions (Token) WHERE Status = 'Active';
CREATE INDEX IX_DeliverySessions_OrderID ON DeliverySessions (OrderID); -- внешний ключ

CREATE INDEX IX_OrderStatusHistory_OrderID_ChangeDate ON OrderStatusHistory (OrderID, ChangeDate DESC);

CREATE INDEX IX_Notifications_UserID_Date ON Notifications (UserID, Date DESC);
CREATE INDEX IX_Notifications_UserID ON Notifications (UserID); -- внешний ключ

CREATE INDEX IX_DeliveryConfirmations_OrderID ON DeliveryConfirmations (OrderID);
CREATE INDEX IX_DeliveryConfirmations_SessionID ON DeliveryConfirmations (SessionID);

CREATE INDEX IX_RouteListOrders_RouteListID ON RouteListOrders (RouteListID);
CREATE INDEX IX_RouteListOrders_OrderID ON RouteListOrders (OrderID);
GO

-- ======================================================================
-- 5. Функция расчёта стоимости (без изменений, но использует ZoneID из заказа)
-- ======================================================================
GO
CREATE FUNCTION fn_CalculateCost (
    @CargoTypeID INT,
    @Weight DECIMAL(8,2),
    @ZoneID INT,
    @IsUrgent BIT,
    @DistanceKm DECIMAL(8,2) = 10
)
RETURNS DECIMAL(10,2)
AS
BEGIN
    DECLARE @PricePerKg DECIMAL(10,2);
    DECLARE @PricePerKm DECIMAL(10,2);
    DECLARE @SurchargeUrgent DECIMAL(10,2);
    DECLARE @Total DECIMAL(10,2);

    SELECT @PricePerKg = PricePerKg, @PricePerKm = PricePerKm, @SurchargeUrgent = SurchargeUrgent
    FROM Tariffs
    WHERE CargoTypeID = @CargoTypeID AND ZoneID = @ZoneID;

    IF @PricePerKg IS NULL
        RETURN 0;

    SET @Total = (@PricePerKg * @Weight) + (@PricePerKm * @DistanceKm);
    IF @IsUrgent = 1
        SET @Total = @Total + @SurchargeUrgent;

    RETURN @Total;
END;
GO

-- ======================================================================
-- 6. Хранимая процедура назначения курьера (исправлена логика поиска зоны)
-- ======================================================================
GO
CREATE PROCEDURE usp_AssignCourier
    @OrderID INT,
    @DispatcherID INT,
    @CourierID INT OUTPUT,
    @Token NVARCHAR(100) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;

    -- Проверяем заказ и получаем ZoneID
    DECLARE @ZoneID INT;
    SELECT @ZoneID = ZoneID
    FROM Orders
    WHERE OrderID = @OrderID AND Status = 'New';

    IF @ZoneID IS NULL
    BEGIN
        RAISERROR('Заказ не найден или не в статусе "Новый"', 16, 1);
        ROLLBACK;
        RETURN;
    END

    -- Находим курьера с наименьшим количеством активных заказов в данной зоне
    SELECT TOP 1 @CourierID = c.CourierID
    FROM Couriers c
    LEFT JOIN Orders o ON c.CourierID = o.AssignedCourierID AND o.Status NOT IN ('Delivered', 'Cancelled')
    WHERE c.ZoneID = @ZoneID
    GROUP BY c.CourierID
    ORDER BY COUNT(o.OrderID) ASC;

    IF @CourierID IS NULL
    BEGIN
        RAISERROR('Нет доступных курьеров в данной зоне', 16, 1);
        ROLLBACK;
        RETURN;
    END

    -- Обновляем заказ
    UPDATE Orders
    SET AssignedCourierID = @CourierID,
        DispatcherID = @DispatcherID,
        Status = 'Assigned'
    WHERE OrderID = @OrderID;

    -- Создаём сессию доставки
    SET @Token = CONCAT('QR', FORMAT(GETDATE(), 'yyyyMMddHHmmss'), '_', @OrderID, '_', @CourierID);

    INSERT INTO DeliverySessions (OrderID, GeneratedDate, Token, StartTime, EndTime, Status)
    VALUES (@OrderID, GETDATE(), @Token, GETDATE(), DATEADD(HOUR, 2, GETDATE()), 'Active');

    -- Записываем историю
    INSERT INTO OrderStatusHistory (OrderID, Status, ChangeDate, Comment)
    VALUES (@OrderID, 'Assigned', GETDATE(), CONCAT('Назначен курьер ID=', @CourierID));

    COMMIT TRANSACTION;
END;
GO

-- ======================================================================
-- 7. Триггер на проверку подтверждения доставки (улучшена проверка геолокации)
-- ======================================================================
GO
CREATE TRIGGER trg_OrderStatusHistory_CheckDelivery
ON OrderStatusHistory
INSTEAD OF INSERT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OrderID INT, @NewStatus NVARCHAR(20), @ChangeDate DATETIME2;
    DECLARE @HasConfirmation BIT = 0;
    DECLARE @DeliveryLat DECIMAL(10,7), @DeliveryLng DECIMAL(10,7);
    DECLARE @ConfirmLat DECIMAL(10,7), @ConfirmLng DECIMAL(10,7);
    DECLARE @Distance DECIMAL(10,7);
    DECLARE @Radius DECIMAL(10,7) = 0.1; -- примерный радиус в градусах (~10 км)

    SELECT @OrderID = OrderID, @NewStatus = Status, @ChangeDate = ChangeDate
    FROM inserted;

    IF @NewStatus = 'Delivered'
    BEGIN
        -- Проверяем, существует ли подтверждение для этого заказа
        IF EXISTS (SELECT 1 FROM DeliveryConfirmations WHERE OrderID = @OrderID)
        BEGIN
            -- Проверяем геолокацию (если есть)
            SELECT @ConfirmLat = Latitude, @ConfirmLng = Longitude
            FROM DeliveryConfirmations
            WHERE OrderID = @OrderID;

            -- В реальной системе координаты адреса доставки можно получать из внешнего сервиса.
            -- Здесь для примера используем фиктивные координаты (СПб)
            SET @DeliveryLat = 59.9343;
            SET @DeliveryLng = 30.3351;

            IF @ConfirmLat IS NOT NULL AND @ConfirmLng IS NOT NULL
            BEGIN
                -- Расчёт евклидова расстояния (упрощённо)
                SET @Distance = SQRT(POWER(@ConfirmLat - @DeliveryLat, 2) + POWER(@ConfirmLng - @DeliveryLng, 2));
                IF @Distance > @Radius
                BEGIN
                    RAISERROR('Геолокация подтверждения не совпадает с адресом доставки (слишком далеко)', 16, 1);
                    RETURN;
                END
            END
        END
        ELSE
        BEGIN
            RAISERROR('Невозможно установить статус "Доставлен" без подтверждения доставки', 16, 1);
            RETURN;
        END
    END

    -- Если все проверки пройдены, выполняем вставку
    INSERT INTO OrderStatusHistory (OrderID, Status, ChangeDate, Latitude, Longitude, Comment)
    SELECT OrderID, Status, ChangeDate, Latitude, Longitude, Comment
    FROM inserted;
END;
GO

-- ======================================================================
-- 8. Представление эффективности курьеров (без изменений)
-- ======================================================================
GO
CREATE VIEW vw_CourierEfficiency
AS
SELECT
    c.CourierID,
    u.FullName AS CourierName,
    COUNT(o.OrderID) AS TotalDeliveries,
    AVG(DATEDIFF(MINUTE, o.CreatedDate, dc.ScanTime)) AS AvgDeliveryMinutes,
    SUM(CASE WHEN o.Status = 'Delivered' AND DATEDIFF(HOUR, o.CreatedDate, dc.ScanTime) > 24 THEN 1 ELSE 0 END) AS LateDeliveries,
    (COUNT(o.OrderID) - SUM(CASE WHEN o.Status = 'Delivered' AND DATEDIFF(HOUR, o.CreatedDate, dc.ScanTime) > 24 THEN 1 ELSE 0 END)) * 1.0 / NULLIF(COUNT(o.OrderID), 0) * 100 AS SuccessRate
FROM Couriers c
INNER JOIN Users u ON c.UserID = u.UserID
LEFT JOIN Orders o ON c.CourierID = o.AssignedCourierID AND o.Status IN ('Delivered', 'Cancelled')
LEFT JOIN DeliveryConfirmations dc ON o.OrderID = dc.OrderID
WHERE o.CreatedDate >= DATEADD(DAY, -30, GETDATE())
GROUP BY c.CourierID, u.FullName;
GO

-- ======================================================================
-- 9. Начальное заполнение справочников и тестовых данных (с учётом нового поля ZoneID)
-- ======================================================================

-- 9.1. Зоны доставки
SET IDENTITY_INSERT Zones ON;
INSERT INTO Zones (ZoneID, ZoneName, City, Coefficient) VALUES
(1, 'Центр', 'Санкт-Петербург', 1.0),
(2, 'Пригород', 'Санкт-Петербург', 1.5),
(3, 'Область', 'Ленинградская обл.', 2.0);
SET IDENTITY_INSERT Zones OFF;

-- 9.2. Типы грузов
SET IDENTITY_INSERT CargoTypes ON;
INSERT INTO CargoTypes (CargoTypeID, Name, MaxWeight, IsFragile) VALUES
(1, 'Документы', 0.5, 0),
(2, 'Одежда/текстиль', 10, 0),
(3, 'Техника/электроника', 20, 1),
(4, 'Продукты', 15, 1);
SET IDENTITY_INSERT CargoTypes OFF;

-- 9.3. Тарифы
INSERT INTO Tariffs (CargoTypeID, ZoneID, PricePerKg, PricePerKm, SurchargeUrgent) VALUES
(1, 1, 50.0, 10.0, 200),
(1, 2, 60.0, 15.0, 250),
(1, 3, 80.0, 20.0, 300),
(2, 1, 40.0, 10.0, 150),
(2, 2, 50.0, 15.0, 200),
(2, 3, 70.0, 20.0, 250),
(3, 1, 100.0, 15.0, 400),
(3, 2, 120.0, 20.0, 500),
(3, 3, 150.0, 25.0, 600),
(4, 1, 60.0, 12.0, 300),
(4, 2, 75.0, 18.0, 350),
(4, 3, 90.0, 22.0, 450);

-- 9.4. Пользователи
INSERT INTO Users (FullName, Email, Phone, PasswordHash, Role, IsActive) VALUES
('Администратор Системы', 'admin@expresslog.ru', '+79990000001', 'hash_admin', 'Admin', 1),
('Диспетчер Петров', 'dispatcher@expresslog.ru', '+79990000002', 'hash_dispatcher', 'Dispatcher', 1),
('Курьер Иванов А.А.', 'courier.ivanov@expresslog.ru', '+79990000003', 'hash_courier1', 'Courier', 1),
('Курьер Петрова М.И.', 'courier.petrova@expresslog.ru', '+79990000004', 'hash_courier2', 'Courier', 1),
('Клиент ООО "Ромашка"', 'client1@expresslog.ru', '+79990000005', 'hash_client1', 'Client', 1),
('Клиент Иванов С.П.', 'client2@expresslog.ru', '+79990000006', 'hash_client2', 'Client', 1);

-- 9.5. Клиенты
INSERT INTO Clients (ClientID, UserID, LegalAddress, ClientType, INN) VALUES
(1, 5, 'г. Санкт-Петербург, ул. Ленина, д.1', 'LegalEntity', '7812345678'),
(2, 6, 'г. Санкт-Петербург, пр. Невский, д.10', 'Individual', NULL);

-- 9.6. Курьеры
INSERT INTO Couriers (CourierID, UserID, TransportType, VehicleNumber, ZoneID, WorkSchedule, Rating) VALUES
(1, 3, 'Auto', 'А123ВС78', 1, 'Пн-Пт 9:00-18:00', 4.8),
(2, 4, 'OnFoot', NULL, 1, 'Пн-Сб 10:00-20:00', 4.5);

-- 9.7. Заказы (с указанием ZoneID)
DECLARE @Tracking1 NVARCHAR(20) = 'TRK100001';
DECLARE @Tracking2 NVARCHAR(20) = 'TRK100002';
DECLARE @Tracking3 NVARCHAR(20) = 'TRK100003';
DECLARE @Tracking4 NVARCHAR(20) = 'TRK100004';

-- Заказ 1 (Новый)
INSERT INTO Orders (ClientID, ZoneID, RecipientName, RecipientPhone, RecipientAddress, CargoTypeID, Weight, PickupAddress, DeliveryAddress, IsUrgent, CreatedDate, DesiredDeliveryDate, Status, TotalCost, TrackingNumber)
VALUES (1, 1, 'Сидоров Д.Н.', '+79110000001', 'г. СПб, ул. Садовая, д.5', 1, 0.5, 'г. СПб, ул. Невский, д.10', 'г. СПб, ул. Садовая, д.5', 0, GETDATE(), DATEADD(DAY, 1, GETDATE()), 'New', 0, @Tracking1);

-- Заказ 2 (Назначен)
INSERT INTO Orders (ClientID, ZoneID, RecipientName, RecipientPhone, RecipientAddress, CargoTypeID, Weight, PickupAddress, DeliveryAddress, IsUrgent, CreatedDate, DesiredDeliveryDate, Status, AssignedCourierID, DispatcherID, TotalCost, TrackingNumber)
VALUES (2, 1, 'ООО "Бета"', '+79220000002', 'г. СПб, пр. Энтузиастов, д.20', 3, 5.0, 'г. СПб, ул. Ленина, д.1', 'г. СПб, пр. Энтузиастов, д.20', 1, GETDATE(), DATEADD(DAY, 2, GETDATE()), 'Assigned', 1, 2, 0, @Tracking2);

-- Заказ 3 (В пути)
INSERT INTO Orders (ClientID, ZoneID, RecipientName, RecipientPhone, RecipientAddress, CargoTypeID, Weight, PickupAddress, DeliveryAddress, IsUrgent, CreatedDate, DesiredDeliveryDate, Status, AssignedCourierID, DispatcherID, TotalCost, TrackingNumber)
VALUES (1, 2, 'Иванова Е.П.', '+79330000003', 'г. СПб, ул. Пушкина, д.3', 2, 8.0, 'г. СПб, ул. Невский, д.10', 'г. СПб, ул. Пушкина, д.3', 0, DATEADD(DAY, -1, GETDATE()), DATEADD(DAY, 1, GETDATE()), 'InTransit', 2, 2, 0, @Tracking3);

-- Заказ 4 (Доставлен)
INSERT INTO Orders (ClientID, ZoneID, RecipientName, RecipientPhone, RecipientAddress, CargoTypeID, Weight, PickupAddress, DeliveryAddress, IsUrgent, CreatedDate, DesiredDeliveryDate, Status, AssignedCourierID, DispatcherID, TotalCost, TrackingNumber)
VALUES (2, 1, 'Петров С.М.', '+79440000004', 'г. СПб, ул. Лесная, д.8', 4, 12.0, 'г. СПб, ул. Ленина, д.1', 'г. СПб, ул. Лесная, д.8', 0, DATEADD(DAY, -2, GETDATE()), DATEADD(DAY, -1, GETDATE()), 'Delivered', 1, 2, 0, @Tracking4);

-- Пересчитываем TotalCost с помощью функции (используя ZoneID)
UPDATE Orders SET TotalCost = dbo.fn_CalculateCost(CargoTypeID, Weight, ZoneID, IsUrgent, 10) WHERE OrderID = 1;
UPDATE Orders SET TotalCost = dbo.fn_CalculateCost(CargoTypeID, Weight, ZoneID, IsUrgent, 15) WHERE OrderID = 2;
UPDATE Orders SET TotalCost = dbo.fn_CalculateCost(CargoTypeID, Weight, ZoneID, IsUrgent, 20) WHERE OrderID = 3;
UPDATE Orders SET TotalCost = dbo.fn_CalculateCost(CargoTypeID, Weight, ZoneID, IsUrgent, 12) WHERE OrderID = 4;

-- 9.8. История статусов
INSERT INTO OrderStatusHistory (OrderID, Status, ChangeDate, Comment)
VALUES
(1, 'New', GETDATE(), 'Заказ создан'),
(2, 'New', DATEADD(MINUTE, -30, GETDATE()), 'Заказ создан'),
(2, 'Assigned', DATEADD(MINUTE, -20, GETDATE()), 'Назначен курьер Иванов'),
(3, 'New', DATEADD(DAY, -1, GETDATE()), 'Заказ создан'),
(3, 'Assigned', DATEADD(HOUR, -23, GETDATE()), 'Назначен курьер Петрова'),
(3, 'Taken', DATEADD(HOUR, -22, GETDATE()), 'Курьер взял заказ'),
(3, 'InTransit', DATEADD(HOUR, -20, GETDATE()), 'Курьер в пути'),
(4, 'New', DATEADD(DAY, -2, GETDATE()), 'Заказ создан'),
(4, 'Assigned', DATEADD(DAY, -2, GETDATE()), 'Назначен курьер Иванов'),
(4, 'Taken', DATEADD(DAY, -2, GETDATE()), 'Курьер взял заказ'),
(4, 'InTransit', DATEADD(DAY, -2, GETDATE()), 'В пути'),
(4, 'AtRecipient', DATEADD(DAY, -2, GETDATE()), 'У получателя'),
(4, 'Delivered', DATEADD(DAY, -1, GETDATE()), 'Доставлен');

-- 9.9. Сессии доставки
INSERT INTO DeliverySessions (OrderID, GeneratedDate, Token, StartTime, EndTime, Status)
VALUES
(2, GETDATE(), 'QR20260515120000_2_1', GETDATE(), DATEADD(HOUR, 2, GETDATE()), 'Active'),
(3, DATEADD(HOUR, -22, GETDATE()), 'QR20260514180000_3_2', DATEADD(HOUR, -22, GETDATE()), DATEADD(HOUR, -20, GETDATE()), 'Used'),
(4, DATEADD(DAY, -2, GETDATE()), 'QR20260513090000_4_1', DATEADD(DAY, -2, GETDATE()), DATEADD(DAY, -2, GETDATE()), 'Used');

-- 9.10. Подтверждение доставки для заказа 4
INSERT INTO DeliveryConfirmations (SessionID, OrderID, ScanTime, Latitude, Longitude, IPAddress, Status, PhotoPath)
VALUES
(3, 4, DATEADD(DAY, -1, GETDATE()), 59.9343, 30.3351, '192.168.1.1', 'QR', '/photos/order4.jpg');

-- 9.11. Уведомления
INSERT INTO Notifications (UserID, Text, Date, IsRead)
VALUES
(3, 'Вам назначен новый заказ #2', GETDATE(), 0),
(2, 'Заказ #4 успешно доставлен', GETDATE(), 1),
(5, 'Ваш заказ #4 доставлен', GETDATE(), 0);

-- 9.12. Маршрутные листы
INSERT INTO RouteLists (CourierID, Date) VALUES (1, GETDATE());
INSERT INTO RouteListOrders (RouteListID, OrderID, SequenceNumber)
SELECT 1, OrderID, ROW_NUMBER() OVER (ORDER BY OrderID)
FROM Orders
WHERE AssignedCourierID = 1 AND Status IN ('Assigned', 'Taken', 'InTransit', 'AtRecipient');

-- ======================================================================
-- 10. Проверка созданных объектов
-- ======================================================================
SELECT 'Users' AS TableName, COUNT(*) AS Rows FROM Users
UNION ALL
SELECT 'Clients', COUNT(*) FROM Clients
UNION ALL
SELECT 'Couriers', COUNT(*) FROM Couriers
UNION ALL
SELECT 'Zones', COUNT(*) FROM Zones
UNION ALL
SELECT 'CargoTypes', COUNT(*) FROM CargoTypes
UNION ALL
SELECT 'Tariffs', COUNT(*) FROM Tariffs
UNION ALL
SELECT 'Orders', COUNT(*) FROM Orders
UNION ALL
SELECT 'DeliverySessions', COUNT(*) FROM DeliverySessions
UNION ALL
SELECT 'OrderStatusHistory', COUNT(*) FROM OrderStatusHistory
UNION ALL
SELECT 'DeliveryConfirmations', COUNT(*) FROM DeliveryConfirmations
UNION ALL
SELECT 'Notifications', COUNT(*) FROM Notifications
UNION ALL
SELECT 'RouteLists', COUNT(*) FROM RouteLists
UNION ALL
SELECT 'RouteListOrders', COUNT(*) FROM RouteListOrders;

-- Проверка представления
SELECT * FROM vw_CourierEfficiency;

-- Проверка функции
SELECT dbo.fn_CalculateCost(1, 0.5, 1, 0, 10) AS Cost;

-- Тестирование процедуры (раскомментировать при необходимости)
-- DECLARE @NewCourierID INT, @NewToken NVARCHAR(100);
-- EXEC usp_AssignCourier @OrderID=1, @DispatcherID=2, @CourierID=@NewCourierID OUTPUT, @Token=@NewToken OUTPUT;
-- SELECT @NewCourierID AS CourierID, @NewToken AS Token;

PRINT 'Скрипт инициализации БД ExpressLog успешно выполнен с улучшенными ограничениями.';
GO