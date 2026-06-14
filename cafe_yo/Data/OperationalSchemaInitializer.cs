using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace cafe_yo.Data
{
    public static class OperationalSchemaInitializer
    {
        public static async Task EnsureAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var connStr = config.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return;
            }

            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var sql = @"
IF OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Orders', 'OrderNumber') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD OrderNumber NVARCHAR(30) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'CashierUserId') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD CashierUserId NVARCHAR(450) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'CookUserId') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD CookUserId NVARCHAR(450) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'StartedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD StartedAt DATETIME2 NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'ReadyAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD ReadyAt DATETIME2 NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'CompletedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD CompletedAt DATETIME2 NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'CancelledAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD CancelledAt DATETIME2 NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'CancelledReason') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD CancelledReason NVARCHAR(250) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'KitchenStatus') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD KitchenStatus NVARCHAR(30) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'UpdatedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_Orders_UpdatedAt DEFAULT SYSUTCDATETIME();
    END;

    IF COL_LENGTH('dbo.Orders', 'PaymentInvoice') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD PaymentInvoice NVARCHAR(120) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'PaymentMethod') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD PaymentMethod NVARCHAR(40) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'PaymentStatus') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD PaymentStatus NVARCHAR(40) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'PaymentCheckoutUrl') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD PaymentCheckoutUrl NVARCHAR(500) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'PaymentQrString') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD PaymentQrString NVARCHAR(MAX) NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'PaidAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Orders ADD PaidAt DATETIME2 NULL;
    END;

    IF COL_LENGTH('dbo.Orders', 'KitchenStatus') IS NOT NULL
    BEGIN
        DECLARE @sqlUpdateKitchenStatus NVARCHAR(MAX) = N'
            UPDATE dbo.Orders
            SET KitchenStatus = CASE
                WHEN KitchenStatus IS NOT NULL AND LTRIM(RTRIM(KitchenStatus)) <> '''' THEN KitchenStatus
                WHEN LOWER(ISNULL([Status], '''')) IN (''pending'', ''menunggu'', ''new'') THEN ''pending''
                WHEN LOWER(ISNULL([Status], '''')) IN (''processing'', ''diproses'', ''cooking'') THEN ''processing''
                WHEN LOWER(ISNULL([Status], '''')) IN (''ready'', ''selesai'') THEN ''ready''
                ELSE ''pending''
            END
            WHERE KitchenStatus IS NULL OR LTRIM(RTRIM(KitchenStatus)) = '''';';
        EXEC sp_executesql @sqlUpdateKitchenStatus;
    END;

    IF COL_LENGTH('dbo.Orders', 'OrderNumber') IS NOT NULL
    BEGIN
        DECLARE @sqlFillOrderNumber NVARCHAR(MAX) = N'
            UPDATE dbo.Orders
            SET OrderNumber = CONCAT(''ORD-'', RIGHT(CONCAT(''000000'', CAST(OrderId AS nvarchar(20))), 6))
            WHERE OrderNumber IS NULL OR LTRIM(RTRIM(OrderNumber)) = '''';';
        EXEC sp_executesql @sqlFillOrderNumber;
    END;
END;

IF OBJECT_ID(N'dbo.OrderItems', N'U') IS NULL AND OBJECT_ID(N'dbo.Orders', N'U') IS NOT NULL
BEGIN
    CREATE TABLE dbo.OrderItems (
        OrderItemId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OrderId INT NOT NULL,
        MenuItemId INT NULL,
        ItemName NVARCHAR(120) NULL,
        Quantity INT NOT NULL CONSTRAINT DF_OrderItems_Quantity DEFAULT(1),
        Notes NVARCHAR(250) NULL,
        UnitPrice DECIMAL(18,2) NULL,
        CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(OrderId) ON DELETE CASCADE
    );
    CREATE INDEX IX_OrderItems_OrderId ON dbo.OrderItems(OrderId);
END;

IF OBJECT_ID(N'dbo.KitchenNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KitchenNotifications (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OrderId INT NOT NULL,
        TargetRole NVARCHAR(40) NOT NULL,
        IsRead BIT NOT NULL CONSTRAINT DF_KitchenNotifications_IsRead DEFAULT(0),
        AcknowledgedAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_KitchenNotifications_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_KitchenNotifications_TargetRoleRead ON dbo.KitchenNotifications(TargetRole, IsRead, CreatedAt DESC);
    CREATE INDEX IX_KitchenNotifications_OrderId ON dbo.KitchenNotifications(OrderId);
END;

IF OBJECT_ID(N'dbo.StockItems', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.StockItems', 'IsActive') IS NULL
    BEGIN
        ALTER TABLE dbo.StockItems ADD IsActive BIT NOT NULL CONSTRAINT DF_StockItems_IsActive DEFAULT(1);
    END;

    IF COL_LENGTH('dbo.StockItems', 'Type') IS NULL
    BEGIN
        ALTER TABLE dbo.StockItems ADD [Type] NVARCHAR(20) NULL;
    END;

    IF COL_LENGTH('dbo.StockItems', 'Unit') IS NULL
    BEGIN
        ALTER TABLE dbo.StockItems ADD Unit NVARCHAR(30) NULL;
    END;

    IF COL_LENGTH('dbo.StockItems', 'PurchasePrice') IS NULL
    BEGIN
        ALTER TABLE dbo.StockItems ADD PurchasePrice DECIMAL(18,2) NULL;
    END;

    IF COL_LENGTH('dbo.StockItems', 'Description') IS NULL
    BEGIN
        ALTER TABLE dbo.StockItems ADD Description NVARCHAR(250) NULL;
    END;

    DECLARE @DefaultStocks TABLE (
        [Name] NVARCHAR(120) NOT NULL,
        [Type] NVARCHAR(20) NULL,
        [Quantity] INT NOT NULL,
        [MinQuantity] INT NOT NULL,
        [Unit] NVARCHAR(30) NULL,
        [PurchasePrice] DECIMAL(18,2) NULL,
        [Description] NVARCHAR(250) NULL
    );

    INSERT INTO @DefaultStocks ([Name], [Type], [Quantity], [MinQuantity], [Unit], [PurchasePrice], [Description])
    VALUES
        (N'Nasi Putih Matang', N'RawMaterial', 120, 30, N'porsi', 2500, N'Bahan utama nasi goreng dan ayam bakar.'),
        (N'Ayam Fillet', N'RawMaterial', 80, 20, N'porsi', 8000, N'Potongan ayam siap olah.'),
        (N'Telur', N'RawMaterial', 180, 40, N'butir', 2200, N'Telur ayam untuk menu goreng.'),
        (N'Mie Kuning', N'RawMaterial', 100, 25, N'porsi', 3000, N'Mie untuk menu mie goreng.'),
        (N'Pisang', N'RawMaterial', 140, 35, N'buah', 1800, N'Pisang untuk menu pisang goreng.'),
        (N'Kulit Pastry', N'RawMaterial', 90, 20, N'lembar', 3500, N'Kulit untuk cromboloni.'),
        (N'Cokelat Spread', N'RawMaterial', 70, 15, N'porsi', 2800, N'Isian cromboloni.'),
        (N'Kopi Espresso Shot', N'RawMaterial', 160, 40, N'shot', 3200, N'Base kopi susu.'),
        (N'Susu', N'RawMaterial', 220, 55, N'porsi', 2600, N'Susu cair untuk minuman.'),
        (N'Teh', N'RawMaterial', 170, 45, N'porsi', 1200, N'Teh untuk es teh manis.'),
        (N'Gula Cair', N'RawMaterial', 260, 70, N'porsi', 500, N'Sirup gula untuk minuman.'),
        (N'Es Batu', N'RawMaterial', 320, 90, N'porsi', 200, N'Es batu untuk minuman dingin.'),
        (N'Minyak Goreng', N'RawMaterial', 260, 70, N'porsi', 700, N'Minyak goreng untuk menu goreng.'),
        (N'Bumbu Nasi Goreng', N'RawMaterial', 110, 30, N'porsi', 1700, N'Bumbu siap pakai nasi/mie goreng.'),
        (N'Bumbu Ayam Bakar', N'RawMaterial', 95, 25, N'porsi', 1900, N'Bumbu marinasi ayam bakar.'),
        (N'Bumbu Soto', N'RawMaterial', 85, 22, N'porsi', 2000, N'Bumbu kuah soto.'),
        (N'Santan', N'RawMaterial', 75, 20, N'porsi', 1600, N'Santan untuk kuah soto.');

    INSERT INTO dbo.StockItems (Name, [Type], Quantity, MinQuantity, Unit, PurchasePrice, Description)
    SELECT d.[Name], d.[Type], d.[Quantity], d.[MinQuantity], d.[Unit], d.[PurchasePrice], d.[Description]
    FROM @DefaultStocks d
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.StockItems s
        WHERE LOWER(LTRIM(RTRIM(s.Name))) = LOWER(LTRIM(RTRIM(d.[Name])))
    );

    UPDATE s
    SET
        s.[Type] = COALESCE(NULLIF(LTRIM(RTRIM(s.[Type])), ''), d.[Type]),
        s.Unit = COALESCE(NULLIF(LTRIM(RTRIM(s.Unit)), ''), d.[Unit]),
        s.MinQuantity = CASE WHEN ISNULL(s.MinQuantity, 0) <= 0 THEN d.[MinQuantity] ELSE s.MinQuantity END,
        s.PurchasePrice = COALESCE(s.PurchasePrice, d.[PurchasePrice]),
        s.Description = COALESCE(NULLIF(LTRIM(RTRIM(s.Description)), ''), d.[Description])
    FROM dbo.StockItems s
    INNER JOIN @DefaultStocks d ON LOWER(LTRIM(RTRIM(s.Name))) = LOWER(LTRIM(RTRIM(d.[Name])));
END;

IF OBJECT_ID(N'dbo.MenuIngredients', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MenuIngredients (
        MenuIngredientId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MenuItemId INT NOT NULL,
        StockItemId INT NOT NULL,
        QuantityNeeded DECIMAL(18,3) NOT NULL CONSTRAINT DF_MenuIngredients_QuantityNeeded DEFAULT(1),
        CONSTRAINT FK_MenuIngredients_MenuItems FOREIGN KEY (MenuItemId) REFERENCES dbo.MenuItems(MenuItemId) ON DELETE CASCADE,
        CONSTRAINT FK_MenuIngredients_StockItems FOREIGN KEY (StockItemId) REFERENCES dbo.StockItems(StockItemId) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_MenuIngredients_MenuStock ON dbo.MenuIngredients(MenuItemId, StockItemId);
END;

IF OBJECT_ID(N'dbo.StockUsageLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockUsageLogs (
        StockUsageLogId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        StockItemId INT NOT NULL,
        OrderId INT NOT NULL,
        QuantityUsed DECIMAL(18,3) NOT NULL,
        UsedAt DATETIME2 NOT NULL CONSTRAINT DF_StockUsageLogs_UsedAt DEFAULT SYSUTCDATETIME(),
        Notes NVARCHAR(250) NULL,
        CONSTRAINT FK_StockUsageLogs_StockItems FOREIGN KEY (StockItemId) REFERENCES dbo.StockItems(StockItemId),
        CONSTRAINT FK_StockUsageLogs_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(OrderId)
    );
    CREATE INDEX IX_StockUsageLogs_StockItemId ON dbo.StockUsageLogs(StockItemId, UsedAt DESC);
    CREATE INDEX IX_StockUsageLogs_OrderId ON dbo.StockUsageLogs(OrderId);
END;

IF OBJECT_ID(N'dbo.StockUsageLogs', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.StockUsageLogs', 'MenuItemId') IS NULL
    BEGIN
        ALTER TABLE dbo.StockUsageLogs ADD MenuItemId INT NULL;
    END;
    IF COL_LENGTH('dbo.StockUsageLogs', 'RemainingStock') IS NULL
    BEGIN
        ALTER TABLE dbo.StockUsageLogs ADD RemainingStock DECIMAL(18,3) NULL;
    END;
    IF COL_LENGTH('dbo.StockUsageLogs', 'CookUserId') IS NULL
    BEGIN
        ALTER TABLE dbo.StockUsageLogs ADD CookUserId NVARCHAR(450) NULL;
    END;
END;

IF OBJECT_ID(N'dbo.InventoryItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryItems (
        InventoryItemId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(120) NOT NULL,
        Category NVARCHAR(60) NOT NULL,
        Unit NVARCHAR(30) NOT NULL CONSTRAINT DF_InventoryItems_Unit DEFAULT('pcs'),
        TotalStock INT NOT NULL CONSTRAINT DF_InventoryItems_TotalStock DEFAULT(0),
        GoodStock INT NOT NULL CONSTRAINT DF_InventoryItems_GoodStock DEFAULT(0),
        BrokenStock INT NOT NULL CONSTRAINT DF_InventoryItems_BrokenStock DEFAULT(0),
        MissingStock INT NOT NULL CONSTRAINT DF_InventoryItems_MissingStock DEFAULT(0),
        Notes NVARCHAR(250) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_InventoryItems_IsActive DEFAULT(1),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryItems_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryItems_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_InventoryItems_ActiveName ON dbo.InventoryItems(IsActive, Name);
END;

IF OBJECT_ID(N'dbo.InventoryDamageLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryDamageLogs (
        DamageLogId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        InventoryItemId INT NOT NULL,
        Quantity INT NOT NULL,
        DamageType NVARCHAR(20) NOT NULL,
        LogDate DATE NOT NULL,
        Reason NVARCHAR(180) NULL,
        Notes NVARCHAR(250) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_InventoryDamageLogs_Status DEFAULT('dicatat'),
        CreatedBy NVARCHAR(450) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_InventoryDamageLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_InventoryDamageLogs_InventoryItems FOREIGN KEY (InventoryItemId) REFERENCES dbo.InventoryItems(InventoryItemId)
    );
    CREATE INDEX IX_InventoryDamageLogs_LogDate ON dbo.InventoryDamageLogs(LogDate DESC);
END;

IF OBJECT_ID(N'dbo.StockExpiredLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockExpiredLogs (
        ExpiredLogId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        StockItemId INT NOT NULL,
        QuantityDisposed DECIMAL(18,3) NOT NULL,
        ExpiredDate DATE NOT NULL,
        Reason NVARCHAR(180) NULL,
        Notes NVARCHAR(250) NULL,
        CreatedBy NVARCHAR(450) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_StockExpiredLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_StockExpiredLogs_StockItems FOREIGN KEY (StockItemId) REFERENCES dbo.StockItems(StockItemId)
    );
    CREATE INDEX IX_StockExpiredLogs_ExpiredDate ON dbo.StockExpiredLogs(ExpiredDate DESC);
END;

IF OBJECT_ID(N'dbo.UserNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserNotifications (
        NotificationId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId NVARCHAR(450) NULL,
        RoleTarget NVARCHAR(40) NULL,
        Type NVARCHAR(40) NOT NULL,
        Title NVARCHAR(120) NOT NULL,
        Message NVARCHAR(300) NOT NULL,
        IsRead BIT NOT NULL CONSTRAINT DF_UserNotifications_IsRead DEFAULT(0),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_UserNotifications_CreatedAt DEFAULT SYSUTCDATETIME(),
        ReadAt DATETIME2 NULL
    );
    CREATE INDEX IX_UserNotifications_TargetRead ON dbo.UserNotifications(RoleTarget, IsRead, CreatedAt DESC);
END;

IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.AspNetUsers', 'Role') IS NULL
    BEGIN
        ALTER TABLE dbo.AspNetUsers ADD Role NVARCHAR(20) NULL;
    END;

    UPDATE u
    SET u.Role = r.Name
    FROM dbo.AspNetUsers u
    INNER JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    INNER JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
    WHERE (u.Role IS NULL OR LTRIM(RTRIM(u.Role)) = '')
      AND r.Name IN ('Admin', 'Owner', 'Supervisor', 'Kasir', 'Koki', 'Dapur');
END;

IF OBJECT_ID(N'dbo.MenuCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MenuCategories (
        CategoryId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(80) NOT NULL UNIQUE,
        IsActive BIT NOT NULL CONSTRAINT DF_MenuCategories_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_MenuCategories_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.MenuItems', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.MenuItems', 'ImageUrl') IS NULL
    BEGIN
        ALTER TABLE dbo.MenuItems ADD ImageUrl NVARCHAR(500) NULL;
    END;

    IF COL_LENGTH('dbo.MenuItems', 'Stock') IS NULL
    BEGIN
        ALTER TABLE dbo.MenuItems ADD Stock INT NOT NULL CONSTRAINT DF_MenuItems_Stock DEFAULT(0);
    END;

    IF COL_LENGTH('dbo.MenuItems', 'Description') IS NULL
    BEGIN
        ALTER TABLE dbo.MenuItems ADD Description NVARCHAR(250) NULL;
    END;

    IF COL_LENGTH('dbo.MenuItems', 'IsAvailable') IS NOT NULL
    BEGIN
        DECLARE @sqlFillMenuStock NVARCHAR(MAX) = N'
            UPDATE dbo.MenuItems
            SET Stock = CASE
                WHEN ISNULL(IsAvailable, 1) = 1 AND ISNULL(Stock, 0) = 0 THEN 15
                ELSE ISNULL(Stock, 0)
            END
            WHERE ISNULL(Stock, 0) = 0;';
        EXEC sp_executesql @sqlFillMenuStock;
    END;

    IF OBJECT_ID('tempdb..#DefaultMenus') IS NOT NULL DROP TABLE #DefaultMenus;
    CREATE TABLE #DefaultMenus (
        [Name] NVARCHAR(120) NOT NULL,
        [Category] NVARCHAR(60) NULL,
        [Price] DECIMAL(18,2) NOT NULL,
        [Description] NVARCHAR(250) NULL
    );

    INSERT INTO #DefaultMenus ([Name], [Category], [Price], [Description])
    VALUES
        (N'Nasi Goreng', N'food', 25000, N'Nasi goreng gurih dengan telur dan bumbu spesial.'),
        (N'Ayam Bakar', N'food', 32000, N'Ayam bakar berbumbu manis gurih, disajikan hangat.'),
        (N'Soto Ayam', N'food', 28000, N'Soto ayam kuah rempah lengkap dengan isian.'),
        (N'Mie Goreng', N'food', 24000, N'Mie goreng tradisional dengan topping pilihan.'),
        (N'Chicken Katsu Rice', N'food', 33000, N'Nasi dengan chicken katsu renyah dan saus gurih.'),
        (N'Kopi Susu', N'drink', 23000, N'Perpaduan espresso dan susu dengan rasa seimbang.'),
        (N'Es Teh Manis', N'drink', 12000, N'Teh manis dingin yang segar untuk teman makan.'),
        (N'Americano', N'drink', 20000, N'Kopi hitam ringan dengan aroma espresso.'),
        (N'Matcha Latte', N'drink', 26000, N'Matcha creamy dengan rasa lembut dan manis ringan.'),
        (N'Lemon Tea', N'drink', 18000, N'Teh lemon dingin dengan rasa asam manis segar.'),
        (N'Pisang Goreng', N'snack', 18000, N'Pisang goreng renyah di luar, lembut di dalam.'),
        (N'French Fries', N'snack', 19000, N'Kentang goreng crispy disajikan dengan saus.'),
        (N'Sosis Bakar', N'snack', 17000, N'Sosis bakar dengan olesan saus manis pedas.'),
        (N'Chicken Popcorn', N'snack', 22000, N'Potongan ayam crispy ukuran bite-size.'),
        (N'Roti Bakar Coklat', N'snack', 20000, N'Roti bakar isi coklat lumer dan topping manis.'),
        (N'Cromboloni', N'dessert', 22000, N'Pastry berlapis dengan isian krim manis.'),
        (N'Brownies', N'dessert', 21000, N'Brownies coklat lembut dengan rasa pekat.'),
        (N'Cheesecake Slice', N'dessert', 27000, N'Potongan cheesecake lembut dengan rasa creamy.'),
        (N'Panna Cotta', N'dessert', 25000, N'Dessert susu lembut dengan saus buah.'),
        (N'Ice Cream Sundae', N'dessert', 23000, N'Es krim dengan topping saus dan taburan manis.');

    IF COL_LENGTH('dbo.MenuItems', 'Description') IS NOT NULL
    BEGIN
        DECLARE @sqlSeedMenusWithDesc NVARCHAR(MAX) = N'
            INSERT INTO dbo.MenuItems (Name, Category, Price, Description, Stock, IsAvailable)
            SELECT d.[Name], d.[Category], d.[Price], d.[Description], 15, 1
            FROM #DefaultMenus d
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.MenuItems m
                WHERE LOWER(LTRIM(RTRIM(m.Name))) = LOWER(LTRIM(RTRIM(d.[Name])))
            );

            UPDATE m
            SET
                m.Category = COALESCE(NULLIF(LTRIM(RTRIM(m.Category)), ''''), d.[Category]),
                m.Price = CASE WHEN ISNULL(m.Price, 0) <= 0 THEN d.[Price] ELSE m.Price END,
                m.Stock = CASE WHEN ISNULL(m.Stock, 0) <= 0 THEN 15 ELSE m.Stock END,
                m.Description = COALESCE(NULLIF(LTRIM(RTRIM(m.Description)), ''''), d.[Description])
            FROM dbo.MenuItems m
            INNER JOIN #DefaultMenus d ON LOWER(LTRIM(RTRIM(m.Name))) = LOWER(LTRIM(RTRIM(d.[Name])));';
        EXEC sp_executesql @sqlSeedMenusWithDesc;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.MenuItems (Name, Category, Price, Stock, IsAvailable)
        SELECT d.[Name], d.[Category], d.[Price], 15, 1
        FROM #DefaultMenus d
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.MenuItems m
            WHERE LOWER(LTRIM(RTRIM(m.Name))) = LOWER(LTRIM(RTRIM(d.[Name])))
        );

        UPDATE m
        SET
            m.Category = COALESCE(NULLIF(LTRIM(RTRIM(m.Category)), ''), d.[Category]),
            m.Price = CASE WHEN ISNULL(m.Price, 0) <= 0 THEN d.[Price] ELSE m.Price END,
            m.Stock = CASE WHEN ISNULL(m.Stock, 0) <= 0 THEN 15 ELSE m.Stock END
        FROM dbo.MenuItems m
        INNER JOIN #DefaultMenus d ON LOWER(LTRIM(RTRIM(m.Name))) = LOWER(LTRIM(RTRIM(d.[Name])));
    END;

    IF COL_LENGTH('dbo.MenuItems', 'ImageUrl') IS NOT NULL
    BEGIN
        DECLARE @sqlFillMenuImageUrl NVARCHAR(MAX) = N'
            UPDATE dbo.MenuItems
            SET ImageUrl = CASE LOWER(LTRIM(RTRIM(Name)))
                WHEN ''nasi goreng'' THEN ''/images/menu/nasi-goreng.jpg''
                WHEN ''ayam bakar'' THEN ''/images/menu/ayam-bakar.jpg''
                WHEN ''soto ayam'' THEN ''/images/menu/soto-ayam.jpg''
                WHEN ''mie goreng'' THEN ''/images/menu/mie-goreng.jpg''
                WHEN ''pisang goreng'' THEN ''/images/menu/pisang-goreng.jpg''
                WHEN ''cromboloni'' THEN ''/images/menu/cromboloni.jpg''
                WHEN ''kopi susu'' THEN ''/images/menu/kopi-susu.jpg''
                WHEN ''es teh manis'' THEN ''/images/menu/es-teh.jpg''
                ELSE ImageUrl
            END
            WHERE ImageUrl IS NULL OR LTRIM(RTRIM(ImageUrl)) = '''';';
        EXEC sp_executesql @sqlFillMenuImageUrl;
    END;
END;

IF OBJECT_ID(N'dbo.Faqs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Faqs (
        FaqId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Question NVARCHAR(255) NOT NULL,
        Answer NVARCHAR(MAX) NOT NULL,
        Keywords NVARCHAR(1000) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Faqs_IsActive DEFAULT(1),
        SortOrder INT NOT NULL CONSTRAINT DF_Faqs_SortOrder DEFAULT(0),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Faqs_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_Faqs_UpdatedAt DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_Faqs_IsActiveSort ON dbo.Faqs(IsActive, SortOrder, FaqId);
END;

IF OBJECT_ID(N'dbo.ChatbotLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatbotLogs (
        ChatbotLogId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SessionId NVARCHAR(100) NULL,
        UserId NVARCHAR(450) NULL,
        Question NVARCHAR(MAX) NOT NULL,
        Answer NVARCHAR(MAX) NOT NULL,
        Intent NVARCHAR(50) NULL,
        MatchedMenuItemId INT NULL,
        MatchedFaqId INT NULL,
        Confidence DECIMAL(5,2) NULL,
        IpAddress NVARCHAR(45) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_ChatbotLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ChatbotLogs_MenuItems FOREIGN KEY (MatchedMenuItemId) REFERENCES dbo.MenuItems(MenuItemId) ON DELETE SET NULL,
        CONSTRAINT FK_ChatbotLogs_Faqs FOREIGN KEY (MatchedFaqId) REFERENCES dbo.Faqs(FaqId) ON DELETE SET NULL
    );
    CREATE INDEX IX_ChatbotLogs_CreatedAt ON dbo.ChatbotLogs(CreatedAt DESC);
    CREATE INDEX IX_ChatbotLogs_Intent ON dbo.ChatbotLogs(Intent, CreatedAt DESC);
END;

IF OBJECT_ID(N'dbo.Faqs', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.Faqs)
BEGIN
    INSERT INTO dbo.Faqs (Question, Answer, Keywords, IsActive, SortOrder, CreatedAt, UpdatedAt)
    VALUES
        (N'Cara pesan', N'Cara pesan: pilih meja, pilih menu, lalu checkout di kasir.', N'cara pesan,pesan,order', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
        (N'Metode pembayaran', N'Pembayaran tersedia tunai, transfer, dan QRIS.', N'pembayaran,metode pembayaran,qris,transfer,tunai', 1, 2, SYSUTCDATETIME(), SYSUTCDATETIME()),
        (N'Jam operasional', N'Jam operasional CafeYo setiap hari 08.00 - 22.00 WIB.', N'jam operasional,jam buka,buka jam', 1, 3, SYSUTCDATETIME(), SYSUTCDATETIME()),
        (N'Dine-in / Take Away', N'Kami melayani dine-in dan take away. Pengiriman mengikuti kebijakan cabang.', N'dine in,take away,pengiriman,delivery', 1, 4, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

IF OBJECT_ID(N'dbo.MenuIngredients', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.MenuItems)
   AND EXISTS (SELECT 1 FROM dbo.StockItems)
BEGIN
    DECLARE @DefaultRecipes TABLE (
        [MenuName] NVARCHAR(120) NOT NULL,
        [StockName] NVARCHAR(120) NOT NULL,
        [QuantityNeeded] DECIMAL(18,3) NOT NULL
    );

    INSERT INTO @DefaultRecipes ([MenuName], [StockName], [QuantityNeeded])
    VALUES
        (N'Nasi Goreng', N'Nasi Putih Matang', 1),
        (N'Nasi Goreng', N'Telur', 1),
        (N'Nasi Goreng', N'Bumbu Nasi Goreng', 1),
        (N'Nasi Goreng', N'Minyak Goreng', 1),
        (N'Ayam Bakar', N'Ayam Fillet', 1),
        (N'Ayam Bakar', N'Bumbu Ayam Bakar', 1),
        (N'Ayam Bakar', N'Nasi Putih Matang', 1),
        (N'Soto Ayam', N'Ayam Fillet', 1),
        (N'Soto Ayam', N'Bumbu Soto', 1),
        (N'Soto Ayam', N'Santan', 1),
        (N'Mie Goreng', N'Mie Kuning', 1),
        (N'Mie Goreng', N'Telur', 1),
        (N'Mie Goreng', N'Bumbu Nasi Goreng', 1),
        (N'Mie Goreng', N'Minyak Goreng', 1),
        (N'Pisang Goreng', N'Pisang', 2),
        (N'Pisang Goreng', N'Minyak Goreng', 1),
        (N'Cromboloni', N'Kulit Pastry', 1),
        (N'Cromboloni', N'Cokelat Spread', 1),
        (N'Kopi Susu', N'Kopi Espresso Shot', 1),
        (N'Kopi Susu', N'Susu', 1),
        (N'Kopi Susu', N'Gula Cair', 1),
        (N'Es Teh Manis', N'Teh', 1),
        (N'Es Teh Manis', N'Gula Cair', 1),
        (N'Es Teh Manis', N'Es Batu', 2);

    DELETE mi
    FROM dbo.MenuIngredients mi
    INNER JOIN dbo.MenuItems m ON m.MenuItemId = mi.MenuItemId
    WHERE EXISTS (
        SELECT 1
        FROM @DefaultRecipes rMenu
        WHERE LOWER(LTRIM(RTRIM(rMenu.MenuName))) = LOWER(LTRIM(RTRIM(m.Name)))
    )
      AND NOT EXISTS (
        SELECT 1
        FROM @DefaultRecipes r
        INNER JOIN dbo.StockItems s ON LOWER(LTRIM(RTRIM(s.Name))) = LOWER(LTRIM(RTRIM(r.StockName)))
        WHERE LOWER(LTRIM(RTRIM(r.MenuName))) = LOWER(LTRIM(RTRIM(m.Name)))
          AND s.StockItemId = mi.StockItemId
    );

    UPDATE mi
    SET mi.QuantityNeeded = r.QuantityNeeded
    FROM dbo.MenuIngredients mi
    INNER JOIN dbo.MenuItems m ON m.MenuItemId = mi.MenuItemId
    INNER JOIN dbo.StockItems s ON s.StockItemId = mi.StockItemId
    INNER JOIN @DefaultRecipes r
        ON LOWER(LTRIM(RTRIM(r.MenuName))) = LOWER(LTRIM(RTRIM(m.Name)))
       AND LOWER(LTRIM(RTRIM(r.StockName))) = LOWER(LTRIM(RTRIM(s.Name)));

    INSERT INTO dbo.MenuIngredients (MenuItemId, StockItemId, QuantityNeeded)
    SELECT m.MenuItemId, s.StockItemId, r.QuantityNeeded
    FROM @DefaultRecipes r
    INNER JOIN dbo.MenuItems m ON LOWER(LTRIM(RTRIM(m.Name))) = LOWER(LTRIM(RTRIM(r.MenuName)))
    INNER JOIN dbo.StockItems s ON LOWER(LTRIM(RTRIM(s.Name))) = LOWER(LTRIM(RTRIM(r.StockName)))
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.MenuIngredients mi
        WHERE mi.MenuItemId = m.MenuItemId
          AND mi.StockItemId = s.StockItemId
    );

    IF NOT EXISTS (SELECT 1 FROM dbo.MenuIngredients)
    BEGIN
        ;WITH MenuList AS (
            SELECT MenuItemId, ROW_NUMBER() OVER (ORDER BY MenuItemId) AS rn
            FROM dbo.MenuItems
        ),
        StockList AS (
            SELECT StockItemId, ROW_NUMBER() OVER (ORDER BY StockItemId) AS rn
            FROM dbo.StockItems
        )
        INSERT INTO dbo.MenuIngredients (MenuItemId, StockItemId, QuantityNeeded)
        SELECT m.MenuItemId, s.StockItemId, 1
        FROM MenuList m
        INNER JOIN StockList s ON s.rn = ((m.rn - 1) % (SELECT COUNT(1) FROM StockList)) + 1;
    END;
END;
";

            using var cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
