using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Authorize(Roles = AppRoles.Supervisor + ",supervisor")]
    [Route("api/supervisor")]
    public class SupervisorApiController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        public SupervisorApiController(IConfiguration configuration) => _configuration = configuration;
        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        public sealed class IngredientUpsertRequest
        {
            public string? Name { get; set; }
            public string? Unit { get; set; }
            public decimal Quantity { get; set; }
            public decimal MinQuantity { get; set; }
            public string? Type { get; set; }
            public string? Description { get; set; }
            public bool? IsActive { get; set; }
        }

        public sealed class IngredientAdjustRequest
        {
            public decimal Amount { get; set; }
            public string? Mode { get; set; }
        }

        public sealed class RecipeUpsertRequest
        {
            public int MenuItemId { get; set; }
            public int StockItemId { get; set; }
            public decimal QuantityNeeded { get; set; }
        }

        public sealed class InventoryItemUpsertRequest
        {
            public string? Name { get; set; }
            public string? Category { get; set; }
            public string? Unit { get; set; }
            public int TotalStock { get; set; }
            public int GoodStock { get; set; }
            public int BrokenStock { get; set; }
            public int MissingStock { get; set; }
            public string? Notes { get; set; }
            public bool? IsActive { get; set; }
        }

        public sealed class InventoryDamageRequest
        {
            public int InventoryItemId { get; set; }
            public int Quantity { get; set; }
            public string? DamageType { get; set; }
            public string? Reason { get; set; }
            public string? Notes { get; set; }
            public string? Status { get; set; }
            public DateTime? LogDate { get; set; }
        }

        public sealed class ExpiredStockRequest
        {
            public int StockItemId { get; set; }
            public decimal QuantityDisposed { get; set; }
            public DateTime? ExpiredDate { get; set; }
            public string? Reason { get; set; }
            public string? Notes { get; set; }
        }

        private sealed class SupervisorOrderRow
        {
            public int OrderId { get; set; }
            public string OrderNumber { get; set; } = "-";
            public int? TableNumber { get; set; }
            public string Status { get; set; } = "Menunggu Diproses";
            public string PaymentMethod { get; set; } = "-";
            public string PaymentStatus { get; set; } = "-";
            public string? OrderDate { get; set; }
            public string? StartedAt { get; set; }
            public string? ReadyAt { get; set; }
            public string? CompletedAt { get; set; }
            public List<object> Items { get; set; } = new();
            public string CashierName { get; set; } = "-";
            public string CookName { get; set; } = "-";
        }

        [HttpGet("dashboard-summary")]
        public IActionResult DashboardSummary()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();

            int totalIngredients;
            int lowStockCount;
            int outStockCount;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT
    COUNT(1),
    SUM(CASE WHEN ISNULL(Quantity,0) > 0 AND ISNULL(Quantity,0) <= ISNULL(MinQuantity,0) THEN 1 ELSE 0 END),
    SUM(CASE WHEN ISNULL(Quantity,0) <= 0 THEN 1 ELSE 0 END)
FROM dbo.StockItems
WHERE ISNULL(IsActive,1)=1;";
                using var r = cmd.ExecuteReader();
                r.Read();
                totalIngredients = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                lowStockCount = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                outStockCount = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            }

            int notSellableCount;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT COUNT(1)
FROM dbo.MenuItems m
WHERE EXISTS (
    SELECT 1
    FROM dbo.MenuIngredients mi
    INNER JOIN dbo.StockItems s ON s.StockItemId = mi.StockItemId
    WHERE mi.MenuItemId = m.MenuItemId
    GROUP BY mi.MenuItemId
    HAVING SUM(CASE WHEN CAST(ISNULL(s.Quantity,0) AS decimal(18,3)) < CAST(ISNULL(mi.QuantityNeeded,0) AS decimal(18,3)) THEN 1 ELSE 0 END) > 0
);";
                notSellableCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }

            int damagedItemsCount;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.InventoryDamageLogs WHERE CAST(LogDate AS date)=CAST(GETDATE() AS date);";
                damagedItemsCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }

            int expiredLogsCount;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.StockExpiredLogs WHERE CAST(ExpiredDate AS date)=CAST(GETDATE() AS date);";
                expiredLogsCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }

            var recentUsage = new List<object>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP 8 l.UsedAt, ISNULL(o.OrderNumber, CONCAT('#', l.OrderId)), ISNULL(mi.Name,'-'), ISNULL(s.Name,'-'), l.QuantityUsed, ISNULL(l.RemainingStock,0)
