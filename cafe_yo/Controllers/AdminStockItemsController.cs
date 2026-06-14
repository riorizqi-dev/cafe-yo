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
    [Route("Admin/StockItems")]
    public class AdminStockItemsController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminStockItemsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var items = new List<StockItem>();
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT StockItemId, Name, [Type], Quantity, MinQuantity
                                FROM [StockItems]
                                ORDER BY StockItemId DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new StockItem
                {
                    StockItemId = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Type = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Quantity = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                    MinQuantity = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
                });
            }
            return View("~/Views/Admin/StockItems/Index.cshtml", items);
        }

        [HttpGet("{id:int}")]
        public IActionResult Details(int id)
        {
            var item = GetStockItem(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/StockItems/Details.cshtml", item);
        }

        [HttpGet("Create")]
        [Authorize(Policy = "AdminOnly")]
        public IActionResult Create()
        {
            return View("~/Views/Admin/StockItems/Create.cshtml", new StockItem());
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "AdminOnly")]
        public IActionResult Create(StockItem model)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(StockItem.Name), "Name is required.");
            }
            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/StockItems/Create.cshtml", model);
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO [StockItems] (Name, [Type], Quantity, MinQuantity)
                                VALUES (@Name, @Type, @Quantity, @MinQuantity);";
            cmd.Parameters.AddWithValue("@Name", (object?)model.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Type", (object?)model.Type ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quantity", (object?)model.Quantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MinQuantity", (object?)model.MinQuantity ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Edit/{id:int}")]
        public IActionResult Edit(int id)
        {
            var item = GetStockItem(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/StockItems/Edit.cshtml", item);
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, StockItem model)
        {
            if (id != model.StockItemId)
            {
                return BadRequest();
            }
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(StockItem.Name), "Name is required.");
            }
            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/StockItems/Edit.cshtml", model);
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE [StockItems]
                                SET Name = @Name,
                                    [Type] = @Type,
                                    Quantity = @Quantity,
                                    MinQuantity = @MinQuantity
                                WHERE StockItemId = @Id;";
            cmd.Parameters.AddWithValue("@Name", (object?)model.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Type", (object?)model.Type ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Quantity", (object?)model.Quantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MinQuantity", (object?)model.MinQuantity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Delete/{id:int}")]
        [Authorize(Policy = "AdminOnly")]
        public IActionResult Delete(int id)
        {
            var item = GetStockItem(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/StockItems/Delete.cshtml", item);
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "AdminOnly")]
        public IActionResult DeleteConfirmed(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM [StockItems] WHERE StockItemId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction(nameof(Index));
        }

        private StockItem? GetStockItem(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT StockItemId, Name, [Type], Quantity, MinQuantity
                                FROM [StockItems]
                                WHERE StockItemId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            return new StockItem
            {
                StockItemId = reader.GetInt32(0),
                Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                Type = reader.IsDBNull(2) ? null : reader.GetString(2),
                Quantity = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                MinQuantity = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
            };
        }
    }
}
