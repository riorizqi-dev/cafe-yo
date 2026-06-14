using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Models;
using cafe_yo.Data;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "KasirOnly")]
    [Route("Kasir")]
    public class KasirController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<KasirController> _logger;

        public KasirController(IConfiguration configuration, ILogger<KasirController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var model = new KasirDashboardViewModel();

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureCallTable(conn);
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);
            model.Tables = TableStateStore.GetAll(conn)
                .Select(t => new KasirTableCard
                {
                    TableNumber = t.TableNumber,
                    Status = t.Status ?? "Kosong"
                })
                .ToList();

            using (var menuCmd = conn.CreateCommand())
            {
                menuCmd.CommandText = @"
SELECT MenuItemId, Name, Price, ISNULL(IsAvailable, 1) AS IsAvailable
     , ImageUrl, Category, Description
FROM dbo.MenuItems
WHERE ISNULL(IsAvailable, 1) = 1
  AND NULLIF(LTRIM(RTRIM(Name)), '') IS NOT NULL
ORDER BY Name ASC;";
                using var menuReader = menuCmd.ExecuteReader();
                while (menuReader.Read())
                {
                    model.MenuItems.Add(new KasirMenuOption
                    {
                        MenuItemId = menuReader.GetInt32(0),
                        Name = menuReader.IsDBNull(1) ? string.Empty : menuReader.GetString(1),
                        Price = menuReader.IsDBNull(2) ? 0 : menuReader.GetDecimal(2),
                        IsAvailable = !menuReader.IsDBNull(3) && menuReader.GetBoolean(3),
                        ImageUrl = menuReader.IsDBNull(4) ? null : menuReader.GetString(4),
                        Category = menuReader.IsDBNull(5) ? null : menuReader.GetString(5),
                        Description = menuReader.IsDBNull(6) ? null : menuReader.GetString(6)
                    });
                }
            }

            return View("~/Views/Kasir/index.cshtml", model);
        }

        public sealed class CreateOrderItemRequest
        {
            public int MenuItemId { get; set; }
            public int Quantity { get; set; }
            public string? Notes { get; set; }
        }

        public sealed class CreateOrderRequest
        {
            public int TableNumber { get; set; }
            public string? Note { get; set; }
            public string? PaymentMethod { get; set; }
            public List<CreateOrderItemRequest> Items { get; set; } = new();
        }

        [HttpPost("CreateOrder")]
        [IgnoreAntiforgeryToken]
        public IActionResult CreateOrder([FromBody] CreateOrderRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return BadRequest(new { success = false, error = "Nomor meja tidak valid." });
            }
            if (req.Items == null)
            {
                return BadRequest(new { success = false, error = "Item pesanan wajib diisi." });
            }

            var normalizedItems = req.Items
                .Where(i => i.MenuItemId > 0 && i.Quantity > 0)
                .Select(i => new CreateOrderItemRequest
                {
                    MenuItemId = i.MenuItemId,
                    Quantity = i.Quantity,
                    Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim()
                })
                .ToList();

            if (normalizedItems.Count == 0)
            {
                return BadRequest(new { success = false, error = "Pesanan kosong. Tambahkan minimal 1 item." });
            }

            _logger.LogInformation("CreateOrder request kasir. Table={TableNumber}, Items={@Items}, Note={Note}",
                req.TableNumber, normalizedItems.Select(x => new { x.MenuItemId, x.Quantity, x.Notes }).ToList(), req.Note);

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);
            var cashierUserId = TryGetCurrentUserId(conn);

            var tableId = 0;
            using (var tableCmd = conn.CreateCommand())
            {
                tableCmd.CommandText = "SELECT TOP 1 TableId FROM dbo.Tables WHERE TableNumber = @TableNumber;";
                tableCmd.Parameters.AddWithValue("@TableNumber", req.TableNumber);
                var tableObj = tableCmd.ExecuteScalar();
                tableId = tableObj == null ? 0 : Convert.ToInt32(tableObj, CultureInfo.InvariantCulture);
            }

            if (tableId <= 0)
            {
                return BadRequest(new { success = false, error = "Meja tidak ditemukan." });
            }

            var menuMap = new Dictionary<int, (string Name, decimal Price, bool IsAvailable)>();
            using (var menuCmd = conn.CreateCommand())
            {
                menuCmd.CommandText = @"
SELECT MenuItemId, Name, Price, ISNULL(IsAvailable, 1) AS IsAvailable
FROM dbo.MenuItems;";
                using var reader = menuCmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt32(0);
                    menuMap[id] = (
                        reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        reader.IsDBNull(2) ? 0 : reader.GetDecimal(2),
                        !reader.IsDBNull(3) && reader.GetBoolean(3));
                }
            }

            foreach (var item in normalizedItems)
            {
                if (!menuMap.TryGetValue(item.MenuItemId, out var menu))
                {
                    return BadRequest(new { success = false, error = $"Menu dengan id {item.MenuItemId} tidak ditemukan." });
                }

                if (!menu.IsAvailable)
                {
                    return BadRequest(new { success = false, error = $"Menu {menu.Name} sedang tidak tersedia." });
                }
            }

            _logger.LogInformation("CreateOrder menu map validated. Items={@Items}",
                normalizedItems.Select(x => new
                {
                    x.MenuItemId,
                    x.Quantity,
                    MenuName = menuMap[x.MenuItemId].Name
                }).ToList());

            using var tx = conn.BeginTransaction();
            int orderId;
            decimal total = 0;
            var paymentMethod = string.Equals(req.PaymentMethod, "qris", StringComparison.OrdinalIgnoreCase) ? "qris" : "kasir";
            var initialPaymentStatus = paymentMethod == "qris" ? "menunggu_qris" : "belum_bayar";
            var initialOrderStatus = "menunggu_pembayaran";

            using (var insertOrder = conn.CreateCommand())
            {
                insertOrder.Transaction = tx;
                insertOrder.CommandText = @"
INSERT INTO dbo.Orders (OrderNumber, TableId, CashierUserId, OrderDate, [Status], Total, KitchenStatus, PaymentMethod, PaymentStatus, UpdatedAt)
VALUES (@OrderNumber, @TableId, @CashierUserId, SYSUTCDATETIME(), @Status, 0, @KitchenStatus, @PaymentMethod, @PaymentStatus, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";
                insertOrder.Parameters.AddWithValue("@OrderNumber", BuildOrderNumber());
                insertOrder.Parameters.AddWithValue("@TableId", tableId);
                insertOrder.Parameters.AddWithValue("@CashierUserId", (object?)cashierUserId ?? DBNull.Value);
                insertOrder.Parameters.AddWithValue("@Status", initialOrderStatus);
                insertOrder.Parameters.AddWithValue("@KitchenStatus", "pending");
                insertOrder.Parameters.AddWithValue("@PaymentMethod", paymentMethod);
                insertOrder.Parameters.AddWithValue("@PaymentStatus", initialPaymentStatus);
                var newOrderObj = insertOrder.ExecuteScalar();
                orderId = newOrderObj == null ? 0 : Convert.ToInt32(newOrderObj, CultureInfo.InvariantCulture);
            }

            if (orderId <= 0)
            {
                tx.Rollback();
                return StatusCode(500, new { success = false, error = "Gagal membuat order." });
            }

            foreach (var item in normalizedItems)
            {
                var menu = menuMap[item.MenuItemId];
                total += (menu.Price * item.Quantity);

                using var insertItem = conn.CreateCommand();
                insertItem.Transaction = tx;
                insertItem.CommandText = @"
INSERT INTO dbo.OrderItems (OrderId, MenuItemId, ItemName, Quantity, Notes, UnitPrice)
VALUES (@OrderId, @MenuItemId, @ItemName, @Quantity, @Notes, @UnitPrice);";
                insertItem.Parameters.AddWithValue("@OrderId", orderId);
                insertItem.Parameters.AddWithValue("@MenuItemId", item.MenuItemId);
                insertItem.Parameters.AddWithValue("@ItemName", menu.Name);
                insertItem.Parameters.AddWithValue("@Quantity", item.Quantity);
                insertItem.Parameters.AddWithValue("@Notes", (object?)item.Notes ?? DBNull.Value);
                insertItem.Parameters.AddWithValue("@UnitPrice", menu.Price);
                insertItem.ExecuteNonQuery();
            }

            using (var updateTotal = conn.CreateCommand())
            {
                updateTotal.Transaction = tx;
                updateTotal.CommandText = @"
UPDATE dbo.Orders
SET Total = @Total,
    UpdatedAt = SYSUTCDATETIME()
WHERE OrderId = @OrderId;";
                updateTotal.Parameters.AddWithValue("@Total", total);
                updateTotal.Parameters.AddWithValue("@OrderId", orderId);
                updateTotal.ExecuteNonQuery();
            }

            tx.Commit();

            try
            {
                OrderTableSync.SyncByTableNumber(conn, req.TableNumber);
            }
            catch
            {
                // Non-critical: order already saved even if table status update fails.
            }

            _logger.LogInformation("CreateOrder saved. OrderId={OrderId}, Table={TableNumber}, Total={Total}", orderId, req.TableNumber, total);

            return Ok(new
            {
                success = true,
                orderId,
                tableNumber = req.TableNumber,
                status = initialOrderStatus,
                paymentMethod,
                paymentStatus = initialPaymentStatus,
                total = total.ToString("0.##", CultureInfo.InvariantCulture),
                message = $"Order #{orderId} masuk ke dapur."
            });
        }

        public sealed class FinalizeOrderRequest
        {
            public int OrderId { get; set; }
            public string? Reason { get; set; }
        }

        private sealed class PendingPaymentRow
        {
            public int OrderId { get; set; }
            public string OrderCode { get; set; } = "-";
            public int? TableNumber { get; set; }
            public decimal Total { get; set; }
            public string PaymentMethod { get; set; } = "-";
            public string PaymentStatus { get; set; } = "-";
            public string? OrderDate { get; set; }
        }

        public sealed class FinalizeTableRequest
        {
            public int TableNumber { get; set; }
        }

        [HttpPost("CompleteOrder")]
        [IgnoreAntiforgeryToken]
        public IActionResult CompleteOrder([FromBody] FinalizeOrderRequest req)
        {
            if (req.OrderId <= 0)
            {
                return BadRequest(new { success = false, error = "Order tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.Orders
SET [Status] = 'Completed',
    CompletedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", req.OrderId);
            var affected = cmd.ExecuteNonQuery();
            if (affected > 0)
            {
                OrderTableSync.SyncByOrderId(conn, req.OrderId);
            }

            return Ok(new { success = affected > 0 });
        }

        [HttpPost("CompleteTable")]
        [IgnoreAntiforgeryToken]
        public IActionResult CompleteTable([FromBody] FinalizeTableRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return BadRequest(new { success = false, error = "Nomor meja tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            TableStateStore.EnsureTables(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE o
SET o.[Status] = 'Completed',
    o.CompletedAt = SYSUTCDATETIME(),
    o.UpdatedAt = SYSUTCDATETIME()
FROM dbo.Orders o
INNER JOIN dbo.Tables t ON t.TableId = o.TableId
WHERE t.TableNumber = @TableNumber
  AND (
        o.[Status] IS NULL
        OR LOWER(LTRIM(RTRIM(o.[Status]))) NOT IN ('completed','paid','cancelled','canceled','selesai','dibatalkan')
      );";
            cmd.Parameters.AddWithValue("@TableNumber", req.TableNumber);
            var affected = cmd.ExecuteNonQuery();

            OrderTableSync.SyncByTableNumber(conn, req.TableNumber);
            TableStateStore.ReleaseBooking(conn, req.TableNumber);

            var currentStatus = TableStateStore.GetAll(conn)
                .FirstOrDefault(x => x.TableNumber == req.TableNumber)?.Status ?? "Kosong";

            return Ok(new
            {
                success = true,
                tableNumber = req.TableNumber,
                completedOrders = affected,
                status = currentStatus
            });
        }

        [HttpPost("CancelOrder")]
        [IgnoreAntiforgeryToken]
        public IActionResult CancelOrder([FromBody] FinalizeOrderRequest req)
        {
            if (req.OrderId <= 0)
            {
                return BadRequest(new { success = false, error = "Order tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.Orders
SET [Status] = 'Cancelled',
    CancelledAt = SYSUTCDATETIME(),
    CancelledReason = @Reason,
    UpdatedAt = SYSUTCDATETIME()
WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", req.OrderId);
            cmd.Parameters.AddWithValue("@Reason", string.IsNullOrWhiteSpace(req.Reason) ? DBNull.Value : req.Reason.Trim());
            var affected = cmd.ExecuteNonQuery();
            if (affected > 0)
            {
                OrderTableSync.SyncByOrderId(conn, req.OrderId);
            }

            if (affected > 0)
            {
                using var notifCmd = conn.CreateCommand();
                notifCmd.CommandText = @"
INSERT INTO dbo.UserNotifications (RoleTarget, Type, Title, Message, IsRead, CreatedAt)
VALUES (@RoleTarget, @Type, @Title, @Message, 0, SYSUTCDATETIME());";
                notifCmd.Parameters.AddWithValue("@RoleTarget", AppRoles.Supervisor);
                notifCmd.Parameters.AddWithValue("@Type", "order_cancelled");
                notifCmd.Parameters.AddWithValue("@Title", "Pesanan Dibatalkan");
                notifCmd.Parameters.AddWithValue("@Message", $"Order #{req.OrderId} dibatalkan oleh kasir.");
                notifCmd.ExecuteNonQuery();
            }

            return Ok(new { success = affected > 0 });
        }

        [HttpGet("PendingPayments")]
        public IActionResult PendingPayments([FromQuery] string? method = null, [FromQuery] string? status = null)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();

            var rows = new List<PendingPaymentRow>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 200
    o.OrderId,
    ISNULL(o.OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(o.OrderId AS nvarchar(20))), 6))) AS OrderNumber,
    t.TableNumber,
    o.Total,
    ISNULL(o.PaymentMethod, '-') AS PaymentMethod,
    ISNULL(o.PaymentStatus, '-') AS PaymentStatus,
    o.OrderDate
FROM dbo.Orders o
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
WHERE (
        ISNULL(LOWER(o.PaymentStatus), '') IN ('belum_bayar','menunggu_qris','pending')
        OR (ISNULL(LOWER(o.[Status]), '') = 'menunggu_pembayaran' AND ISNULL(LOWER(o.PaymentStatus), '') <> 'lunas')
      )
ORDER BY o.OrderDate DESC, o.OrderId DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var paymentMethod = reader.IsDBNull(4) ? "-" : reader.GetString(4);
                var paymentStatus = reader.IsDBNull(5) ? "-" : reader.GetString(5);
                rows.Add(new PendingPaymentRow
                {
                    OrderId = reader.GetInt32(0),
                    OrderCode = reader.IsDBNull(1) ? "-" : reader.GetString(1),
                    TableNumber = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                    Total = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    PaymentMethod = paymentMethod,
                    PaymentStatus = paymentStatus,
                    OrderDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6).ToString("o")
                });
            }

            IEnumerable<PendingPaymentRow> filtered = rows;
            var methodFilter = (method ?? string.Empty).Trim().ToLowerInvariant();
            if (methodFilter is "kasir" or "qris")
            {
                filtered = filtered.Where(x => string.Equals(x.PaymentMethod, methodFilter, StringComparison.OrdinalIgnoreCase));
            }

            var statusFilter = (status ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                filtered = filtered.Where(x => string.Equals(x.PaymentStatus, statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            return Ok(new { success = true, orders = filtered.ToList() });
        }

        public sealed class ConfirmPaymentRequest
        {
            public int OrderId { get; set; }
        }

        [HttpPost("ConfirmPayment")]
        [IgnoreAntiforgeryToken]
        public IActionResult ConfirmPayment([FromBody] ConfirmPaymentRequest req)
        {
            if (req.OrderId <= 0)
            {
                return BadRequest(new { success = false, error = "Order tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.Orders
SET PaymentStatus = 'lunas',
    [Status] = 'diproses',
    KitchenStatus = 'processing',
    PaidAt = COALESCE(PaidAt, SYSUTCDATETIME()),
    StartedAt = COALESCE(StartedAt, SYSUTCDATETIME()),
    UpdatedAt = SYSUTCDATETIME()
WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", req.OrderId);
            var affected = cmd.ExecuteNonQuery();
            if (affected <= 0)
            {
                return NotFound(new { success = false, error = "Order tidak ditemukan." });
            }

            return Ok(new { success = true, orderId = req.OrderId, paymentStatus = "lunas", orderStatus = "diproses" });
        }

        public sealed class CallReadyRequest
        {
            public int TableNumber { get; set; }
            public string? Message { get; set; }
        }

        [HttpPost("CallReady")]
        [IgnoreAntiforgeryToken]
        public IActionResult CallReady([FromBody] CallReadyRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return Json(new { success = false, error = "Nomor meja tidak valid." });
            }

            var message = string.IsNullOrWhiteSpace(req.Message)
                ? $"Pesanan Anda sudah siap. Silakan ke meja {req.TableNumber}."
                : req.Message.Trim();

            if (message.Length > 200)
            {
                message = message[..200];
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureCallTable(conn);

            using var insert = conn.CreateCommand();
            insert.CommandText = @"
INSERT INTO [TableCallNotifications] (TableNumber, Message, CreatedAt, TriggeredBy)
VALUES (@TableNumber, @Message, SYSUTCDATETIME(), @TriggeredBy);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            insert.Parameters.AddWithValue("@TableNumber", req.TableNumber);
            insert.Parameters.AddWithValue("@Message", message);
            insert.Parameters.AddWithValue("@TriggeredBy", User?.Identity?.Name ?? "Kasir");

            var idObj = insert.ExecuteScalar();
            var callId = idObj != null ? Convert.ToInt32(idObj, CultureInfo.InvariantCulture) : 0;

            try
            {
                TableStateStore.EnsureTables(conn);
                OrderTableSync.SyncByTableNumber(conn, req.TableNumber);
            }
            catch
            {
                // Non-critical: tables master may not be available.
            }

            return Json(new { success = true, callId, message });
        }

        private static void EnsureCallTable(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF OBJECT_ID(N'dbo.TableCallNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TableCallNotifications (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TableNumber INT NOT NULL,
        Message NVARCHAR(200) NOT NULL,
        CreatedAt DATETIME2 NOT NULL,
        TriggeredBy NVARCHAR(100) NULL
    );
END;";
            cmd.ExecuteNonQuery();
        }

        private string? TryGetCurrentUserId(SqlConnection conn)
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 Id FROM dbo.AspNetUsers WHERE UserName = @UserName;";
            cmd.Parameters.AddWithValue("@UserName", username);
            return cmd.ExecuteScalar()?.ToString();
        }

        private static string BuildOrderNumber()
        {
            return $"ORD-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        }
    }
}
