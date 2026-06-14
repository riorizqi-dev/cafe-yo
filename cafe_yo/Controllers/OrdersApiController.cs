using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Data;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrdersApiController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public OrdersApiController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        public sealed class CreateOrderItemRequest
        {
            public int MenuId { get; set; }
            public int Qty { get; set; }
            public string? Catatan { get; set; }
        }

        public sealed class CreateOrderRequest
        {
            public int NomorMeja { get; set; }
            public List<CreateOrderItemRequest> Items { get; set; } = new();
        }

        [HttpPost("")]
        [Authorize(Policy = "KasirOnly")]
        [IgnoreAntiforgeryToken]
        public IActionResult Create([FromBody] CreateOrderRequest req)
        {
            if (req.NomorMeja <= 0 || req.Items.Count == 0)
            {
                return BadRequest(new { success = false, error = "Data order tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);

            var tableIdCmd = conn.CreateCommand();
            tableIdCmd.CommandText = "SELECT TOP 1 TableId FROM dbo.Tables WHERE TableNumber = @TableNumber;";
            tableIdCmd.Parameters.AddWithValue("@TableNumber", req.NomorMeja);
            var tableIdObj = tableIdCmd.ExecuteScalar();
            if (tableIdObj == null)
            {
                return BadRequest(new { success = false, error = "Meja tidak ditemukan." });
            }
            var tableId = Convert.ToInt32(tableIdObj, CultureInfo.InvariantCulture);

            using var tx = conn.BeginTransaction();
            var orderId = 0;
            decimal total = 0;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO dbo.Orders (OrderNumber, TableId, OrderDate, [Status], Total, KitchenStatus, UpdatedAt)
VALUES (@OrderNumber, @TableId, SYSUTCDATETIME(), 'Pending', 0, 'pending', SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";
                cmd.Parameters.AddWithValue("@OrderNumber", $"ORD-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
                cmd.Parameters.AddWithValue("@TableId", tableId);
                orderId = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
            }

            foreach (var item in req.Items.Where(x => x.MenuId > 0 && x.Qty > 0))
            {
                using var menuCmd = conn.CreateCommand();
                menuCmd.Transaction = tx;
                menuCmd.CommandText = "SELECT TOP 1 Name, Price FROM dbo.MenuItems WHERE MenuItemId = @MenuItemId;";
                menuCmd.Parameters.AddWithValue("@MenuItemId", item.MenuId);
                using var menuReader = menuCmd.ExecuteReader();
                if (!menuReader.Read())
                {
                    tx.Rollback();
                    return BadRequest(new { success = false, error = $"Menu {item.MenuId} tidak ditemukan." });
                }
                var name = menuReader.IsDBNull(0) ? "Item" : menuReader.GetString(0);
                var price = menuReader.IsDBNull(1) ? 0m : menuReader.GetDecimal(1);
                menuReader.Close();

                total += price * item.Qty;
                using var insItem = conn.CreateCommand();
                insItem.Transaction = tx;
                insItem.CommandText = @"
INSERT INTO dbo.OrderItems (OrderId, MenuItemId, ItemName, Quantity, Notes, UnitPrice)
VALUES (@OrderId, @MenuItemId, @ItemName, @Quantity, @Notes, @UnitPrice);";
                insItem.Parameters.AddWithValue("@OrderId", orderId);
                insItem.Parameters.AddWithValue("@MenuItemId", item.MenuId);
                insItem.Parameters.AddWithValue("@ItemName", name);
                insItem.Parameters.AddWithValue("@Quantity", item.Qty);
                insItem.Parameters.AddWithValue("@Notes", (object?)item.Catatan ?? DBNull.Value);
                insItem.Parameters.AddWithValue("@UnitPrice", price);
                insItem.ExecuteNonQuery();
            }

            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE dbo.Orders SET Total = @Total, UpdatedAt = SYSUTCDATETIME() WHERE OrderId = @OrderId;";
                upd.Parameters.AddWithValue("@Total", total);
                upd.Parameters.AddWithValue("@OrderId", orderId);
                upd.ExecuteNonQuery();
            }
            tx.Commit();
            OrderTableSync.SyncByTableNumber(conn, req.NomorMeja);
            return Ok(new { success = true, orderId });
        }

        [HttpGet("")]
        [Authorize(Roles = AppRoles.Kasir + "," + AppRoles.Supervisor + "," + AppRoles.Admin + "," + AppRoles.Owner + ",kasir,supervisor,admin,owner")]
        public IActionResult List()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 200
    o.OrderId,
    ISNULL(o.OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(o.OrderId AS nvarchar(20))), 6))) AS OrderNumber,
    t.TableNumber,
    o.OrderDate,
    o.[Status],
    o.KitchenStatus,
    o.Total
FROM dbo.Orders o
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
ORDER BY o.OrderDate DESC, o.OrderId DESC;";
            var rows = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new
                {
                    id = reader.GetInt32(0),
                    nomorPesanan = reader.IsDBNull(1) ? "-" : reader.GetString(1),
                    nomorMeja = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                    waktuPesan = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToString("o"),
                    status = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                    kitchenStatus = reader.IsDBNull(5) ? "-" : reader.GetString(5),
                    total = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6)
                });
            }
            return Ok(new { success = true, orders = rows });
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = AppRoles.Kasir + "," + AppRoles.Supervisor + "," + AppRoles.Admin + "," + AppRoles.Owner + ",kasir,supervisor,admin,owner")]
        public IActionResult Detail(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT o.OrderId,
       ISNULL(o.OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(o.OrderId AS nvarchar(20))), 6))) AS OrderNumber,
       t.TableNumber,
       o.OrderDate,
       o.[Status],
       o.KitchenStatus,
       o.Total
FROM dbo.Orders o
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
WHERE o.OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return NotFound(new { success = false, error = "Order tidak ditemukan." });
            }
            var payload = new
            {
                id = reader.GetInt32(0),
                nomorPesanan = reader.IsDBNull(1) ? "-" : reader.GetString(1),
                nomorMeja = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                waktuPesan = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToString("o"),
                status = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                kitchenStatus = reader.IsDBNull(5) ? "-" : reader.GetString(5),
                total = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6)
            };
            reader.Close();

            var items = new List<object>();
            using var itemCmd = conn.CreateCommand();
            itemCmd.CommandText = @"
SELECT
    ISNULL(oi.ItemName, ISNULL(m.Name, 'Item')) AS ItemName,
    ISNULL(oi.Quantity, 1) AS Quantity,
    ISNULL(oi.UnitPrice, ISNULL(m.Price, 0)) AS UnitPrice,
    ISNULL(oi.Notes, '') AS Notes
FROM dbo.OrderItems oi
LEFT JOIN dbo.MenuItems m ON m.MenuItemId = oi.MenuItemId
WHERE oi.OrderId = @OrderId
ORDER BY oi.OrderItemId ASC;";
            itemCmd.Parameters.AddWithValue("@OrderId", id);
            using var itemReader = itemCmd.ExecuteReader();
            while (itemReader.Read())
            {
                var qty = itemReader.IsDBNull(1) ? 1 : itemReader.GetInt32(1);
                var unit = itemReader.IsDBNull(2) ? 0m : itemReader.GetDecimal(2);
                items.Add(new
                {
                    name = itemReader.IsDBNull(0) ? "Item" : itemReader.GetString(0),
                    quantity = qty,
                    unitPrice = unit,
                    subtotal = unit * qty,
                    notes = itemReader.IsDBNull(3) ? string.Empty : itemReader.GetString(3)
                });
            }
            return Ok(new { success = true, order = payload, items });
        }
    }
}
