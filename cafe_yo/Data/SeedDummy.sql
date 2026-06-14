IF COL_LENGTH('dbo.StockItems', 'Type') IS NULL
BEGIN
    ALTER TABLE dbo.StockItems ADD [Type] nvarchar(20) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems)
BEGIN
    INSERT INTO dbo.MenuItems (Name, Category, Price, IsAvailable)
    VALUES
        ('Espresso', 'Coffee', 18000, 1),
        ('Latte', 'Coffee', 25000, 1),
        ('Matcha Latte', 'Tea', 28000, 1),
        ('Croissant', 'Bakery', 22000, 1),
        ('French Fries', 'Snack', 20000, 1);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Tables)
BEGIN
    INSERT INTO dbo.Tables (TableNumber, Status)
    VALUES
        (1, 'Available'),
        (2, 'Available'),
        (3, 'Reserved'),
        (4, 'Available');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.StockItems)
BEGIN
    INSERT INTO dbo.StockItems (Name, [Type], Quantity, MinQuantity)
    VALUES
        ('Coffee Beans', 'RawMaterial', 20, 10),
        ('Milk', 'RawMaterial', 8, 12),
        ('Paper Cups', 'Fragile', 50, 30),
        ('Matcha Powder', 'RawMaterial', 6, 8);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.Orders)
BEGIN
    DECLARE @Table1 INT = (SELECT TOP 1 TableId FROM dbo.Tables ORDER BY TableNumber);
    DECLARE @Table2 INT = (SELECT TOP 1 TableId FROM dbo.Tables ORDER BY TableNumber DESC);

    INSERT INTO dbo.Orders (TableId, OrderDate, Status, Total)
    VALUES
        (@Table1, DATEADD(hour, -2, GETDATE()), 'New', 54000),
        (@Table2, DATEADD(hour, -1, GETDATE()), 'Cooking', 47000),
        (@Table1, DATEADD(minute, -20, GETDATE()), 'Ready', 32000),
        (@Table2, DATEADD(minute, -5, GETDATE()), 'Paid', 76000);
END;
