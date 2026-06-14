using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using cafe_yo.Models;

namespace cafe_yo.Services
{
    public sealed class ChatbotService : IChatbotService
    {
        private readonly IConfiguration _configuration;
        private const int DefaultListLimit = 8;

        public ChatbotService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<ChatbotReply> AskAsync(string message, HttpContext httpContext)
        {
            var cleaned = (message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return new ChatbotReply
                {
                    Intent = "validation",
                    Answer = "Silakan tulis pertanyaan dulu ya.",
                    Confidence = 100
                };
            }

            var lowered = cleaned.ToLowerInvariant();
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return new ChatbotReply
                {
                    Intent = "system_error",
                    Answer = "Maaf, koneksi data belum tersedia.",
                    Confidence = 100
                };
            }

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var faqReply = await MatchFaqAsync(conn, lowered);
            if (faqReply != null)
            {
                await SaveLogAsync(conn, httpContext, cleaned, faqReply);
                return faqReply;
            }

            if (IsListIntent(lowered))
            {
                var category = ExtractCategory(lowered);
                var listReply = await BuildListMenuReplyAsync(conn, category);
                await SaveLogAsync(conn, httpContext, cleaned, listReply);
                return listReply;
            }

            var menuMatch = await FindBestMenuMatchAsync(conn, lowered);
            if (menuMatch != null)
            {
                var reply = BuildMenuReply(lowered, menuMatch);
                await SaveLogAsync(conn, httpContext, cleaned, reply);
                return reply;
            }

            var fallback = new ChatbotReply
            {
                Intent = "fallback",
                Answer = "Maaf, menu yang kamu maksud belum ketemu. Coba tulis nama menu atau kategorinya, ya.",
                Confidence = 40
            };
            await SaveLogAsync(conn, httpContext, cleaned, fallback);
            return fallback;
        }

        private static bool IsListIntent(string lowered)
        {
            return lowered.Contains("daftar menu")
                || lowered.Contains("list menu")
                || lowered.Contains("menu apa")
                || lowered.Contains("menu kategori");
        }

        private static bool IsStockIntent(string lowered)
        {
            return lowered.Contains("stok") || lowered.Contains("ready") || lowered.Contains("tersedia") || lowered.Contains("habis");
        }

        private static bool IsPriceIntent(string lowered)
        {
            return lowered.Contains("harga") || lowered.Contains("berapa");
        }

        private static string? ExtractCategory(string lowered)
        {
            var categories = new[] { "kopi", "makanan", "dessert", "snack", "drink", "food" };
            foreach (var cat in categories)
            {
                if (lowered.Contains(cat))
                {
                    return cat;
                }
            }
            return null;
        }

        private async Task<ChatbotReply?> MatchFaqAsync(SqlConnection conn, string lowered)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT FaqId, Question, Answer, Keywords
FROM dbo.Faqs
WHERE IsActive = 1
ORDER BY SortOrder ASC, FaqId ASC;";
            await using var reader = await cmd.ExecuteReaderAsync();

            int? bestFaqId = null;
            string? bestAnswer = null;
            decimal bestScore = 0;

            while (await reader.ReadAsync())
            {
                var faqId = reader.GetInt32(0);
                var question = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var answer = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var keywords = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);

                var score = 0m;
                if (!string.IsNullOrWhiteSpace(question) && lowered.Contains(question.ToLowerInvariant()))
                {
                    score += 70;
                }

