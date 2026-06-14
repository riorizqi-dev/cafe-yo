using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Authorize(Policy = "KasirOnly")]
    [Route("cashier/notifications")]
    public class CashierNotificationsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public CashierNotificationsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult GetUnread()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 50
    n.Id,
    n.OrderId,
    t.TableNumber,
    n.CreatedAt,
    ISNULL(items.ItemSummary, '-') AS ItemSummary
FROM dbo.KitchenNotifications n
LEFT JOIN dbo.Orders o ON o.OrderId = n.OrderId
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
OUTER APPLY (
    SELECT STRING_AGG(CONCAT(CAST(ISNULL(oi.Quantity, 1) AS nvarchar(10)), 'x ', COALESCE(NULLIF(LTRIM(RTRIM(oi.ItemName)), ''), 'Item')), ', ') AS ItemSummary
    FROM (
        SELECT TOP 3 oi.Quantity, oi.ItemName
        FROM dbo.OrderItems oi
        WHERE oi.OrderId = n.OrderId
        ORDER BY oi.OrderItemId ASC
    ) oi
) items
WHERE n.TargetRole = @TargetRole
  AND n.IsRead = 0
ORDER BY n.CreatedAt DESC;";
            cmd.Parameters.AddWithValue("@TargetRole", AppRoles.Kasir);

            var rows = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var orderId = reader.GetInt32(1);
                var tableNumber = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                rows.Add(new
                {
                    id = reader.GetInt32(0),
                    orderId,
                    tableNumber,
                    createdAt = reader.GetDateTime(3).ToString("o"),
                    itemSummary = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                    message = $"Pesanan Meja {(tableNumber.HasValue ? tableNumber.Value.ToString() : "-")} / Order #{orderId} sudah siap."
                });
            }

            return Ok(new { success = true, notifications = rows });
        }

        [HttpPost("{id:int}/ack")]
        [IgnoreAntiforgeryToken]
        public IActionResult Ack(int id)
        {
            if (id <= 0)
            {
                return BadRequest(new { success = false, error = "ID notifikasi tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.KitchenNotifications
SET IsRead = 1,
    AcknowledgedAt = SYSUTCDATETIME()
WHERE Id = @Id
  AND IsRead = 0;";
            cmd.Parameters.AddWithValue("@Id", id);
            var affected = cmd.ExecuteNonQuery();

            return Ok(new { success = true, acknowledged = affected > 0 });
        }
    }
}