FROM dbo.StockUsageLogs l
LEFT JOIN dbo.Orders o ON o.OrderId = l.OrderId
LEFT JOIN dbo.MenuItems mi ON mi.MenuItemId = l.MenuItemId
LEFT JOIN dbo.StockItems s ON s.StockItemId = l.StockItemId
ORDER BY l.UsedAt DESC;";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    recentUsage.Add(new
                    {
                        usedAt = rd.IsDBNull(0) ? null : rd.GetDateTime(0).ToString("o"),
                        orderNumber = rd.IsDBNull(1) ? "-" : rd.GetString(1),
                        menuName = rd.IsDBNull(2) ? "-" : rd.GetString(2),
                        ingredient = rd.IsDBNull(3) ? "-" : rd.GetString(3),
                        qtyUsed = rd.IsDBNull(4) ? 0 : rd.GetDecimal(4),
                        remainingStock = rd.IsDBNull(5) ? 0 : rd.GetDecimal(5)
                    });
                }
            }

            return Ok(new
            {
                success = true,
                summary = new
                {
                    totalIngredients,
                    lowStockCount,
                    outStockCount,
                    notSellableCount,
                    damagedItemsCount,
                    expiredLogsCount,
                    recentUsage
                }
            });
        }

        [HttpGet("ingredients/all")]
        public IActionResult IngredientsAll()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT StockItemId, Name, ISNULL([Type],'RawMaterial'), CAST(ISNULL(Quantity,0) AS decimal(18,3)), CAST(ISNULL(MinQuantity,0) AS decimal(18,3)), ISNULL(Unit,'porsi'), ISNULL(Description,''), ISNULL(IsActive,1)
FROM dbo.StockItems
ORDER BY ISNULL(IsActive,1) DESC, Name ASC;";
            var rows = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var qty = rd.IsDBNull(3) ? 0m : rd.GetDecimal(3);
                var min = rd.IsDBNull(4) ? 0m : rd.GetDecimal(4);
                rows.Add(new
                {
                    stockItemId = rd.GetInt32(0),
                    name = rd.IsDBNull(1) ? "-" : rd.GetString(1),
                    type = rd.IsDBNull(2) ? "RawMaterial" : rd.GetString(2),
                    quantity = qty,
                    minQuantity = min,
                    unit = rd.IsDBNull(5) ? "porsi" : rd.GetString(5),
                    description = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    isActive = !rd.IsDBNull(7) && rd.GetBoolean(7),
                    status = qty <= 0 ? "Habis" : qty <= min ? "Stok Menipis" : "Aman"
                });
            }
            return Ok(new { success = true, ingredients = rows });
        }

        [HttpGet("ingredients/critical")]
        public IActionResult IngredientsCritical()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT StockItemId, Name, CAST(ISNULL(Quantity,0) AS decimal(18,3)), CAST(ISNULL(MinQuantity,0) AS decimal(18,3)), ISNULL(Unit,'porsi')
FROM dbo.StockItems
WHERE ISNULL(IsActive,1)=1
  AND CAST(ISNULL(Quantity,0) AS decimal(18,3)) <= CAST(ISNULL(MinQuantity,0) AS decimal(18,3))
