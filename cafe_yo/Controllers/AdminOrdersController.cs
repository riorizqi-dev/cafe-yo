using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using cafe_yo.Models;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/Orders")]
    public class AdminOrdersController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminOrdersController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var orders = new List<AdminOrderRow>();
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT o.OrderId, o.TableId, t.TableNumber, o.OrderDate, o.Status, o.Total
FROM [Orders] o
LEFT JOIN [Tables] t ON o.TableId = t.TableId
ORDER BY o.OrderDate DESC, o.OrderId DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                orders.Add(new AdminOrderRow
                {
                    OrderId = reader.GetInt32(0),
                    TableId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                    TableNumber = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                    OrderDate = reader.IsDBNull(3) ? (System.DateTime?)null : reader.GetDateTime(3),
                    Status = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Total = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5)
                });
            }

            return View("~/Views/Admin/Orders/Index.cshtml", orders);
        }

        [HttpGet("{id:int}")]
        public IActionResult Details(int id)
        {
            var order = GetOrder(id);
            if (order == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/Orders/Details.cshtml", order);
        }

        [HttpGet("Edit/{id:int}")]
        public IActionResult Edit(int id)
        {
            var order = GetOrder(id);
            if (order == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/Orders/Edit.cshtml", order);
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, AdminOrderEditVM model)
        {
            if (id != model.OrderId)
            {
                return BadRequest();
            }
            if (string.IsNullOrWhiteSpace(model.Status))
            {
                ModelState.AddModelError(nameof(AdminOrderEditVM.Status), "Status is required.");
            }
            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/Orders/Edit.cshtml", model);
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE [Orders] SET Status = @Status WHERE OrderId = @Id;";
            cmd.Parameters.AddWithValue("@Status", model.Status);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            cafe_yo.Data.OrderTableSync.SyncByOrderId(conn, id);

            return RedirectToAction(nameof(Index));
        }

        private AdminOrderEditVM? GetOrder(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT o.OrderId, o.TableId, t.TableNumber, o.OrderDate, o.Status, o.Total
FROM [Orders] o
LEFT JOIN [Tables] t ON o.TableId = t.TableId
WHERE o.OrderId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            var vm = new AdminOrderEditVM
            {
                OrderId = reader.GetInt32(0),
                TableId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                TableNumber = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                OrderDate = reader.IsDBNull(3) ? (System.DateTime?)null : reader.GetDateTime(3),
                Status = reader.IsDBNull(4) ? null : reader.GetString(4),
                Total = reader.IsDBNull(5) ? (decimal?)null : reader.GetDecimal(5)
            };

            using var itemCmd = conn.CreateCommand();
            itemCmd.CommandText = @"
SELECT
    COALESCE(NULLIF(LTRIM(RTRIM(oi.ItemName)), ''), 'Item') AS ItemName,
    ISNULL(oi.Quantity, 1) AS Quantity,
    ISNULL(oi.UnitPrice, 0) AS UnitPrice,
    NULLIF(LTRIM(RTRIM(oi.Notes)), '') AS Notes
FROM dbo.OrderItems oi
WHERE oi.OrderId = @OrderId
ORDER BY oi.OrderItemId ASC;";
            itemCmd.Parameters.AddWithValue("@OrderId", id);
            using var itemReader = itemCmd.ExecuteReader();
            while (itemReader.Read())
            {
                vm.Items.Add(new AdminOrderItemRow
                {
                    Name = itemReader.IsDBNull(0) ? "Item" : itemReader.GetString(0),
                    Quantity = itemReader.IsDBNull(1) ? 1 : itemReader.GetInt32(1),
                    UnitPrice = itemReader.IsDBNull(2) ? 0m : itemReader.GetDecimal(2),
                    Notes = itemReader.IsDBNull(3) ? null : itemReader.GetString(3)
                });
            }

            return vm;
        }
    }
}
