using System.Diagnostics;
using System.Globalization;
using cafe_yo.Models;
using cafe_yo.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace cafe_yo.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("/")]
        [HttpGet("/menu")]
        public IActionResult Index([FromQuery(Name = "meja")] int? meja = null)
        {
            if (meja.HasValue && meja.Value > 0)
            {
                Response.Cookies.Append(
                    "nr_tableNumber",
                    meja.Value.ToString(CultureInfo.InvariantCulture),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(30),
                        HttpOnly = false,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax
                    });

                Response.Cookies.Append(
                    "nr_tableLocked",
                    "1",
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(30),
                        HttpOnly = false,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax
                    });
            }

            var model = new HomeIndexViewModel
            {
                QrisImageUrl = GetQrisImageUrl() ?? string.Empty,
                MenuItems = GetCustomerMenuItems()
            };

            return View(model);
        }

        [HttpGet("/keranjang")]
        [AllowAnonymous]
        public IActionResult Cart()
        {
            var model = new HomeIndexViewModel
            {
                QrisImageUrl = GetQrisImageUrl() ?? string.Empty,
                MenuItems = GetCustomerMenuItems()
            };
            ViewData["CartOnly"] = true;
            return View("~/Views/Home/Index.cshtml", model);
        }

        [HttpGet("/privacy")]
        public IActionResult Privacy()
        {
            return View("~/Views/Home/Privacy.cshtml");
        }

        [HttpGet("/pesanan-saya")]
        [AllowAnonymous]
        public IActionResult MyOrders()
        {
            return View("~/Views/Home/MyOrders.cshtml");
        }

        public sealed class CustomerOrderItemRequest
        {
            public string? MenuId { get; set; }
            public string? Name { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public string? Notes { get; set; }
        }

        public sealed class CustomerCreateOrderRequest
        {
            public int TableNumber { get; set; }
            public bool IsMember { get; set; }
            public string? PaymentMethod { get; set; }
            public string? OrderType { get; set; }
            public List<CustomerOrderItemRequest> Items { get; set; } = new();
        }

        public sealed class CustomerMyOrdersRequest
        {
            public int TableNumber { get; set; }
            public List<int> OrderIds { get; set; } = new();
        }

        [HttpPost("/api/customer/orders/create")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public IActionResult CreateCustomerOrder([FromBody] CustomerCreateOrderRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return BadRequest(new { success = false, error = "Nomor meja tidak valid." });
            }

            var normalizedItems = (req.Items ?? new List<CustomerOrderItemRequest>())
                .Where(i => !string.IsNullOrWhiteSpace(i.Name) && i.Quantity > 0 && i.Price >= 0)
                .Select(i => new CustomerOrderItemRequest
                {
                    MenuId = string.IsNullOrWhiteSpace(i.MenuId) ? null : i.MenuId.Trim(),
                    Name = i.Name?.Trim(),
                    Price = i.Price,
                    Quantity = i.Quantity,
                    Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim()
                })
                .ToList();

            if (normalizedItems.Count == 0)
            {
                return BadRequest(new { success = false, error = "Keranjang kosong." });
            }

            var connString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connString))
            {
                return StatusCode(500, new { success = false, error = "Koneksi database belum dikonfigurasi." });
            }

            using var conn = new SqlConnection(connString);
            conn.Open();
            TableStateStore.EnsureTables(conn);
            OrderTableSync.SyncAllTableStatuses(conn);

            var tableId = 0;
            using (var tableCmd = conn.CreateCommand())
            {
                tableCmd.CommandText = "SELECT TOP 1 TableId FROM dbo.Tables WHERE TableNumber = @TableNumber;";
                tableCmd.Parameters.AddWithValue("@TableNumber", req.TableNumber);
                var tableIdObj = tableCmd.ExecuteScalar();
                tableId = tableIdObj == null ? 0 : Convert.ToInt32(tableIdObj, CultureInfo.InvariantCulture);
            }

            if (tableId <= 0)
            {
                return BadRequest(new { success = false, error = "Meja tidak ditemukan." });
            }

            decimal subtotal = 0;
            var totalQty = 0;
            foreach (var item in normalizedItems)
            {
                subtotal += item.Price * item.Quantity;
                totalQty += item.Quantity;
            }

            var discountRate = 0m;
            if (req.IsMember && subtotal > 0)
            {
                if (totalQty >= 6)
                {
                    discountRate = 0.20m;
                }
                else if (totalQty >= 3)
                {
                    discountRate = 0.15m;
                }
                else
                {
                    discountRate = 0.10m;
                }
            }

            var discount = Math.Round(subtotal * discountRate, 0, MidpointRounding.AwayFromZero);
            var service = Math.Round((subtotal - discount) * 0.05m, 0, MidpointRounding.AwayFromZero);
            var total = subtotal - discount + service;
            if (total < 0)
            {
                total = 0;
            }

            var paymentMethod = (req.PaymentMethod ?? string.Empty).Trim().ToLowerInvariant();
            if (paymentMethod != "kasir" && paymentMethod != "qris")
            {
                paymentMethod = "qris";
            }

            var paymentStatus = paymentMethod == "kasir" ? "belum_bayar" : "menunggu_qris";
            var orderStatus = "menunggu_pembayaran";
            var orderType = string.Equals((req.OrderType ?? string.Empty).Trim(), "takeaway", StringComparison.OrdinalIgnoreCase)
                ? "takeaway"
                : "dinein";
            var orderTypeLabel = orderType == "takeaway" ? "Bawa Pulang" : "Makan di Tempat";

            using var tx = conn.BeginTransaction();
            int orderId;
            var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            using (var insertOrder = conn.CreateCommand())
            {
                insertOrder.Transaction = tx;
                insertOrder.CommandText = @"
INSERT INTO dbo.Orders (OrderNumber, TableId, OrderDate, [Status], Total, KitchenStatus, PaymentMethod, PaymentStatus, UpdatedAt)
VALUES (@OrderNumber, @TableId, SYSUTCDATETIME(), @Status, @Total, @KitchenStatus, @PaymentMethod, @PaymentStatus, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);";
                insertOrder.Parameters.AddWithValue("@OrderNumber", orderNumber);
                insertOrder.Parameters.AddWithValue("@TableId", tableId);
                insertOrder.Parameters.AddWithValue("@Status", orderStatus);
                insertOrder.Parameters.AddWithValue("@Total", total);
                insertOrder.Parameters.AddWithValue("@KitchenStatus", "pending");
                insertOrder.Parameters.AddWithValue("@PaymentMethod", paymentMethod);
                insertOrder.Parameters.AddWithValue("@PaymentStatus", paymentStatus);
                var newOrder = insertOrder.ExecuteScalar();
                orderId = newOrder == null ? 0 : Convert.ToInt32(newOrder, CultureInfo.InvariantCulture);
            }

            if (orderId <= 0)
            {
                tx.Rollback();
                return StatusCode(500, new { success = false, error = "Gagal membuat order customer." });
            }

            var orderTypeNoteAttached = false;
            foreach (var item in normalizedItems)
            {
                var itemNotes = item.Notes;
                if (!orderTypeNoteAttached)
                {
                    itemNotes = string.IsNullOrWhiteSpace(itemNotes)
                        ? $"Tipe Pesanan: {orderTypeLabel}"
                        : $"{itemNotes} | Tipe Pesanan: {orderTypeLabel}";
                    orderTypeNoteAttached = true;
                }

                using var insertItem = conn.CreateCommand();
                insertItem.Transaction = tx;
                insertItem.CommandText = @"
INSERT INTO dbo.OrderItems (OrderId, MenuItemId, ItemName, Quantity, Notes, UnitPrice)
VALUES (@OrderId, @MenuItemId, @ItemName, @Quantity, @Notes, @UnitPrice);";
                insertItem.Parameters.AddWithValue("@OrderId", orderId);
                insertItem.Parameters.AddWithValue("@MenuItemId", DBNull.Value);
                insertItem.Parameters.AddWithValue("@ItemName", item.Name ?? "Item");
                insertItem.Parameters.AddWithValue("@Quantity", item.Quantity);
                insertItem.Parameters.AddWithValue("@Notes", (object?)itemNotes ?? DBNull.Value);
                insertItem.Parameters.AddWithValue("@UnitPrice", item.Price);
                insertItem.ExecuteNonQuery();
            }

            tx.Commit();
            OrderTableSync.SyncByTableNumber(conn, req.TableNumber);

            return Ok(new
            {
                success = true,
                orderId,
                orderCode = orderNumber,
                tableNumber = req.TableNumber,
                paymentMethod,
                orderType,
                paymentStatus,
                orderStatus,
                subtotal,
                discount,
                service,
                total
            });
        }

        [HttpPost("/api/customer/orders/mine")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public IActionResult GetMyOrders([FromBody] CustomerMyOrdersRequest req)
        {
            if (req.TableNumber <= 0)
            {
                return BadRequest(new { success = false, error = "Nomor meja tidak valid." });
            }

            var ids = (req.OrderIds ?? new List<int>())
                .Where(x => x > 0)
                .Distinct()
                .Take(100)
                .ToList();

            if (ids.Count == 0)
            {
                return Ok(new { success = true, orders = Array.Empty<object>() });
            }

            var connString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connString))
            {
                return StatusCode(500, new { success = false, error = "Koneksi database belum dikonfigurasi." });
            }

            using var conn = new SqlConnection(connString);
            conn.Open();

            var idList = string.Join(",", ids);
            var orders = new List<MyOrderRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
