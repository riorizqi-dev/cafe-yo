using System.Data;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using cafe_yo.Models;
using System.Globalization;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "OwnerOnly")]
    [Route("Owner")]
    public class OwnerController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public OwnerController(IConfiguration configuration, IWebHostEnvironment env)
        {
            _configuration = configuration;
            _env = env;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        [HttpGet("")]
        public IActionResult Index()
        {
            var vm = LoadDashboard(Request.Query["range"].ToString());
            return View("~/Views/Owner/Index.cshtml", vm);
        }

        [HttpGet("Reports")]
        public IActionResult Reports()
        {
            var vm = LoadDashboard(Request.Query["range"].ToString());
            return View("~/Views/Owner/Reports.cshtml", vm);
        }

        [HttpGet("Analytics")]
        public IActionResult Analytics()
        {
            var vm = LoadDashboard(Request.Query["range"].ToString());
            return View("~/Views/Owner/Analytics.cshtml", vm);
        }

        [HttpGet("Users")]
        public IActionResult Users() => Redirect("/Admin/Users");

        [HttpGet("System")]
        public IActionResult SystemSettings() => Redirect("/Admin/Settings");

        [HttpGet("MenuControl")]
        public IActionResult MenuControl()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureOwnerMenuApprovalColumns(conn);
            var rows = new List<(int Id, string Name, string Category, decimal Price, int Stock, bool IsAvailable, string Status, string? Notes, DateTime? At, string? By)>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT MenuItemId,Name,ISNULL(Category,'-'),ISNULL(Price,0),ISNULL(Stock,0),ISNULL(IsAvailable,1),
       ISNULL(ApprovalStatus,'approved'),ApprovalNotes,ApprovalUpdatedAt,ApprovalUpdatedBy
FROM dbo.MenuItems
ORDER BY MenuItemId DESC;";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add((
                    rd.GetInt32(0),
                    rd.IsDBNull(1) ? "-" : rd.GetString(1),
                    rd.IsDBNull(2) ? "-" : rd.GetString(2),
                    rd.IsDBNull(3) ? 0m : rd.GetDecimal(3),
                    rd.IsDBNull(4) ? 0 : rd.GetInt32(4),
                    rd.IsDBNull(5) || rd.GetBoolean(5),
                    rd.IsDBNull(6) ? "approved" : rd.GetString(6),
                    rd.IsDBNull(7) ? null : rd.GetString(7),
                    rd.IsDBNull(8) ? null : rd.GetDateTime(8),
                    rd.IsDBNull(9) ? null : rd.GetString(9)
                ));
            }
            return View("~/Views/Owner/MenuControl.cshtml", rows);
        }

        [HttpPost("MenuControl/{id:int}/Approve")]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveMenu(int id, string? notes)
        {
            return UpdateMenuApproval(id, "approved", notes);
        }

        [HttpPost("MenuControl/{id:int}/Reject")]
        [ValidateAntiForgeryToken]
        public IActionResult RejectMenu(int id, string? notes)
        {
            return UpdateMenuApproval(id, "rejected", notes);
        }

        [HttpGet("Export")]
        public IActionResult Export([FromQuery] string? range = "monthly")
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            if (!TableExists(conn, "Orders"))
            {
                return BadRequest("Tabel Orders tidak ditemukan.");
            }

            var orderColumns = GetColumns(conn, "Orders");
            var paidAtCol = FirstColumn(orderColumns, "PaidAt");
            var createdCol = FirstColumn(orderColumns, "OrderDate", "CreatedAt", "CreatedOn", "Created");
            var totalCol = FirstColumn(orderColumns, "Total", "TotalAmount", "GrandTotal", "TotalPrice");
            var statusCol = FirstColumn(orderColumns, "Status", "OrderStatus");
            var paymentStatusCol = FirstColumn(orderColumns, "PaymentStatus");
            var txnCol = !string.IsNullOrWhiteSpace(paidAtCol) ? paidAtCol! : createdCol;
            if (string.IsNullOrWhiteSpace(txnCol) || string.IsNullOrWhiteSpace(totalCol) || string.IsNullOrWhiteSpace(statusCol) || string.IsNullOrWhiteSpace(paymentStatusCol))
            {
                return BadRequest("Struktur tabel order belum lengkap untuk export.");
            }

            var normalizedRange = NormalizeRange(range);
            var tz = ResolveJakartaTimeZone();
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
            var todayLocal = nowLocal.Date;
            var rangeStartLocal = normalizedRange switch
            {
                "daily" => todayLocal,
                "weekly" => todayLocal.AddDays(-6),
                _ => new DateTime(nowLocal.Year, nowLocal.Month, 1)
            };

            var rows = new List<PaidOrderRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