ORDER BY CAST(ISNULL(Quantity,0) AS decimal(18,3)) ASC, Name ASC;";
            var rows = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var qty = rd.IsDBNull(2) ? 0m : rd.GetDecimal(2);
                var min = rd.IsDBNull(3) ? 0m : rd.GetDecimal(3);
                rows.Add(new
                {
                    stockItemId = rd.GetInt32(0),
                    name = rd.IsDBNull(1) ? "-" : rd.GetString(1),
                    stock = qty,
                    minStock = min,
                    unit = rd.IsDBNull(4) ? "porsi" : rd.GetString(4),
                    status = qty <= 0 ? "Habis" : "Stok Menipis"
                });
            }
            return Ok(new { success = true, criticalIngredients = rows });
        }

        [HttpPost("ingredients")]
        [IgnoreAntiforgeryToken]
        public IActionResult CreateIngredient([FromBody] IngredientUpsertRequest req)
        {
            var name = (req.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new { success = false, error = "Nama bahan wajib diisi." });
            }
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.StockItems (Name,[Type],Quantity,MinQuantity,Unit,Description,IsActive)
VALUES (@Name,@Type,@Quantity,@MinQuantity,@Unit,@Description,@IsActive);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Type", NormalizeIngredientType(req.Type));
            cmd.Parameters.AddWithValue("@Quantity", Math.Max(0m, req.Quantity));
            cmd.Parameters.AddWithValue("@MinQuantity", Math.Max(0m, req.MinQuantity));
            cmd.Parameters.AddWithValue("@Unit", NormalizeUnit(req.Unit, "porsi"));
            cmd.Parameters.AddWithValue("@Description", string.IsNullOrWhiteSpace(req.Description) ? DBNull.Value : req.Description.Trim());
            cmd.Parameters.AddWithValue("@IsActive", req.IsActive ?? true);
            var id = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            return Ok(new { success = true, stockItemId = id });
        }

        [HttpPut("ingredients/{id:int}")]
        [IgnoreAntiforgeryToken]
        public IActionResult UpdateIngredient(int id, [FromBody] IngredientUpsertRequest req)
        {
            if (id <= 0) return BadRequest(new { success = false, error = "Id bahan tidak valid." });
            var name = (req.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { success = false, error = "Nama bahan wajib diisi." });

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.StockItems
SET Name=@Name,[Type]=@Type,Quantity=@Quantity,MinQuantity=@MinQuantity,Unit=@Unit,Description=@Description,IsActive=@IsActive
WHERE StockItemId=@StockItemId;";
            cmd.Parameters.AddWithValue("@StockItemId", id);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Type", NormalizeIngredientType(req.Type));
            cmd.Parameters.AddWithValue("@Quantity", Math.Max(0m, req.Quantity));
            cmd.Parameters.AddWithValue("@MinQuantity", Math.Max(0m, req.MinQuantity));
            cmd.Parameters.AddWithValue("@Unit", NormalizeUnit(req.Unit, "porsi"));
            cmd.Parameters.AddWithValue("@Description", string.IsNullOrWhiteSpace(req.Description) ? DBNull.Value : req.Description.Trim());
            cmd.Parameters.AddWithValue("@IsActive", req.IsActive ?? true);
            var affected = cmd.ExecuteNonQuery();
            if (affected == 0) return NotFound(new { success = false, error = "Bahan tidak ditemukan." });
            return Ok(new { success = true });
        }

        [HttpPost("ingredients/{id:int}/adjust")]
        [IgnoreAntiforgeryToken]
        public IActionResult AdjustIngredient(int id, [FromBody] IngredientAdjustRequest req)
        {
            if (id <= 0) return BadRequest(new { success = false, error = "Id bahan tidak valid." });
            var amount = Math.Abs(req.Amount);
            if (amount <= 0) return BadRequest(new { success = false, error = "Jumlah penyesuaian harus > 0." });
            var subtract = (req.Mode ?? "add").Trim().ToLowerInvariant() is "subtract" or "minus" or "kurang";

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using var lockCmd = conn.CreateCommand();
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT CAST(ISNULL(Quantity,0) AS decimal(18,3)) FROM dbo.StockItems WITH (UPDLOCK, ROWLOCK) WHERE StockItemId=@StockItemId;";
            lockCmd.Parameters.AddWithValue("@StockItemId", id);
            var curr = Convert.ToDecimal(lockCmd.ExecuteScalar() ?? -1m);
            if (curr < 0)
            {
                tx.Rollback();
                return NotFound(new { success = false, error = "Bahan tidak ditemukan." });
            }
            var next = subtract ? curr - amount : curr + amount;
            if (next < 0)
            {
                tx.Rollback();
                return BadRequest(new { success = false, error = "Stok tidak cukup untuk pengurangan manual." });
            }

            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE dbo.StockItems SET Quantity=@Quantity WHERE StockItemId=@StockItemId;";
            upd.Parameters.AddWithValue("@Quantity", next);
            upd.Parameters.AddWithValue("@StockItemId", id);
            upd.ExecuteNonQuery();
            tx.Commit();
            return Ok(new { success = true, quantity = next });
        }

        [HttpDelete("ingredients/{id:int}")]
        [IgnoreAntiforgeryToken]
        public IActionResult DisableIngredient(int id)
        {
            if (id <= 0) return BadRequest(new { success = false, error = "Id bahan tidak valid." });
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.StockItems SET IsActive=0 WHERE StockItemId=@StockItemId;";
            cmd.Parameters.AddWithValue("@StockItemId", id);
            var affected = cmd.ExecuteNonQuery();
            if (affected == 0) return NotFound(new { success = false, error = "Bahan tidak ditemukan." });
            return Ok(new { success = true });
        }

        [HttpPost("ingredients/{id:int}/set-active")]
        [IgnoreAntiforgeryToken]
        public IActionResult SetIngredientActive(int id, [FromBody] IngredientUpsertRequest req)
        {
            if (id <= 0) return BadRequest(new { success = false, error = "Id bahan tidak valid." });
            var isActive = req.IsActive ?? true;
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.StockItems SET IsActive=@IsActive WHERE StockItemId=@StockItemId;";
            cmd.Parameters.AddWithValue("@StockItemId", id);
            cmd.Parameters.AddWithValue("@IsActive", isActive);
            var affected = cmd.ExecuteNonQuery();
            if (affected == 0) return NotFound(new { success = false, error = "Bahan tidak ditemukan." });
            return Ok(new { success = true, isActive });
        }

        [HttpGet("menu-recipes")]
        public IActionResult MenuRecipes()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT
    m.MenuItemId,
    m.Name AS MenuName,
    mi.MenuIngredientId,
    s.StockItemId,
    s.Name AS IngredientName,
    CAST(ISNULL(mi.QuantityNeeded,0) AS decimal(18,3)),
    ISNULL(s.Unit,'porsi'),
    CAST(ISNULL(s.Quantity,0) AS decimal(18,3))