                if (!string.IsNullOrWhiteSpace(keywords))
                {
                    var parts = keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var key in parts)
                    {
                        if (!string.IsNullOrWhiteSpace(key) && lowered.Contains(key.ToLowerInvariant()))
                        {
                            score += 20;
                        }
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestFaqId = faqId;
                    bestAnswer = answer;
                }
            }

            if (bestFaqId.HasValue && bestScore >= 45 && !string.IsNullOrWhiteSpace(bestAnswer))
            {
                return new ChatbotReply
                {
                    Intent = "faq",
                    Answer = bestAnswer,
                    Confidence = bestScore,
                    MatchedFaqId = bestFaqId
                };
            }

            var defaultFaq = MatchDefaultFaq(lowered);
            return defaultFaq;
        }

        private static ChatbotReply? MatchDefaultFaq(string lowered)
        {
            if (lowered.Contains("cara pesan"))
            {
                return new ChatbotReply { Intent = "faq", Answer = "Cara pesan: pilih meja, pilih menu, lalu checkout. Bisa dine-in atau take away.", Confidence = 60 };
            }

            if (lowered.Contains("metode pembayaran") || lowered.Contains("pembayaran"))
            {
                return new ChatbotReply { Intent = "faq", Answer = "Metode pembayaran: tunai, transfer, dan QRIS.", Confidence = 60 };
            }

            if (lowered.Contains("jam operasional") || lowered.Contains("buka jam"))
            {
                return new ChatbotReply { Intent = "faq", Answer = "Jam operasional: setiap hari 08.00 - 22.00 WIB.", Confidence = 60 };
            }

            if (lowered.Contains("dine in") || lowered.Contains("take away") || lowered.Contains("pengiriman"))
            {
                return new ChatbotReply { Intent = "faq", Answer = "Kami melayani dine-in dan take away. Pengiriman mengikuti kebijakan cabang.", Confidence = 60 };
            }

            return null;
        }

        private async Task<ChatbotReply> BuildListMenuReplyAsync(SqlConnection conn, string? category)
        {
            await using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(category))
            {
                cmd.CommandText = @"
SELECT TOP (@TopN) MenuItemId, Name, Category, Price, ISNULL(Stock,0) AS Stock, ISNULL(IsAvailable,1) AS IsAvailable
FROM dbo.MenuItems
ORDER BY Name ASC;";
                cmd.Parameters.AddWithValue("@TopN", DefaultListLimit);
            }
            else
            {
                cmd.CommandText = @"
SELECT TOP (@TopN) MenuItemId, Name, Category, Price, ISNULL(Stock,0) AS Stock, ISNULL(IsAvailable,1) AS IsAvailable
FROM dbo.MenuItems
WHERE LOWER(ISNULL(Category,'')) LIKE @Category
ORDER BY Name ASC;";
                cmd.Parameters.AddWithValue("@TopN", DefaultListLimit);
                cmd.Parameters.AddWithValue("@Category", "%" + category.ToLowerInvariant() + "%");
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            var lines = new List<string>();
            while (await reader.ReadAsync())
            {
                var name = reader.IsDBNull(1) ? "-" : reader.GetString(1);
                var price = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                var stock = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                var isAvailable = !reader.IsDBNull(5) && reader.GetBoolean(5);
                var availableText = (isAvailable && stock > 0) ? "Tersedia" : "Habis";
                lines.Add($"- {name} ({availableText}) | Stok: {stock} | Harga: Rp{price:N0}");
            }

            if (lines.Count == 0)
            {
                return new ChatbotReply
                {
                    Intent = "menu_list",
                    Answer = "Maaf, daftar menu untuk kategori itu belum tersedia.",
                    Confidence = 70
                };
            }

            var intro = string.IsNullOrWhiteSpace(category)
                ? "Berikut daftar menu yang tersedia:"
                : $"Berikut daftar menu kategori {category}:";

            return new ChatbotReply
            {
                Intent = "menu_list",
                Answer = intro + Environment.NewLine + string.Join(Environment.NewLine, lines),
                Confidence = 80
            };
        }

        private async Task<MenuItem?> FindBestMenuMatchAsync(SqlConnection conn, string lowered)
        {
            var tokens = Tokenize(lowered);
            if (tokens.Count == 0)
            {
                return null;
            }

            var whereBuilder = new StringBuilder();
            for (var i = 0; i < tokens.Count; i++)
            {
                if (i > 0)
                {
                    whereBuilder.Append(" OR ");
                }

                whereBuilder.Append($"LOWER(Name) LIKE @p{i} OR LOWER(ISNULL(Category,'')) LIKE @p{i}");
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
SELECT TOP 30 MenuItemId, Name, Category, Price, ISNULL(Stock,0) AS Stock, ISNULL(IsAvailable,1) AS IsAvailable
FROM dbo.MenuItems
WHERE {whereBuilder}
ORDER BY Name ASC;";

            for (var i = 0; i < tokens.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@p{i}", $"%{tokens[i]}%");
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            MenuItem? best = null;
            var bestScore = 0m;
            while (await reader.ReadAsync())
            {
                var item = new MenuItem
                {
                    MenuItemId = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Price = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                    Stock = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    IsAvailable = reader.IsDBNull(5) ? null : reader.GetBoolean(5)
                };

                var score = ScoreMenu(item, lowered, tokens);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }

            return bestScore >= 25 ? best : null;
        }

        private static decimal ScoreMenu(MenuItem item, string lowered, IReadOnlyCollection<string> tokens)
        {
            var score = 0m;
            var name = item.Name.ToLowerInvariant();
            var category = (item.Category ?? string.Empty).ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(name) && lowered.Contains(name))
            {
                score += 60;
            }

            foreach (var token in tokens)
            {
                if (name.Contains(token))
                {
                    score += 12;
                }
                if (!string.IsNullOrWhiteSpace(category) && category.Contains(token))
                {
                    score += 6;
                }
            }

            return score;
        }

        private static List<string> Tokenize(string lowered)
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "apakah", "ada", "menu", "yang", "di", "ke", "dan", "atau", "berapa", "harga", "stok",
                "ready", "tersedia", "habis", "cara", "pesan", "tolong", "saya", "mau", "ingin", "list", "daftar"
            };

            return lowered.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x.Length >= 2 && !stopWords.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ChatbotReply BuildMenuReply(string lowered, MenuItem menu)
        {
            var available = (menu.IsAvailable ?? true) && menu.Stock > 0;
            var status = available ? "tersedia" : "sedang habis";

            if (IsPriceIntent(lowered))
            {
                return new ChatbotReply
                {
                    Intent = "price_check",
                    Answer = $"Harga {menu.Name} adalah Rp{menu.Price:N0}.",
                    Confidence = 85,
                    MatchedMenuItemId = menu.MenuItemId
                };
            }

            if (IsStockIntent(lowered))
            {
                return new ChatbotReply
                {
                    Intent = "stock_check",
                    Answer = $"{menu.Name} {status}. Stok saat ini: {menu.Stock}. Harga: Rp{menu.Price:N0}.",
                    Confidence = 86,
                    MatchedMenuItemId = menu.MenuItemId
                };
            }

            return new ChatbotReply
            {
                Intent = "menu_info",
                Answer = $"{menu.Name} ({menu.Category ?? "menu"}): {status}, stok {menu.Stock}, harga Rp{menu.Price:N0}.",
                Confidence = 80,
                MatchedMenuItemId = menu.MenuItemId
            };
        }

        private static async Task SaveLogAsync(SqlConnection conn, HttpContext httpContext, string question, ChatbotReply reply)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = @"
INSERT INTO dbo.ChatbotLogs
    (SessionId, UserId, Question, Answer, Intent, MatchedMenuItemId, MatchedFaqId, Confidence, IpAddress, CreatedAt)
VALUES
    (@SessionId, @UserId, @Question, @Answer, @Intent, @MatchedMenuItemId, @MatchedFaqId, @Confidence, @IpAddress, SYSUTCDATETIME());";

            cmd.Parameters.AddWithValue("@SessionId", httpContext.TraceIdentifier);
            cmd.Parameters.AddWithValue("@UserId", (object?)httpContext.User?.Identity?.Name ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Question", question);
            cmd.Parameters.AddWithValue("@Answer", reply.Answer ?? string.Empty);
            cmd.Parameters.AddWithValue("@Intent", reply.Intent ?? "unknown");
            cmd.Parameters.AddWithValue("@MatchedMenuItemId", (object?)reply.MatchedMenuItemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MatchedFaqId", (object?)reply.MatchedFaqId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Confidence", reply.Confidence);
            cmd.Parameters.AddWithValue("@IpAddress", (object?)httpContext.Connection.RemoteIpAddress?.ToString() ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
