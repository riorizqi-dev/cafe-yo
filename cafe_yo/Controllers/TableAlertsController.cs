using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace cafe_yo.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/table-alerts")]
    public class TableAlertsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public TableAlertsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("latest")]
        public IActionResult Latest([FromQuery] int tableNumber, [FromQuery] int? afterId = null)
        {
            if (tableNumber <= 0)
            {
                return BadRequest(new { success = false, error = "tableNumber tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureCallTable(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 1 Id, TableNumber, Message, CreatedAt
FROM [TableCallNotifications]
WHERE TableNumber = @TableNumber
  AND (@AfterId IS NULL OR Id > @AfterId)
ORDER BY Id DESC;";
            cmd.Parameters.AddWithValue("@TableNumber", tableNumber);
            cmd.Parameters.AddWithValue("@AfterId", (object?)afterId ?? DBNull.Value);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return Ok(new { success = true, hasAlert = false });
            }

            return Ok(new
            {
                success = true,
                hasAlert = true,
                id = reader.GetInt32(0),
                tableNumber = reader.GetInt32(1),
                message = reader.GetString(2),
                createdAt = reader.GetDateTime(3).ToString("o")
            });
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
    }
}