FROM dbo.MenuItems m
LEFT JOIN dbo.MenuIngredients mi ON mi.MenuItemId = m.MenuItemId
LEFT JOIN dbo.StockItems s ON s.StockItemId = mi.StockItemId
ORDER BY m.Name ASC, s.Name ASC;";

            var map = new Dictionary<int, (string MenuName, List<object> Ingredients)>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var menuId = rd.GetInt32(0);
                if (!map.TryGetValue(menuId, out var row))
                {
                    row = (rd.IsDBNull(1) ? "-" : rd.GetString(1), new List<object>());
                    map[menuId] = row;
                }

                if (rd.IsDBNull(2)) continue;
                row.Ingredients.Add(new
                {
                    menuIngredientId = rd.GetInt32(2),
                    stockItemId = rd.IsDBNull(3) ? 0 : rd.GetInt32(3),
                    ingredientName = rd.IsDBNull(4) ? "-" : rd.GetString(4),
                    quantityNeeded = rd.IsDBNull(5) ? 0 : rd.GetDecimal(5),
                    unit = rd.IsDBNull(6) ? "porsi" : rd.GetString(6),
                    currentStock = rd.IsDBNull(7) ? 0 : rd.GetDecimal(7)
                });
            }

            var rows = map.Select(x =>
            {
                var canSell = x.Value.Ingredients.Count == 0 || x.Value.Ingredients.All(i =>
                {
                    var t = i.GetType();
                    var need = Convert.ToDecimal(t.GetProperty("quantityNeeded")?.GetValue(i) ?? 0m);
                    var stock = Convert.ToDecimal(t.GetProperty("currentStock")?.GetValue(i) ?? 0m);
                    return stock >= need;
                });
                return new { menuItemId = x.Key, menuName = x.Value.MenuName, canSell, ingredients = x.Value.Ingredients };
            });

            return Ok(new { success = true, recipes = rows });
        }

        [HttpGet("menu-options")]
        public IActionResult MenuOptions()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            var hasIsActive = ColumnExists(conn, "MenuItems", "IsActive");
            cmd.CommandText = hasIsActive
                ? "SELECT MenuItemId, Name FROM dbo.MenuItems WHERE ISNULL(IsActive,1)=1 ORDER BY Name ASC;"
                : "SELECT MenuItemId, Name FROM dbo.MenuItems ORDER BY Name ASC;";
            var rows = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new
                {
                    menuItemId = rd.GetInt32(0),
                    name = rd.IsDBNull(1) ? "-" : rd.GetString(1)
                });
            }
            return Ok(new { success = true, menuItems = rows });
        }

        [HttpPost("menu-recipes")]
        [IgnoreAntiforgeryToken]
        public IActionResult UpsertRecipe([FromBody] RecipeUpsertRequest req)
        {
            if (req.MenuItemId <= 0 || req.StockItemId <= 0 || req.QuantityNeeded <= 0)
            {
                return BadRequest(new { success = false, error = "Data resep tidak valid." });
            }
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.MenuIngredients WHERE MenuItemId=@MenuItemId AND StockItemId=@StockItemId)
BEGIN
    UPDATE dbo.MenuIngredients
    SET QuantityNeeded = @QuantityNeeded
    WHERE MenuItemId=@MenuItemId AND StockItemId=@StockItemId;
END
ELSE
BEGIN
    INSERT INTO dbo.MenuIngredients (MenuItemId, StockItemId, QuantityNeeded)
    VALUES (@MenuItemId, @StockItemId, @QuantityNeeded);
END;";
            cmd.Parameters.AddWithValue("@MenuItemId", req.MenuItemId);
            cmd.Parameters.AddWithValue("@StockItemId", req.StockItemId);
            cmd.Parameters.AddWithValue("@QuantityNeeded", Math.Round(req.QuantityNeeded, 3, MidpointRounding.AwayFromZero));
            cmd.ExecuteNonQuery();
            return Ok(new { success = true });
        }

        [HttpPut("menu-recipes/{menuIngredientId:int}")]
        [IgnoreAntiforgeryToken]
        public IActionResult UpdateRecipe(int menuIngredientId, [FromBody] RecipeUpsertRequest req)
        {
            if (menuIngredientId <= 0 || req.QuantityNeeded <= 0) return BadRequest(new { success = false, error = "Data resep tidak valid." });
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.MenuIngredients SET QuantityNeeded=@QuantityNeeded WHERE MenuIngredientId=@MenuIngredientId;";
            cmd.Parameters.AddWithValue("@QuantityNeeded", Math.Round(req.QuantityNeeded, 3, MidpointRounding.AwayFromZero));
            cmd.Parameters.AddWithValue("@MenuIngredientId", menuIngredientId);
            var affected = cmd.ExecuteNonQuery();
            if (affected == 0) return NotFound(new { success = false, error = "Item resep tidak ditemukan." });
            return Ok(new { success = true });
        }

        [HttpDelete("menu-recipes/{menuIngredientId:int}")]
        [IgnoreAntiforgeryToken]
        public IActionResult DeleteRecipe(int menuIngredientId)
        {
            if (menuIngredientId <= 0) return BadRequest(new { success = false, error = "Id resep tidak valid." });
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM dbo.MenuIngredients WHERE MenuIngredientId=@MenuIngredientId;";
            cmd.Parameters.AddWithValue("@MenuIngredientId", menuIngredientId);
            var affected = cmd.ExecuteNonQuery();
            if (affected == 0) return NotFound(new { success = false, error = "Item resep tidak ditemukan." });
            return Ok(new { success = true });
        }

        [HttpGet("stock-usage-logs")]
        public IActionResult StockUsageLogs()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 200
    l.StockUsageLogId,
    l.OrderId,
    ISNULL(o.OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(o.OrderId AS nvarchar(20))), 6))) AS OrderNumber,
    ISNULL(mi.Name, '-') AS MenuName,
    ISNULL(s.Name, '-') AS Ingredient,
    l.QuantityUsed,
    ISNULL(l.RemainingStock,0) AS RemainingStock,
    l.UsedAt,
    ISNULL(l.Notes,'') AS Notes,
    ISNULL(u.UserName,'-') AS CookName