SELECT
    o.OrderId,
    ISNULL(o.OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(o.OrderId AS nvarchar(20))), 6))) AS OrderCode,
    t.TableNumber,
    o.Total,
    o.OrderDate,
    ISNULL(o.PaymentMethod, '-') AS PaymentMethod,
    ISNULL(o.PaymentStatus, '-') AS PaymentStatus,
    ISNULL(o.[Status], '-') AS OrderStatus,
    ISNULL(o.PaymentInvoice, '') AS PaymentInvoice,
    ISNULL(o.PaymentCheckoutUrl, '') AS PaymentCheckoutUrl,
    ISNULL(o.PaymentQrString, '') AS PaymentQrString
FROM dbo.Orders o
LEFT JOIN dbo.Tables t ON t.TableId = o.TableId
WHERE o.OrderId IN ({idList})
  AND t.TableNumber = @TableNumber
ORDER BY o.OrderDate DESC, o.OrderId DESC;";
                cmd.Parameters.AddWithValue("@TableNumber", req.TableNumber);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    orders.Add(new MyOrderRow
                    {
                        OrderId = reader.GetInt32(0),
                        OrderCode = reader.IsDBNull(1) ? "-" : reader.GetString(1),
                        TableNumber = reader.IsDBNull(2) ? req.TableNumber : reader.GetInt32(2),
                        Total = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                        OrderDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                        PaymentMethod = reader.IsDBNull(5) ? "-" : reader.GetString(5),
                        PaymentStatus = reader.IsDBNull(6) ? "-" : reader.GetString(6),
                        OrderStatus = reader.IsDBNull(7) ? "-" : reader.GetString(7),
                        PaymentInvoice = reader.IsDBNull(8) ? null : reader.GetString(8),
                        PaymentCheckoutUrl = reader.IsDBNull(9) ? null : reader.GetString(9),
                        PaymentQrString = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }

            if (orders.Count > 0)
            {
                var existingIds = string.Join(",", orders.Select(x => x.OrderId));
                using var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = $@"
SELECT oi.OrderId,
       ISNULL(oi.Quantity, 1) AS Quantity,
       COALESCE(NULLIF(LTRIM(RTRIM(oi.ItemName)), ''), 'Item') AS ItemName,
       ISNULL(oi.UnitPrice, 0) AS UnitPrice,
       NULLIF(LTRIM(RTRIM(oi.Notes)), '') AS Notes
FROM dbo.OrderItems oi
WHERE oi.OrderId IN ({existingIds})
ORDER BY oi.OrderItemId ASC;";
                var map = orders.ToDictionary(x => x.OrderId);
                using var itemReader = itemCmd.ExecuteReader();
                while (itemReader.Read())
                {
                    var oid = itemReader.GetInt32(0);
                    if (!map.TryGetValue(oid, out var row))
                    {
                        continue;
                    }
                    row.Items.Add(new MyOrderItemRow
                    {
                        Quantity = itemReader.IsDBNull(1) ? 1 : itemReader.GetInt32(1),
                        Name = itemReader.IsDBNull(2) ? "Item" : itemReader.GetString(2),
                        UnitPrice = itemReader.IsDBNull(3) ? 0m : itemReader.GetDecimal(3),
                        Notes = itemReader.IsDBNull(4) ? null : itemReader.GetString(4)
                    });
                }
            }

            return Ok(new
            {
                success = true,
                orders = orders.Select(x => new
                {
                    orderId = x.OrderId,
                    orderCode = x.OrderCode,
                    tableNumber = x.TableNumber,
                    total = x.Total,
                    orderTime = x.OrderDate?.ToString("o"),
                    paymentMethod = NormalizePaymentMethod(x.PaymentMethod),
                    paymentStatus = NormalizePaymentStatus(x.PaymentStatus),
                    orderStatus = NormalizeCustomerOrderStatus(x.OrderStatus),
                    paymentInvoice = x.PaymentInvoice,
                    paymentUrl = x.PaymentCheckoutUrl,
                    qrString = x.PaymentQrString,
                    items = x.Items.Select(i => new
                    {
                        quantity = i.Quantity,
                        name = i.Name,
                        unitPrice = i.UnitPrice,
                        notes = i.Notes
                    })
                })
            });
        }

        private static string NormalizePaymentMethod(string? method)
        {
            var m = (method ?? string.Empty).Trim().ToLowerInvariant();
            return m == "kasir" ? "kasir" : "qris";
        }

        private static string NormalizePaymentStatus(string? status)
        {
            var s = (status ?? string.Empty).Trim().ToLowerInvariant();
            if (s is "lunas" or "paid") return "lunas";
            if (s == "menunggu_qris") return "menunggu_qris";
            if (s == "belum_bayar") return "belum_bayar";
            return string.IsNullOrWhiteSpace(s) ? "-" : s;
        }

        private static string NormalizeCustomerOrderStatus(string? status)
        {
            var s = (status ?? string.Empty).Trim().ToLowerInvariant();
            if (s == "menunggu_pembayaran") return "menunggu_pembayaran";
            if (s is "diproses" or "processing" or "cooking") return "diproses";
            if (s is "siap" or "ready") return "siap";
            if (s is "selesai" or "completed" or "paid") return "selesai";
            if (s is "dibatalkan" or "cancelled" or "canceled") return "dibatalkan";
            return "menunggu_pembayaran";
        }

        private sealed class MyOrderRow
        {
            public int OrderId { get; set; }
            public string OrderCode { get; set; } = "-";
            public int TableNumber { get; set; }
            public decimal Total { get; set; }
            public DateTime? OrderDate { get; set; }
            public string PaymentMethod { get; set; } = "-";
            public string PaymentStatus { get; set; } = "-";
            public string OrderStatus { get; set; } = "-";
            public string? PaymentInvoice { get; set; }
            public string? PaymentCheckoutUrl { get; set; }
            public string? PaymentQrString { get; set; }
            public List<MyOrderItemRow> Items { get; set; } = new();
        }

        private sealed class MyOrderItemRow
        {
            public int Quantity { get; set; }
            public string Name { get; set; } = "Item";
            public decimal UnitPrice { get; set; }
            public string? Notes { get; set; }
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private string? GetQrisImageUrl()
        {
            var connString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connString))
            {
                return null;
            }

            try
            {
                using var conn = new SqlConnection(connString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT TOP 1 [Value] FROM dbo.SystemSettings WHERE [Key] = @Key;";
                cmd.Parameters.AddWithValue("@Key", "QrisImageUrl");
                var value = cmd.ExecuteScalar() as string;
                return value?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private List<MenuItem> GetCustomerMenuItems()
        {
            var items = new List<MenuItem>();
            var connString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connString))
            {
                return items;
            }

            try
            {
                using var conn = new SqlConnection(connString);
                conn.Open();
                EnsureMenuItemsImageColumn(conn);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
SELECT MenuItemId, Name, Category, ImageUrl, ISNULL([Description], '') AS [Description], Price, ISNULL(Stock,0) AS Stock, ISNULL(IsAvailable,1) AS IsAvailable
FROM dbo.MenuItems
WHERE ISNULL(IsAvailable,1) = 1
ORDER BY Name ASC;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.IsDBNull(1) ? "Menu" : reader.GetString(1);
                    items.Add(new MenuItem
                    {
                        MenuItemId = reader.GetInt32(0),
                        Name = name,
                        Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                        ImageUrl = ResolveMenuImage(name, reader.IsDBNull(3) ? null : reader.GetString(3)),
                        Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Price = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                        Stock = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                        IsAvailable = reader.IsDBNull(7) ? true : reader.GetBoolean(7)
                    });
                }
            }
            catch
            {
                // Keep fallback empty list if DB error happens.
            }

            return items;
        }

        private static void EnsureMenuItemsImageColumn(SqlConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
IF COL_LENGTH('dbo.MenuItems', 'ImageUrl') IS NULL
BEGIN
    ALTER TABLE dbo.MenuItems ADD ImageUrl NVARCHAR(500) NULL;
END;";
            cmd.ExecuteNonQuery();
        }

        private static string ResolveMenuImage(string menuName, string? dbImageUrl)
        {
            if (!string.IsNullOrWhiteSpace(dbImageUrl))
            {
                return dbImageUrl.Trim();
            }

            var slug = menuName.Trim().ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("/", "-")
                .Replace("--", "-");
            return $"/images/menu/{slug}.jpg";
        }
    }
}
