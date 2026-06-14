using System.Globalization;
using System.Text.Json;
using cafe_yo.Data;
using cafe_yo.Security;
using cafe_yo.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentsApiController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IBayarGgClient _bayarGgClient;
        private readonly ILogger<PaymentsApiController> _logger;

        public PaymentsApiController(
            IConfiguration configuration,
            IBayarGgClient bayarGgClient,
            ILogger<PaymentsApiController> logger)
        {
            _configuration = configuration;
            _bayarGgClient = bayarGgClient;
            _logger = logger;
        }

        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        public sealed class CreateOrderPaymentRequest
        {
            public string? CustomerName { get; set; }
            public string? CustomerEmail { get; set; }
            public string? CustomerPhone { get; set; }
            public string? CallbackUrl { get; set; }
            public string? RedirectUrl { get; set; }
            public string? PaymentMethod { get; set; } = "qris";
            public bool UseQrisConverter { get; set; }
        }

        public sealed class GatewayListQuery
        {
            public string? Search { get; set; }
            public string? Status { get; set; }
            public string? PaymentMethod { get; set; }
            public string? PaidVia { get; set; }
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
            public int? Page { get; set; }
            public int? Limit { get; set; }
        }

        [HttpPost("orders/{orderId:int}/create")]
        [Authorize(Policy = "KasirOnly")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateOrderPayment(int orderId, [FromBody] CreateOrderPaymentRequest request, CancellationToken cancellationToken)
        {
            return await CreateOrderPaymentCore(orderId, request, cancellationToken);
        }

        [AllowAnonymous]
        [HttpPost("customer/orders/{orderId:int}/create")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateCustomerOrderPayment(int orderId, [FromBody] CreateOrderPaymentRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            {
                request.PaymentMethod = "qris";
            }

            return await CreateOrderPaymentCore(orderId, request, cancellationToken);
        }

        private async Task<IActionResult> CreateOrderPaymentCore(int orderId, CreateOrderPaymentRequest request, CancellationToken cancellationToken)
        {
            if (orderId <= 0)
            {
                return BadRequest(new { success = false, error = "Order tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync(cancellationToken);

            var order = GetOrderPaymentInfo(conn, orderId);
            if (order == null)
            {
                return NotFound(new { success = false, error = "Order tidak ditemukan." });
            }

            if (order.Total < 1000)
            {
                return BadRequest(new { success = false, error = "Nominal order minimal Rp1.000 untuk pembayaran QRIS." });
            }

            var gatewayFee = GetGatewayFee();
            var payableTotal = order.Total + gatewayFee;

            var gatewayRequest = new BayarGgCreatePaymentRequest
            {
                Amount = payableTotal,
                Description = $"Pembayaran {order.OrderNumber}",
                CustomerName = request.CustomerName,
                CustomerEmail = request.CustomerEmail,
                CustomerPhone = request.CustomerPhone,
                CallbackUrl = request.CallbackUrl,
                RedirectUrl = request.RedirectUrl,
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "qris" : request.PaymentMethod.Trim(),
                UseQrisConverter = request.UseQrisConverter
            };

            BayarGgApiResponse gatewayResponse;
            try
            {
                gatewayResponse = await _bayarGgClient.CreatePaymentAsync(gatewayRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal create payment ke BayarGG untuk OrderId={OrderId}", orderId);
                return StatusCode(502, new { success = false, error = "Gagal terhubung ke payment gateway." });
            }

            SavePaymentResult(conn, orderId, gatewayResponse.Invoice, gatewayRequest.PaymentMethod, gatewayResponse.Status, gatewayResponse.PaymentUrl, gatewayResponse.QrString);
            var gatewayPayable = ResolveGatewayPayableTotal(order.Total, gatewayResponse, payableTotal);
            var effectiveFee = gatewayPayable - order.Total;
            if (effectiveFee < 0) effectiveFee = 0;

            return StatusCode(
                gatewayResponse.IsHttpSuccess ? 200 : 502,
                new
                {
                    success = gatewayResponse.IsHttpSuccess,
                    orderId,
                    total = order.Total,
                    gatewayFee = effectiveFee,
                    payableTotal = gatewayPayable,
                    uniqueCode = gatewayResponse.UniqueCode,
                    gatewayExtraFee = gatewayResponse.FeeAmount,
                    invoice = gatewayResponse.Invoice,
                    status = gatewayResponse.Status,
                    paymentUrl = gatewayResponse.PaymentUrl,
                    qrString = gatewayResponse.QrString,
                    qrImageUrl = gatewayResponse.QrImageUrl,
                    message = gatewayResponse.Message,
                    gatewayStatusCode = gatewayResponse.HttpStatusCode,
                    raw = gatewayResponse.RawJson
                });
        }

        [HttpGet("orders/{orderId:int}/refresh")]
        [Authorize(Roles = AppRoles.Kasir + "," + AppRoles.Supervisor + "," + AppRoles.Admin + "," + AppRoles.Owner)]
        public async Task<IActionResult> RefreshOrderPayment(int orderId, CancellationToken cancellationToken)
        {
            return await RefreshOrderPaymentCore(orderId, cancellationToken);
        }

        [AllowAnonymous]
        [HttpGet("customer/orders/{orderId:int}/refresh")]
        public async Task<IActionResult> RefreshCustomerOrderPayment(int orderId, CancellationToken cancellationToken)
        {
            return await RefreshOrderPaymentCore(orderId, cancellationToken);
        }

        private async Task<IActionResult> RefreshOrderPaymentCore(int orderId, CancellationToken cancellationToken)
        {
            if (orderId <= 0)
            {
                return BadRequest(new { success = false, error = "Order tidak valid." });
            }

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync(cancellationToken);

            var snapshot = GetOrderPaymentSnapshot(conn, orderId);
            var invoice = snapshot?.Invoice;
            if (string.IsNullOrWhiteSpace(invoice))
            {
                return BadRequest(new { success = false, error = "Order belum punya invoice payment." });
            }

            BayarGgApiResponse gatewayResponse;
            try
            {
                gatewayResponse = await _bayarGgClient.CheckPaymentAsync(invoice, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal check payment BayarGG untuk OrderId={OrderId}, Invoice={Invoice}", orderId, invoice);
                return StatusCode(502, new { success = false, error = "Gagal cek status payment ke gateway." });
            }

            SavePaymentResult(conn, orderId, gatewayResponse.Invoice ?? invoice, null, gatewayResponse.Status, gatewayResponse.PaymentUrl, gatewayResponse.QrString);

            var finalPaymentUrl = string.IsNullOrWhiteSpace(gatewayResponse.PaymentUrl)
                ? snapshot?.PaymentUrl
                : gatewayResponse.PaymentUrl;
            var finalQrString = string.IsNullOrWhiteSpace(gatewayResponse.QrString)
                ? snapshot?.QrString
                : gatewayResponse.QrString;
            var finalQrImageUrl = string.IsNullOrWhiteSpace(gatewayResponse.QrImageUrl)
                ? null
                : gatewayResponse.QrImageUrl;

            var order = GetOrderPaymentInfo(conn, orderId);
            var baseTotal = order?.Total ?? 0m;
            var fallbackPayable = baseTotal + GetGatewayFee();
            var gatewayPayable = ResolveGatewayPayableTotal(baseTotal, gatewayResponse, fallbackPayable);
            var effectiveFee = gatewayPayable - baseTotal;
            if (effectiveFee < 0) effectiveFee = 0;

            return StatusCode(
                gatewayResponse.IsHttpSuccess ? 200 : 502,
                new
                {
                    success = gatewayResponse.IsHttpSuccess,
                    orderId,
                    total = baseTotal,
                    gatewayFee = effectiveFee,
                    payableTotal = gatewayPayable,
                    uniqueCode = gatewayResponse.UniqueCode,
                    gatewayExtraFee = gatewayResponse.FeeAmount,
                    invoice = gatewayResponse.Invoice ?? invoice,
                    status = gatewayResponse.Status,
                    paymentUrl = finalPaymentUrl,
                    qrString = finalQrString,
                    qrImageUrl = finalQrImageUrl,
                    message = gatewayResponse.Message,
                    gatewayStatusCode = gatewayResponse.HttpStatusCode,
                    raw = gatewayResponse.RawJson
                });
        }

        [HttpGet("gateway/check")]
        [Authorize(Roles = AppRoles.Kasir + "," + AppRoles.Supervisor + "," + AppRoles.Admin + "," + AppRoles.Owner)]
        public async Task<IActionResult> CheckInvoice([FromQuery] string invoice, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(invoice))
            {
                return BadRequest(new { success = false, error = "Invoice wajib diisi." });
            }

            BayarGgApiResponse gatewayResponse;
            try
            {
                gatewayResponse = await _bayarGgClient.CheckPaymentAsync(invoice.Trim(), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal check payment BayarGG untuk Invoice={Invoice}", invoice);
                return StatusCode(502, new { success = false, error = "Gagal cek status payment ke gateway." });
            }

            return StatusCode(
                gatewayResponse.IsHttpSuccess ? 200 : 502,
                new
                {
                    success = gatewayResponse.IsHttpSuccess,
                    invoice = gatewayResponse.Invoice ?? invoice.Trim(),
                    status = gatewayResponse.Status,
                    paymentUrl = gatewayResponse.PaymentUrl,
                    qrString = gatewayResponse.QrString,
                    qrImageUrl = gatewayResponse.QrImageUrl,
                    message = gatewayResponse.Message,
                    gatewayStatusCode = gatewayResponse.HttpStatusCode,
                    raw = gatewayResponse.RawJson
                });
        }

        [HttpGet("gateway/list")]
        [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Owner + "," + AppRoles.Supervisor)]
        public async Task<IActionResult> ListGatewayPayments([FromQuery] GatewayListQuery query, CancellationToken cancellationToken)
        {
            BayarGgApiResponse gatewayResponse;
            try
            {
                gatewayResponse = await _bayarGgClient.ListPaymentsAsync(
                    new BayarGgListPaymentsRequest
                    {
                        Search = query.Search,
                        Status = query.Status,
                        PaymentMethod = query.PaymentMethod,
                        PaidVia = query.PaidVia,
                        StartDate = query.StartDate,
                        EndDate = query.EndDate,
                        Page = query.Page,
                        Limit = query.Limit
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal list payments dari BayarGG");
                return StatusCode(502, new { success = false, error = "Gagal mengambil daftar payment dari gateway." });
            }

            return StatusCode(
                gatewayResponse.IsHttpSuccess ? 200 : 502,
                new
                {
                    success = gatewayResponse.IsHttpSuccess,
                    status = gatewayResponse.Status,
                    message = gatewayResponse.Message,
                    gatewayStatusCode = gatewayResponse.HttpStatusCode,
                    raw = gatewayResponse.RawJson
                });
        }

        [AllowAnonymous]
        [HttpPost("gateway/callback")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GatewayCallback(CancellationToken cancellationToken)
        {
            string? invoice = null;
            string? status = null;
            string? paidVia = null;
            string rawBody = string.Empty;

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(cancellationToken);
                invoice = form["invoice"].FirstOrDefault();
                status = form["status"].FirstOrDefault();
                paidVia = form["paid_via"].FirstOrDefault();
            }
            else
            {
                using var reader = new StreamReader(Request.Body);
                rawBody = await reader.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(rawBody);
                        invoice = TryReadJsonString(doc.RootElement, "invoice", "invoice_id");
                        status = TryReadJsonString(doc.RootElement, "status", "payment_status");
                        paidVia = TryReadJsonString(doc.RootElement, "paid_via", "payment_method");
                    }
                    catch (JsonException)
                    {
                        // Non-json body is ignored.
                    }
                }
            }

            invoice ??= Request.Query["invoice"].FirstOrDefault();
            status ??= Request.Query["status"].FirstOrDefault();
            paidVia ??= Request.Query["paid_via"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(invoice))
            {
                return BadRequest(new { success = false, error = "Invoice tidak ditemukan pada callback." });
            }

            using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync(cancellationToken);

            var orderId = FindOrderIdByInvoice(conn, invoice.Trim());
            if (orderId > 0)
            {
                SavePaymentResult(conn, orderId, invoice.Trim(), paidVia, status, null, null);
            }

            _logger.LogInformation("Payment callback diterima. Invoice={Invoice}, Status={Status}, PaidVia={PaidVia}, OrderId={OrderId}",
                invoice, status, paidVia, orderId);

            return Ok(new { success = true });
        }

        private sealed class OrderPaymentInfo
        {
            public int OrderId { get; set; }
            public string OrderNumber { get; set; } = string.Empty;
            public decimal Total { get; set; }
        }

        private OrderPaymentInfo? GetOrderPaymentInfo(SqlConnection conn, int orderId)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 1
    o.OrderId,
    ISNULL(o.OrderNumber, CONCAT('ORD-', RIGHT(CONCAT('000000', CAST(o.OrderId AS nvarchar(20))), 6))) AS OrderNumber,
    ISNULL(o.Total, 0)
FROM dbo.Orders o
WHERE o.OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", orderId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new OrderPaymentInfo
            {
                OrderId = reader.GetInt32(0),
                OrderNumber = reader.IsDBNull(1) ? $"ORD-{reader.GetInt32(0)}" : reader.GetString(1),
                Total = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2)
            };
        }

        private string? GetOrderInvoice(SqlConnection conn, int orderId)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 PaymentInvoice FROM dbo.Orders WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", orderId);
            var value = cmd.ExecuteScalar()?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class OrderPaymentSnapshot
        {
            public string? Invoice { get; set; }
            public string? PaymentUrl { get; set; }
            public string? QrString { get; set; }
        }

        private OrderPaymentSnapshot? GetOrderPaymentSnapshot(SqlConnection conn, int orderId)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 1
    NULLIF(LTRIM(RTRIM(PaymentInvoice)), '') AS PaymentInvoice,
    NULLIF(LTRIM(RTRIM(PaymentCheckoutUrl)), '') AS PaymentCheckoutUrl,
    NULLIF(LTRIM(RTRIM(PaymentQrString)), '') AS PaymentQrString
FROM dbo.Orders
WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", orderId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new OrderPaymentSnapshot
            {
                Invoice = reader.IsDBNull(0) ? null : reader.GetString(0),
                PaymentUrl = reader.IsDBNull(1) ? null : reader.GetString(1),
                QrString = reader.IsDBNull(2) ? null : reader.GetString(2)
            };
        }

        private int FindOrderIdByInvoice(SqlConnection conn, string invoice)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP 1 OrderId FROM dbo.Orders WHERE PaymentInvoice = @Invoice;";
            cmd.Parameters.AddWithValue("@Invoice", invoice);
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value)
            {
                return 0;
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static void SavePaymentResult(
            SqlConnection conn,
            int orderId,
            string? invoice,
            string? paymentMethod,
            string? paymentStatus,
            string? paymentUrl,
            string? qrString)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE dbo.Orders
SET PaymentInvoice = COALESCE(@PaymentInvoice, PaymentInvoice),
    PaymentMethod = COALESCE(@PaymentMethod, PaymentMethod),
    PaymentStatus = CASE
        WHEN LOWER(ISNULL(@PaymentStatus, '')) = 'paid' THEN 'lunas'
        ELSE COALESCE(@PaymentStatus, PaymentStatus)
    END,
    PaymentCheckoutUrl = COALESCE(@PaymentCheckoutUrl, PaymentCheckoutUrl),
    PaymentQrString = COALESCE(@PaymentQrString, PaymentQrString),
    [Status] = CASE
        WHEN LOWER(ISNULL(@PaymentStatus, '')) = 'paid' THEN 'diproses'
        ELSE [Status]
    END,
    KitchenStatus = CASE
        WHEN LOWER(ISNULL(@PaymentStatus, '')) = 'paid' THEN 'processing'
        ELSE KitchenStatus
    END,
    StartedAt = CASE
        WHEN LOWER(ISNULL(@PaymentStatus, '')) = 'paid' THEN COALESCE(StartedAt, SYSUTCDATETIME())
        ELSE StartedAt
    END,
    PaidAt = CASE WHEN LOWER(ISNULL(@PaymentStatus, '')) = 'paid' THEN SYSUTCDATETIME() ELSE PaidAt END,
    UpdatedAt = SYSUTCDATETIME()
WHERE OrderId = @OrderId;";
            cmd.Parameters.AddWithValue("@OrderId", orderId);
            cmd.Parameters.AddWithValue("@PaymentInvoice", string.IsNullOrWhiteSpace(invoice) ? DBNull.Value : invoice.Trim());
            cmd.Parameters.AddWithValue("@PaymentMethod", string.IsNullOrWhiteSpace(paymentMethod) ? DBNull.Value : paymentMethod.Trim());
            cmd.Parameters.AddWithValue("@PaymentStatus", string.IsNullOrWhiteSpace(paymentStatus) ? DBNull.Value : paymentStatus.Trim());
            cmd.Parameters.AddWithValue("@PaymentCheckoutUrl", string.IsNullOrWhiteSpace(paymentUrl) ? DBNull.Value : paymentUrl.Trim());
            cmd.Parameters.AddWithValue("@PaymentQrString", string.IsNullOrWhiteSpace(qrString) ? DBNull.Value : qrString.Trim());
            cmd.ExecuteNonQuery();
        }

        private static string? TryReadJsonString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
                {
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in names)
                {
                    if (data.TryGetProperty(name, out var value))
                    {
                        var text = value.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text.Trim();
                        }
                    }
                }
            }

            return null;
        }

        private decimal GetGatewayFee()
        {
            var configured = _configuration.GetValue<decimal?>("PaymentGateway:BayarGG:FixedFee");
            if (!configured.HasValue || configured.Value < 0)
            {
                return 200m;
            }

            return Math.Round(configured.Value, 0, MidpointRounding.AwayFromZero);
        }

        private decimal ResolveGatewayPayableTotal(decimal baseTotal, BayarGgApiResponse gatewayResponse, decimal fallbackPayable)
        {
            // Hindari nilai liar dari field total gateway yang kadang bukan total tagihan transaksi ini.
            // Batas tambahan fee bisa diubah via config bila perlu.
            var maxExtra = _configuration.GetValue<decimal?>("PaymentGateway:BayarGG:MaxExtraFee");
            var saneMaxExtra = (!maxExtra.HasValue || maxExtra.Value <= 0) ? 5000m : maxExtra.Value;
            var maxAllowed = baseTotal + saneMaxExtra;

            var candidates = new List<decimal>();
            if (gatewayResponse.TotalPayment.HasValue) candidates.Add(gatewayResponse.TotalPayment.Value);
            if (gatewayResponse.Amount.HasValue) candidates.Add(gatewayResponse.Amount.Value);
            if (gatewayResponse.UniqueCode.HasValue || gatewayResponse.FeeAmount.HasValue)
            {
                var byComponents = baseTotal + (gatewayResponse.UniqueCode ?? 0m) + (gatewayResponse.FeeAmount ?? 0m);
                candidates.Add(byComponents);
            }

            foreach (var c in candidates)
            {
                if (c >= baseTotal && c <= maxAllowed)
                {
                    return Math.Round(c, 0, MidpointRounding.AwayFromZero);
                }
            }

            return Math.Round(fallbackPayable, 0, MidpointRounding.AwayFromZero);
        }
    }
}