FROM dbo.StockUsageLogs l
LEFT JOIN dbo.StockItems s ON s.StockItemId = l.StockItemId
LEFT JOIN dbo.Orders o ON o.OrderId = l.OrderId
LEFT JOIN dbo.MenuItems mi ON mi.MenuItemId = l.MenuItemId
LEFT JOIN dbo.AspNetUsers u ON u.Id = l.CookUserId
ORDER BY l.UsedAt DESC;";
            var list = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    id = reader.GetInt32(0),
                    orderId = reader.GetInt32(1),
                    orderNumber = reader.IsDBNull(2) ? "-" : reader.GetString(2),
                    menuName = reader.IsDBNull(3) ? "-" : reader.GetString(3),
                    ingredient = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                    qtyUsed = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                    remainingStock = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                    usedAt = reader.GetDateTime(7).ToString("o"),
                    note = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    cookName = reader.IsDBNull(9) ? "-" : reader.GetString(9)
                });
            }
            return Ok(new { success = true, logs = list });
        }

        [HttpGet("orders")]
        public IActionResult Orders()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            var hasTableNumber = ColumnExists(conn, "Orders", "TableNumber");
            var tableNumberSql = hasTableNumber ? "o.TableNumber" : "NULL AS TableNumber";
            cmd.CommandText = @"
SELECT TOP 200
    o.OrderId,
    ISNULL(o.OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(o.OrderId AS nvarchar(20))), 6))) AS OrderNumber,
    " + tableNumberSql + @",
    ISNULL(o.Status,'Menunggu Diproses'),
    ISNULL(o.PaymentMethod,'-'),
    ISNULL(o.PaymentStatus,'-'),
    o.OrderDate,
    o.StartedAt,
    o.ReadyAt,
    o.CompletedAt
FROM dbo.Orders o
ORDER BY ISNULL(o.OrderDate, GETDATE()) DESC, o.OrderId DESC;";

            var orders = new List<SupervisorOrderRow>();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    orders.Add(new SupervisorOrderRow
                    {
                        OrderId = rd.GetInt32(0),
                        OrderNumber = rd.IsDBNull(1) ? "-" : rd.GetString(1),
                        TableNumber = rd.IsDBNull(2) ? null : rd.GetInt32(2),
                        Status = rd.IsDBNull(3) ? "Menunggu Diproses" : rd.GetString(3),
                        PaymentMethod = rd.IsDBNull(4) ? "-" : rd.GetString(4),
                        PaymentStatus = rd.IsDBNull(5) ? "-" : rd.GetString(5),
                        OrderDate = rd.IsDBNull(6) ? null : rd.GetDateTime(6).ToString("o"),
                        StartedAt = rd.IsDBNull(7) ? null : rd.GetDateTime(7).ToString("o"),
                        ReadyAt = rd.IsDBNull(8) ? null : rd.GetDateTime(8).ToString("o"),
                        CompletedAt = rd.IsDBNull(9) ? null : rd.GetDateTime(9).ToString("o")
                    });
                }
            }

            if (orders.Count > 0)
            {
                var ids = string.Join(",", orders.Select(x => x.OrderId.ToString()));
                using var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = $@"
SELECT oi.OrderId, ISNULL(oi.ItemName, ISNULL(mi.Name,'-')) AS ItemName, ISNULL(oi.Quantity,0) AS Qty, ISNULL(oi.Notes,'') AS Notes
FROM dbo.OrderItems oi
LEFT JOIN dbo.MenuItems mi ON mi.MenuItemId = oi.MenuItemId
WHERE oi.OrderId IN ({ids});";
                var itemMap = new Dictionary<int, List<object>>();
                using var itemRd = itemCmd.ExecuteReader();
                while (itemRd.Read())
                {
                    var orderId = itemRd.GetInt32(0);
                    if (!itemMap.TryGetValue(orderId, out var list))
                    {
                        list = new List<object>();
                        itemMap[orderId] = list;
                    }
                    list.Add(new
                    {
                        name = itemRd.IsDBNull(1) ? "-" : itemRd.GetString(1),
                        quantity = itemRd.IsDBNull(2) ? 0 : itemRd.GetInt32(2),
                        notes = itemRd.IsDBNull(3) ? "" : itemRd.GetString(3)
                    });
                }

                foreach (var x in orders)
                {
                    var id = x.OrderId;
                    x.Items = itemMap.TryGetValue(id, out var list) ? list : new List<object>();
                }
            }

            return Ok(new
            {
                success = true,
                orders = orders.Select(x => new
                {
                    orderId = x.OrderId,
                    orderNumber = x.OrderNumber,
                    tableNumber = x.TableNumber,
                    status = x.Status,
                    paymentMethod = x.PaymentMethod,
                    paymentStatus = x.PaymentStatus,
                    orderDate = x.OrderDate,
                    startedAt = x.StartedAt,
                    readyAt = x.ReadyAt,
                    completedAt = x.CompletedAt,
                    items = x.Items,
                    cashierName = x.CashierName,
                    cookName = x.CookName
                })
            });
        }

        [HttpGet("operational-alerts")]
        public IActionResult OperationalAlerts()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            var alerts = new List<object>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP 50 Name, CAST(ISNULL(Quantity,0) AS decimal(18,3)), CAST(ISNULL(MinQuantity,0) AS decimal(18,3)), ISNULL(Unit,'porsi')
FROM dbo.StockItems
WHERE ISNULL(IsActive,1)=1
  AND CAST(ISNULL(Quantity,0) AS decimal(18,3)) <= CAST(ISNULL(MinQuantity,0) AS decimal(18,3))
