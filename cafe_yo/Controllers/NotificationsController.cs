using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public NotificationsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult GetNotifications()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            var userId = TryGetCurrentUserId(conn);
            var roles = CurrentRoles();
            if (roles.Count == 0 && string.IsNullOrWhiteSpace(userId))
            {
                return Ok(new { success = true, notifications = Array.Empty<object>() });
            }

            using var cmd = conn.CreateCommand();
            var roleClause = roles.Count > 0
                ? $" OR RoleTarget IN ({string.Join(",", roles.Select((_, idx) => $"@Role{idx}"))})"
                : string.Empty;
            cmd.CommandText = @"
SELECT TOP 100 NotificationId, Type, Title, Message, IsRead, CreatedAt
FROM dbo.UserNotifications
WHERE (@UserId IS NOT NULL AND UserId = @UserId)
{ROLE_CLAUSE}
ORDER BY CreatedAt DESC;"
            .Replace("{ROLE_CLAUSE}", roleClause);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            for (var i = 0; i < roles.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@Role{i}", roles[i]);
            }

            var list = new List<object>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new
                {
                    id = reader.GetInt32(0),
                    type = reader.IsDBNull(1) ? "-" : reader.GetString(1),
                    title = reader.IsDBNull(2) ? "-" : reader.GetString(2),
                    message = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    isRead = !reader.IsDBNull(4) && reader.GetBoolean(4),
                    createdAt = reader.GetDateTime(5).ToString("o")
                });
            }

            return Ok(new { success = true, notifications = list });
        }

        [HttpPatch("{id:int}/read")]
        [IgnoreAntiforgeryToken]
        public IActionResult MarkRead(int id)
        {
            if (id <= 0)
            {
                return BadRequest(new { success = false, error = "ID tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            var userId = TryGetCurrentUserId(conn);
            var roles = CurrentRoles();

            using var cmd = conn.CreateCommand();
            var roleClause = roles.Count > 0
                ? $" OR RoleTarget IN ({string.Join(",", roles.Select((_, idx) => $"@Role{idx}"))})"
                : string.Empty;
            cmd.CommandText = @"
UPDATE dbo.UserNotifications
SET IsRead = 1,
    ReadAt = SYSUTCDATETIME()
WHERE NotificationId = @Id
  AND (
        (@UserId IS NOT NULL AND UserId = @UserId)
{ROLE_CLAUSE}
      );"
            .Replace("{ROLE_CLAUSE}", roleClause);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@UserId", (object?)userId ?? DBNull.Value);
            for (var i = 0; i < roles.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@Role{i}", roles[i]);
            }

            var affected = cmd.ExecuteNonQuery();
            return Ok(new { success = true, updated = affected > 0 });
        }

        private List<string> CurrentRoles()
        {
            var roles = new List<string>();
            if (User?.IsInRole(AppRoles.Supervisor) == true) roles.Add(AppRoles.Supervisor);
            if (User?.IsInRole(AppRoles.Admin) == true) roles.Add(AppRoles.Admin);
            if (User?.IsInRole(AppRoles.Kasir) == true) roles.Add(AppRoles.Kasir);
            if (User?.IsInRole(AppRoles.Koki) == true || User?.IsInRole(AppRoles.DapurLegacy) == true) roles.Add(AppRoles.Koki);
            if (User?.IsInRole(AppRoles.Owner) == true) roles.Add(AppRoles.Owner);
            return roles;
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
    }
}
