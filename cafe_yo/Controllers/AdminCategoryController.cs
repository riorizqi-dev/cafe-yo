using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Models;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/Category")]
    public class AdminCategoryController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminCategoryController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var items = new List<MenuCategory>();
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureTable(conn);
            EnsureDefaultCategories(conn);
            SyncCategoriesFromMenuItems(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CategoryId, Name, IsActive FROM dbo.MenuCategories ORDER BY Name ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new MenuCategory
                {
                    CategoryId = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    IsActive = reader.IsDBNull(2) || reader.GetBoolean(2)
                });
            }
            return View("~/Views/Admin/Category/Index.cshtml", items);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Nama kategori wajib diisi.";
                return RedirectToAction(nameof(Index));
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureTable(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO dbo.MenuCategories (Name, IsActive) VALUES (@Name, 1);";
            cmd.Parameters.AddWithValue("@Name", name.Trim());
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch
            {
                TempData["Error"] = "Kategori sudah ada atau gagal ditambahkan.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("{id:int}/Toggle")]
        [ValidateAntiForgeryToken]
        public IActionResult Toggle(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureTable(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE dbo.MenuCategories SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE CategoryId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("{id:int}/Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureTable(conn);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM dbo.MenuCategories WHERE CategoryId = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction(nameof(Index));
        }

        private static void EnsureTable(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF OBJECT_ID(N'dbo.MenuCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MenuCategories (
        CategoryId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(80) NOT NULL UNIQUE,
        IsActive BIT NOT NULL DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureDefaultCategories(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO dbo.MenuCategories (Name, IsActive)
SELECT v.Name, 1
FROM (VALUES (N'Makanan'), (N'Jajanan'), (N'Minuman'), (N'Pencuci Mulut')) v(Name)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.MenuCategories c
    WHERE LOWER(LTRIM(RTRIM(c.Name))) = LOWER(LTRIM(RTRIM(v.Name)))
);";
            cmd.ExecuteNonQuery();
        }

        private static void SyncCategoriesFromMenuItems(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF OBJECT_ID(N'dbo.MenuItems', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.MenuCategories (Name, IsActive)
    SELECT DISTINCT LTRIM(RTRIM(m.Category)) AS Name, 1
    FROM dbo.MenuItems m
    WHERE NULLIF(LTRIM(RTRIM(m.Category)), '') IS NOT NULL
      AND NOT EXISTS (
            SELECT 1
            FROM dbo.MenuCategories c
            WHERE LOWER(LTRIM(RTRIM(c.Name))) = LOWER(LTRIM(RTRIM(m.Category)))
      );
END;";
            cmd.ExecuteNonQuery();
        }
    }
}