ORDER BY CAST(ISNULL(Quantity,0) AS decimal(18,3)) ASC;";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var qty = rd.IsDBNull(1) ? 0m : rd.GetDecimal(1);
                    var min = rd.IsDBNull(2) ? 0m : rd.GetDecimal(2);
                    var unit = rd.IsDBNull(3) ? "porsi" : rd.GetString(3);
                    alerts.Add(new
                    {
                        type = "stock",
                        severity = qty <= 0 ? "danger" : "warning",
                        title = qty <= 0 ? "Stok Habis" : "Stok Menipis",
                        message = $"{(rd.IsDBNull(0) ? "-" : rd.GetString(0))}: {qty} {unit} (min {min} {unit})"
                    });
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT TOP 30 ISNULL(OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(OrderId AS nvarchar(20))), 6))), ISNULL(Status,'Menunggu Diproses')
FROM dbo.Orders
WHERE ISNULL(Status,'') IN ('Menunggu Diproses', 'Diproses')
ORDER BY ISNULL(OrderDate,GETDATE()) ASC;";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var status = rd.IsDBNull(1) ? "Menunggu Diproses" : rd.GetString(1);
                    alerts.Add(new
                    {
                        type = "order",
                        severity = status.Equals("Diproses", StringComparison.OrdinalIgnoreCase) ? "warning" : "info",
                        title = "Order Perlu Perhatian",
                        message = $"{(rd.IsDBNull(0) ? "-" : rd.GetString(0))} masih status {status}."
                    });
                }
            }

            return Ok(new { success = true, alerts });
        }

        [HttpGet("inventory/items")]
        public IActionResult InventoryItems()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT InventoryItemId, Name, Category, Unit, TotalStock, GoodStock, BrokenStock, MissingStock, ISNULL(Notes,''), ISNULL(IsActive,1) FROM dbo.InventoryItems ORDER BY ISNULL(IsActive,1) DESC, Name ASC;";
            var items = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                items.Add(new
                {
                    inventoryItemId = rd.GetInt32(0),
                    name = rd.IsDBNull(1) ? "-" : rd.GetString(1),
                    category = rd.IsDBNull(2) ? "umum" : rd.GetString(2),
                    unit = rd.IsDBNull(3) ? "pcs" : rd.GetString(3),
                    totalStock = rd.IsDBNull(4) ? 0 : rd.GetInt32(4),
                    goodStock = rd.IsDBNull(5) ? 0 : rd.GetInt32(5),
                    brokenStock = rd.IsDBNull(6) ? 0 : rd.GetInt32(6),
                    missingStock = rd.IsDBNull(7) ? 0 : rd.GetInt32(7),
                    notes = rd.IsDBNull(8) ? "" : rd.GetString(8),
                    isActive = !rd.IsDBNull(9) && rd.GetBoolean(9)
                });
            }
            return Ok(new { success = true, items });
        }

        [HttpPost("inventory/items")]
        [IgnoreAntiforgeryToken]
        public IActionResult CreateInventoryItem([FromBody] InventoryItemUpsertRequest req)
        {
            var name = (req.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { success = false, error = "Nama barang wajib diisi." });
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.InventoryItems (Name,Category,Unit,TotalStock,GoodStock,BrokenStock,MissingStock,Notes,IsActive,UpdatedAt)
VALUES (@Name,@Category,@Unit,@TotalStock,@GoodStock,@BrokenStock,@MissingStock,@Notes,@IsActive,SYSUTCDATETIME());";
            var total = Math.Max(0, req.TotalStock);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Category", string.IsNullOrWhiteSpace(req.Category) ? "umum" : req.Category.Trim());
            cmd.Parameters.AddWithValue("@Unit", NormalizeUnit(req.Unit, "pcs"));
            cmd.Parameters.AddWithValue("@TotalStock", total);
            cmd.Parameters.AddWithValue("@GoodStock", Math.Min(total, Math.Max(0, req.GoodStock)));
            cmd.Parameters.AddWithValue("@BrokenStock", Math.Min(total, Math.Max(0, req.BrokenStock)));
            cmd.Parameters.AddWithValue("@MissingStock", Math.Min(total, Math.Max(0, req.MissingStock)));
            cmd.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(req.Notes) ? DBNull.Value : req.Notes.Trim());
            cmd.Parameters.AddWithValue("@IsActive", req.IsActive ?? true);
            cmd.ExecuteNonQuery();
            return Ok(new { success = true });
        }

        [HttpPut("inventory/items/{id:int}")]
        [IgnoreAntiforgeryToken]
        public IActionResult UpdateInventoryItem(int id, [FromBody] InventoryItemUpsertRequest req)
        {
            if (id <= 0) return BadRequest(new { success = false, error = "Id barang tidak valid." });
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.InventoryItems
SET Name=@Name, Category=@Category, Unit=@Unit, TotalStock=@TotalStock, GoodStock=@GoodStock, BrokenStock=@BrokenStock, MissingStock=@MissingStock, Notes=@Notes, IsActive=@IsActive, UpdatedAt=SYSUTCDATETIME()
WHERE InventoryItemId=@InventoryItemId;";
            var total = Math.Max(0, req.TotalStock);
            cmd.Parameters.AddWithValue("@InventoryItemId", id);
            cmd.Parameters.AddWithValue("@Name", string.IsNullOrWhiteSpace(req.Name) ? "-" : req.Name.Trim());
            cmd.Parameters.AddWithValue("@Category", string.IsNullOrWhiteSpace(req.Category) ? "umum" : req.Category.Trim());
            cmd.Parameters.AddWithValue("@Unit", NormalizeUnit(req.Unit, "pcs"));
            cmd.Parameters.AddWithValue("@TotalStock", total);
            cmd.Parameters.AddWithValue("@GoodStock", Math.Min(total, Math.Max(0, req.GoodStock)));
            cmd.Parameters.AddWithValue("@BrokenStock", Math.Min(total, Math.Max(0, req.BrokenStock)));
            cmd.Parameters.AddWithValue("@MissingStock", Math.Min(total, Math.Max(0, req.MissingStock)));
            cmd.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(req.Notes) ? DBNull.Value : req.Notes.Trim());
            cmd.Parameters.AddWithValue("@IsActive", req.IsActive ?? true);
            var affected = cmd.ExecuteNonQuery();
            if (affected == 0) return NotFound(new { success = false, error = "Barang tidak ditemukan." });
            return Ok(new { success = true });
        }

        [HttpGet("inventory/damages")]
        public IActionResult InventoryDamages()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 200 d.DamageLogId,d.InventoryItemId,i.Name,d.Quantity,d.DamageType,d.LogDate,ISNULL(d.Reason,''),ISNULL(d.Notes,''),ISNULL(d.Status,'dicatat')
