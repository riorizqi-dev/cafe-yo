using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using cafe_yo.Models;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/MenuItems")]
    [Route("Admin/Menu")]
    public class AdminMenuItemsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public AdminMenuItemsController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var items = new List<MenuItem>();
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureMenuImageColumn(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT MenuItemId, Name, Category, ImageUrl, Price, ISNULL(Stock,0) AS Stock, IsAvailable
                                FROM [MenuItems]
                                ORDER BY MenuItemId DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new MenuItem
                {
                    MenuItemId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ImageUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Price = reader.GetDecimal(4),
                    Stock = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    IsAvailable = reader.IsDBNull(6) ? (bool?)null : reader.GetBoolean(6)
                });
            }
            return View("~/Views/Admin/MenuItems/Index.cshtml", items);
        }

        [HttpGet("{id:int}")]
        public IActionResult Details(int id)
        {
            var item = GetMenuItem(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/MenuItems/Details.cshtml", item);
        }

        [HttpGet("Create")]
        public IActionResult Create()
        {
            return View("~/Views/Admin/MenuItems/Create.cshtml", new MenuItem { IsAvailable = true });
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public IActionResult Create(MenuItem model, IFormFile? imageFile)
        {
            model.ImageUrl = NormalizeImagePath(model.ImageUrl);
            if (imageFile != null && imageFile.Length > 0)
            {
                model.ImageUrl = SaveMenuImage(imageFile);
            }

            ValidateMenuItem(model);
            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/MenuItems/Create.cshtml", model);
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureMenuImageColumn(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO [MenuItems] (Name, Category, ImageUrl, Price, Stock, IsAvailable)
                                VALUES (@Name, @Category, @ImageUrl, @Price, @Stock, @IsAvailable);";
            cmd.Parameters.AddWithValue("@Name", model.Name);
            cmd.Parameters.AddWithValue("@Category", (object?)model.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)model.ImageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", model.Price);
            cmd.Parameters.AddWithValue("@Stock", model.Stock);
            cmd.Parameters.AddWithValue("@IsAvailable", (object?)model.IsAvailable ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Edit/{id:int}")]
        public IActionResult Edit(int id)
        {
            var item = GetMenuItem(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/MenuItems/Edit.cshtml", item);
        }

        [HttpPost("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, MenuItem model, IFormFile? imageFile)
        {
            if (id != model.MenuItemId)
            {
                return BadRequest();
            }

            model.ImageUrl = NormalizeImagePath(model.ImageUrl);
            if (imageFile != null && imageFile.Length > 0)
            {
                model.ImageUrl = SaveMenuImage(imageFile);
            }

            ValidateMenuItem(model);
            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/MenuItems/Edit.cshtml", model);
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureMenuImageColumn(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE [MenuItems]
                                SET Name = @Name,
                                    Category = @Category,
                                    ImageUrl = @ImageUrl,
                                    Price = @Price,
                                    Stock = @Stock,
                                    IsAvailable = @IsAvailable
                                WHERE MenuItemId = @Id;";
            cmd.Parameters.AddWithValue("@Name", model.Name);
            cmd.Parameters.AddWithValue("@Category", (object?)model.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)model.ImageUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", model.Price);
            cmd.Parameters.AddWithValue("@Stock", model.Stock);
            cmd.Parameters.AddWithValue("@IsAvailable", (object?)model.IsAvailable ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Delete/{id:int}")]
        public IActionResult Delete(int id)
        {
            var item = GetMenuItem(id);
            if (item == null)
            {
                return NotFound();
            }
            return View("~/Views/Admin/MenuItems/Delete.cshtml", item);
        }

        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM [MenuItems] WHERE MenuItemId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction(nameof(Index));
        }

        private MenuItem? GetMenuItem(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureMenuImageColumn(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT MenuItemId, Name, Category, ImageUrl, Price, ISNULL(Stock,0) AS Stock, IsAvailable
                                FROM [MenuItems]
                                WHERE MenuItemId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new MenuItem
            {
                MenuItemId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                ImageUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                Price = reader.GetDecimal(4),
                Stock = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                IsAvailable = reader.IsDBNull(6) ? (bool?)null : reader.GetBoolean(6)
            };
        }

        private void ValidateMenuItem(MenuItem model)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(MenuItem.Name), "Name is required.");
            }
            if (model.Price <= 0)
            {
                ModelState.AddModelError(nameof(MenuItem.Price), "Price must be greater than 0.");
            }
            if (model.Stock < 0)
            {
                ModelState.AddModelError(nameof(MenuItem.Stock), "Stock must be 0 or greater.");
            }
        }

        private static void EnsureMenuImageColumn(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF COL_LENGTH('dbo.MenuItems', 'ImageUrl') IS NULL
BEGIN
    ALTER TABLE dbo.MenuItems ADD ImageUrl NVARCHAR(500) NULL;
END;";
            cmd.ExecuteNonQuery();
        }

        private string SaveMenuImage(IFormFile imageFile)
        {
            var ext = Path.GetExtension(imageFile.FileName)?.ToLowerInvariant() ?? string.Empty;
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowed.Contains(ext))
            {
                ModelState.AddModelError(nameof(MenuItem.ImageUrl), "Format gambar harus .jpg, .jpeg, .png, atau .webp.");
                return string.Empty;
            }

            if (imageFile.Length > 2 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(MenuItem.ImageUrl), "Ukuran gambar maksimal 2MB.");
                return string.Empty;
            }

            var webRoot = string.IsNullOrWhiteSpace(_environment.WebRootPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
                : _environment.WebRootPath;
            var uploadDir = Path.Combine(webRoot, "images", "menu", "uploads");
            Directory.CreateDirectory(uploadDir);

            var fileName = $"menu-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{ext}";
            var path = Path.Combine(uploadDir, fileName);
            using (var fs = System.IO.File.Create(path))
            {
                imageFile.CopyTo(fs);
            }

            return $"/images/menu/uploads/{fileName}";
        }

        private static string? NormalizeImagePath(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            var value = imageUrl.Trim();
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if (!value.StartsWith("/"))
            {
                value = "/" + value;
            }

            return value;
        }
    }
}
