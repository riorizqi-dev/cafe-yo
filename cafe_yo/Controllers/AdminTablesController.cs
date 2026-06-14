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
    [Route("Admin/Tables")]
    public class AdminTablesController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminTablesController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var items = new List<CafeTable>();
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            cafe_yo.Data.TableStateStore.EnsureTables(conn);
            cafe_yo.Data.OrderTableSync.SyncAllTableStatuses(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT TableId, TableNumber, Status
                                FROM [Tables]
                                ORDER BY TableNumber ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new CafeTable
                {
                    TableId = reader.GetInt32(0),
                    TableNumber = reader.GetInt32(1),
                    Status = reader.IsDBNull(2) ? null : reader.GetString(2)
                });
            }
            return View("~/Views/Admin/Tables/Index.cshtml", items);
        }

        [HttpGet("{id:int}")]
        public IActionResult Details(int id)
        {
            var item = GetTable(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/Tables/Details.cshtml", item);
        }

        [HttpGet("Create")]
        public IActionResult Create()
        {
            return View("~/Views/Admin/Tables/Create.cshtml", new CafeTable());
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public IActionResult Create(CafeTable model)
        {
            if (model.TableNumber <= 0)
            {
                ModelState.AddModelError(nameof(CafeTable.TableNumber), "Table number is required.");
            }
            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/Tables/Create.cshtml", model);
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO [Tables] (TableNumber, Status)
                                VALUES (@TableNumber, @Status);";
            cmd.Parameters.AddWithValue("@TableNumber", model.TableNumber);
            cmd.Parameters.AddWithValue("@Status", (object?)model.Status ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Edit/{id:int}")]
        public IActionResult Edit(int id)
        {
            var item = GetTable(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/Tables/Edit.cshtml", item);
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, CafeTable model)
        {
            if (id != model.TableId)
            {
                return BadRequest();
            }
            if (model.TableNumber <= 0)
            {
                ModelState.AddModelError(nameof(CafeTable.TableNumber), "Table number is required.");
            }
            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/Tables/Edit.cshtml", model);
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE [Tables]
                                SET TableNumber = @TableNumber,
                                    Status = @Status
                                WHERE TableId = @Id;";
            cmd.Parameters.AddWithValue("@TableNumber", model.TableNumber);
            cmd.Parameters.AddWithValue("@Status", (object?)model.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Delete/{id:int}")]
        public IActionResult Delete(int id)
        {
            var item = GetTable(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/Tables/Delete.cshtml", item);
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM [Tables] WHERE TableId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction(nameof(Index));
        }

        private CafeTable? GetTable(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT TableId, TableNumber, Status
                                FROM [Tables]
                                WHERE TableId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            return new CafeTable
            {
                TableId = reader.GetInt32(0),
                TableNumber = reader.GetInt32(1),
                Status = reader.IsDBNull(2) ? null : reader.GetString(2)
            };
        }
    }
}