FROM dbo.InventoryDamageLogs d
INNER JOIN dbo.InventoryItems i ON i.InventoryItemId = d.InventoryItemId
ORDER BY d.LogDate DESC,d.DamageLogId DESC;";
            var rows = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new
                {
                    damageLogId = rd.GetInt32(0),
                    inventoryItemId = rd.GetInt32(1),
                    itemName = rd.IsDBNull(2) ? "-" : rd.GetString(2),
                    quantity = rd.IsDBNull(3) ? 0 : rd.GetInt32(3),
                    damageType = rd.IsDBNull(4) ? "rusak" : rd.GetString(4),
                    logDate = rd.IsDBNull(5) ? null : rd.GetDateTime(5).ToString("yyyy-MM-dd"),
                    reason = rd.IsDBNull(6) ? "" : rd.GetString(6),
                    notes = rd.IsDBNull(7) ? "" : rd.GetString(7),
                    status = rd.IsDBNull(8) ? "dicatat" : rd.GetString(8)
                });
            }
            return Ok(new { success = true, damages = rows });
        }

        [HttpPost("inventory/damages")]
        [IgnoreAntiforgeryToken]
        public IActionResult CreateInventoryDamage([FromBody] InventoryDamageRequest req)
        {
            if (req.InventoryItemId <= 0 || req.Quantity <= 0) return BadRequest(new { success = false, error = "Data barang rusak/hilang tidak valid." });
            var damageType = NormalizeDamageType(req.DamageType);
            var status = NormalizeDamageStatus(req.Status);

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.Transaction = tx;
                lockCmd.CommandText = "SELECT TOP 1 GoodStock FROM dbo.InventoryItems WITH (UPDLOCK, ROWLOCK) WHERE InventoryItemId=@InventoryItemId;";
                lockCmd.Parameters.AddWithValue("@InventoryItemId", req.InventoryItemId);
                var good = Convert.ToInt32(lockCmd.ExecuteScalar() ?? -1);
                if (good < 0)
                {
                    tx.Rollback();
                    return NotFound(new { success = false, error = "Barang inventaris tidak ditemukan." });
                }
                if (good < req.Quantity)
                {
                    tx.Rollback();
                    return BadRequest(new { success = false, error = "Stok kondisi baik tidak cukup." });
                }

                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = @"
UPDATE dbo.InventoryItems
SET GoodStock = GoodStock - @Quantity,
    BrokenStock = BrokenStock + @BrokenAdd,
    MissingStock = MissingStock + @MissingAdd,
    UpdatedAt = SYSUTCDATETIME()
WHERE InventoryItemId = @InventoryItemId;";
                upd.Parameters.AddWithValue("@Quantity", req.Quantity);
                upd.Parameters.AddWithValue("@BrokenAdd", damageType == "rusak" ? req.Quantity : 0);
                upd.Parameters.AddWithValue("@MissingAdd", damageType == "hilang" ? req.Quantity : 0);
                upd.Parameters.AddWithValue("@InventoryItemId", req.InventoryItemId);
                upd.ExecuteNonQuery();
            }

            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = @"
INSERT INTO dbo.InventoryDamageLogs (InventoryItemId, Quantity, DamageType, LogDate, Reason, Notes, Status, CreatedBy)
VALUES (@InventoryItemId, @Quantity, @DamageType, @LogDate, @Reason, @Notes, @Status, @CreatedBy);";
                ins.Parameters.AddWithValue("@InventoryItemId", req.InventoryItemId);
                ins.Parameters.AddWithValue("@Quantity", req.Quantity);
                ins.Parameters.AddWithValue("@DamageType", damageType);
                ins.Parameters.AddWithValue("@LogDate", (req.LogDate ?? DateTime.UtcNow).Date);
                ins.Parameters.AddWithValue("@Reason", string.IsNullOrWhiteSpace(req.Reason) ? DBNull.Value : req.Reason.Trim());
                ins.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(req.Notes) ? DBNull.Value : req.Notes.Trim());
                ins.Parameters.AddWithValue("@Status", status);
                ins.Parameters.AddWithValue("@CreatedBy", DBNull.Value);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
            return Ok(new { success = true });
        }

        [HttpGet("expired-logs")]
        public IActionResult ExpiredLogs()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 200 e.ExpiredLogId,e.StockItemId,s.Name,e.QuantityDisposed,e.ExpiredDate,ISNULL(e.Reason,''),ISNULL(e.Notes,'')