SELECT
    o.OrderId,
    CAST(o.{txnCol} AS datetime2) AS TxnAt,
    CAST(o.{totalCol} AS decimal(18,2)) AS Total,
    CAST(o.{statusCol} AS nvarchar(80)) AS OrderStatus,
    CAST(o.{paymentStatusCol} AS nvarchar(80)) AS PaymentStatus,
    ISNULL(t.TableNumber, 0) AS TableNumber
FROM dbo.Orders o
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
WHERE o.{txnCol} IS NOT NULL
  AND LOWER(LTRIM(RTRIM(ISNULL(o.{statusCol}, '')))) IN ('selesai', 'completed')
  AND LOWER(LTRIM(RTRIM(ISNULL(o.{paymentStatusCol}, '')))) IN ('lunas', 'paid')
  AND CAST(o.{txnCol} AS datetime2) >= @UtcFrom
ORDER BY TxnAt ASC;";
                cmd.Parameters.AddWithValue("@UtcFrom", nowUtc.AddDays(-120));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(1)) continue;
                    var localTxn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc), tz);
                    rows.Add(new PaidOrderRow
                    {
                        OrderId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                        TxnLocal = localTxn,
                        Total = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                        OrderStatus = reader.IsDBNull(3) ? "-" : reader.GetString(3),
                        PaymentStatus = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                        TableNumber = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader[5], CultureInfo.InvariantCulture)
                    });
                }
            }

            var filtered = rows.Where(x => x.TxnLocal.Date >= rangeStartLocal && x.TxnLocal.Date <= todayLocal).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("OrderId,Meja,WaktuLocal,Total,StatusOrder,StatusPembayaran");
            foreach (var r in filtered)
            {
                sb.AppendLine($"{r.OrderId},{r.TableNumber},{r.TxnLocal:yyyy-MM-dd HH:mm:ss},{r.Total.ToString(CultureInfo.InvariantCulture)},{EscapeCsv(r.OrderStatus)},{EscapeCsv(r.PaymentStatus)}");
            }
            sb.AppendLine($",,TOTAL,{filtered.Sum(x => x.Total).ToString(CultureInfo.InvariantCulture)},,");
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"owner_report_{normalizedRange}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv", fileName);
        }

        private OwnerDashboardVm LoadDashboard(string? range)
        {
            var vm = new OwnerDashboardVm
            {
                ReportRange = NormalizeRange(range),
                ShowDebug = _env.IsDevelopment()
            };

            using var conn = new SqlConnection(ConnectionString);
            conn.Open();

            if (!TableExists(conn, "Orders"))
            {
                return vm;
            }

            var orderColumns = GetColumns(conn, "Orders");
            var paidAtCol = FirstColumn(orderColumns, "PaidAt");
            var createdCol = FirstColumn(orderColumns, "OrderDate", "CreatedAt", "CreatedOn", "Created");
            var totalCol = FirstColumn(orderColumns, "Total", "TotalAmount", "GrandTotal", "TotalPrice");
            var statusCol = FirstColumn(orderColumns, "Status", "OrderStatus");
            var paymentStatusCol = FirstColumn(orderColumns, "PaymentStatus");

            if (string.IsNullOrWhiteSpace(totalCol) || string.IsNullOrWhiteSpace(statusCol) || string.IsNullOrWhiteSpace(paymentStatusCol))
            {
                return vm;
            }

            var txnCol = !string.IsNullOrWhiteSpace(paidAtCol) ? paidAtCol! : createdCol;
            if (string.IsNullOrWhiteSpace(txnCol))
            {
                return vm;
            }

            var tz = ResolveJakartaTimeZone();
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
            var todayLocal = nowLocal.Date;
            var monthStartLocal = new DateTime(nowLocal.Year, nowLocal.Month, 1);
            var sevenDaysAgoLocal = todayLocal.AddDays(-6);
            var rangeStartLocal = vm.ReportRange switch
            {
                "daily" => todayLocal,
                "weekly" => todayLocal.AddDays(-6),
                _ => monthStartLocal
            };

            var paidOrders = new List<PaidOrderRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandType = CommandType.Text;
                cmd.CommandText = $@"
SELECT
    o.OrderId,
    CAST(o.{txnCol} AS datetime2) AS TxnAt,
    CAST(o.{totalCol} AS decimal(18,2)) AS Total,
    CAST(o.{statusCol} AS nvarchar(80)) AS OrderStatus,
    CAST(o.{paymentStatusCol} AS nvarchar(80)) AS PaymentStatus,
    ISNULL(t.TableNumber, 0) AS TableNumber
FROM dbo.Orders o
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
WHERE o.{txnCol} IS NOT NULL
  AND LOWER(LTRIM(RTRIM(ISNULL(o.{statusCol}, '')))) IN ('selesai', 'completed')
  AND LOWER(LTRIM(RTRIM(ISNULL(o.{paymentStatusCol}, '')))) IN ('lunas', 'paid')
  AND CAST(o.{txnCol} AS datetime2) >= @UtcFrom
ORDER BY TxnAt DESC;";
                cmd.Parameters.AddWithValue("@UtcFrom", nowUtc.AddDays(-120));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(1)) continue;
                    var localTxn = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc), tz);
                    paidOrders.Add(new PaidOrderRow
                    {
                        OrderId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                        TxnLocal = localTxn,
                        Total = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                        OrderStatus = reader.IsDBNull(3) ? "-" : reader.GetString(3),
                        PaymentStatus = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                        TableNumber = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader[5], CultureInfo.InvariantCulture)
                    });
                }
            }

            vm.DebugQualifiedOrders = paidOrders.Count;
            vm.DebugQualifiedTotal = paidOrders.Sum(x => x.Total);

            var todayOrders = paidOrders.Where(x => x.TxnLocal.Date == todayLocal).ToList();
            vm.TodayRevenue = todayOrders.Sum(x => x.Total);
            vm.TodayTransactions = todayOrders.Count;

            var weekOrders = paidOrders.Where(x => x.TxnLocal.Date >= sevenDaysAgoLocal && x.TxnLocal.Date <= todayLocal).ToList();
            vm.WeekRevenue = weekOrders.Sum(x => x.Total);
            vm.WeekTransactions = weekOrders.Count;

            var monthOrders = paidOrders.Where(x => x.TxnLocal.Date >= monthStartLocal && x.TxnLocal.Date <= todayLocal).ToList();
            vm.MonthRevenue = monthOrders.Sum(x => x.Total);
            vm.MonthTransactions = monthOrders.Count;

            var rangeOrders = paidOrders.Where(x => x.TxnLocal.Date >= rangeStartLocal && x.TxnLocal.Date <= todayLocal).ToList();
            var rangeRevenue = rangeOrders.Sum(x => x.Total);
            vm.AverageTransaction = rangeOrders.Count == 0 ? 0m : rangeRevenue / rangeOrders.Count;

            var dailyMap = new Dictionary<DateTime, (decimal Revenue, int Txn)>();
            for (var i = 0; i < 7; i++)
            {
                var d = sevenDaysAgoLocal.AddDays(i);
                dailyMap[d] = (0m, 0);
            }
            foreach (var item in paidOrders.Where(x => x.TxnLocal.Date >= sevenDaysAgoLocal && x.TxnLocal.Date <= todayLocal))
            {
                var key = item.TxnLocal.Date;
                if (!dailyMap.ContainsKey(key)) continue;
                var cur = dailyMap[key];
                dailyMap[key] = (cur.Revenue + item.Total, cur.Txn + 1);
            }
            foreach (var day in dailyMap.Keys.OrderBy(x => x))
            {
                vm.OmzetSeries.Add(new OwnerOmzetPointVm { Label = day.ToString("dd MMM", CultureInfo.GetCultureInfo("id-ID")), Value = dailyMap[day].Revenue });
                vm.TransactionSeries.Add(new OwnerOmzetPointVm { Label = day.ToString("dd MMM", CultureInfo.GetCultureInfo("id-ID")), Value = dailyMap[day].Txn });
            }

            foreach (var t in paidOrders.OrderByDescending(x => x.TxnLocal).Take(8))
            {
                vm.RecentTransactions.Add(new OwnerRecentTransactionVm
                {
                    OrderId = t.OrderId,
                    Date = t.TxnLocal,
                    Total = t.Total,
                    Status = t.OrderStatus,
                    Table = t.TableNumber > 0 ? t.TableNumber.ToString(CultureInfo.InvariantCulture) : "-"
                });
            }

            var busiestHour = rangeOrders
                .GroupBy(x => x.TxnLocal.Hour)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .FirstOrDefault();
            if (busiestHour != null)
            {
                vm.BusiestHourLabel = $"{busiestHour.Key:00}:00 - {busiestHour.Key:00}:59";
            }

            var busiestDay = rangeOrders
                .GroupBy(x => x.TxnLocal.DayOfWeek)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (busiestDay != null)
            {
                vm.BusiestDayLabel = CultureInfo.GetCultureInfo("id-ID").DateTimeFormat.GetDayName(busiestDay.Key);
            }

            FillProductAndCategoryInsights(conn, vm, paidOrders.Select(x => x.OrderId).ToList());
            vm.EstimatedCost = EstimateCost(conn, rangeOrders.Select(x => x.OrderId).ToList(), rangeRevenue);
            vm.EstimatedProfit = rangeRevenue - vm.EstimatedCost;
            FillExpenseSummary(conn, vm, todayLocal, sevenDaysAgoLocal, monthStartLocal);
            vm.AveragePurchasePerCustomer = CalculateAveragePerCustomer(conn, rangeOrders.Count, rangeRevenue, rangeStartLocal, todayLocal);
            FillAlerts(conn, vm);
            FillAutoInsights(vm);

            return vm;
        }

        private void FillProductAndCategoryInsights(SqlConnection conn, OwnerDashboardVm vm, List<int> paidOrderIds)
        {
            if (paidOrderIds.Count == 0 || !TableExists(conn, "OrderItems"))
            {
                EnsureDefaultCategories(vm);
                return;
            }

            var idCsv = string.Join(",", paidOrderIds.Distinct());
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT
    COALESCE(NULLIF(LTRIM(RTRIM(oi.ItemName)), ''), mi.Name, 'Item') AS ProductName,
    SUM(ISNULL(oi.Quantity, 1)) AS QtySold,
    LOWER(LTRIM(RTRIM(ISNULL(mi.Category, 'lainnya')))) AS RawCategory,
    SUM(CAST(ISNULL(oi.UnitPrice, 0) * ISNULL(oi.Quantity, 1) AS decimal(18,2))) AS Revenue
FROM dbo.OrderItems oi
LEFT JOIN dbo.MenuItems mi ON mi.MenuItemId = oi.MenuItemId
WHERE oi.OrderId IN ({idCsv})
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(oi.ItemName)), ''), mi.Name, 'Item'), LOWER(LTRIM(RTRIM(ISNULL(mi.Category, 'lainnya'))));";

            var topMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var profitMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var categoryMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["makanan"] = 0m,
                ["minuman"] = 0m,
                ["jajanan"] = 0m
            };
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var product = reader.IsDBNull(0) ? "Item" : reader.GetString(0);
                var qty = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader[1], CultureInfo.InvariantCulture);
                var raw = reader.IsDBNull(2) ? "lainnya" : reader.GetString(2);
                var revenue = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);

                if (!topMap.ContainsKey(product)) topMap[product] = 0;
                topMap[product] += qty;
                if (!profitMap.ContainsKey(product)) profitMap[product] = 0m;
                // Gross margin quick estimate per item, safe fallback for analytics.
                profitMap[product] += revenue * 0.62m;

                var key = NormalizeCategory(raw);
                if (!categoryMap.ContainsKey(key)) categoryMap[key] = 0m;
                categoryMap[key] += revenue;
            }

            vm.TopProducts = topMap
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .Take(5)
                .Select(x => new OwnerTopProductVm { Name = x.Key, Qty = x.Value })
                .ToList();

            vm.LowProducts = topMap
                .Where(x => x.Value > 0)
                .OrderBy(x => x.Value)
                .ThenBy(x => x.Key)
                .Take(5)
                .Select(x => new OwnerTopProductVm { Name = x.Key, Qty = x.Value })
                .ToList();

            vm.TopProfitProducts = profitMap
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .Take(5)
                .Select(x => new OwnerTopProductVm { Name = x.Key, Qty = decimal.ToInt32(Math.Round(x.Value, 0, MidpointRounding.AwayFromZero)) })
                .ToList();

            vm.CategoryRevenue = categoryMap
                .Select(x => new OwnerCategoryRevenueVm { Category = x.Key, Revenue = x.Value })
                .OrderByDescending(x => x.Revenue)
                .ToList();
            EnsureDefaultCategories(vm);
        }

        private decimal EstimateCost(SqlConnection conn, List<int> orderIds, decimal fallbackRevenue)
        {
            if (orderIds.Count == 0 || !TableExists(conn, "OrderItems") || !TableExists(conn, "MenuIngredients") || !TableExists(conn, "StockItems"))
            {
                return Math.Round(fallbackRevenue * 0.35m, 2);
            }
            var csv = string.Join(",", orderIds.Distinct());
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT
    SUM(
        CAST(ISNULL(oi.Quantity,1) AS decimal(18,3))
        * CAST(ISNULL(mi.QuantityNeeded, 0) AS decimal(18,3))
        * CAST(ISNULL(si.PurchasePrice, 0) AS decimal(18,3))
    ) AS EstimatedCost
FROM dbo.OrderItems oi
INNER JOIN dbo.MenuIngredients mi ON mi.MenuItemId = oi.MenuItemId
INNER JOIN dbo.StockItems si ON si.StockItemId = mi.StockItemId
WHERE oi.OrderId IN ({csv});";
            var obj = cmd.ExecuteScalar();
            if (obj == null || obj == DBNull.Value) return Math.Round(fallbackRevenue * 0.35m, 2);
            var result = Convert.ToDecimal(obj, CultureInfo.InvariantCulture);
            if (result <= 0) return Math.Round(fallbackRevenue * 0.35m, 2);
            return result;
        }

        private void FillExpenseSummary(SqlConnection conn, OwnerDashboardVm vm, DateTime todayLocal, DateTime weekStartLocal, DateTime monthStartLocal)
        {
            if (!TableExists(conn, "StockExpiredLogs") || !TableExists(conn, "StockItems"))
            {
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT
    CAST(e.ExpiredDate AS date) AS ExpenseDate,
    SUM(CAST(ISNULL(e.QuantityDisposed,0) * ISNULL(s.PurchasePrice,0) AS decimal(18,2))) AS LossAmount
FROM dbo.StockExpiredLogs e
INNER JOIN dbo.StockItems s ON s.StockItemId = e.StockItemId
GROUP BY CAST(e.ExpiredDate AS date);";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                if (rd.IsDBNull(0) || rd.IsDBNull(1)) continue;
                var d = rd.GetDateTime(0).Date;
                var amount = rd.GetDecimal(1);
                if (d == todayLocal) vm.ExpensesToday += amount;
                if (d >= weekStartLocal && d <= todayLocal) vm.ExpensesWeek += amount;
                if (d >= monthStartLocal && d <= todayLocal) vm.ExpensesMonth += amount;
            }
        }

        private decimal CalculateAveragePerCustomer(SqlConnection conn, int rangeTxnCount, decimal rangeRevenue, DateTime fromLocal, DateTime toLocal)
        {
            if (!TableExists(conn, "Orders") || rangeTxnCount <= 0 || rangeRevenue <= 0) return 0m;

            var orderColumns = GetColumns(conn, "Orders");
            var paidAtCol = FirstColumn(orderColumns, "PaidAt");
            var createdCol = FirstColumn(orderColumns, "OrderDate", "CreatedAt", "CreatedOn", "Created");
            var statusCol = FirstColumn(orderColumns, "Status", "OrderStatus");
            var paymentStatusCol = FirstColumn(orderColumns, "PaymentStatus");
            var customerCol = FirstColumn(orderColumns, "UserId", "CustomerId");
            var txnCol = !string.IsNullOrWhiteSpace(paidAtCol) ? paidAtCol! : createdCol;
            if (string.IsNullOrWhiteSpace(txnCol) || string.IsNullOrWhiteSpace(statusCol) || string.IsNullOrWhiteSpace(paymentStatusCol) || string.IsNullOrWhiteSpace(customerCol))
            {
                return rangeTxnCount == 0 ? 0m : rangeRevenue / rangeTxnCount;
            }

            var tz = ResolveJakartaTimeZone();
            var utcFrom = TimeZoneInfo.ConvertTimeToUtc(fromLocal, tz);
            var utcTo = TimeZoneInfo.ConvertTimeToUtc(toLocal.AddDays(1).AddSeconds(-1), tz);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT COUNT(DISTINCT NULLIF(LTRIM(RTRIM(ISNULL(CAST(o.{customerCol} AS nvarchar(200)),''))),''))
FROM dbo.Orders o
WHERE o.{txnCol} IS NOT NULL
  AND LOWER(LTRIM(RTRIM(ISNULL(o.{statusCol}, '')))) IN ('selesai', 'completed')
  AND LOWER(LTRIM(RTRIM(ISNULL(o.{paymentStatusCol}, '')))) IN ('lunas', 'paid')
  AND CAST(o.{txnCol} AS datetime2) >= @FromUtc
  AND CAST(o.{txnCol} AS datetime2) <= @ToUtc;";
            cmd.Parameters.AddWithValue("@FromUtc", utcFrom);
            cmd.Parameters.AddWithValue("@ToUtc", utcTo);
            var customerCount = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
            return customerCount <= 0 ? (rangeRevenue / Math.Max(1, rangeTxnCount)) : (rangeRevenue / customerCount);
        }

        private static void FillAutoInsights(OwnerDashboardVm vm)
        {
            vm.AutoInsights.Clear();
            vm.AutoInsights.Add(new OwnerAutoInsightVm
            {
                Title = "Momentum Penjualan",
                Description = vm.WeekRevenue > vm.TodayRevenue
                    ? $"Omzet 7 hari terakhir {vm.WeekRevenue:N0}, transaksi {vm.WeekTransactions}."
                    : "Belum ada tren mingguan yang kuat."
            });
            vm.AutoInsights.Add(new OwnerAutoInsightVm
            {
                Title = "Menu Paling Potensial",
                Description = vm.TopProducts.Count > 0 ? $"{vm.TopProducts[0].Name} paling laku saat ini." : "Belum ada data menu terlaris."
            });
            vm.AutoInsights.Add(new OwnerAutoInsightVm
            {
                Title = "Waktu Operasional",
                Description = $"Jam ramai: {vm.BusiestHourLabel}, hari ramai: {vm.BusiestDayLabel}."
            });
            vm.AutoInsights.Add(new OwnerAutoInsightVm
            {
                Title = "Kontrol Biaya",
                Description = $"Pengeluaran bulan berjalan dari log expired: {vm.ExpensesMonth:N0}."
            });
        }

        private void FillAlerts(SqlConnection conn, OwnerDashboardVm vm)
        {
            if (TableExists(conn, "StockItems"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT
    SUM(CASE WHEN ISNULL(Quantity,0) <= 0 THEN 1 ELSE 0 END) AS OutStock,
    SUM(CASE WHEN ISNULL(Quantity,0) > 0 AND ISNULL(Quantity,0) <= ISNULL(MinQuantity,0) THEN 1 ELSE 0 END) AS LowStock,
    SUM(CASE WHEN ISNULL(IsActive,1) = 0 THEN 1 ELSE 0 END) AS Inactive
FROM dbo.StockItems;";
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    var outStock = r.IsDBNull(0) ? 0 : Convert.ToInt32(r[0], CultureInfo.InvariantCulture);
                    var lowStock = r.IsDBNull(1) ? 0 : Convert.ToInt32(r[1], CultureInfo.InvariantCulture);
                    var inactive = r.IsDBNull(2) ? 0 : Convert.ToInt32(r[2], CultureInfo.InvariantCulture);
                    if (lowStock > 0) vm.Alerts.Add(new OwnerBusinessAlertVm { Severity = "warning", Type = "stok", Message = $"{lowStock} bahan menipis." });
                    if (outStock > 0) vm.Alerts.Add(new OwnerBusinessAlertVm { Severity = "danger", Type = "stok", Message = $"{outStock} bahan habis." });
                    if (inactive > 0) vm.Alerts.Add(new OwnerBusinessAlertVm { Severity = "warning", Type = "menu", Message = $"{inactive} bahan dinonaktifkan." });
                }
            }
            if (TableExists(conn, "MenuItems"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT SUM(CASE WHEN ISNULL(IsAvailable,1)=0 OR ISNULL(Stock,0)<=0 THEN 1 ELSE 0 END) FROM dbo.MenuItems;";
                var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
                if (count > 0) vm.Alerts.Add(new OwnerBusinessAlertVm { Severity = "warning", Type = "menu", Message = $"{count} menu tidak bisa dijual." });
            }
            if (TableExists(conn, "StockExpiredLogs"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.StockExpiredLogs WHERE ExpiredDate >= DATEADD(day,-30,CAST(GETDATE() AS date));";
                var c = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
                if (c > 0) vm.Alerts.Add(new OwnerBusinessAlertVm { Severity = "danger", Type = "expired", Message = $"{c} log bahan kedaluwarsa (30 hari terakhir)." });
            }
            if (TableExists(conn, "InventoryDamageLogs"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM dbo.InventoryDamageLogs WHERE LogDate >= DATEADD(day,-30,CAST(GETDATE() AS date));";
                var c = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
                if (c > 0) vm.Alerts.Add(new OwnerBusinessAlertVm { Severity = "danger", Type = "rusak", Message = $"{c} log barang rusak/hilang (30 hari terakhir)." });
            }
            if (vm.Alerts.Count == 0)
            {
                vm.Alerts.Add(new OwnerBusinessAlertVm { Severity = "success", Type = "ok", Message = "Tidak ada alert operasional kritis saat ini." });
            }
        }

        private static string NormalizeRange(string? range)
        {
            var r = (range ?? string.Empty).Trim().ToLowerInvariant();
            return r is "daily" or "weekly" or "monthly" ? r : "monthly";
        }

        private static string NormalizeCategory(string? raw)
        {
            var s = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (s is "food" or "makanan" or "daging") return "makanan";
            if (s is "drink" or "minuman") return "minuman";
            if (s is "snack" or "jajanan" or "cemilan" or "camilan") return "jajanan";
            return "jajanan";
        }

        private static void EnsureDefaultCategories(OwnerDashboardVm vm)
        {
            var existing = vm.CategoryRevenue.Select(x => x.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var c in new[] { "makanan", "minuman", "jajanan" })
            {
                if (!existing.Contains(c))
                {
                    vm.CategoryRevenue.Add(new OwnerCategoryRevenueVm { Category = c, Revenue = 0m });
                }
            }
            vm.CategoryRevenue = vm.CategoryRevenue.OrderByDescending(x => x.Revenue).ToList();
        }

        private static TimeZoneInfo ResolveJakartaTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        private IActionResult UpdateMenuApproval(int id, string status, string? notes)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            EnsureOwnerMenuApprovalColumns(conn);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.MenuItems
SET ApprovalStatus = @Status,
    ApprovalNotes = @Notes,
    ApprovalUpdatedAt = SYSUTCDATETIME(),
    ApprovalUpdatedBy = @By
WHERE MenuItemId = @Id;";
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@By", User?.Identity?.Name ?? "owner");
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return Redirect("/Owner/MenuControl");
        }

        private static void EnsureOwnerMenuApprovalColumns(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF OBJECT_ID(N'dbo.MenuItems', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.MenuItems', 'ApprovalStatus') IS NULL
        ALTER TABLE dbo.MenuItems ADD ApprovalStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_MenuItems_ApprovalStatus DEFAULT('approved');
    IF COL_LENGTH('dbo.MenuItems', 'ApprovalNotes') IS NULL
        ALTER TABLE dbo.MenuItems ADD ApprovalNotes NVARCHAR(300) NULL;
    IF COL_LENGTH('dbo.MenuItems', 'ApprovalUpdatedAt') IS NULL
        ALTER TABLE dbo.MenuItems ADD ApprovalUpdatedAt DATETIME2 NULL;
    IF COL_LENGTH('dbo.MenuItems', 'ApprovalUpdatedBy') IS NULL
        ALTER TABLE dbo.MenuItems ADD ApprovalUpdatedBy NVARCHAR(120) NULL;
    IF COL_LENGTH('dbo.MenuItems', 'Description') IS NULL
        ALTER TABLE dbo.MenuItems ADD Description NVARCHAR(500) NULL;
END;";
            cmd.ExecuteNonQuery();
        }

        private sealed class PaidOrderRow
        {
            public int OrderId { get; set; }
            public DateTime TxnLocal { get; set; }
            public decimal Total { get; set; }
            public string OrderStatus { get; set; } = "-";
            public string PaymentStatus { get; set; } = "-";
            public int TableNumber { get; set; }
        }

        private static HashSet<string> GetColumns(SqlConnection conn, string tableName)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName;";
            cmd.Parameters.AddWithValue("@TableName", tableName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
            return columns;
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

        private static bool TableExists(SqlConnection conn, string tableName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END;";
            cmd.Parameters.AddWithValue("@TableName", $"dbo.{tableName}");
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
        }
    }
}
