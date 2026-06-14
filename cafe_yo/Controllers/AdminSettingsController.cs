using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/Settings")]
    public class AdminSettingsController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminSettingsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureTable(conn);

            var model = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [Key], [Value] FROM dbo.SystemSettings;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                model[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            }

            ViewBag.Success = TempData["Success"];
            return View("~/Views/Admin/Settings/Index.cshtml", model);
        }

        [HttpPost("")]
        [ValidateAntiForgeryToken]
        public IActionResult Save(string? taxPercent, string? serviceChargePercent, string? qrisImageUrl)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureTable(conn);

            Upsert(conn, "TaxPercent", taxPercent ?? "0");
            Upsert(conn, "ServiceChargePercent", serviceChargePercent ?? "0");
            Upsert(conn, "QrisImageUrl", qrisImageUrl ?? string.Empty);

            TempData["Success"] = "Settings berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }

        private static void Upsert(SqlConnection conn, string key, string value)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF EXISTS(SELECT 1 FROM dbo.SystemSettings WHERE [Key] = @Key)
    UPDATE dbo.SystemSettings SET [Value] = @Value, UpdatedAt = SYSUTCDATETIME() WHERE [Key] = @Key;
ELSE
    INSERT INTO dbo.SystemSettings ([Key], [Value], UpdatedAt) VALUES (@Key, @Value, SYSUTCDATETIME());";
            cmd.Parameters.AddWithValue("@Key", key);
            cmd.Parameters.AddWithValue("@Value", value);
            cmd.ExecuteNonQuery();
        }

        private static void EnsureTable(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF OBJECT_ID(N'dbo.SystemSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemSettings (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Key] NVARCHAR(120) NOT NULL UNIQUE,
        [Value] NVARCHAR(MAX) NULL,
        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;";
            cmd.CommandText += @"
BEGIN TRY
    ALTER TABLE dbo.SystemSettings ALTER COLUMN [Value] NVARCHAR(MAX) NULL;
END TRY
BEGIN CATCH
END CATCH;";
            cmd.ExecuteNonQuery();
        }
    }
}
