using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Models;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "KokiOnly")]
    [Route("Kitchen")]
    [Route("Dapur")]
    [Route("api/kitchen")]
    public class KitchenController : Controller
    {
        private readonly IConfiguration _configuration;

        public KitchenController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();

            if (!TableExists(conn, "Orders"))
            {
                return View("~/Views/Kitchen/Index.cshtml", new KitchenDashboardVm());
            }

            var all = LoadKitchenOrders(conn);
            var vm = new KitchenDashboardVm
            {
                PendingOrders = all.Where(o => o.Status == "pending").ToList(),
                ProcessingOrders = all.Where(o => o.Status == "processing").ToList(),
                ReadyOrders = all.Where(o => o.Status == "ready").ToList()
            };

            return View("~/Views/Kitchen/Index.cshtml", vm);
        }

        [HttpGet("orders")]
        public IActionResult GetOrders([FromQuery] string? status = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();

            if (!TableExists(conn, "Orders"))
            {
                return Ok(new { success = true, orders = Array.Empty<object>() });
            }

            var orders = LoadKitchenOrders(conn);
            var normalized = NormalizeKitchenStatus(status);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                orders = orders.Where(o => o.Status == normalized).ToList();
            }

            var rows = orders.Select(o => new
            {
                orderId = o.OrderId,
                tableNumber = o.TableNumber,
                orderDate = o.OrderDate?.ToString("o"),
                status = o.Status,
                updatedAt = o.UpdatedAt.ToString("o"),
                note = o.Note,
                items = o.Items.Select(i => new { quantity = i.Quantity, name = i.Name, notes = i.Notes })
            });

            return Ok(new { success = true, orders = rows });
        }

        public sealed class UpdateKitchenOrderRequest
        {
            public string? Status { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }

        [HttpPatch("orders/{id:int}/status")]
        [IgnoreAntiforgeryToken]
        public IActionResult UpdateStatus(int id, [FromBody] UpdateKitchenOrderRequest request)
        {
            if (id <= 0)
            {
                return BadRequest(new { success = false, error = "Order tidak valid." });
            }

            var next = NormalizeKitchenStatus(request.Status);
            if (next != "processing" && next != "ready")
            {
                return BadRequest(new { success = false, error = "Status dapur harus processing atau ready." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            if (!TableExists(conn, "Orders"))
            {
                return NotFound(new { success = false, error = "Data order belum tersedia." });
            }
            using var tx = conn.BeginTransaction();

            var current = GetOrderState(conn, tx, id);
            if (current == null)
            {
                return NotFound(new { success = false, error = "Order tidak ditemukan." });
            }

            if (!CanTransition(current.KitchenStatus, next))
            {
                tx.Rollback();
                return Conflict(new
                {
                    success = false,
                    error = "Status sudah diupdate oleh user lain.",
                    currentStatus = current.KitchenStatus,
                    updatedAt = current.UpdatedAt.ToString("o")
                });
            }

            var cookUserId = TryGetCurrentUserId(conn, tx);

            if (next == "processing")
            {
                var consume = TryConsumeIngredients(conn, tx, id, cookUserId);
                if (!consume.Success)
                {
                    InsertRoleNotification(conn, tx, AppRoles.Supervisor, "stock_insufficient", "Bahan tidak cukup", consume.Error ?? "Bahan tidak cukup untuk memproses pesanan.");
                    tx.Rollback();
                    return BadRequest(new { success = false, error = consume.Error ?? "Stok bahan tidak cukup." });
                }
            }

            var orderStatus = next switch
            {
                "processing" => "diproses",
                "ready" => "siap",
                _ => "diproses"
            };
            using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            if (next == "processing")
            {
                updateCmd.CommandText = @"
UPDATE dbo.Orders
SET KitchenStatus = @KitchenStatus,
    [Status] = @OrderStatus,
    CookUserId = COALESCE(@CookUserId, CookUserId),
    StartedAt = COALESCE(StartedAt, SYSUTCDATETIME()),
    UpdatedAt = SYSUTCDATETIME()
WHERE OrderId = @OrderId
  ;";
            }
            else
            {
                updateCmd.CommandText = @"
UPDATE dbo.Orders
SET KitchenStatus = @KitchenStatus,
    [Status] = @OrderStatus,
    ReadyAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE OrderId = @OrderId
  ;";
            }
            updateCmd.Parameters.AddWithValue("@KitchenStatus", next);
            updateCmd.Parameters.AddWithValue("@OrderStatus", orderStatus);
            updateCmd.Parameters.AddWithValue("@CookUserId", (object?)cookUserId ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@OrderId", id);

            var affected = updateCmd.ExecuteNonQuery();
            if (affected == 0)
            {
                tx.Rollback();
                return Conflict(new { success = false, error = "Order sudah diupdate oleh user lain." });
            }

            if (next == "ready")
            {
                using var notifCmd = conn.CreateCommand();
                notifCmd.Transaction = tx;
                notifCmd.CommandText = @"
INSERT INTO dbo.KitchenNotifications (OrderId, TargetRole, IsRead, CreatedAt)
VALUES (@OrderId, @TargetRole, 0, SYSUTCDATETIME());";
                notifCmd.Parameters.AddWithValue("@OrderId", id);
                notifCmd.Parameters.AddWithValue("@TargetRole", AppRoles.Kasir);
                notifCmd.ExecuteNonQuery();
            }

            using var readCmd = conn.CreateCommand();
            readCmd.Transaction = tx;
            readCmd.CommandText = "SELECT UpdatedAt FROM dbo.Orders WHERE OrderId = @OrderId;";
            readCmd.Parameters.AddWithValue("@OrderId", id);
            var updatedAt = (DateTime?)readCmd.ExecuteScalar() ?? DateTime.UtcNow;

            tx.Commit();
            return Ok(new { success = true, orderId = id, status = next, updatedAt = updatedAt.ToString("o") });
        }

        [HttpPatch("orders/{id:int}/start")]
        [IgnoreAntiforgeryToken]
        public IActionResult StartOrder(int id, [FromBody] UpdateKitchenOrderRequest request)
        {
            request.Status = "processing";
            return UpdateStatus(id, request);
        }

        [HttpPatch("orders/{id:int}/ready")]
        [IgnoreAntiforgeryToken]
        public IActionResult ReadyOrder(int id, [FromBody] UpdateKitchenOrderRequest request)
        {
            request.Status = "ready";
            return UpdateStatus(id, request);
        }

        private List<KitchenOrderCardVm> LoadKitchenOrders(SqlConnection conn)
        {
            var orderColumns = GetColumns(conn, "Orders");
            var hasOrderItems = TableExists(conn, "OrderItems");
            var dateCol = FirstColumn(orderColumns, "OrderDate", "CreatedAt", "CreatedOn", "Created");
            var noteCol = FirstColumn(orderColumns, "Notes", "Note", "CustomerNote");
            var hasKitchenStatus = orderColumns.Contains("KitchenStatus");

            var selectSql = $@"
SELECT o.OrderId,
       t.TableNumber,
       {(string.IsNullOrWhiteSpace(dateCol) ? "CAST(NULL AS datetime2)" : $"CAST(o.{dateCol} AS datetime2)")},
       CAST(o.[Status] AS nvarchar(50)),
       {(hasKitchenStatus ? "CAST(o.KitchenStatus AS nvarchar(50))" : "CAST(NULL AS nvarchar(50))")},
       CAST(o.UpdatedAt AS datetime2),
       {(string.IsNullOrWhiteSpace(noteCol) ? "CAST(NULL AS nvarchar(250))" : $"CAST(o.{noteCol} AS nvarchar(250))")}
FROM dbo.Orders o
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
WHERE ISNULL(LOWER(o.PaymentStatus), '') IN ('lunas','paid')
ORDER BY {(string.IsNullOrWhiteSpace(dateCol) ? "o.OrderId" : $"o.{dateCol}")} ASC;";

            var result = new List<KitchenOrderCardVm>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = selectSql;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var status = NormalizeKitchenStatus(reader.IsDBNull(4) ? null : reader.GetString(4));
                    if (string.IsNullOrWhiteSpace(status))
                    {
                        status = NormalizeKitchenStatus(reader.IsDBNull(3) ? null : reader.GetString(3)) ?? "pending";
                    }

                    result.Add(new KitchenOrderCardVm
                    {
                        OrderId = reader.GetInt32(0),
                        TableNumber = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                        OrderDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                        Status = status,
                        UpdatedAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                        Note = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }
            }

            if (hasOrderItems && result.Count > 0)
            {
                LoadOrderItems(conn, result);
            }

            foreach (var order in result)
            {
                if (order.Items.Count == 0)
                {
                    order.Items.Add(new KitchenOrderItemVm
                    {
                        Quantity = 1,
                        Name = "Detail item belum tersedia",
                        Notes = order.Note
                    });
                }
            }

            return result;
        }

        private static void LoadOrderItems(SqlConnection conn, List<KitchenOrderCardVm> orders)
        {
            var orderMap = orders.ToDictionary(o => o.OrderId);
            var orderIdList = string.Join(",", orders.Select(o => o.OrderId));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT oi.OrderId,
       ISNULL(oi.Quantity, 1) AS Quantity,
       COALESCE(NULLIF(LTRIM(RTRIM(oi.ItemName)), ''), mi.Name, 'Item') AS ItemName,
       oi.Notes
FROM dbo.OrderItems oi
LEFT JOIN dbo.MenuItems mi ON mi.MenuItemId = oi.MenuItemId
WHERE oi.OrderId IN ({orderIdList})
ORDER BY oi.OrderItemId ASC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var orderId = reader.GetInt32(0);
                if (!orderMap.TryGetValue(orderId, out var order))
                {
                    continue;
                }

                order.Items.Add(new KitchenOrderItemVm
                {
                    Quantity = reader.IsDBNull(1) ? 1 : reader.GetInt32(1),
                    Name = reader.IsDBNull(2) ? "Item" : reader.GetString(2),
                    Notes = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }
        }

        private static KitchenOrderState? GetOrderState(SqlConnection conn, SqlTransaction tx, int orderId)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT TOP 1
    CAST([Status] AS nvarchar(50)) AS OrderStatus,
    CAST(KitchenStatus AS nvarchar(50)) AS KitchenStatus,
    UpdatedAt
FROM dbo.Orders WITH (UPDLOCK, ROWLOCK)
WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", orderId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var kitchenStatus = NormalizeKitchenStatus(reader.IsDBNull(1) ? null : reader.GetString(1));
            if (string.IsNullOrWhiteSpace(kitchenStatus))
            {
                kitchenStatus = NormalizeKitchenStatus(reader.IsDBNull(0) ? null : reader.GetString(0)) ?? "pending";
            }

            return new KitchenOrderState
            {
                KitchenStatus = kitchenStatus,
                UpdatedAt = reader.GetDateTime(2)
            };
        }

        private static bool CanTransition(string current, string next)
        {
            return (current, next) switch
            {
                ("pending", "processing") => true,
                ("processing", "ready") => true,
                _ => false
            };
        }

        private static string? NormalizeKitchenStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var key = value.Trim().ToLowerInvariant();
            if (key is "pending" or "menunggu" or "menunggu diproses" or "new")
            {
                return "pending";
            }

            if (key is "processing" or "diproses" or "cooking")
            {
                return "processing";
            }

            if (key is "ready" or "selesai")
            {
                return "ready";
            }

            if (key is "completed" or "done" or "finish" or "paid" or "canceled" or "cancelled")
            {
                return "ready";
            }

            return null;
        }

        private string? TryGetCurrentUserId(SqlConnection conn, SqlTransaction tx)
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT TOP 1 Id FROM dbo.AspNetUsers WHERE UserName = @UserName;";
            cmd.Parameters.AddWithValue("@UserName", username);
            return cmd.ExecuteScalar()?.ToString();
        }

        private static ConsumeResult TryConsumeIngredients(SqlConnection conn, SqlTransaction tx, int orderId, string? cookUserId)
        {
            using var reqCmd = conn.CreateCommand();
            reqCmd.Transaction = tx;
            reqCmd.CommandText = @"
SELECT
    oi.MenuItemId,
    mi.StockItemId,
    SUM(CAST(ISNULL(oi.Quantity, 1) AS decimal(18,3)) * CAST(mi.QuantityNeeded AS decimal(18,3))) AS NeededQty
FROM dbo.OrderItems oi
INNER JOIN dbo.MenuIngredients mi ON mi.MenuItemId = oi.MenuItemId
WHERE oi.OrderId = @OrderId
GROUP BY oi.MenuItemId, mi.StockItemId;";
            reqCmd.Parameters.AddWithValue("@OrderId", orderId);

            var required = new List<(int MenuItemId, int StockItemId, decimal NeededQty)>();
            using (var reader = reqCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    required.Add((
                        reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.IsDBNull(2) ? 0 : reader.GetDecimal(2)
                    ));
                }
            }

            if (required.Count == 0)
            {
                return new ConsumeResult { Success = true };
            }

            var aggregateRequired = required
                .GroupBy(x => x.StockItemId)
                .Select(g => new { StockItemId = g.Key, NeededQty = g.Sum(x => x.NeededQty) })
                .ToList();

            foreach (var item in aggregateRequired)
            {
                using var stockCmd = conn.CreateCommand();
                stockCmd.Transaction = tx;
                stockCmd.CommandText = @"
SELECT TOP 1 Name, CAST(ISNULL(Quantity, 0) AS decimal(18,3))
FROM dbo.StockItems WITH (UPDLOCK, ROWLOCK)
WHERE StockItemId = @StockItemId;";
                stockCmd.Parameters.AddWithValue("@StockItemId", item.StockItemId);

                using var stockReader = stockCmd.ExecuteReader();
                if (!stockReader.Read())
                {
                    return new ConsumeResult { Success = false, Error = "Data ingredient tidak ditemukan." };
                }

                var name = stockReader.IsDBNull(0) ? "Ingredient" : stockReader.GetString(0);
                var currentQty = stockReader.IsDBNull(1) ? 0m : stockReader.GetDecimal(1);
                var neededQty = Math.Round(item.NeededQty, 3, MidpointRounding.AwayFromZero);

                if (currentQty < neededQty)
                {
                    return new ConsumeResult { Success = false, Error = $"Stok {name} tidak cukup. Dibutuhkan {neededQty}, tersisa {currentQty}." };
                }
                stockReader.Close();

                using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = @"
UPDATE dbo.StockItems
SET Quantity = Quantity - @NeededQty
WHERE StockItemId = @StockItemId;";
                updateCmd.Parameters.AddWithValue("@NeededQty", neededQty);
                updateCmd.Parameters.AddWithValue("@StockItemId", item.StockItemId);
                updateCmd.ExecuteNonQuery();
            }

            foreach (var item in required)
            {
                using var remainingCmd = conn.CreateCommand();
                remainingCmd.Transaction = tx;
                remainingCmd.CommandText = "SELECT CAST(ISNULL(Quantity,0) AS decimal(18,3)) FROM dbo.StockItems WHERE StockItemId = @StockItemId;";
                remainingCmd.Parameters.AddWithValue("@StockItemId", item.StockItemId);
                var remaining = Convert.ToDecimal(remainingCmd.ExecuteScalar() ?? 0m);

                using var logCmd = conn.CreateCommand();
                logCmd.Transaction = tx;
                logCmd.CommandText = @"
INSERT INTO dbo.StockUsageLogs (StockItemId, OrderId, MenuItemId, QuantityUsed, RemainingStock, CookUserId, UsedAt, Notes)
VALUES (@StockItemId, @OrderId, @MenuItemId, @QuantityUsed, @RemainingStock, @CookUserId, SYSUTCDATETIME(), @Notes);";
                logCmd.Parameters.AddWithValue("@StockItemId", item.StockItemId);
                logCmd.Parameters.AddWithValue("@OrderId", orderId);
                logCmd.Parameters.AddWithValue("@MenuItemId", item.MenuItemId <= 0 ? DBNull.Value : item.MenuItemId);
                logCmd.Parameters.AddWithValue("@QuantityUsed", Math.Round(item.NeededQty, 3, MidpointRounding.AwayFromZero));
                logCmd.Parameters.AddWithValue("@RemainingStock", remaining);
                logCmd.Parameters.AddWithValue("@CookUserId", (object?)cookUserId ?? DBNull.Value);
                logCmd.Parameters.AddWithValue("@Notes", "Auto-consume saat order mulai diproses");
                logCmd.ExecuteNonQuery();
            }

            return new ConsumeResult { Success = true };
        }

        private static void InsertRoleNotification(SqlConnection conn, SqlTransaction tx, string roleTarget, string type, string title, string message)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO dbo.UserNotifications (RoleTarget, Type, Title, Message, IsRead, CreatedAt)
VALUES (@RoleTarget, @Type, @Title, @Message, 0, SYSUTCDATETIME());";
            cmd.Parameters.AddWithValue("@RoleTarget", roleTarget);
            cmd.Parameters.AddWithValue("@Type", type);
            cmd.Parameters.AddWithValue("@Title", title);
            cmd.Parameters.AddWithValue("@Message", message.Length > 280 ? message[..280] : message);
            cmd.ExecuteNonQuery();
        }

        private static HashSet<string> GetColumns(SqlConnection conn, string tableName)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName;";
            cmd.Parameters.AddWithValue("@TableName", tableName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
            return columns;
        }

        private static string? FirstColumn(HashSet<string> columns, params string[] names)
        {
            foreach (var name in names)
            {
                if (columns.Contains(name))
                {
                    return name;
                }
            }
            return null;
        }

        private static bool TableExists(SqlConnection conn, string tableName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END;";
            cmd.Parameters.AddWithValue("@TableName", $"dbo.{tableName}");
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
        }

        private sealed class KitchenOrderState
        {
            public string KitchenStatus { get; set; } = "pending";
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class ConsumeResult
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
        }
    }
}
