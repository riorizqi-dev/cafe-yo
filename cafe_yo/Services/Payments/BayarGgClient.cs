using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace cafe_yo.Services.Payments
{
    public interface IBayarGgClient
    {
        Task<BayarGgApiResponse> CreatePaymentAsync(BayarGgCreatePaymentRequest request, CancellationToken cancellationToken = default);
        Task<BayarGgApiResponse> CheckPaymentAsync(string invoice, CancellationToken cancellationToken = default);
        Task<BayarGgApiResponse> ListPaymentsAsync(BayarGgListPaymentsRequest request, CancellationToken cancellationToken = default);
    }

    public sealed class BayarGgClient : IBayarGgClient
    {
        private readonly HttpClient _httpClient;
        private readonly BayarGgOptions _options;

        public BayarGgClient(HttpClient httpClient, IOptions<BayarGgOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;
        }

        public async Task<BayarGgApiResponse> CreatePaymentAsync(BayarGgCreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["amount"] = request.Amount,
                ["description"] = request.Description,
                ["customer_name"] = request.CustomerName,
                ["customer_email"] = request.CustomerEmail,
                ["customer_phone"] = request.CustomerPhone,
                ["callback_url"] = request.CallbackUrl,
                ["redirect_url"] = request.RedirectUrl,
                ["payment_method"] = request.PaymentMethod,
                ["use_qris_converter"] = request.UseQrisConverter
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var httpRequest = BuildRequest(HttpMethod.Post, "/api/create-payment.php", content);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseApiResponse(response, raw);
        }

        public async Task<BayarGgApiResponse> CheckPaymentAsync(string invoice, CancellationToken cancellationToken = default)
        {
            var encodedInvoice = Uri.EscapeDataString(invoice ?? string.Empty);
            using var httpRequest = BuildRequest(HttpMethod.Get, $"/api/check-payment.php?invoice={encodedInvoice}");
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseApiResponse(response, raw);
        }

        public async Task<BayarGgApiResponse> ListPaymentsAsync(BayarGgListPaymentsRequest request, CancellationToken cancellationToken = default)
        {
            var query = new List<string>();
            AddQuery(query, "status", request.Status);
            AddQuery(query, "search", request.Search);
            AddQuery(query, "payment_method", request.PaymentMethod);
            AddQuery(query, "paid_via", request.PaidVia);
            AddQuery(query, "start_date", request.StartDate);
            AddQuery(query, "end_date", request.EndDate);
            AddQuery(query, "page", request.Page?.ToString(CultureInfo.InvariantCulture));
            AddQuery(query, "limit", request.Limit?.ToString(CultureInfo.InvariantCulture));

            var queryString = query.Count == 0 ? string.Empty : "?" + string.Join("&", query);
            using var httpRequest = BuildRequest(HttpMethod.Get, "/api/list-payments.php" + queryString);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseApiResponse(response, raw);
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string path, HttpContent? content = null)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException("Payment gateway API key belum diisi.");
            }

            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-API-Key", _options.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (content != null)
            {
                request.Content = content;
            }
            return request;
        }

        private static BayarGgApiResponse ParseApiResponse(HttpResponseMessage response, string raw)
        {
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(raw);
            }
            catch
            {
                // Keep raw response for diagnostics.
            }

            var root = doc?.RootElement;
            var dataNode = root.HasValue && TryGetProperty(root.Value, out var dataElement, "data", "result", "payment")
                ? dataElement
                : (JsonElement?)null;

            var invoice = GetString(root, dataNode, "invoice", "invoice_id", "payment_id");
            var status = GetString(root, dataNode, "status", "payment_status");
            var paymentUrl = GetString(root, dataNode, "payment_url", "checkout_url", "url");
            var qrString = GetString(root, dataNode, "qr_string", "qris_string", "qris");
            var qrImageUrl = GetString(root, dataNode, "qr_image_url", "qris_image_url", "qr_url");
            var message = GetString(root, dataNode, "message", "msg");
            var amount = GetDecimal(root, dataNode, "amount", "harga", "price");
            var totalPayment = GetDecimal(root, dataNode, "total_payment", "total_amount", "gross_amount", "total");
            var uniqueCode = GetDecimal(root, dataNode, "unique_code", "kode_unik", "kode");
            var feeAmount = GetDecimal(root, dataNode, "fee", "fee_amount", "admin_fee", "biaya");

            return new BayarGgApiResponse
            {
                IsHttpSuccess = response.IsSuccessStatusCode,
                HttpStatusCode = (int)response.StatusCode,
                Invoice = invoice,
                Status = status,
                PaymentUrl = paymentUrl,
                QrString = qrString,
                QrImageUrl = qrImageUrl,
                Message = message,
                Amount = amount,
                TotalPayment = totalPayment,
                UniqueCode = uniqueCode,
                FeeAmount = feeAmount,
                RawJson = raw
            };
        }

        private static bool TryGetProperty(JsonElement root, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value))
                {
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string? GetString(JsonElement? root, JsonElement? nested, params string[] names)
        {
            foreach (var name in names)
            {
                if (nested.HasValue && TryGetProperty(nested.Value, out var nestedValue, name))
                {
                    var value = nestedValue.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                if (root.HasValue && TryGetProperty(root.Value, out var rootValue, name))
                {
                    var value = rootValue.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }

            return null;
        }

        private static void AddQuery(ICollection<string> query, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }

        private static decimal? GetDecimal(JsonElement? root, JsonElement? nested, params string[] names)
        {
            static decimal? Parse(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var cleaned = raw.Trim();
                cleaned = cleaned.Replace("Rp", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "")
                    .Replace(".", "")
                    .Replace(",", ".");
                return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
            }

            foreach (var name in names)
            {
                if (nested.HasValue && TryGetProperty(nested.Value, out var nestedValue, name))
                {
                    var parsed = Parse(nestedValue.ToString());
                    if (parsed.HasValue) return parsed.Value;
                }
                if (root.HasValue && TryGetProperty(root.Value, out var rootValue, name))
                {
                    var parsed = Parse(rootValue.ToString());
                    if (parsed.HasValue) return parsed.Value;
                }
            }

            return null;
        }
    }

    public sealed class BayarGgCreatePaymentRequest
    {
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CallbackUrl { get; set; }
        public string? RedirectUrl { get; set; }
        public string PaymentMethod { get; set; } = "qris";
        public bool UseQrisConverter { get; set; }
    }

    public sealed class BayarGgListPaymentsRequest
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

    public sealed class BayarGgApiResponse
    {
        public bool IsHttpSuccess { get; set; }
        public int HttpStatusCode { get; set; }
        public string? Invoice { get; set; }
        public string? Status { get; set; }
        public string? PaymentUrl { get; set; }
        public string? QrString { get; set; }
        public string? QrImageUrl { get; set; }
        public string? Message { get; set; }
        public decimal? Amount { get; set; }
        public decimal? TotalPayment { get; set; }
        public decimal? UniqueCode { get; set; }
        public decimal? FeeAmount { get; set; }
        public string RawJson { get; set; } = string.Empty;
    }
}