FROM dbo.StockExpiredLogs e
INNER JOIN dbo.StockItems s ON s.StockItemId = e.StockItemId
ORDER BY e.ExpiredDate DESC,e.ExpiredLogId DESC;";
            var rows = new List<object>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new
                {
                    expiredLogId = rd.GetInt32(0),
                    stockItemId = rd.GetInt32(1),
                    stockName = rd.IsDBNull(2) ? "-" : rd.GetString(2),
                    quantityDisposed = rd.IsDBNull(3) ? 0 : rd.GetDecimal(3),
                    expiredDate = rd.IsDBNull(4) ? null : rd.GetDateTime(4).ToString("yyyy-MM-dd"),
                    reason = rd.IsDBNull(5) ? "" : rd.GetString(5),
                    notes = rd.IsDBNull(6) ? "" : rd.GetString(6)
                });
            }
            return Ok(new { success = true, expiredLogs = rows });
        }

        [HttpPost("expired-logs")]
        [IgnoreAntiforgeryToken]
        public IActionResult CreateExpiredLog([FromBody] ExpiredStockRequest req)
        {
            if (req.StockItemId <= 0 || req.QuantityDisposed <= 0)
            {
                return BadRequest(new { success = false, error = "Data bahan basi/expired tidak valid." });
            }
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.Transaction = tx;
                lockCmd.CommandText = "SELECT CAST(ISNULL(Quantity,0) AS decimal(18,3)) FROM dbo.StockItems WITH (UPDLOCK, ROWLOCK) WHERE StockItemId=@StockItemId;";
                lockCmd.Parameters.AddWithValue("@StockItemId", req.StockItemId);
                var qty = Convert.ToDecimal(lockCmd.ExecuteScalar() ?? -1m);
                if (qty < 0)
                {
                    tx.Rollback();
                    return NotFound(new { success = false, error = "Bahan tidak ditemukan." });
                }
                if (qty < req.QuantityDisposed)
                {
                    tx.Rollback();
                    return BadRequest(new { success = false, error = "Stok bahan tidak cukup untuk dibuang." });
                }

                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE dbo.StockItems SET Quantity = Quantity - @Quantity WHERE StockItemId = @StockItemId;";
                upd.Parameters.AddWithValue("@Quantity", Math.Round(req.QuantityDisposed, 3, MidpointRounding.AwayFromZero));
                upd.Parameters.AddWithValue("@StockItemId", req.StockItemId);
                upd.ExecuteNonQuery();
            }

            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = @"
INSERT INTO dbo.StockExpiredLogs (StockItemId, QuantityDisposed, ExpiredDate, Reason, Notes, CreatedBy)
VALUES (@StockItemId, @QuantityDisposed, @ExpiredDate, @Reason, @Notes, @CreatedBy);";
                ins.Parameters.AddWithValue("@StockItemId", req.StockItemId);
                ins.Parameters.AddWithValue("@QuantityDisposed", Math.Round(req.QuantityDisposed, 3, MidpointRounding.AwayFromZero));
                ins.Parameters.AddWithValue("@ExpiredDate", (req.ExpiredDate ?? DateTime.UtcNow).Date);
                ins.Parameters.AddWithValue("@Reason", string.IsNullOrWhiteSpace(req.Reason) ? DBNull.Value : req.Reason.Trim());
                ins.Parameters.AddWithValue("@Notes", string.IsNullOrWhiteSpace(req.Notes) ? DBNull.Value : req.Notes.Trim());
                ins.Parameters.AddWithValue("@CreatedBy", DBNull.Value);
                ins.ExecuteNonQuery();
            }

            tx.Commit();
            return Ok(new { success = true });
        }

        private static string NormalizeIngredientType(string? type)
        {
            var t = (type ?? "RawMaterial").Trim();
            return string.IsNullOrWhiteSpace(t) ? "RawMaterial" : t;
        }

        private static string NormalizeUnit(string? unit, string fallback)
        {
            var u = (unit ?? "").Trim();
            return string.IsNullOrWhiteSpace(u) ? fallback : u;
        }

        private static string NormalizeDamageType(string? damageType)
        {
            var t = (damageType ?? "rusak").Trim().ToLowerInvariant();
            return t == "hilang" ? "hilang" : "rusak";
        }

        private static string NormalizeDamageStatus(string? status)
        {
            var s = (status ?? "dicatat").Trim().ToLowerInvariant();
            return s is "diganti" or "selesai" ? s : "dicatat";
        }

        private static bool ColumnExists(SqlConnection conn, string tableName, string columnName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT COUNT(1)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME=@TableName AND COLUMN_NAME=@ColumnName;";
            cmd.Parameters.AddWithValue("@TableName", tableName);
            cmd.Parameters.AddWithValue("@ColumnName", columnName);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
        }
    }
}
