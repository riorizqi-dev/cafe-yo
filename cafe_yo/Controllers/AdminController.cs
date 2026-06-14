using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using cafe_yo.Models;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;

        public AdminController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View("~/Views/Admin/Home.cshtml");
        }

        [HttpGet("Dashboard")]
        public IActionResult Dashboard()
        {
            // kept for compatibility if needed
            var vm = new AdminDashboardViewModel();

            vm.Users = new List<AdminUserDto>
            {
                new AdminUserDto { Id = 1, FullName = "Admin Ryu", Username = "admin_ryu", Role = "Admin", IsOnline = true },
                new AdminUserDto { Id = 2, FullName = "Budi Santoso", Username = "budi.s", Role = "Kasir", IsOnline = false }
            };

            vm.TotalUsers = vm.Users.Count;
            vm.AdminCount = vm.Users.FindAll(u => u.Role == "Admin").Count;
            vm.NonAdminCount = vm.Users.FindAll(u => u.Role != "Admin").Count;
            vm.OnlineCount = vm.Users.FindAll(u => u.IsOnline).Count;
            vm.TotalRoleAdmin = vm.Users.FindAll(u => u.Role == "Admin").Count;
            vm.TotalRoleKasir = vm.Users.FindAll(u => u.Role == "Kasir").Count;

            return View("~/Views/Admin/Dashboard.cshtml", vm);
        }

        private static HashSet<string> GetOrderColumns(SqlConnection conn)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Orders';";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader[0] != null)
                {
                    columns.Add(reader[0].ToString() ?? string.Empty);
                }
            }
            return columns;
        }

        private static void LoadTodayOrderStats(SqlConnection conn, HashSet<string> orderColumns, AdminDashboardVM model)
        {
            var dateCol = FirstColumn(orderColumns, "CreatedAt", "OrderDate", "CreatedOn", "Created");
            if (string.IsNullOrWhiteSpace(dateCol))
            {
                return;
            }

            var totalCol = FirstColumn(orderColumns, "TotalAmount", "Total", "GrandTotal", "TotalPrice");
            var selectParts = new List<string> { "COUNT(*) AS TodayOrders" };
            if (!string.IsNullOrWhiteSpace(totalCol))
            {
                selectParts.Add($"SUM(CAST({totalCol} AS decimal(18,2))) AS TodayRevenue");
            }

            var query = $@"
SELECT {string.Join(", ", selectParts)}
FROM [Orders]
WHERE CAST({dateCol} AS date) = CAST(GETDATE() AS date);";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                model.TodayOrders = reader["TodayOrders"] != DBNull.Value ? Convert.ToInt32(reader["TodayOrders"]) : (int?)null;
                if (!string.IsNullOrWhiteSpace(totalCol))
                {
                    model.TodayRevenue = reader["TodayRevenue"] != DBNull.Value ? Convert.ToDecimal(reader["TodayRevenue"]) : (decimal?)null;
                }
            }
        }

        private static List<RecentOrderRow> LoadRecentOrders(SqlConnection conn, HashSet<string> orderColumns)
        {
            var orderIdCol = FirstColumn(orderColumns, "Id", "OrderId");
            var tableCol = FirstColumn(orderColumns, "TableNumber", "TableNo", "TableId");
            var totalCol = FirstColumn(orderColumns, "TotalAmount", "Total", "GrandTotal", "TotalPrice");
            var dateCol = FirstColumn(orderColumns, "CreatedAt", "OrderDate", "CreatedOn", "Created");
            var statusCol = FirstColumn(orderColumns, "Status", "OrderStatus");

            var selectParts = new List<string>
            {
                orderIdCol != null ? $"CAST({orderIdCol} AS int) AS OrderId" : "CAST(NULL AS int) AS OrderId",
                tableCol != null ? $"CAST({tableCol} AS nvarchar(50)) AS TableNumber" : "CAST(NULL AS nvarchar(50)) AS TableNumber",
                totalCol != null ? $"CAST({totalCol} AS decimal(18,2)) AS TotalAmount" : "CAST(NULL AS decimal(18,2)) AS TotalAmount",
                dateCol != null ? $"CAST({dateCol} AS datetime2) AS CreatedAt" : "CAST(NULL AS datetime2) AS CreatedAt",
                statusCol != null ? $"CAST({statusCol} AS nvarchar(50)) AS Status" : "CAST(NULL AS nvarchar(50)) AS Status"
            };

            var orderBy = dateCol ?? orderIdCol ?? "(SELECT 0)";
            var query = $@"
SELECT TOP 5 {string.Join(", ", selectParts)}
FROM [Orders]
ORDER BY {orderBy} DESC;";

            var results = new List<RecentOrderRow>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new RecentOrderRow
                {
                    OrderId = reader["OrderId"] != DBNull.Value ? Convert.ToInt32(reader["OrderId"]) : (int?)null,
                    TableNumber = reader["TableNumber"] != DBNull.Value ? reader["TableNumber"].ToString() : null,
                    TotalAmount = reader["TotalAmount"] != DBNull.Value ? Convert.ToDecimal(reader["TotalAmount"]) : (decimal?)null,
                    CreatedAt = reader["CreatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedAt"]) : (DateTime?)null,
                    Status = reader["Status"] != DBNull.Value ? reader["Status"].ToString() : null
                });
            }

            return results;
        }

        private static string? FirstColumn(HashSet<string> columns, params string[] names)
        {
            foreach (var name in names)
            {
                if (columns.Contains(name))
                {
                    return name;
                }
            }
            return null;
        }

        private int SafeScalarCount(SqlConnection conn, string tableName)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {tableName};";
                cmd.CommandType = CommandType.Text;
                var result = cmd.ExecuteScalar();
                if (result != null && int.TryParse(result.ToString(), out int value))
                    return value;
            }
            catch
            {
                // table may not exist or other error - return 0
            }
            return 0;
        }
    }
}
